using LlmGateway.Core.Telemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace LlmGateway.Api.Observability;

public static class TelemetrySetup
{
    public static IServiceCollection AddGatewayObservability(this IServiceCollection services, IConfiguration configuration)
    {
        var metricsEndpoint = configuration["Telemetry:MetricsEndpoint"];
        var tracesEndpoint = configuration["Telemetry:TracesEndpoint"];
        var exportIntervalMs = configuration.GetValue("Telemetry:ExportIntervalMs", 5000);

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: configuration["Telemetry:ServiceName"] ?? "llm-gateway",
                serviceVersion: typeof(TelemetrySetup).Assembly.GetName().Version?.ToString()))
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(GatewayTelemetry.Name)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();
                if (!string.IsNullOrWhiteSpace(metricsEndpoint))
                {
                    metrics.AddOtlpExporter((exporter, reader) =>
                    {
                        exporter.Endpoint = new Uri(metricsEndpoint);
                        exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
                        reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = exportIntervalMs;
                        reader.TemporalityPreference = MetricReaderTemporalityPreference.Cumulative;
                    });
                }
            })
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(GatewayTelemetry.Name)
                    .AddAspNetCoreInstrumentation(options => options.Filter = http =>
                        !http.Request.Path.StartsWithSegments("/health"))
                    .AddHttpClientInstrumentation();
                if (!string.IsNullOrWhiteSpace(tracesEndpoint))
                {
                    tracing.AddOtlpExporter(exporter =>
                    {
                        exporter.Endpoint = new Uri(tracesEndpoint);
                        exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
                    });
                }
            });
        return services;
    }
}
