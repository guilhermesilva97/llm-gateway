namespace LlmGateway.Core.Chat;

public sealed class GatewayException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    public string Code { get; } = code;
}

public sealed class ProviderUnavailableException(string provider, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public string Provider { get; } = provider;
}

public sealed class ProviderRejectedException(string provider, int statusCode, string message) : Exception(message)
{
    public string Provider { get; } = provider;

    public int StatusCode { get; } = statusCode;
}
