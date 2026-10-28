using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using LlmGateway.Core.Chat;
using LlmGateway.Infrastructure.Configuration;

namespace LlmGateway.Infrastructure.Providers;

public sealed class MockChatProvider(string name, ProviderOptions options, TimeProvider time) : IChatProvider
{
    private static readonly TimeSpan ChunkInterval = TimeSpan.FromMilliseconds(15);

    public string Name => name;

    public string Type => "mock";

    public bool IsConfigured => true;

    public async Task<JsonObject> CompleteAsync(JsonObject request, CancellationToken cancellationToken)
    {
        await SimulateAsync(cancellationToken);
        var model = ModelOf(request);
        var content = Reply(request, model);
        var usage = UsageFor(request, content);
        return new JsonObject
        {
            ["id"] = IdFor(request),
            ["object"] = "chat.completion",
            ["created"] = time.GetUtcNow().ToUnixTimeSeconds(),
            ["model"] = model,
            ["choices"] = new JsonArray(new JsonObject
            {
                ["index"] = 0,
                ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = content },
                ["finish_reason"] = "stop",
            }),
            ["usage"] = usage.ToJson(),
        };
    }

    public async Task<IChatStream> OpenStreamAsync(JsonObject request, CancellationToken cancellationToken)
    {
        await SimulateAsync(cancellationToken);
        return new ChatStream(StreamAsync(request, CancellationToken.None));
    }

    private async IAsyncEnumerable<JsonObject> StreamAsync(JsonObject request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var model = ModelOf(request);
        var id = IdFor(request);
        var created = time.GetUtcNow().ToUnixTimeSeconds();
        var content = Reply(request, model);
        var pieces = content.Split(' ');

        for (var i = 0; i < pieces.Length; i++)
        {
            var text = i == 0 ? pieces[i] : " " + pieces[i];
            yield return Chunk(id, created, model, new JsonObject { ["content"] = text }, null);
            await Task.Delay(ChunkInterval, time, cancellationToken);
        }
        yield return Chunk(id, created, model, [], "stop");

        if (request.WantsStreamUsage())
        {
            yield return new JsonObject
            {
                ["id"] = id,
                ["object"] = "chat.completion.chunk",
                ["created"] = created,
                ["model"] = model,
                ["choices"] = new JsonArray(),
                ["usage"] = UsageFor(request, content).ToJson(),
            };
        }
    }

    private async Task SimulateAsync(CancellationToken cancellationToken)
    {
        if (options.LatencyMs > 0)
        {
            var jitter = Random.Shared.Next(0, Math.Max(1, options.LatencyMs / 2));
            await Task.Delay(TimeSpan.FromMilliseconds(options.LatencyMs + jitter), time, cancellationToken);
        }
        if (options.FailureRate > 0 && Random.Shared.NextDouble() < options.FailureRate)
        {
            throw new ProviderTransientException($"{name}: falha simulada (taxa configurada de {options.FailureRate:P0}).");
        }
    }

    private static JsonObject Chunk(string id, long created, string model, JsonObject delta, string? finishReason) => new()
    {
        ["id"] = id,
        ["object"] = "chat.completion.chunk",
        ["created"] = created,
        ["model"] = model,
        ["choices"] = new JsonArray(new JsonObject
        {
            ["index"] = 0,
            ["delta"] = delta,
            ["finish_reason"] = finishReason,
        }),
    };

    private static string Reply(JsonObject request, string model)
    {
        var count = request["messages"] is JsonArray messages ? messages.Count : 0;
        var last = ChatPayload.LastUserMessage(request);
        return $"Resposta simulada pelo {model}. Recebi {count} mensagem(ns); a última foi: \"{last}\"";
    }

    private static TokenUsage UsageFor(JsonObject request, string content) =>
        new(TokenUsage.Estimate(ChatPayload.PromptText(request)), TokenUsage.Estimate(content));

    private static string ModelOf(JsonObject request) => request.Model() ?? "mock";

    private static string IdFor(JsonObject request)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(request.ToJsonString()));
        return "chatcmpl-mock-" + Convert.ToHexStringLower(hash)[..16];
    }
}
