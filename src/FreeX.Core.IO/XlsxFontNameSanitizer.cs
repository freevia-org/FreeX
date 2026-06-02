using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxFontNameSanitizer
{
    private const int MaxFontNameLength = 31;
    private const string FallbackFontName = "Calibri";

    public static bool SanitizeValAttribute(XElement? element)
    {
        if (element is null)
            return false;

        var attribute = element.Attribute("val");
        if (attribute is null)
            return false;

        var normalized = NormalizeFontName(attribute.Value);
        if (string.Equals(attribute.Value, normalized, StringComparison.Ordinal))
            return false;

        attribute.Value = normalized;
        return true;
    }

    private static string NormalizeFontName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return FallbackFontName;

        var candidate = FirstFontFamily(value).Trim();
        if (candidate.Length >= 2 &&
            ((candidate[0] == '"' && candidate[^1] == '"') ||
             (candidate[0] == '\'' && candidate[^1] == '\'')))
        {
            candidate = candidate[1..^1].Trim();
        }

        candidate = candidate.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
        if (candidate.Length == 0)
            return FallbackFontName;

        if (candidate.Length > MaxFontNameLength)
            candidate = candidate[..MaxFontNameLength].TrimEnd();

        return candidate.Length == 0 ? FallbackFontName : candidate;
    }

    private static string FirstFontFamily(string value)
    {
        var quote = '\0';
        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            if (quote != '\0')
            {
                if (ch == quote)
                    quote = '\0';
                continue;
            }

            if (ch is '"' or '\'')
            {
                quote = ch;
                continue;
            }

            if (ch == ',')
                return value[..index];
        }

        return value;
    }
}
