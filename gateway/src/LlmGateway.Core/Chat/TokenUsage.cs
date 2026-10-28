using System.Text.Json.Nodes;

namespace LlmGateway.Core.Chat;

public readonly record struct TokenUsage(int PromptTokens, int CompletionTokens)
{
    public int TotalTokens => PromptTokens + CompletionTokens;

    public static TokenUsage? FromJson(JsonNode? usage)
    {
        if (usage is not JsonObject json
            || json["prompt_tokens"] is not JsonValue prompt
            || json["completion_tokens"] is not JsonValue completion)
        {
            return null;
        }
        return new TokenUsage(prompt.GetValue<int>(), completion.GetValue<int>());
    }

    public static int Estimate(string text) => string.IsNullOrEmpty(text) ? 0 : (text.Length + 3) / 4;

    public JsonObject ToJson() => new()
    {
        ["prompt_tokens"] = PromptTokens,
        ["completion_tokens"] = CompletionTokens,
        ["total_tokens"] = TotalTokens,
    };
}
