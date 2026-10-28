namespace LlmGateway.Core.Clients;

public interface IClientDirectory
{
    Task<GatewayClient?> FindByApiKeyAsync(string apiKey, CancellationToken cancellationToken);
}
