using LlmGateway.Core.Clients;

namespace LlmGateway.Api.Http;

public sealed class ClientAuthenticationMiddleware(RequestDelegate next)
{
    private const string ClientItem = "llm-gateway.client";

    public async Task InvokeAsync(HttpContext http, IClientDirectory clients)
    {
        if (!http.Request.Path.StartsWithSegments("/v1"))
        {
            await next(http);
            return;
        }

        var apiKey = ReadApiKey(http.Request);
        if (apiKey is null)
        {
            await ErrorResponses.WriteAsync(http, 401, "missing_api_key",
                "Informe a chave do cliente no cabeçalho Authorization: Bearer <chave>.");
            return;
        }

        var client = await clients.FindByApiKeyAsync(apiKey, http.RequestAborted);
        if (client is null)
        {
            await ErrorResponses.WriteAsync(http, 401, "invalid_api_key", "Chave inválida ou cliente desativado.");
            return;
        }

        http.Items[ClientItem] = client;
        await next(http);
    }

    public static GatewayClient? ClientOf(HttpContext http) => http.Items[ClientItem] as GatewayClient;

    private static string? ReadApiKey(HttpRequest request)
    {
        var authorization = request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = authorization[7..].Trim();
            return token.Length > 0 ? token : null;
        }
        var header = request.Headers["api-key"].ToString();
        return header.Length > 0 ? header : null;
    }
}
