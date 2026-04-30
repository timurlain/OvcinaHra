using System.Globalization;
using System.Text;

namespace OvcinaHra.Api.Services;

public static class ExportFilenameBuilder
{
    public static string BuildExportFilename(
        string exportType,
        string gameName,
        bool includeDate = false,
        string extension = "pdf")
    {
        var date = includeDate ? $"_{DateTime.Today:yyyy-MM-dd}" : string.Empty;
        return $"{SanitizePart(exportType, "Export")}_{SanitizePart(gameName, "Hra")}{date}.{SanitizeExtension(extension)}";
    }

    private static string SanitizePart(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        var previousDash = false;

        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;

            var current = ch switch
            {
                '–' or '—' or '−' => '-',
                'Ł' => 'L',
                'ł' => 'l',
                _ => ch
            };

            if (current is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(current);
                previousDash = false;
                continue;
            }

            if (!previousDash)
            {
                builder.Append('-');
                previousDash = true;
            }
        }

        var result = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(result) ? fallback : result;
    }

    private static string SanitizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return "pdf";

        var sanitized = string.Concat(extension.Trim().TrimStart('.').Where(char.IsLetterOrDigit));
        return string.IsNullOrWhiteSpace(sanitized) ? "pdf" : sanitized.ToLowerInvariant();
    }
}
