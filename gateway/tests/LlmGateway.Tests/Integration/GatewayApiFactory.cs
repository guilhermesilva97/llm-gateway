using LlmGateway.Core.Chat;
using LlmGateway.Infrastructure.Configuration;
using LlmGateway.Infrastructure.Persistence;
using LlmGateway.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LlmGateway.Tests.Integration;

public sealed class GatewayApiFactory : WebApplicationFactory<Program>
{
    public const string AdminKey = "test-admin-key";
    public const string AppKey = "gw_test_app_key";
    public const string TinyKey = "gw_test_tiny_key";
    public const string RawClientKey = "gw_test_raw_key";

    private readonly string _database = "gateway-tests-" + Guid.NewGuid().ToString("N");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<GatewayOptions>(options =>
            {
                options.AdminKey = AdminKey;
                options.Providers = new Dictionary<string, ProviderOptions>
                {
                    ["mock"] = new() { Type = "mock" },
                    ["offline"] = new()
                    {
                        Type = "openai-compatible",
                        BaseUrl = "http://127.0.0.1:9/v1",
                        RequireApiKey = false,
                        TimeoutSeconds = 2,
                    },
                };
                options.Models =
                [
                    new() { Alias = "mock-1", Targets = ["mock/mock-1"] },
                    new() { Alias = "resilient", Targets = ["offline/llama", "mock/mock-1"] },
                ];
                options.Pricing = [new() { Target = "mock/mock-1", InputPerMillion = 1m, OutputPerMillion = 2m }];
                options.SeedClients =
                [
                    new() { Id = "app", Name = "App", ApiKey = AppKey, RequestsPerMinute = 1000 },
                    new() { Id = "tiny", Name = "Tiny", ApiKey = TinyKey, RequestsPerMinute = 2 },
                    new() { Id = "raw", Name = "Raw", ApiKey = RawClientKey, RequestsPerMinute = 1000, MaskPii = false },
                ];
                options.Resilience = new ResilienceOptions { RetryAttempts = 1, RetryDelayMs = 1 };
            });

            services.RemoveAll<IResponseCache>();
            services.AddSingleton<IResponseCache, InMemoryResponseCache>();
            services.RemoveAll<IQuotaStore>();
            services.AddSingleton<IQuotaStore, InMemoryQuotaStore>();

            services.RemoveAll<DbContextOptions<GatewayDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<GatewayDbContext>>();
            services.AddDbContextFactory<GatewayDbContext>(options => options.UseInMemoryDatabase(_database));
        });
    }
}
