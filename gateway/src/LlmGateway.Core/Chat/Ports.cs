using System.Text.Json.Nodes;
using LlmGateway.Core.Routing;

namespace LlmGateway.Core.Chat;

public interface IChatStream : IAsyncEnumerable<JsonObject>, IAsyncDisposable;

public interface IProviderExecutor
{
    bool IsAvailable(string provider);

    Task<JsonObject> CompleteAsync(RouteTarget target, JsonObject request, CancellationToken cancellationToken);

    Task<IChatStream> OpenStreamAsync(RouteTarget target, JsonObject request, CancellationToken cancellationToken);
}

public sealed record CachedCompletion(RouteTarget Target, JsonObject Body);

public interface IResponseCache
{
    Task<CachedCompletion?> GetAsync(string key, CancellationToken cancellationToken);

    Task SetAsync(string key, CachedCompletion completion, CancellationToken cancellationToken);
}

public interface IQuotaStore
{
    Task<long> GetUsedAsync(string clientId, CancellationToken cancellationToken);

    Task AddAsync(string clientId, long tokens, CancellationToken cancellationToken);
}

public interface IUsageSink
{
    void Record(RequestRecord record);
}

public sealed record RequestRecord(
    string RequestId,
    string ClientId,
    DateTimeOffset Timestamp,
    string Alias,
    string? Provider,
    string? Model,
    bool Stream,
    int PromptTokens,
    int CompletionTokens,
    decimal CostUsd,
    double LatencyMs,
    string Status,
    int HttpStatus,
    bool CacheHit,
    int Fallbacks,
    int PiiMasked,
    string PromptPreview,
    string? Error);
