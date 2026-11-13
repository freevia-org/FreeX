using System.Text.RegularExpressions;

namespace FreeX.App.Services;

/// <summary>
/// Excel's AutoCorrect "Internet and network paths with hyperlinks": a cell entry that is nothing
/// but a web address, e-mail address or network path becomes a hyperlink as it is typed.
/// </summary>
public static partial class AutoHyperlinkPlanner
{
    /// <summary>
    /// Returns the hyperlink target Excel would create for a typed entry, or <see langword="null"/>
    /// when the entry is not a bare link (it has spaces, is a formula, or is ordinary text).
    /// </summary>
    public static string? TryGetTarget(string typedText)
    {
        if (string.IsNullOrEmpty(typedText) || typedText.Length > 2_000)
            return null;

        var text = typedText.Trim();
        if (text.Length == 0 || text != typedText || text.Any(char.IsWhiteSpace) ||
            text[0] is '=' or '\'' or '+' or '-' or '@')
        {
            return null;
        }

        if (SchemeUrl().IsMatch(text))
            return text;

        if (text.StartsWith("www.", StringComparison.OrdinalIgnoreCase) && text.Length > 4 && text.IndexOf('.', 4) > 4)
            return "http://" + text;

        if (EmailAddress().IsMatch(text))
            return "mailto:" + text;

        if (text.StartsWith(@"\\", StringComparison.Ordinal) && text.Length > 2 && text.IndexOf('\\', 2) > 2)
            return text;

        return null;
    }

    [GeneratedRegex(@"^(https?|ftp|file|mailto|news):\S+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SchemeUrl();

    [GeneratedRegex(@"^[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(\.[A-Za-z0-9\-]+)+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailAddress();
}
