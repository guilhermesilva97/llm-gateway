using System.Security.Cryptography;
using System.Text;

namespace LlmGateway.Core.Clients;

public static class ApiKeys
{
    public const string Prefix = "gw_";

    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Prefix + Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static string Hash(string apiKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));

    public static string Preview(string apiKey) =>
        apiKey.Length <= 10 ? apiKey : apiKey[..10] + "…";
}
