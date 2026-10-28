namespace LlmGateway.Core.Privacy;

public static class DocumentValidators
{
    private static readonly int[] CnpjFirstWeights = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
    private static readonly int[] CnpjSecondWeights = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

    public static bool IsValidCpf(string value)
    {
        var digits = value.Where(char.IsAsciiDigit).Select(c => c - '0').ToArray();
        if (digits.Length != 11 || digits.All(d => d == digits[0]))
        {
            return false;
        }
        return CpfDigit(digits, 9) == digits[9] && CpfDigit(digits, 10) == digits[10];
    }

    public static bool IsValidCnpj(string value)
    {
        var chars = value.Where(c => c is not ('.' or '/' or '-')).Select(char.ToUpperInvariant).ToArray();
        if (chars.Length != 14
            || !chars[..12].All(c => char.IsAsciiDigit(c) || char.IsAsciiLetterUpper(c))
            || !chars[12..].All(char.IsAsciiDigit)
            || chars.All(c => c == chars[0]))
        {
            return false;
        }
        var values = chars.Select(c => c - '0').ToArray();
        return CnpjDigit(values, CnpjFirstWeights) == values[12]
            && CnpjDigit(values, CnpjSecondWeights) == values[13];
    }

    public static bool IsValidCard(string value)
    {
        var digits = value.Where(char.IsAsciiDigit).Select(c => c - '0').ToArray();
        if (digits.Length is < 13 or > 19)
        {
            return false;
        }
        var sum = 0;
        for (var i = 0; i < digits.Length; i++)
        {
            var digit = digits[digits.Length - 1 - i];
            if (i % 2 == 1)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }
            sum += digit;
        }
        return sum % 10 == 0;
    }

    private static int CpfDigit(int[] digits, int length)
    {
        var sum = 0;
        for (var i = 0; i < length; i++)
        {
            sum += digits[i] * (length + 1 - i);
        }
        var remainder = sum * 10 % 11;
        return remainder == 10 ? 0 : remainder;
    }

    private static int CnpjDigit(int[] values, int[] weights)
    {
        var sum = 0;
        for (var i = 0; i < weights.Length; i++)
        {
            sum += values[i] * weights[i];
        }
        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }
}
