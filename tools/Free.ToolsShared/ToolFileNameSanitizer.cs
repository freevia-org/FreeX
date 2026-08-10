using System.Text;

namespace Free.ToolsShared;

public static class ToolFileNameSanitizer
{
    public static string SanitizeSheetToken(string value, string fallback = "sheet")
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character) || character == '-')
                builder.Append(character);
            else if (character is ' ' or '_')
                builder.Append('_');
        }

        return builder.Length > 0 ? builder.ToString() : fallback;
    }

    public static string ReplaceInvalidFileNameChars(string value, bool lowerInvariant = false)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(invalid.Contains(character)
                ? '_'
                : lowerInvariant ? char.ToLowerInvariant(character) : character);
        }

        return builder.ToString();
    }

    public static string ReplaceInvalidFileNameChars(
        string value,
        string fallback,
        bool lowerInvariant = false)
    {
        var sanitized = ReplaceInvalidFileNameChars(value, lowerInvariant);
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }

    public static string ReplaceNonAlphaNumericWithUnderscore(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        return builder.ToString();
    }
}
