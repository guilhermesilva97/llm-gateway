using System.Globalization;
using System.Threading.RateLimiting;
using LlmGateway.Core.Chat;
using LlmGateway.Core.Telemetry;
using Microsoft.AspNetCore.RateLimiting;

namespace LlmGateway.Api.Http;

public static class RateLimiting
{
    public const string PerClientPolicy = "per-client";

    public static IServiceCollection AddPerClientRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = OnRejectedAsync;
            options.AddPolicy(PerClientPolicy, http =>
            {
                var client = ClientAuthenticationMiddleware.ClientOf(http);
                if (client is null)
                {
                    return RateLimitPartition.GetNoLimiter("anonymous");
                }
                return RateLimitPartition.GetFixedWindowLimiter(
                    $"{client.Id}:{client.RequestsPerMinute}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = client.RequestsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    });
            });
        });

    private static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;
        var client = ClientAuthenticationMiddleware.ClientOf(http);
        var services = http.RequestServices;
        var time = services.GetRequiredService<TimeProvider>();

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            http.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        if (client is not null)
        {
            services.GetRequiredService<GatewayTelemetry>().Requests.Add(1,
                new KeyValuePair<string, object?>("client", client.Id),
                new KeyValuePair<string, object?>("model", "n/a"),
                new KeyValuePair<string, object?>("provider", "none"),
                new KeyValuePair<string, object?>("status", "rate_limited"),
                new KeyValuePair<string, object?>("cache", "miss"),
                new KeyValuePair<string, object?>("stream", "false"));
            services.GetRequiredService<IUsageSink>().Record(new RequestRecord(
                http.TraceIdentifier, client.Id, time.GetUtcNow(), "n/a", null, null, false,
                0, 0, 0m, 0, "rate_limited", 429, false, 0, 0, string.Empty,
                $"Limite de {client.RequestsPerMinute} requisições por minuto excedido."));
        }

        await ErrorResponses.WriteAsync(http, 429, "rate_limit_exceeded",
            client is null
                ? "Limite de requisições excedido."
                : $"Limite de {client.RequestsPerMinute} requisições por minuto excedido para o cliente '{client.Id}'.");
    }
}
