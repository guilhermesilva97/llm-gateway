namespace LlmGateway.Infrastructure.Configuration;

public sealed class GatewayOptions
{
    public const string Section = "Gateway";

    public string AdminKey { get; set; } = string.Empty;

    public int CacheTtlSeconds { get; set; } = 3600;

    public bool InitializeStorage { get; set; } = true;

    public Dictionary<string, ProviderOptions> Providers { get; set; } = [];

    public List<ModelRouteOptions> Models { get; set; } = [];

    public List<PriceOptions> Pricing { get; set; } = [];

    public List<SeedClientOptions> SeedClients { get; set; } = [];

    public ResilienceOptions Resilience { get; set; } = new();
}

public sealed class ProviderOptions
{
    public string Type { get; set; } = "openai-compatible";

    public string? BaseUrl { get; set; }

    public string? ApiKey { get; set; }

    public bool RequireApiKey { get; set; } = true;

    public int TimeoutSeconds { get; set; } = 60;

    public int LatencyMs { get; set; }

    public double FailureRate { get; set; }
}

public sealed class ModelRouteOptions
{
    public string Alias { get; set; } = string.Empty;

    public List<string> Targets { get; set; } = [];
}

public sealed class PriceOptions
{
    public string Target { get; set; } = string.Empty;

    public decimal InputPerMillion { get; set; }

    public decimal OutputPerMillion { get; set; }
}

public sealed class SeedClientOptions
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public int RequestsPerMinute { get; set; } = 60;

    public long MonthlyTokenQuota { get; set; } = 1_000_000;

    public bool MaskPii { get; set; } = true;
}

public sealed class ResilienceOptions
{
    public int RetryAttempts { get; set; } = 2;

    public int RetryDelayMs { get; set; } = 200;

    public int MinimumThroughput { get; set; } = 4;

    public double FailureRatio { get; set; } = 0.5;

    public int SamplingSeconds { get; set; } = 30;

    public int BreakSeconds { get; set; } = 15;
}
