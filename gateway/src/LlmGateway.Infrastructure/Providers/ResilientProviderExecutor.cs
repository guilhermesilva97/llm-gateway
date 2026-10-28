using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json.Nodes;
using LlmGateway.Core.Chat;
using LlmGateway.Core.Routing;
using LlmGateway.Core.Telemetry;
using LlmGateway.Infrastructure.Configuration;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace LlmGateway.Infrastructure.Providers;

public sealed record ProviderStatus(
    string Name,
    string Type,
    bool Configured,
    string Circuit,
    long Successes,
    long Failures,
    double? LastLatencyMs,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastFailureAt,
    string? LastError);

public sealed class ResilientProviderExecutor : IProviderExecutor
{
    private readonly Dictionary<string, ProviderSlot> _slots;
    private readonly TimeProvider _time;

    public ResilientProviderExecutor(
        IEnumerable<IChatProvider> providers,
        ResilienceOptions resilience,
        GatewayTelemetry telemetry,
        TimeProvider time)
    {
        _time = time;
        _slots = providers.ToDictionary(
            p => p.Name,
            p => new ProviderSlot(p, resilience, time),
            StringComparer.OrdinalIgnoreCase);

        telemetry.Meter.CreateObservableGauge(
            "llm_gateway.circuit.state",
            () => _slots.Values.Select(s => new Measurement<int>(
                s.CircuitCode,
                new KeyValuePair<string, object?>("provider", s.Provider.Name))),
            "{state}",
            "Estado do circuit breaker por provedor: 0 fechado, 1 meio aberto, 2 aberto");
    }

    public bool IsAvailable(string provider) =>
        _slots.TryGetValue(provider, out var slot)
        && slot.Provider.IsConfigured
        && slot.State.CircuitState is not (CircuitState.Open or CircuitState.Isolated);

    public Task<JsonObject> CompleteAsync(RouteTarget target, JsonObject request, CancellationToken cancellationToken) =>
        RunAsync(target, (provider, token) => provider.CompleteAsync(request, token), cancellationToken);

    public Task<IChatStream> OpenStreamAsync(RouteTarget target, JsonObject request, CancellationToken cancellationToken) =>
        RunAsync(target, (provider, token) => provider.OpenStreamAsync(request, token), cancellationToken);

    public IReadOnlyList<ProviderStatus> Statuses() =>
        [.. _slots.Values.OrderBy(s => s.Provider.Name).Select(s => s.Snapshot())];

    private async Task<T> RunAsync<T>(
        RouteTarget target,
        Func<IChatProvider, CancellationToken, Task<T>> call,
        CancellationToken cancellationToken)
    {
        if (!_slots.TryGetValue(target.Provider, out var slot))
        {
            throw new ProviderUnavailableException(target.Provider, $"Provedor '{target.Provider}' não está configurado.");
        }

        var start = _time.GetTimestamp();
        try
        {
            var result = await slot.Pipeline.ExecuteAsync(
                async token => await call(slot.Provider, token),
                cancellationToken);
            slot.RecordSuccess(_time.GetElapsedTime(start), _time.GetUtcNow());
            return result;
        }
        catch (BrokenCircuitException error)
        {
            slot.RecordFailure("circuito aberto", _time.GetUtcNow());
            throw new ProviderUnavailableException(target.Provider, "circuito aberto após falhas consecutivas", error);
        }
        catch (ProviderTransientException error)
        {
            slot.RecordFailure(error.Message, _time.GetUtcNow());
            throw new ProviderUnavailableException(target.Provider, error.Message, error);
        }
    }

    private sealed class ProviderSlot
    {
        private readonly Lock _gate = new();
        private long _successes;
        private long _failures;
        private double? _lastLatencyMs;
        private DateTimeOffset? _lastSuccessAt;
        private DateTimeOffset? _lastFailureAt;
        private string? _lastError;

        public ProviderSlot(IChatProvider provider, ResilienceOptions options, TimeProvider time)
        {
            Provider = provider;
            State = new CircuitBreakerStateProvider();
            var transient = new PredicateBuilder().Handle<ProviderTransientException>();
            Pipeline = new ResiliencePipelineBuilder { TimeProvider = time }
                .AddRetry(new RetryStrategyOptions
                {
                    ShouldHandle = transient,
                    MaxRetryAttempts = Math.Max(1, options.RetryAttempts),
                    Delay = TimeSpan.FromMilliseconds(options.RetryDelayMs),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                })
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions
                {
                    ShouldHandle = transient,
                    FailureRatio = options.FailureRatio,
                    MinimumThroughput = Math.Max(2, options.MinimumThroughput),
                    SamplingDuration = TimeSpan.FromSeconds(options.SamplingSeconds),
                    BreakDuration = TimeSpan.FromSeconds(options.BreakSeconds),
                    StateProvider = State,
                })
                .Build();
        }

        public IChatProvider Provider { get; }

        public CircuitBreakerStateProvider State { get; }

        public ResiliencePipeline Pipeline { get; }

        public int CircuitCode => State.CircuitState switch
        {
            CircuitState.Closed => 0,
            CircuitState.HalfOpen => 1,
            _ => 2,
        };

        public void RecordSuccess(TimeSpan elapsed, DateTimeOffset now)
        {
            lock (_gate)
            {
                _successes++;
                _lastLatencyMs = Math.Round(elapsed.TotalMilliseconds, 1);
                _lastSuccessAt = now;
            }
        }

        public void RecordFailure(string error, DateTimeOffset now)
        {
            lock (_gate)
            {
                _failures++;
                _lastFailureAt = now;
                _lastError = error;
            }
        }

        public ProviderStatus Snapshot()
        {
            lock (_gate)
            {
                var circuit = State.CircuitState switch
                {
                    CircuitState.Closed => "closed",
                    CircuitState.HalfOpen => "half-open",
                    _ => "open",
                };
                return new ProviderStatus(
                    Provider.Name,
                    Provider.Type,
                    Provider.IsConfigured,
                    circuit,
                    _successes,
                    _failures,
                    _lastLatencyMs,
                    _lastSuccessAt,
                    _lastFailureAt,
                    _lastError);
            }
        }
    }
}
