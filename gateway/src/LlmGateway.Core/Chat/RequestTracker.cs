using System.Diagnostics;
using LlmGateway.Core.Privacy;
using LlmGateway.Core.Routing;
using LlmGateway.Core.Telemetry;

namespace LlmGateway.Core.Chat;

internal sealed class RequestTracker
{
    private const int PreviewLength = 300;

    private readonly ChatCommand _command;
    private readonly GatewayTelemetry _telemetry;
    private readonly IUsageSink _sink;
    private readonly TimeProvider _time;
    private readonly bool _stream;
    private readonly long _start;
    private readonly DateTimeOffset _startedAt;
    private readonly Activity? _activity;
    private int _piiMasked;
    private string _preview = string.Empty;
    private bool _completed;

    public RequestTracker(ChatCommand command, GatewayTelemetry telemetry, IUsageSink sink, TimeProvider time, bool stream)
    {
        _command = command;
        _telemetry = telemetry;
        _sink = sink;
        _time = time;
        _stream = stream;
        _start = time.GetTimestamp();
        _startedAt = time.GetUtcNow();
        _activity = GatewayTelemetry.ActivitySource.StartActivity("chat");
        _activity?.SetTag("gen_ai.operation.name", "chat");
        _activity?.SetTag("llm_gateway.client", command.Client.Id);
    }

    public string Alias { get; private set; } = "desconhecido";

    public int Fallbacks { get; private set; }

    public void Routed(string alias)
    {
        Alias = alias;
        if (_activity is not null)
        {
            _activity.DisplayName = $"chat {alias}";
            _activity.SetTag("gen_ai.request.model", alias);
        }
    }

    public void Masked(PiiVault vault, string preview)
    {
        _piiMasked = vault.Count;
        _preview = preview.Length > PreviewLength ? preview[..PreviewLength] + "…" : preview;
        foreach (var (kind, count) in vault.CountsByKind)
        {
            _telemetry.PiiMasked.Add(count, new KeyValuePair<string, object?>("type", PiiVault.Label(kind).ToLowerInvariant()));
        }
        _activity?.SetTag("llm_gateway.pii_masked", vault.Count);
    }

    public void FellBack(RouteTarget from, string reason)
    {
        Fallbacks++;
        _telemetry.Fallbacks.Add(1,
            new KeyValuePair<string, object?>("from_provider", from.Provider),
            new KeyValuePair<string, object?>("reason", reason));
        _activity?.AddEvent(new ActivityEvent("fallback", tags: new ActivityTagsCollection
        {
            ["from"] = from.ToString(),
            ["reason"] = reason,
        }));
    }

    public void Succeeded(RouteTarget target, TokenUsage usage, decimal cost, bool cacheHit) =>
        Complete(target, usage, cost, cacheHit, "ok", 200, null);

    public void Failed(Exception error, RouteTarget? target = null, TokenUsage usage = default)
    {
        var (status, httpStatus) = error switch
        {
            GatewayException gateway => (gateway.Code, gateway.StatusCode),
            ProviderRejectedException rejected => ("provider_rejected", rejected.StatusCode),
            OperationCanceledException => ("cancelled", 499),
            _ => ("internal_error", 500),
        };
        Complete(target, usage, 0m, false, status, httpStatus, error.Message);
    }

    private void Complete(RouteTarget? target, TokenUsage usage, decimal cost, bool cacheHit, string status, int httpStatus, string? error)
    {
        if (_completed)
        {
            return;
        }
        _completed = true;

        var elapsed = _time.GetElapsedTime(_start);
        var provider = cacheHit ? "cache" : target?.Provider ?? "none";
        var tags = new TagList
        {
            { "client", _command.Client.Id },
            { "model", Alias },
            { "provider", provider },
            { "status", status },
            { "cache", cacheHit ? "hit" : "miss" },
            { "stream", _stream ? "true" : "false" },
        };
        _telemetry.Requests.Add(1, tags);
        _telemetry.Duration.Record(elapsed.TotalSeconds, tags);

        if (target is not null && !cacheHit)
        {
            RecordTokens(target, "input", usage.PromptTokens);
            RecordTokens(target, "output", usage.CompletionTokens);
            if (cost > 0)
            {
                _telemetry.Cost.Add((double)cost,
                    new KeyValuePair<string, object?>("client", _command.Client.Id),
                    new KeyValuePair<string, object?>("provider", target.Provider),
                    new KeyValuePair<string, object?>("model", target.Model));
            }
        }

        if (_activity is not null)
        {
            _activity.SetTag("gen_ai.provider.name", provider);
            _activity.SetTag("gen_ai.response.model", target?.Model);
            _activity.SetTag("gen_ai.usage.input_tokens", usage.PromptTokens);
            _activity.SetTag("gen_ai.usage.output_tokens", usage.CompletionTokens);
            _activity.SetTag("llm_gateway.cost_usd", (double)cost);
            _activity.SetTag("llm_gateway.cache_hit", cacheHit);
            _activity.SetTag("llm_gateway.fallbacks", Fallbacks);
            if (error is not null)
            {
                _activity.SetStatus(ActivityStatusCode.Error, error);
            }
            _activity.Dispose();
        }

        _sink.Record(new RequestRecord(
            _command.RequestId,
            _command.Client.Id,
            _startedAt,
            Alias,
            cacheHit ? "cache" : target?.Provider,
            target?.Model,
            _stream,
            usage.PromptTokens,
            usage.CompletionTokens,
            cost,
            Math.Round(elapsed.TotalMilliseconds, 1),
            status,
            httpStatus,
            cacheHit,
            Fallbacks,
            _piiMasked,
            _preview,
            error));
    }

    private void RecordTokens(RouteTarget target, string type, int tokens)
    {
        if (tokens <= 0)
        {
            return;
        }
        _telemetry.Tokens.Add(tokens,
            new KeyValuePair<string, object?>("client", _command.Client.Id),
            new KeyValuePair<string, object?>("provider", target.Provider),
            new KeyValuePair<string, object?>("model", target.Model),
            new KeyValuePair<string, object?>("type", type));
    }
}
