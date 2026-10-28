using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Text.Json.Nodes;
using LlmGateway.Core.Chat;
using LlmGateway.Core.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace LlmGateway.Tests.Support;

public sealed class InMemoryResponseCache : IResponseCache
{
    private readonly ConcurrentDictionary<string, CachedCompletion> _entries = new();

    public Task<CachedCompletion?> GetAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult(_entries.TryGetValue(key, out var entry)
            ? entry with { Body = (JsonObject)entry.Body.DeepClone() }
            : null);

    public Task SetAsync(string key, CachedCompletion completion, CancellationToken cancellationToken)
    {
        _entries[key] = completion with { Body = (JsonObject)completion.Body.DeepClone() };
        return Task.CompletedTask;
    }
}

public sealed class InMemoryQuotaStore : IQuotaStore
{
    private readonly ConcurrentDictionary<string, long> _used = new();

    public Task<long> GetUsedAsync(string clientId, CancellationToken cancellationToken) =>
        Task.FromResult(_used.GetValueOrDefault(clientId));

    public Task AddAsync(string clientId, long tokens, CancellationToken cancellationToken)
    {
        _used.AddOrUpdate(clientId, tokens, (_, current) => current + tokens);
        return Task.CompletedTask;
    }
}

public sealed class ListUsageSink : IUsageSink
{
    public List<RequestRecord> Records { get; } = [];

    public void Record(RequestRecord record) => Records.Add(record);
}

public sealed class FakeProviderExecutor : IProviderExecutor
{
    public HashSet<string> Unavailable { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Failing { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Rejecting { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<(RouteTarget Target, JsonObject Request)> Calls { get; } = [];

    public JsonObject? FixedUsage { get; set; } = TokenUsageJson(10, 5);

    public bool IsAvailable(string provider) => !Unavailable.Contains(provider);

    public Task<JsonObject> CompleteAsync(RouteTarget target, JsonObject request, CancellationToken cancellationToken)
    {
        Register(target, request);
        var response = new JsonObject
        {
            ["id"] = "chatcmpl-test",
            ["object"] = "chat.completion",
            ["model"] = target.Model,
            ["choices"] = new JsonArray(new JsonObject
            {
                ["index"] = 0,
                ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = Echo(request) },
                ["finish_reason"] = "stop",
            }),
        };
        if (FixedUsage is not null)
        {
            response["usage"] = FixedUsage.DeepClone();
        }
        return Task.FromResult(response);
    }

    public Task<IChatStream> OpenStreamAsync(RouteTarget target, JsonObject request, CancellationToken cancellationToken)
    {
        Register(target, request);
        var text = Echo(request);
        var chunks = new List<JsonObject>();
        for (var i = 0; i < text.Length; i += 5)
        {
            chunks.Add(Chunk(new JsonObject { ["content"] = text.Substring(i, Math.Min(5, text.Length - i)) }, null));
        }
        chunks.Add(Chunk([], "stop"));
        chunks.Add(new JsonObject { ["choices"] = new JsonArray(), ["usage"] = TokenUsageJson(7, 3) });
        return Task.FromResult<IChatStream>(new ListStream(chunks));
    }

    public static string Echo(JsonObject request) => "eco: " + ChatPayload.LastUserMessage(request);

    public static JsonObject TokenUsageJson(int prompt, int completion) => new TokenUsage(prompt, completion).ToJson();

    private void Register(RouteTarget target, JsonObject request)
    {
        Calls.Add((target, request));
        if (Rejecting.Contains(target.Provider))
        {
            throw new ProviderRejectedException(target.Provider, 400, "requisição inválida");
        }
        if (Failing.Contains(target.Provider))
        {
            throw new ProviderUnavailableException(target.Provider, "falha simulada");
        }
    }

    private static JsonObject Chunk(JsonObject delta, string? finish) => new()
    {
        ["object"] = "chat.completion.chunk",
        ["choices"] = new JsonArray(new JsonObject { ["index"] = 0, ["delta"] = delta, ["finish_reason"] = finish }),
    };

    private sealed class ListStream(IEnumerable<JsonObject> chunks) : IChatStream
    {
        public async IAsyncEnumerator<JsonObject> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            foreach (var chunk in chunks)
            {
                await Task.Yield();
                yield return chunk;
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

public static class TestMeters
{
    public static IMeterFactory Create() =>
        new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>();
}
