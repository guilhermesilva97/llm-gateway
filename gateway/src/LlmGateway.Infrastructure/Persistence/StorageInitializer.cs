using LlmGateway.Core.Clients;
using LlmGateway.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LlmGateway.Infrastructure.Persistence;

public sealed class StorageInitializer(
    IDbContextFactory<GatewayDbContext> contexts,
    IOptions<GatewayOptions> options,
    TimeProvider time,
    ILogger<StorageInitializer> logger) : IHostedService
{
    private const int MaxAttempts = 30;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.InitializeStorage)
        {
            return;
        }
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var db = await contexts.CreateDbContextAsync(cancellationToken);
                await db.Database.EnsureCreatedAsync(cancellationToken);
                await SeedAsync(db, cancellationToken);
                return;
            }
            catch (Exception error) when (attempt < MaxAttempts && error is not OperationCanceledException)
            {
                logger.LogWarning("Banco indisponível (tentativa {Attempt}/{Max}): {Message}", attempt, MaxAttempts, error.Message);
                await Task.Delay(TimeSpan.FromSeconds(2), time, cancellationToken);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SeedAsync(GatewayDbContext db, CancellationToken cancellationToken)
    {
        foreach (var seed in options.Value.SeedClients.Where(s => !string.IsNullOrWhiteSpace(s.ApiKey)))
        {
            var hash = ApiKeys.Hash(seed.ApiKey);
            if (await db.Clients.AnyAsync(c => c.Id == seed.Id || c.KeyHash == hash, cancellationToken))
            {
                continue;
            }
            db.Clients.Add(new ClientEntity
            {
                Id = seed.Id,
                Name = seed.Name,
                KeyHash = hash,
                KeyPreview = ApiKeys.Preview(seed.ApiKey),
                RequestsPerMinute = seed.RequestsPerMinute,
                MonthlyTokenQuota = seed.MonthlyTokenQuota,
                MaskPii = seed.MaskPii,
                Active = true,
                CreatedAt = time.GetUtcNow(),
            });
            logger.LogInformation("Cliente {Client} criado a partir da configuração", seed.Id);
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
