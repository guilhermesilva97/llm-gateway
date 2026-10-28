using System.Globalization;
using System.Text;
using LlmGateway.Core.Chat;
using LlmGateway.Core.Clients;
using LlmGateway.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LlmGateway.Infrastructure.Administration;

public sealed record ClientView(
    string Id,
    string Name,
    string KeyPreview,
    int RequestsPerMinute,
    long MonthlyTokenQuota,
    long TokensUsedThisMonth,
    bool MaskPii,
    bool Active,
    DateTimeOffset CreatedAt);

public sealed record CreatedClient(ClientView Client, string ApiKey);

public sealed record NewClient(string Name, int RequestsPerMinute = 60, long MonthlyTokenQuota = 1_000_000, bool MaskPii = true);

public sealed record ClientPatch(string? Name, int? RequestsPerMinute, long? MonthlyTokenQuota, bool? MaskPii, bool? Active);

public sealed record ClientUsage(
    string ClientId,
    int Requests,
    int Errors,
    int CacheHits,
    long PromptTokens,
    long CompletionTokens,
    decimal CostUsd,
    double AverageLatencyMs,
    int Fallbacks);

public sealed record UsagePoint(DateTimeOffset Hour, int Requests, int Errors, decimal CostUsd);

public sealed record UsageOverview(int Hours, ClientUsage Totals, IReadOnlyList<ClientUsage> Clients, IReadOnlyList<UsagePoint> Timeline);

public sealed record RequestView(
    string RequestId,
    string ClientId,
    DateTimeOffset Timestamp,
    string Alias,
    string? Provider,
    string? Model,
    bool Stream,
    int PromptTokens,
    int CompletionTokens,
    decimal CostUsd,
    double LatencyMs,
    string Status,
    int HttpStatus,
    bool CacheHit,
    int Fallbacks,
    int PiiMasked,
    string PromptPreview,
    string? Error);

public sealed class AdminService(IDbContextFactory<GatewayDbContext> contexts, IQuotaStore quotas, TimeProvider time)
{
    public async Task<IReadOnlyList<ClientView>> ListClientsAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var clients = await db.Clients.AsNoTracking().OrderBy(c => c.Name).ToListAsync(cancellationToken);
        var views = new List<ClientView>(clients.Count);
        foreach (var client in clients)
        {
            views.Add(await ToViewAsync(client, cancellationToken));
        }
        return views;
    }

    public async Task<CreatedClient> CreateClientAsync(NewClient request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new GatewayException(400, "invalid_request", "O nome do cliente é obrigatório.");
        }
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var id = await UniqueIdAsync(db, Slug(request.Name), cancellationToken);
        var apiKey = ApiKeys.Generate();
        var entity = new ClientEntity
        {
            Id = id,
            Name = request.Name.Trim(),
            KeyHash = ApiKeys.Hash(apiKey),
            KeyPreview = ApiKeys.Preview(apiKey),
            RequestsPerMinute = Math.Clamp(request.RequestsPerMinute, 1, 100_000),
            MonthlyTokenQuota = Math.Max(1, request.MonthlyTokenQuota),
            MaskPii = request.MaskPii,
            Active = true,
            CreatedAt = time.GetUtcNow(),
        };
        db.Clients.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return new CreatedClient(await ToViewAsync(entity, cancellationToken), apiKey);
    }

    public async Task<ClientView> UpdateClientAsync(string id, ClientPatch patch, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var entity = await FindAsync(db, id, cancellationToken);
        entity.Name = string.IsNullOrWhiteSpace(patch.Name) ? entity.Name : patch.Name.Trim();
        entity.RequestsPerMinute = patch.RequestsPerMinute is { } rpm ? Math.Clamp(rpm, 1, 100_000) : entity.RequestsPerMinute;
        entity.MonthlyTokenQuota = patch.MonthlyTokenQuota is { } quota ? Math.Max(1, quota) : entity.MonthlyTokenQuota;
        entity.MaskPii = patch.MaskPii ?? entity.MaskPii;
        entity.Active = patch.Active ?? entity.Active;
        await db.SaveChangesAsync(cancellationToken);
        return await ToViewAsync(entity, cancellationToken);
    }

    public async Task<CreatedClient> RotateKeyAsync(string id, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var entity = await FindAsync(db, id, cancellationToken);
        var apiKey = ApiKeys.Generate();
        entity.KeyHash = ApiKeys.Hash(apiKey);
        entity.KeyPreview = ApiKeys.Preview(apiKey);
        await db.SaveChangesAsync(cancellationToken);
        return new CreatedClient(await ToViewAsync(entity, cancellationToken), apiKey);
    }

    public async Task<UsageOverview> OverviewAsync(int hours, CancellationToken cancellationToken)
    {
        hours = Math.Clamp(hours, 1, 24 * 30);
        var since = time.GetUtcNow().AddHours(-hours);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var window = db.RequestLogs.AsNoTracking().Where(r => r.Timestamp >= since);

        var clients = await window
            .GroupBy(r => r.ClientId)
            .Select(g => new ClientUsage(
                g.Key,
                g.Count(),
                g.Count(r => r.HttpStatus >= 400),
                g.Count(r => r.CacheHit),
                g.Sum(r => (long)r.PromptTokens),
                g.Sum(r => (long)r.CompletionTokens),
                g.Sum(r => r.CostUsd),
                g.Average(r => r.LatencyMs),
                g.Sum(r => r.Fallbacks)))
            .ToListAsync(cancellationToken);

        var samples = await window
            .Select(r => new { r.Timestamp, r.HttpStatus, r.CostUsd })
            .ToListAsync(cancellationToken);
        var timeline = samples
            .GroupBy(s => new DateTimeOffset(s.Timestamp.UtcDateTime.Year, s.Timestamp.UtcDateTime.Month, s.Timestamp.UtcDateTime.Day, s.Timestamp.UtcDateTime.Hour, 0, 0, TimeSpan.Zero))
            .OrderBy(g => g.Key)
            .Select(g => new UsagePoint(g.Key, g.Count(), g.Count(s => s.HttpStatus >= 400), g.Sum(s => s.CostUsd)))
            .ToList();

        var totals = new ClientUsage(
            "total",
            clients.Sum(c => c.Requests),
            clients.Sum(c => c.Errors),
            clients.Sum(c => c.CacheHits),
            clients.Sum(c => c.PromptTokens),
            clients.Sum(c => c.CompletionTokens),
            clients.Sum(c => c.CostUsd),
            clients.Count == 0 ? 0 : clients.Sum(c => c.AverageLatencyMs * c.Requests) / Math.Max(1, clients.Sum(c => c.Requests)),
            clients.Sum(c => c.Fallbacks));

        return new UsageOverview(hours, totals, [.. clients.OrderByDescending(c => c.CostUsd)], timeline);
    }

    public async Task<IReadOnlyList<RequestView>> RecentRequestsAsync(int limit, string? clientId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var query = db.RequestLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            query = query.Where(r => r.ClientId == clientId);
        }
        return await query
            .OrderByDescending(r => r.Id)
            .Take(Math.Clamp(limit, 1, 500))
            .Select(r => new RequestView(
                r.RequestId, r.ClientId, r.Timestamp, r.Alias, r.Provider, r.Model, r.Stream,
                r.PromptTokens, r.CompletionTokens, r.CostUsd, r.LatencyMs, r.Status, r.HttpStatus,
                r.CacheHit, r.Fallbacks, r.PiiMasked, r.PromptPreview, r.Error))
            .ToListAsync(cancellationToken);
    }

    private async Task<ClientView> ToViewAsync(ClientEntity entity, CancellationToken cancellationToken) => new(
        entity.Id,
        entity.Name,
        entity.KeyPreview,
        entity.RequestsPerMinute,
        entity.MonthlyTokenQuota,
        await quotas.GetUsedAsync(entity.Id, cancellationToken),
        entity.MaskPii,
        entity.Active,
        entity.CreatedAt);

    private static async Task<ClientEntity> FindAsync(GatewayDbContext db, string id, CancellationToken cancellationToken) =>
        await db.Clients.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
        ?? throw new GatewayException(404, "client_not_found", $"Cliente '{id}' não encontrado.");

    private static async Task<string> UniqueIdAsync(GatewayDbContext db, string baseId, CancellationToken cancellationToken)
    {
        var candidate = baseId;
        for (var suffix = 2; await db.Clients.AnyAsync(c => c.Id == candidate, cancellationToken); suffix++)
        {
            candidate = $"{baseId}-{suffix}";
        }
        return candidate;
    }

    private static string Slug(string name)
    {
        var normalized = name.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }
            builder.Append(char.IsAsciiLetterOrDigit(c) ? c : '-');
        }
        var slug = string.Join('-', builder.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length == 0 ? "cliente" : slug.Length > 48 ? slug[..48] : slug;
    }
}
