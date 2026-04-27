using System.Globalization;
using System.Text;

namespace OvcinaHra.Shared.Ciphers;

public static class CipherTextNormalizer
{
    public static string NormalizeMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "";

        var decomposed = message.Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(decomposed.Length);

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;

            var upper = char.ToUpperInvariant(ch);
            if (upper is >= 'A' and <= 'Z')
                result.Append(upper);
        }

        return result.ToString();
    }

    public static string BuildEncodedPreview(string? message) =>
        $"XOX{NormalizeMessage(message)}XOX";
}
