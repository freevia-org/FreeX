using System.Text.RegularExpressions;

namespace FreeX.Core.Commands;

internal static partial class AccessibilityTextRules
{
    private static readonly HashSet<string> GenericHyperlinkDisplayTexts = new(StringComparer.OrdinalIgnoreCase)
    {
        "additional details",
        "additional information",
        "click",
        "click for details",
        "click for more",
        "click for more details",
        "click here",
        "click here for details",
        "click this link",
        "click to download",
        "click to open",
        "click to view",
        "contact us",
        "continue",
        "continue reading",
        "apply now",
        "book now",
        "buy now",
        "claim offer",
        "details",
        "details here",
        "download",
        "download document",
        "download file",
        "download here",
        "download now",
        "find out more",
        "full details",
        "get started",
        "go",
        "go to",
        "here",
        "link",
        "link here",
        "more",
        "more details",
        "more info",
        "more information",
        "open",
        "open details",
        "open document",
        "open file",
        "open here",
        "open link",
        "order now",
        "read details",
        "read more",
        "register",
        "register now",
        "request info",
        "request quote",
        "schedule now",
        "learn details",
        "learn more",
        "learn more here",
        "see details",
        "see here",
        "see more",
        "sign up",
        "sign up now",
        "shop now",
        "source",
        "start now",
        "subscribe",
        "subscribe now",
        "this link",
        "view",
        "view details",
        "view document",
        "view here",
        "view item",
        "view link",
        "view more",
        "view offer",
        "view product",
        "visit",
        "visit link",
        "visit site",
        "visit website",
        "url",
        "website",
        "web page"
    };

    private static readonly HashSet<string> GenericAltTexts = new(StringComparer.OrdinalIgnoreCase)
    {
        "autoshape",
        "arrow",
        "block arrow",
        "callout",
        "connector",
        "curved connector",
        "diagram",
        "drawing",
        "ellipse",
        "flowchart",
        "freeform",
        "graphic",
        "group",
        "icon",
        "image",
        "line",
        "object",
        "oval",
        "picture",
        "pivot table",
        "pivottable",
        "photo",
        "rectangle",
        "screenshot",
        "shape",
        "smartart",
        "straight connector",
        "text",
        "text box",
        "textbox",
        "wordart"
    };

    private static readonly string[] GenericNumberedAltTextPrefixes =
    [
        "autoshape",
        "arrow",
        "block arrow",
        "callout",
        "connector",
        "curved connector",
        "diagram",
        "drawing",
        "ellipse",
        "flowchart",
        "freeform",
        "graphic",
        "group",
        "icon",
        "image",
        "line",
        "object",
        "oval",
        "picture",
        "pivot table",
        "pivottable",
        "photo",
        "rectangle",
        "screenshot",
        "shape",
        "smartart",
        "straight connector",
        "text box",
        "textbox",
        "wordart"
    ];

    private static readonly HashSet<string> GenericChartTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        "chart",
        "chart title",
        "graph",
        "graph title",
        "title"
    };

    private static readonly HashSet<string> GenericChartAxisTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        "axis",
        "axis title",
        "category axis",
        "category axis title",
        "horizontal axis",
        "horizontal axis title",
        "value axis",
        "value axis title",
        "vertical axis",
        "vertical axis title",
        "x axis",
        "x axis title",
        "x-axis",
        "x-axis title",
        "y axis",
        "y axis title",
        "y-axis",
        "y-axis title"
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
            HasGenericNumberedAltText(text) ||
            LooksLikeScreenshotOrPhotoDateDefault(text) ||
            LooksLikeCameraDefaultImageFileName(text) ||
            LooksLikeImageFileName(text);
    }

    public static bool IsDefaultWorksheetName(string name) =>
        name.StartsWith("Sheet", StringComparison.OrdinalIgnoreCase) &&
        IsAllAsciiDigits(name.AsSpan("Sheet".Length));

    private static bool IsAllAsciiDigits(ReadOnlySpan<char> text)
    {
        if (text.Length == 0)
            return false;

        foreach (var c in text)
        {
            if (c < '0' || c > '9')
                return false;
        }

        return true;
    }

    public static bool IsGenericChartTitle(string title)
    {
        var text = NormalizeComparableText(title);
        return GenericChartTitles.Contains(text) ||
            ChartNumberTitleRegex().IsMatch(text);
    }

    public static bool IsGenericChartAxisTitle(string title)
    {
        var text = NormalizeComparableText(title);
        return GenericChartAxisTitles.Contains(text) ||
            ChartAxisNumberTitleRegex().IsMatch(text);
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

    private static bool LooksLikeCameraDefaultImageFileName(string text) =>
        CameraDefaultImageFileNameRegex().IsMatch(text);

    private static bool HasGenericNumberedAltText(string text)
    {
        foreach (var prefix in GenericNumberedAltTextPrefixes)
        {
            if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var suffix = text.AsSpan(prefix.Length);
            if (suffix.Length > 0 &&
                (suffix[0] == ' ' || suffix[0] == '-' || suffix[0] == '_'))
            {
                suffix = suffix[1..];
            }

            if (IsNumberSuffix(suffix))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeScreenshotOrPhotoDateDefault(string text) =>
        ScreenshotOrPhotoDateDefaultRegex().IsMatch(text);

    private static bool IsNumberSuffix(ReadOnlySpan<char> suffix) =>
        suffix.Length > 0 && int.TryParse(suffix, out _);

    [GeneratedRegex(@"(?i)^Chart\s*\d+$")]
    private static partial Regex ChartNumberTitleRegex();

    [GeneratedRegex(@"(?i)^(?:Axis|X\s*Axis|Y\s*Axis|Horizontal\s*Axis|Vertical\s*Axis|Category\s*Axis|Value\s*Axis)\s*\d+$")]
    private static partial Regex ChartAxisNumberTitleRegex();

    [GeneratedRegex(@"(?i)^Column\s*\d+$")]
    private static partial Regex DefaultTableHeaderRegex();

    [GeneratedRegex(@"(?i)^[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}$")]
    private static partial Regex EmailAddressRegex();

    [GeneratedRegex(@"(?i)^[A-Z0-9](?:[A-Z0-9-]{0,61}[A-Z0-9])?(?:\.[A-Z0-9](?:[A-Z0-9-]{0,61}[A-Z0-9])?)*\.[A-Z]{2,}(?:[/:?#][^\s]*)?$")]
    private static partial Regex DomainLikeTextRegex();

    [GeneratedRegex(@"(?i)^[\w .-]+(?:\s+\((?:copy|\d+)\))?\.(?:png|jpe?g|gif|bmp|tiff?|webp)$")]
    private static partial Regex ImageFileNameRegex();

    [GeneratedRegex(@"(?i)^(?:IMG[\s_-]?\d{4,}|DSC[\s_-]?\d{4,}|DSCF\d{4,}|PXL[\s_-]\d{8}[\s_-]\d{6,}(?:[\s_-]\d+)?)$")]
    private static partial Regex CameraDefaultImageFileNameRegex();

    [GeneratedRegex(@"(?i)^(?:Screenshot|Screen\s*Shot|Photo)[\s_-]+(?:\d{4}[-_]\d{2}[-_]\d{2}|\d{8})(?:[\s_-]+(?:at[\s_-]+)?(?:\d{6}|\d{1,2}(?:[._:-]\d{2}){1,2})(?:[\s_-]*(?:AM|PM))?)?(?:\.(?:png|jpe?g|gif|bmp|tiff?|webp))?$")]
    private static partial Regex ScreenshotOrPhotoDateDefaultRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
