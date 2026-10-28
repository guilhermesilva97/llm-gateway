namespace LlmGateway.Core.Privacy;

public enum PiiKind
{
    Email,
    Cnpj,
    Cpf,
    Card,
    Phone,
}

public sealed class PiiVault
{
    private readonly Dictionary<string, string> _placeholderByValue = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _valueByPlaceholder = new(StringComparer.Ordinal);
    private readonly Dictionary<PiiKind, int> _counts = [];

    public int Count => _valueByPlaceholder.Count;

    public IReadOnlyDictionary<PiiKind, int> CountsByKind => _counts;

    public string Protect(PiiKind kind, string value)
    {
        if (_placeholderByValue.TryGetValue(value, out var existing))
        {
            return existing;
        }
        var next = _counts.GetValueOrDefault(kind) + 1;
        _counts[kind] = next;
        var placeholder = $"[{Label(kind)}_{next}]";
        _placeholderByValue[value] = placeholder;
        _valueByPlaceholder[placeholder] = value;
        return placeholder;
    }

    public bool TryReveal(string placeholder, out string value) =>
        _valueByPlaceholder.TryGetValue(placeholder, out value!);

    public static string Label(PiiKind kind) => kind switch
    {
        PiiKind.Email => "EMAIL",
        PiiKind.Cnpj => "CNPJ",
        PiiKind.Cpf => "CPF",
        PiiKind.Card => "CARTAO",
        PiiKind.Phone => "TELEFONE",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
