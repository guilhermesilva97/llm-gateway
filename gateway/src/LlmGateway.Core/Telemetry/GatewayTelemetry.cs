using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace LlmGateway.Core.Telemetry;

public sealed class GatewayTelemetry
{
    public const string Name = "LlmGateway";

    public static readonly ActivitySource ActivitySource = new(Name);

    private static readonly double[] DurationBuckets =
        [0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60];

    public GatewayTelemetry(IMeterFactory meterFactory)
    {
        Meter = meterFactory.Create(Name);
        Requests = Meter.CreateCounter<long>("llm_gateway.requests", "{request}", "Requisições processadas pelo gateway");
        Tokens = Meter.CreateCounter<long>("llm_gateway.tokens", "{token}", "Tokens consumidos por tipo");
        Cost = Meter.CreateCounter<double>("llm_gateway.cost_usd", "{usd}", "Custo estimado em dólares");
        Fallbacks = Meter.CreateCounter<long>("llm_gateway.fallbacks", "{fallback}", "Destinos pulados por falha ou indisponibilidade");
        PiiMasked = Meter.CreateCounter<long>("llm_gateway.pii_masked", "{item}", "Dados pessoais mascarados por tipo");
        Duration = Meter.CreateHistogram(
            "llm_gateway.request.duration",
            "s",
            "Duração das requisições",
            tags: null,
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = DurationBuckets });
    }

    public Meter Meter { get; }

    public Counter<long> Requests { get; }

    public Counter<long> Tokens { get; }

    public Counter<double> Cost { get; }

    public Counter<long> Fallbacks { get; }

    public Counter<long> PiiMasked { get; }

    public Histogram<double> Duration { get; }
}
