namespace FreeX.Core.Commands;

internal static class AccessibilityTextRules
{
    private static readonly HashSet<string> GenericHyperlinkDisplayTexts = new(StringComparer.OrdinalIgnoreCase)
    {
        "click here",
        "here",
        "link",
        "more",
        "read more",
        "learn more"
    };

    private static readonly HashSet<string> GenericAltTexts = new(StringComparer.OrdinalIgnoreCase)
    {
        "image",
        "picture",
        "photo",
        "shape",
        "text box",
        "object",
        "graphic"
    };

    public static bool IsDescriptiveHyperlinkText(string displayText, string target)
    {
        var text = displayText.Trim();
        return text.Length > 0 &&
            !GenericHyperlinkDisplayTexts.Contains(text) &&
            !string.Equals(text, target.Trim(), StringComparison.OrdinalIgnoreCase) &&
            !LooksLikeUrl(text);
    }

    public static bool IsGenericAltText(string altText)
    {
        var text = altText.Trim();
        return GenericAltTexts.Contains(text) ||
            text.StartsWith("picture ", StringComparison.OrdinalIgnoreCase) && IsNumberSuffix(text, "picture ") ||
            text.StartsWith("image ", StringComparison.OrdinalIgnoreCase) && IsNumberSuffix(text, "image ") ||
            text.StartsWith("shape ", StringComparison.OrdinalIgnoreCase) && IsNumberSuffix(text, "shape ") ||
            text.StartsWith("text box ", StringComparison.OrdinalIgnoreCase) && IsNumberSuffix(text, "text box ");
    }

    public static bool IsDefaultWorksheetName(string name) =>
        name.StartsWith("Sheet", StringComparison.OrdinalIgnoreCase) &&
        int.TryParse(name["Sheet".Length..], out _);

    public static bool IsGenericChartTitle(string title)
    {
        var text = title.Trim();
        return string.Equals(text, "Chart Title", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "Title", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeUrl(string text) =>
        (Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp ||
             uri.Scheme == Uri.UriSchemeHttps ||
             uri.Scheme == Uri.UriSchemeMailto ||
             uri.Scheme == Uri.UriSchemeFtp)) ||
        text.StartsWith("www.", StringComparison.OrdinalIgnoreCase);

    private static bool IsNumberSuffix(string text, string prefix) =>
        int.TryParse(text[prefix.Length..], out _);
}
