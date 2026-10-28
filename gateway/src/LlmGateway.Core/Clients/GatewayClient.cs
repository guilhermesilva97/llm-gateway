namespace LlmGateway.Core.Clients;

public sealed record GatewayClient(
    string Id,
    string Name,
    int RequestsPerMinute,
    long MonthlyTokenQuota,
    bool MaskPii = true,
    bool Active = true);
