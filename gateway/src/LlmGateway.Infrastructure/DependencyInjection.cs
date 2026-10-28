using LlmGateway.Core.Chat;
using LlmGateway.Core.Clients;
using LlmGateway.Core.Pricing;
using LlmGateway.Core.Privacy;
using LlmGateway.Core.Routing;
using LlmGateway.Core.Telemetry;
using LlmGateway.Infrastructure.Administration;
using LlmGateway.Infrastructure.Caching;
using LlmGateway.Infrastructure.Configuration;
using LlmGateway.Infrastructure.Persistence;
using LlmGateway.Infrastructure.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace LlmGateway.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddGatewayInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<GatewayOptions>().Bind(configuration.GetSection(GatewayOptions.Section));
        services.TryAddSingleton(TimeProvider.System);
        services.AddMemoryCache();
        services.AddMetrics();
        services.AddHttpClient(OpenAiCompatibleProvider.HttpClientName);

        services.AddSingleton<GatewayTelemetry>();
        services.AddSingleton<PiiMasker>();
        services.AddSingleton(sp => BuildCatalog(sp.GetRequiredService<IOptions<GatewayOptions>>().Value));
        services.AddSingleton(sp => BuildPrices(sp.GetRequiredService<IOptions<GatewayOptions>>().Value));
        services.AddSingleton(sp => BuildProviders(sp));
        services.AddSingleton(sp => new ResilientProviderExecutor(
            sp.GetRequiredService<IReadOnlyList<IChatProvider>>(),
            sp.GetRequiredService<IOptions<GatewayOptions>>().Value.Resilience,
            sp.GetRequiredService<GatewayTelemetry>(),
            sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IProviderExecutor>(sp => sp.GetRequiredService<ResilientProviderExecutor>());

        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var redis = ConfigurationOptions.Parse(configuration.GetConnectionString("Redis") ?? "localhost:6379");
            redis.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(redis);
        });
        services.AddSingleton<IResponseCache, RedisResponseCache>();
        services.AddSingleton<IQuotaStore, RedisQuotaStore>();

        services.AddDbContextFactory<GatewayDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Postgres")));
        services.AddSingleton<IClientDirectory, ClientDirectory>();
        services.AddSingleton<ChannelUsageSink>();
        services.AddSingleton<IUsageSink>(sp => sp.GetRequiredService<ChannelUsageSink>());
        services.AddHostedService<StorageInitializer>();
        services.AddHostedService<UsageWriter>();

        services.AddSingleton<AdminService>();
        services.AddSingleton<ChatGateway>();
        return services;
    }

    private static ModelCatalog BuildCatalog(GatewayOptions options) =>
        new(options.Models.Select(m => new ModelRoute(m.Alias, [.. m.Targets.Select(RouteTarget.Parse)])));

    private static PriceTable BuildPrices(GatewayOptions options) =>
        new(options.Pricing.ToDictionary(
            p => p.Target,
            p => new ModelPrice(p.InputPerMillion, p.OutputPerMillion),
            StringComparer.OrdinalIgnoreCase));

    private static IReadOnlyList<IChatProvider> BuildProviders(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<GatewayOptions>>().Value;
        var http = services.GetRequiredService<IHttpClientFactory>();
        var time = services.GetRequiredService<TimeProvider>();
        return
        [
            .. options.Providers.Select(entry => entry.Value.Type.ToLowerInvariant() switch
            {
                "mock" => (IChatProvider)new MockChatProvider(entry.Key, entry.Value, time),
                "openai-compatible" => new OpenAiCompatibleProvider(entry.Key, entry.Value, http),
                var type => throw new InvalidOperationException($"Tipo de provedor desconhecido '{type}' em '{entry.Key}'."),
            }),
        ];
    }
}
