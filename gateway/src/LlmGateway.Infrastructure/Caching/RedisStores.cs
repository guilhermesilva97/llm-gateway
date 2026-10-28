using System.Text.Json.Nodes;
using LlmGateway.Core.Chat;
using LlmGateway.Core.Routing;
using LlmGateway.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace LlmGateway.Infrastructure.Caching;

public sealed class RedisResponseCache(
    IConnectionMultiplexer redis,
    IOptions<GatewayOptions> options,
    ILogger<RedisResponseCache> logger) : IResponseCache
{
    private const string Prefix = "llmgw:cache:";

    public async Task<CachedCompletion?> GetAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var value = await redis.GetDatabase().StringGetAsync(Prefix + key);
            if (value.IsNullOrEmpty || JsonNode.Parse(value.ToString()) is not JsonObject entry
                || entry["body"] is not JsonObject body)
            {
                return null;
            }
            var target = new RouteTarget(entry["provider"]!.GetValue<string>(), entry["model"]!.GetValue<string>());
            return new CachedCompletion(target, (JsonObject)body.DeepClone());
        }
        catch (RedisException error)
        {
            logger.LogWarning("Cache indisponível, seguindo sem cache: {Message}", error.Message);
            return null;
        }
    }

    public async Task SetAsync(string key, CachedCompletion completion, CancellationToken cancellationToken)
    {
        var entry = new JsonObject
        {
            ["provider"] = completion.Target.Provider,
            ["model"] = completion.Target.Model,
            ["body"] = completion.Body.DeepClone(),
        };
        try
        {
            await redis.GetDatabase().StringSetAsync(
                Prefix + key,
                entry.ToJsonString(),
                TimeSpan.FromSeconds(options.Value.CacheTtlSeconds));
        }
        catch (RedisException error)
        {
            logger.LogWarning("Não foi possível gravar no cache: {Message}", error.Message);
        }
    }
}

public sealed class RedisQuotaStore(
    IConnectionMultiplexer redis,
    TimeProvider time,
    ILogger<RedisQuotaStore> logger) : IQuotaStore
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(40);

    public async Task<long> GetUsedAsync(string clientId, CancellationToken cancellationToken)
    {
        try
        {
            var value = await redis.GetDatabase().StringGetAsync(Key(clientId));
            return value.IsNullOrEmpty ? 0 : (long)value;
        }
        catch (RedisException error)
        {
            logger.LogWarning("Cota indisponível, liberando a requisição: {Message}", error.Message);
            return 0;
        }
    }

    public async Task AddAsync(string clientId, long tokens, CancellationToken cancellationToken)
    {
        if (tokens <= 0)
        {
            return;
        }
        try
        {
            var db = redis.GetDatabase();
            var key = Key(clientId);
            await db.StringIncrementAsync(key, tokens);
            await db.KeyExpireAsync(key, Retention);
        }
        catch (RedisException error)
        {
            logger.LogWarning("Não foi possível registrar consumo de cota: {Message}", error.Message);
        }
    }

    private string Key(string clientId) => $"llmgw:quota:{clientId}:{time.GetUtcNow():yyyyMM}";
}
