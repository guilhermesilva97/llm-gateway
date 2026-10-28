using System.Text.Json.Nodes;
using LlmGateway.Core.Clients;
using LlmGateway.Core.Pricing;
using LlmGateway.Core.Privacy;
using LlmGateway.Core.Routing;
using LlmGateway.Core.Telemetry;

namespace LlmGateway.Core.Chat;

public sealed record ChatCommand(
    GatewayClient Client,
    JsonObject Payload,
    string RequestId,
    bool RehydratePii = true,
    bool BypassCache = false);

public sealed record ChatMetadata(string Alias, RouteTarget Target, int Fallbacks, bool CacheHit);

public sealed record ChatResult(JsonObject Body, ChatMetadata Metadata, TokenUsage Usage, decimal CostUsd);

public sealed class ChatGateway(
    ModelCatalog catalog,
    PriceTable prices,
    PiiMasker masker,
    IProviderExecutor providers,
    IResponseCache cache,
    IQuotaStore quotas,
    IUsageSink usage,
    GatewayTelemetry telemetry,
    TimeProvider time)
{
    public async Task<ChatResult> CompleteAsync(ChatCommand command, CancellationToken cancellationToken)
    {
        var tracker = new RequestTracker(command, telemetry, usage, time, stream: false);
        RouteTarget? target = null;
        try
        {
            var (route, vault) = await PrepareAsync(command, tracker, cancellationToken);
            var cacheKey = command.BypassCache ? null : CacheKeys.For(route.Alias, command.Payload);

            if (cacheKey is not null && await cache.GetAsync(cacheKey, cancellationToken) is { } cached)
            {
                var cachedUsage = TokenUsage.FromJson(cached.Body["usage"]) ?? default;
                Deliver(cached.Body, vault, command);
                tracker.Succeeded(cached.Target, cachedUsage, 0m, cacheHit: true);
                return new ChatResult(cached.Body, new ChatMetadata(route.Alias, cached.Target, 0, CacheHit: true), cachedUsage, 0m);
            }

            (target, var body) = await ExecuteAsync(
                route,
                tracker,
                (t, token) => providers.CompleteAsync(t, command.Payload.ForTarget(t.Model, stream: false), token),
                cancellationToken);

            var tokens = TokenUsage.FromJson(body["usage"]) ?? new TokenUsage(
                TokenUsage.Estimate(ChatPayload.PromptText(command.Payload)),
                TokenUsage.Estimate(ChatPayload.CompletionText(body)));
            var cost = prices.Cost(target, tokens);

            if (cacheKey is not null)
            {
                await cache.SetAsync(cacheKey, new CachedCompletion(target, (JsonObject)body.DeepClone()), cancellationToken);
            }
            await quotas.AddAsync(command.Client.Id, tokens.TotalTokens, cancellationToken);

            Deliver(body, vault, command);
            tracker.Succeeded(target, tokens, cost, cacheHit: false);
            return new ChatResult(body, new ChatMetadata(route.Alias, target, tracker.Fallbacks, CacheHit: false), tokens, cost);
        }
        catch (Exception error)
        {
            tracker.Failed(error, target);
            throw;
        }
    }

    public async Task<ChatStreamSession> StreamAsync(ChatCommand command, CancellationToken cancellationToken)
    {
        var tracker = new RequestTracker(command, telemetry, usage, time, stream: true);
        try
        {
            var (route, vault) = await PrepareAsync(command, tracker, cancellationToken);
            var (target, stream) = await ExecuteAsync(
                route,
                tracker,
                (t, token) => providers.OpenStreamAsync(t, command.Payload.ForTarget(t.Model, stream: true), token),
                cancellationToken);
            var metadata = new ChatMetadata(route.Alias, target, tracker.Fallbacks, CacheHit: false);
            return new ChatStreamSession(stream, metadata, command, vault, tracker, prices, quotas);
        }
        catch (Exception error)
        {
            tracker.Failed(error);
            throw;
        }
    }

    private async Task<(ModelRoute Route, PiiVault Vault)> PrepareAsync(
        ChatCommand command, RequestTracker tracker, CancellationToken cancellationToken)
    {
        var alias = command.Payload.Model()
            ?? throw new GatewayException(400, "invalid_request", "O campo 'model' é obrigatório.");
        tracker.Routed(alias);

        if (command.Payload["messages"] is not JsonArray { Count: > 0 })
        {
            throw new GatewayException(400, "invalid_request", "O campo 'messages' precisa ter ao menos uma mensagem.");
        }

        var route = catalog.Find(alias)
            ?? throw new GatewayException(404, "model_not_found", $"O modelo '{alias}' não está configurado neste gateway.");
        tracker.Routed(route.Alias);

        var used = await quotas.GetUsedAsync(command.Client.Id, cancellationToken);
        if (used >= command.Client.MonthlyTokenQuota)
        {
            throw new GatewayException(429, "quota_exceeded",
                $"O cliente '{command.Client.Id}' atingiu a cota mensal de {command.Client.MonthlyTokenQuota} tokens.");
        }

        var vault = new PiiVault();
        if (command.Client.MaskPii)
        {
            command.Payload.MaskPii(masker, vault);
            tracker.Masked(vault, ChatPayload.LastUserMessage(command.Payload));
        }
        else
        {
            tracker.Masked(vault, masker.Mask(ChatPayload.LastUserMessage(command.Payload), new PiiVault()));
        }
        return (route, vault);
    }

    private async Task<(RouteTarget Target, T Result)> ExecuteAsync<T>(
        ModelRoute route,
        RequestTracker tracker,
        Func<RouteTarget, CancellationToken, Task<T>> call,
        CancellationToken cancellationToken)
    {
        var failures = new List<string>();
        foreach (var target in route.Targets)
        {
            if (!providers.IsAvailable(target.Provider))
            {
                failures.Add($"{target}: indisponível");
                tracker.FellBack(target, "unavailable");
                continue;
            }
            try
            {
                return (target, await call(target, cancellationToken));
            }
            catch (ProviderUnavailableException error)
            {
                failures.Add($"{target}: {error.Message}");
                tracker.FellBack(target, "error");
            }
        }
        throw new GatewayException(502, "all_providers_failed",
            "Nenhum provedor respondeu. " + string.Join(" | ", failures));
    }

    private static void Deliver(JsonObject body, PiiVault vault, ChatCommand command)
    {
        if (command.RehydratePii)
        {
            ChatPayload.Rehydrate(body, vault);
        }
    }
}
