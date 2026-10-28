using System.Text.RegularExpressions;

namespace LlmGateway.Core.Privacy;

public sealed partial class PiiMasker
{
    private readonly (PiiKind Kind, Regex Pattern, Func<string, bool> Accept)[] _detectors =
    [
        (PiiKind.Email, EmailPattern(), _ => true),
        (PiiKind.Cnpj, CnpjPattern(), AcceptCnpj),
        (PiiKind.Cpf, CpfPattern(), AcceptCpf),
        (PiiKind.Card, CardPattern(), DocumentValidators.IsValidCard),
        (PiiKind.Phone, PhonePattern(), _ => true),
    ];

    public string Mask(string text, PiiVault vault)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }
        foreach (var (kind, pattern, accept) in _detectors)
        {
            text = pattern.Replace(text, match => accept(match.Value) ? vault.Protect(kind, match.Value) : match.Value);
        }
        return text;
    }

    private static bool AcceptCpf(string value) =>
        value.Contains('.') || value.Contains('-') || DocumentValidators.IsValidCpf(value);

    private static bool AcceptCnpj(string value) =>
        value.Contains('/') || DocumentValidators.IsValidCnpj(value);

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"(?<![\w.])[A-Z0-9]{2}\.?[A-Z0-9]{3}\.?[A-Z0-9]{3}/?[A-Z0-9]{4}-?\d{2}(?![\w])")]
    private static partial Regex CnpjPattern();

    [GeneratedRegex(@"(?<![\w.])\d{3}\.?\d{3}\.?\d{3}-?\d{2}(?![\w])")]
    private static partial Regex CpfPattern();

    [GeneratedRegex(@"(?<![\w])\d(?:[ -]?\d){12,18}(?![\w])")]
    private static partial Regex CardPattern();

    [GeneratedRegex(@"(?<![\w+])(?:\+55\s?\d{2}\s?|(?:\+55\s?)?\(\d{2}\)\s?|\d{2}[\s-])9?\d{4}[\s-]?\d{4}(?![\w])")]
    private static partial Regex PhonePattern();
}
