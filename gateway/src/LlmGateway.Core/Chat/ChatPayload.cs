using System.Text;
using System.Text.Json.Nodes;
using LlmGateway.Core.Privacy;

namespace LlmGateway.Core.Chat;

public static class ChatPayload
{
    public static string? Model(this JsonObject payload) =>
        payload["model"] is JsonValue value && value.TryGetValue<string>(out var model) && !string.IsNullOrWhiteSpace(model)
            ? model
            : null;

    public static bool IsStream(this JsonObject payload) =>
        payload["stream"] is JsonValue value && value.TryGetValue<bool>(out var stream) && stream;

    public static bool WantsStreamUsage(this JsonObject payload) =>
        payload["stream_options"]?["include_usage"] is JsonValue value
        && value.TryGetValue<bool>(out var include)
        && include;

    public static JsonObject ForTarget(this JsonObject payload, string model, bool stream)
    {
        var request = (JsonObject)payload.DeepClone();
        request["model"] = model;
        request["stream"] = stream;
        if (stream)
        {
            request["stream_options"] = new JsonObject { ["include_usage"] = true };
        }
        else
        {
            request.Remove("stream_options");
        }
        return request;
    }

    public static void MaskPii(this JsonObject payload, PiiMasker masker, PiiVault vault)
    {
        foreach (var message in Messages(payload))
        {
            MaskContent(message, masker, vault);
            if (message["tool_calls"] is JsonArray calls)
            {
                foreach (var call in calls.OfType<JsonObject>())
                {
                    if (call["function"] is JsonObject function && function["arguments"] is JsonValue arguments
                        && arguments.TryGetValue<string>(out var text))
                    {
                        function["arguments"] = masker.Mask(text, vault);
                    }
                }
            }
        }
    }

    public static string PromptText(JsonObject payload)
    {
        var builder = new StringBuilder();
        foreach (var message in Messages(payload))
        {
            builder.Append(ContentText(message["content"])).Append('\n');
        }
        return builder.ToString();
    }

    public static string LastUserMessage(JsonObject payload) =>
        Messages(payload)
            .LastOrDefault(m => m["role"] is JsonValue role && role.TryGetValue<string>(out var r) && r == "user")
            is { } message
            ? ContentText(message["content"])
            : string.Empty;

    public static string CompletionText(JsonObject response)
    {
        var builder = new StringBuilder();
        if (response["choices"] is JsonArray choices)
        {
            foreach (var choice in choices.OfType<JsonObject>())
            {
                builder.Append(ContentText(choice["message"]?["content"]));
            }
        }
        return builder.ToString();
    }

    public static string DeltaText(JsonObject chunk)
    {
        var builder = new StringBuilder();
        if (chunk["choices"] is JsonArray choices)
        {
            foreach (var choice in choices.OfType<JsonObject>())
            {
                builder.Append(ContentText(choice["delta"]?["content"]));
            }
        }
        return builder.ToString();
    }

    public static void Rehydrate(JsonObject response, PiiVault vault)
    {
        if (vault.Count == 0 || response["choices"] is not JsonArray choices)
        {
            return;
        }
        foreach (var message in choices.OfType<JsonObject>().Select(c => c["message"]).OfType<JsonObject>())
        {
            if (message["content"] is JsonValue content && content.TryGetValue<string>(out var text))
            {
                message["content"] = PiiRehydrator.Rehydrate(text, vault);
            }
            if (message["tool_calls"] is JsonArray calls)
            {
                foreach (var function in calls.OfType<JsonObject>().Select(c => c["function"]).OfType<JsonObject>())
                {
                    if (function["arguments"] is JsonValue arguments && arguments.TryGetValue<string>(out var args))
                    {
                        function["arguments"] = PiiRehydrator.Rehydrate(args, vault);
                    }
                }
            }
        }
    }

    private static IEnumerable<JsonObject> Messages(JsonObject payload) =>
        payload["messages"] is JsonArray messages ? messages.OfType<JsonObject>() : [];

    private static void MaskContent(JsonObject message, PiiMasker masker, PiiVault vault)
    {
        switch (message["content"])
        {
            case JsonValue value when value.TryGetValue<string>(out var text):
                message["content"] = masker.Mask(text, vault);
                break;
            case JsonArray parts:
                foreach (var part in parts.OfType<JsonObject>())
                {
                    if (part["text"] is JsonValue partText && partText.TryGetValue<string>(out var text))
                    {
                        part["text"] = masker.Mask(text, vault);
                    }
                }
                break;
        }
    }

    private static string ContentText(JsonNode? content) => content switch
    {
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        JsonArray parts => string.Join(' ', parts.OfType<JsonObject>()
            .Select(p => p["text"] is JsonValue t && t.TryGetValue<string>(out var s) ? s : string.Empty)),
        _ => string.Empty,
    };
}
