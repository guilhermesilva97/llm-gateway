using Microsoft.EntityFrameworkCore;

namespace LlmGateway.Infrastructure.Persistence;

public sealed class ClientEntity
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string KeyHash { get; set; } = string.Empty;

    public string KeyPreview { get; set; } = string.Empty;

    public int RequestsPerMinute { get; set; }

    public long MonthlyTokenQuota { get; set; }

    public bool MaskPii { get; set; } = true;

    public bool Active { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class RequestLogEntity
{
    public long Id { get; set; }

    public string RequestId { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public DateTimeOffset Timestamp { get; set; }

    public string Alias { get; set; } = string.Empty;

    public string? Provider { get; set; }

    public string? Model { get; set; }

    public bool Stream { get; set; }

    public int PromptTokens { get; set; }

    public int CompletionTokens { get; set; }

    public decimal CostUsd { get; set; }

    public double LatencyMs { get; set; }

    public string Status { get; set; } = string.Empty;

    public int HttpStatus { get; set; }

    public bool CacheHit { get; set; }

    public int Fallbacks { get; set; }

    public int PiiMasked { get; set; }

    public string PromptPreview { get; set; } = string.Empty;

    public string? Error { get; set; }
}

public sealed class GatewayDbContext(DbContextOptions<GatewayDbContext> options) : DbContext(options)
{
    public DbSet<ClientEntity> Clients => Set<ClientEntity>();

    public DbSet<RequestLogEntity> RequestLogs => Set<RequestLogEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ClientEntity>(client =>
        {
            client.ToTable("clients");
            client.HasKey(c => c.Id);
            client.Property(c => c.Id).HasMaxLength(64);
            client.Property(c => c.Name).HasMaxLength(120);
            client.Property(c => c.KeyHash).HasMaxLength(64);
            client.Property(c => c.KeyPreview).HasMaxLength(16);
            client.HasIndex(c => c.KeyHash).IsUnique();
        });

        modelBuilder.Entity<RequestLogEntity>(log =>
        {
            log.ToTable("request_logs");
            log.HasKey(r => r.Id);
            log.Property(r => r.RequestId).HasMaxLength(64);
            log.Property(r => r.ClientId).HasMaxLength(64);
            log.Property(r => r.Alias).HasMaxLength(120);
            log.Property(r => r.Provider).HasMaxLength(64);
            log.Property(r => r.Model).HasMaxLength(120);
            log.Property(r => r.Status).HasMaxLength(40);
            log.Property(r => r.PromptPreview).HasMaxLength(400);
            log.Property(r => r.Error).HasMaxLength(1000);
            log.Property(r => r.CostUsd).HasPrecision(18, 8);
            log.HasIndex(r => r.Timestamp);
            log.HasIndex(r => new { r.ClientId, r.Timestamp });
        });
    }
}
