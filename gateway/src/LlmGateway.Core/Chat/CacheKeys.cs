using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace LlmGateway.Core.Chat;

public static class CacheKeys
{
    private static readonly string[] IgnoredFields = ["stream", "stream_options", "user"];

    public static string For(string alias, JsonObject maskedPayload)
    {
        var canonical = (JsonObject)maskedPayload.DeepClone();
        canonical["model"] = alias.ToLowerInvariant();
        foreach (var field in IgnoredFields)
        {
            canonical.Remove(field);
        }
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToJsonString()));
        return Convert.ToHexStringLower(hash);
    }
}
