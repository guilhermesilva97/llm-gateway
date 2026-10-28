using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using LlmGateway.Api.Http;
using LlmGateway.Core.Chat;
using LlmGateway.Core.Routing;

namespace LlmGateway.Api.Endpoints;

public static class OpenAiEndpoints
{
    public static void MapOpenAiEndpoints(this WebApplication app)
    {
        var v1 = app.MapGroup("/v1").RequireRateLimiting(RateLimiting.PerClientPolicy);
        v1.MapGet("/models", ListModels);
        v1.MapPost("/chat/completions", CompleteChatAsync);
    }

    private static IResult ListModels(ModelCatalog catalog) => Results.Json(new JsonObject
    {
        ["object"] = "list",
        ["data"] = new JsonArray([.. catalog.All
            .OrderBy(r => r.Alias, StringComparer.OrdinalIgnoreCase)
            .Select(r => (JsonNode)new JsonObject
            {
                ["id"] = r.Alias,
                ["object"] = "model",
                ["owned_by"] = "llm-gateway",
                ["targets"] = new JsonArray([.. r.Targets.Select(t => (JsonNode)JsonValue.Create(t.ToString()))]),
            })]),
    });

    private static async Task CompleteChatAsync(HttpContext http, ChatGateway gateway)
    {
        var cancellationToken = http.RequestAborted;
        JsonObject? payload;
        try
        {
            payload = await JsonNode.ParseAsync(http.Request.Body, cancellationToken: cancellationToken) as JsonObject;
        }
        catch (JsonException)
        {
            payload = null;
        }
        if (payload is null)
        {
            await ErrorResponses.WriteAsync(http, 400, "invalid_request", "O corpo da requisição precisa ser um objeto JSON.");
            return;
        }

        var command = new ChatCommand(
            ClientAuthenticationMiddleware.ClientOf(http)!,
            payload,
            http.TraceIdentifier,
            RehydratePii: !HeaderIs(http, "x-gateway-pii-rehydrate", "false"),
            BypassCache: HeaderIs(http, "x-gateway-cache", "bypass") || HeaderIs(http, "cache-control", "no-cache"));

        try
        {
            if (payload.IsStream())
            {
                await StreamAsync(http, gateway, command, cancellationToken);
            }
            else
            {
                var result = await gateway.CompleteAsync(command, cancellationToken);
                WriteMetadata(http, result.Metadata);
                http.Response.Headers["x-gateway-cost-usd"] = result.CostUsd.ToString("0.########", CultureInfo.InvariantCulture);
                http.Response.Headers["x-gateway-tokens"] = result.Usage.TotalTokens.ToString(CultureInfo.InvariantCulture);
                http.Response.ContentType = "application/json; charset=utf-8";
                await http.Response.WriteAsync(result.Body.ToJsonString(), cancellationToken);
            }
        }
        catch (GatewayException error)
        {
            await ErrorResponses.WriteAsync(http, error.StatusCode, error.Code, error.Message);
        }
        catch (ProviderRejectedException error)
        {
            await ErrorResponses.WriteAsync(http, error.StatusCode, "provider_rejected", $"{error.Provider}: {error.Message}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static async Task StreamAsync(HttpContext http, ChatGateway gateway, ChatCommand command, CancellationToken cancellationToken)
    {
        await using var session = await gateway.StreamAsync(command, cancellationToken);
        WriteMetadata(http, session.Metadata);
        http.Response.ContentType = "text/event-stream; charset=utf-8";
        http.Response.Headers.CacheControl = "no-cache";
        http.Response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            await foreach (var chunk in session.ReadAllAsync(cancellationToken))
            {
                await http.Response.WriteAsync($"data: {chunk.ToJsonString()}\n\n", cancellationToken);
                await http.Response.Body.FlushAsync(cancellationToken);
            }
            await http.Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            var body = ErrorResponses.Body(502, "stream_interrupted", "O provedor interrompeu o streaming.");
            await http.Response.WriteAsync($"data: {body.ToJsonString()}\n\n", CancellationToken.None);
        }
    }

    private static void WriteMetadata(HttpContext http, ChatMetadata metadata)
    {
        http.Response.Headers["x-gateway-provider"] = metadata.CacheHit ? "cache" : metadata.Target.Provider;
        http.Response.Headers["x-gateway-model"] = metadata.Target.Model;
        http.Response.Headers["x-gateway-cache"] = metadata.CacheHit ? "hit" : "miss";
        http.Response.Headers["x-gateway-fallbacks"] = metadata.Fallbacks.ToString(CultureInfo.InvariantCulture);
        http.Response.Headers["x-request-id"] = http.TraceIdentifier;
    }

    private static bool HeaderIs(HttpContext http, string name, string value) =>
        string.Equals(http.Request.Headers[name].ToString().Trim(), value, StringComparison.OrdinalIgnoreCase);
}
