using System.Text.Json.Nodes;
using LlmGateway.Core.Privacy;

namespace LlmGateway.Core.Chat;

public sealed class StreamRehydration(PiiVault vault)
{
    private readonly Dictionary<int, StreamingRehydrator> _content = [];
    private readonly Dictionary<(int Choice, int Tool), StreamingRehydrator> _arguments = [];

    public void Apply(JsonObject chunk)
    {
        if (chunk["choices"] is not JsonArray choices)
        {
            return;
        }
        foreach (var choice in choices.OfType<JsonObject>())
        {
            var index = choice["index"]?.GetValue<int>() ?? 0;
            var delta = choice["delta"] as JsonObject;
            if (delta is not null)
            {
                RehydrateContent(index, delta);
                RehydrateArguments(index, delta);
            }
            if (choice["finish_reason"] is JsonValue)
            {
                FlushChoice(index, choice);
            }
        }
    }

    private void RehydrateContent(int index, JsonObject delta)
    {
        if (delta["content"] is JsonValue content && content.TryGetValue<string>(out var text))
        {
            delta["content"] = Rehydrator(_content, index).Push(text);
        }
    }

    private void RehydrateArguments(int index, JsonObject delta)
    {
        if (delta["tool_calls"] is not JsonArray calls)
        {
            return;
        }
        foreach (var call in calls.OfType<JsonObject>())
        {
            var toolIndex = call["index"]?.GetValue<int>() ?? 0;
            if (call["function"] is JsonObject function && function["arguments"] is JsonValue arguments
                && arguments.TryGetValue<string>(out var text))
            {
                function["arguments"] = Rehydrator(_arguments, (index, toolIndex)).Push(text);
            }
        }
    }

    private void FlushChoice(int index, JsonObject choice)
    {
        var delta = choice["delta"] as JsonObject ?? [];
        choice["delta"] = delta;
        if (_content.TryGetValue(index, out var content) && content.HasPending)
        {
            var current = delta["content"] is JsonValue value && value.TryGetValue<string>(out var text) ? text : string.Empty;
            delta["content"] = current + content.Flush();
        }
        foreach (var ((choiceIndex, toolIndex), rehydrator) in _arguments.Where(a => a.Key.Choice == index && a.Value.HasPending))
        {
            var calls = delta["tool_calls"] as JsonArray ?? [];
            delta["tool_calls"] = calls;
            calls.Add(new JsonObject
            {
                ["index"] = toolIndex,
                ["function"] = new JsonObject { ["arguments"] = rehydrator.Flush() },
            });
        }
    }

    private StreamingRehydrator Rehydrator<TKey>(Dictionary<TKey, StreamingRehydrator> map, TKey key)
        where TKey : notnull
    {
        if (!map.TryGetValue(key, out var rehydrator))
        {
            rehydrator = new StreamingRehydrator(vault);
            map[key] = rehydrator;
        }
        return rehydrator;
    }
}
