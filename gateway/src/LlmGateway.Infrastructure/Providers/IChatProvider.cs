using System.Text.Json.Nodes;
using LlmGateway.Core.Chat;

namespace LlmGateway.Infrastructure.Providers;

public interface IChatProvider
{
    string Name { get; }

    string Type { get; }

    bool IsConfigured { get; }

    Task<JsonObject> CompleteAsync(JsonObject request, CancellationToken cancellationToken);

    Task<IChatStream> OpenStreamAsync(JsonObject request, CancellationToken cancellationToken);
}

public sealed class ProviderTransientException(string message, Exception? inner = null) : Exception(message, inner);

internal sealed class ChatStream(IAsyncEnumerable<JsonObject> chunks, IDisposable? owner = null) : IChatStream
{
    public IAsyncEnumerator<JsonObject> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
        chunks.GetAsyncEnumerator(cancellationToken);

    public ValueTask DisposeAsync()
    {
        owner?.Dispose();
        return ValueTask.CompletedTask;
    }
}
