using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using LlmGateway.Core.Pricing;
using LlmGateway.Core.Privacy;

namespace LlmGateway.Core.Chat;

public sealed class ChatStreamSession : IAsyncDisposable
{
    private readonly IChatStream _stream;
    private readonly ChatCommand _command;
    private readonly PiiVault _vault;
    private readonly RequestTracker _tracker;
    private readonly PriceTable _prices;
    private readonly IQuotaStore _quotas;

    internal ChatStreamSession(
        IChatStream stream,
        ChatMetadata metadata,
        ChatCommand command,
        PiiVault vault,
        RequestTracker tracker,
        PriceTable prices,
        IQuotaStore quotas)
    {
        _stream = stream;
        Metadata = metadata;
        _command = command;
        _vault = vault;
        _tracker = tracker;
        _prices = prices;
        _quotas = quotas;
    }

    public ChatMetadata Metadata { get; }

    public async IAsyncEnumerable<JsonObject> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var rehydration = _command.RehydratePii && _vault.Count > 0 ? new StreamRehydration(_vault) : null;
        var clientWantsUsage = _command.Payload.WantsStreamUsage();
        var completion = new StringBuilder();
        TokenUsage? reported = null;
        var finished = false;

        try
        {
            await foreach (var chunk in _stream.WithCancellation(cancellationToken))
            {
                reported = TokenUsage.FromJson(chunk["usage"]) ?? reported;
                completion.Append(ChatPayload.DeltaText(chunk));
                rehydration?.Apply(chunk);

                if (!clientWantsUsage)
                {
                    if (chunk["choices"] is JsonArray { Count: 0 })
                    {
                        continue;
                    }
                    chunk.Remove("usage");
                }
                yield return chunk;
            }
            finished = true;
        }
        finally
        {
            await FinishAsync(finished, reported, completion.ToString(), cancellationToken.IsCancellationRequested);
        }
    }

    public ValueTask DisposeAsync() => _stream.DisposeAsync();

    private async Task FinishAsync(bool finished, TokenUsage? reported, string completion, bool cancelled)
    {
        var usage = reported ?? new TokenUsage(
            TokenUsage.Estimate(ChatPayload.PromptText(_command.Payload)),
            TokenUsage.Estimate(completion));
        await _quotas.AddAsync(_command.Client.Id, usage.TotalTokens, CancellationToken.None);

        if (finished)
        {
            _tracker.Succeeded(Metadata.Target, usage, _prices.Cost(Metadata.Target, usage), cacheHit: false);
            return;
        }
        Exception reason = cancelled
            ? new OperationCanceledException("O cliente encerrou a conexão durante o streaming.")
            : new GatewayException(502, "stream_interrupted", "O provedor interrompeu o streaming.");
        _tracker.Failed(reason, Metadata.Target, usage);
    }
}
