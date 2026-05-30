using System.Text.RegularExpressions;

namespace FreeX.Core.Commands;

internal static partial class AccessibilityTextRules
{
    private static readonly HashSet<string> GenericHyperlinkDisplayTexts = new(StringComparer.OrdinalIgnoreCase)
    {
        "click",
        "click here",
        "here",
        "link",
        "more",
        "open link",
        "read more",
        "learn more",
        "url",
        "website",
        "web page"
    };

    private static readonly HashSet<string> GenericAltTexts = new(StringComparer.OrdinalIgnoreCase)
    {
        "diagram",
        "image",
        "picture",
        "photo",
        "screenshot",
        "shape",
        "text box",
        "object",
        "graphic"
    };

    public static bool IsDescriptiveHyperlinkText(string displayText, string target)
    {
        var text = NormalizeComparableText(displayText);
        return text.Length > 0 &&
            !GenericHyperlinkDisplayTexts.Contains(text) &&
            !string.Equals(text, target.Trim(), StringComparison.OrdinalIgnoreCase) &&
            !LooksLikeUrl(text);
    }

    public static bool IsGenericAltText(string altText)
    {
        var text = NormalizeComparableText(altText);
        return GenericAltTexts.Contains(text) ||
            text.StartsWith("picture ", StringComparison.OrdinalIgnoreCase) && IsNumberSuffix(text, "picture ") ||
            text.StartsWith("image ", StringComparison.OrdinalIgnoreCase) && IsNumberSuffix(text, "image ") ||
            text.StartsWith("shape ", StringComparison.OrdinalIgnoreCase) && IsNumberSuffix(text, "shape ") ||
            text.StartsWith("text box ", StringComparison.OrdinalIgnoreCase) && IsNumberSuffix(text, "text box ") ||
            LooksLikeImageFileName(text);
    }

    public static bool IsDefaultWorksheetName(string name) =>
        name.StartsWith("Sheet", StringComparison.OrdinalIgnoreCase) &&
        int.TryParse(name["Sheet".Length..], out _);

    public static bool IsGenericChartTitle(string title)
    {
        var text = NormalizeComparableText(title);
        return string.Equals(text, "Chart Title", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "Title", StringComparison.OrdinalIgnoreCase) ||
            ChartNumberTitleRegex().IsMatch(text);
    }

    public static bool IsDefaultTableHeaderText(string headerText)
    {
        var text = NormalizeComparableText(headerText);
        return string.Equals(text, "Column", StringComparison.OrdinalIgnoreCase) ||
            DefaultTableHeaderRegex().IsMatch(text);
    }

    private static bool LooksLikeUrl(string text) =>
        (Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp ||
             uri.Scheme == Uri.UriSchemeHttps ||
             uri.Scheme == Uri.UriSchemeMailto ||
             uri.Scheme == Uri.UriSchemeFtp)) ||
        text.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ||
        EmailAddressRegex().IsMatch(text) ||
        DomainLikeTextRegex().IsMatch(text);

    private static string NormalizeComparableText(string text)
    {
        var normalized = WhitespaceRegex().Replace(text.Trim(), " ");
        return normalized.Trim(' ', '.', ',', ';', ':', '!', '?', '>', '<', '-', '_', '|');
    }

    private static bool LooksLikeImageFileName(string text) =>
        ImageFileNameRegex().IsMatch(text);

    private static bool IsNumberSuffix(string text, string prefix) =>
        int.TryParse(text[prefix.Length..], out _);

    [GeneratedRegex(@"(?i)^Chart\s*\d+$")]
    private static partial Regex ChartNumberTitleRegex();

    [GeneratedRegex(@"(?i)^Column\s*\d+$")]
    private static partial Regex DefaultTableHeaderRegex();

    [GeneratedRegex(@"(?i)^[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}$")]
    private static partial Regex EmailAddressRegex();

    [GeneratedRegex(@"(?i)^[A-Z0-9](?:[A-Z0-9-]{0,61}[A-Z0-9])?(?:\.[A-Z0-9](?:[A-Z0-9-]{0,61}[A-Z0-9])?)*\.[A-Z]{2,}(?:[/:?#][^\s]*)?$")]
    private static partial Regex DomainLikeTextRegex();

    [GeneratedRegex(@"(?i)^[\w .-]+\.(?:png|jpe?g|gif|bmp|tiff?|webp)$")]
    private static partial Regex ImageFileNameRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
