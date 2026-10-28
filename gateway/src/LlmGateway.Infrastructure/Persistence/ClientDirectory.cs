using LlmGateway.Core.Clients;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LlmGateway.Infrastructure.Persistence;

public sealed class ClientDirectory(IDbContextFactory<GatewayDbContext> contexts, IMemoryCache cache) : IClientDirectory
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(15);

    public async Task<GatewayClient?> FindByApiKeyAsync(string apiKey, CancellationToken cancellationToken)
    {
        var hash = ApiKeys.Hash(apiKey);
        var cacheKey = "client:" + hash;
        if (cache.TryGetValue(cacheKey, out GatewayClient? cached))
        {
            return cached;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var entity = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.KeyHash == hash, cancellationToken);
        var client = entity is { Active: true } ? ToClient(entity) : null;
        cache.Set(cacheKey, client, CacheDuration);
        return client;
    }

    internal static GatewayClient ToClient(ClientEntity entity) => new(
        entity.Id,
        entity.Name,
        entity.RequestsPerMinute,
        entity.MonthlyTokenQuota,
        entity.MaskPii,
        entity.Active);
}
