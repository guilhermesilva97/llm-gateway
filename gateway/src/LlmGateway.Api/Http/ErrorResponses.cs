using System.Text.Json.Nodes;

namespace LlmGateway.Api.Http;

public static class ErrorResponses
{
    public static JsonObject Body(int statusCode, string code, string message) => new()
    {
        ["error"] = new JsonObject
        {
            ["message"] = message,
            ["type"] = TypeFor(statusCode),
            ["code"] = code,
        },
    };

    public static async Task WriteAsync(HttpContext http, int statusCode, string code, string message)
    {
        if (http.Response.HasStarted)
        {
            return;
        }
        http.Response.StatusCode = statusCode;
        http.Response.ContentType = "application/json; charset=utf-8";
        await http.Response.WriteAsync(Body(statusCode, code, message).ToJsonString());
    }

    private static string TypeFor(int statusCode) => statusCode switch
    {
        401 => "authentication_error",
        403 => "permission_error",
        404 => "not_found_error",
        429 => "rate_limit_error",
        >= 500 => "api_error",
        _ => "invalid_request_error",
    };
}
