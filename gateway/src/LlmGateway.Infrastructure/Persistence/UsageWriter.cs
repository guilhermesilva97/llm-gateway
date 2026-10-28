using System.Threading.Channels;
using LlmGateway.Core.Chat;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LlmGateway.Infrastructure.Persistence;

public sealed class ChannelUsageSink : IUsageSink
{
    private readonly Channel<RequestRecord> _channel = Channel.CreateBounded<RequestRecord>(
        new BoundedChannelOptions(10_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

    public ChannelReader<RequestRecord> Reader => _channel.Reader;

    public void Record(RequestRecord record) => _channel.Writer.TryWrite(record);
}

public sealed class UsageWriter(
    ChannelUsageSink sink,
    IDbContextFactory<GatewayDbContext> contexts,
    ILogger<UsageWriter> logger) : BackgroundService
{
    private const int BatchSize = 200;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<RequestRecord>(BatchSize);
        try
        {
            while (await sink.Reader.WaitToReadAsync(stoppingToken))
            {
                while (batch.Count < BatchSize && sink.Reader.TryRead(out var record))
                {
                    batch.Add(record);
                }
                await PersistAsync(batch, stoppingToken);
                batch.Clear();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Gravação de uso encerrada");
        }
    }

    private async Task PersistAsync(List<RequestRecord> batch, CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await contexts.CreateDbContextAsync(cancellationToken);
            db.RequestLogs.AddRange(batch.Select(ToEntity));
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogError(error, "Falha ao gravar {Count} registros de uso; o lote foi descartado", batch.Count);
        }
    }

    private static RequestLogEntity ToEntity(RequestRecord record) => new()
    {
        RequestId = record.RequestId,
        ClientId = record.ClientId,
        Timestamp = record.Timestamp,
        Alias = record.Alias,
        Provider = record.Provider,
        Model = record.Model,
        Stream = record.Stream,
        PromptTokens = record.PromptTokens,
        CompletionTokens = record.CompletionTokens,
        CostUsd = record.CostUsd,
        LatencyMs = record.LatencyMs,
        Status = record.Status,
        HttpStatus = record.HttpStatus,
        CacheHit = record.CacheHit,
        Fallbacks = record.Fallbacks,
        PiiMasked = record.PiiMasked,
        PromptPreview = record.PromptPreview,
        Error = record.Error is { Length: > 1000 } error ? error[..1000] : record.Error,
    };
}
