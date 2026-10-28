using System.Text;
using System.Text.RegularExpressions;

namespace LlmGateway.Core.Privacy;

public static partial class PiiRehydrator
{
    public const int MaxPlaceholderLength = 24;

    public static string Rehydrate(string text, PiiVault vault)
    {
        if (string.IsNullOrEmpty(text) || vault.Count == 0)
        {
            return text;
        }
        return PlaceholderPattern().Replace(text, match => vault.TryReveal(match.Value, out var value) ? value : match.Value);
    }

    [GeneratedRegex(@"\[(?:EMAIL|CNPJ|CPF|CARTAO|TELEFONE)_\d+\]")]
    private static partial Regex PlaceholderPattern();
}

public sealed class StreamingRehydrator(PiiVault vault)
{
    private readonly StringBuilder _pending = new();

    public bool HasPending => _pending.Length > 0;

    public string Push(string delta)
    {
        _pending.Append(delta);
        var buffer = _pending.ToString();
        var open = buffer.LastIndexOf('[');
        var holdFrom = open >= 0
            && buffer.IndexOf(']', open) < 0
            && buffer.Length - open < PiiRehydrator.MaxPlaceholderLength
            ? open
            : buffer.Length;
        _pending.Clear();
        _pending.Append(buffer, holdFrom, buffer.Length - holdFrom);
        return PiiRehydrator.Rehydrate(buffer[..holdFrom], vault);
    }

    public string Flush()
    {
        var rest = _pending.ToString();
        _pending.Clear();
        return PiiRehydrator.Rehydrate(rest, vault);
    }
}
