using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum HyperlinkDialogTargetKind
{
    Url,
    Slide
}

public enum HyperlinkDialogField
{
    None,
    Url,
    Slide
}

public sealed record HyperlinkDialogInitialState(
    HyperlinkDialogTargetKind TargetKind,
    string UrlText,
    string? TargetSlideId,
    string TooltipText);

public sealed record HyperlinkDialogValidationMessage(
    string Caption,
    string Message,
    HyperlinkDialogField FocusField);

public sealed record HyperlinkDialogResultPlan(
    bool ShouldApply,
    Hyperlink? Result,
    HyperlinkDialogValidationMessage? Validation);

public static class HyperlinkDialogPlanner
{
    public const string Caption = "Insert Hyperlink";
    public const string MissingUrlMessage =
        "Please enter a URL (e.g. https://example.com).";
    public const string UnsupportedUrlMessage =
        "Only http, https, and mailto URLs are supported.";
    public const string MissingSlideMessage =
        "Please select a target slide.";

    public static HyperlinkDialogInitialState BuildInitialState(Hyperlink? current)
    {
        if (current is null)
        {
            return new HyperlinkDialogInitialState(
                HyperlinkDialogTargetKind.Url,
                string.Empty,
                null,
                string.Empty);
        }

        return current.IsExternal
            ? new HyperlinkDialogInitialState(
                HyperlinkDialogTargetKind.Url,
                current.Url ?? string.Empty,
                null,
                current.Tooltip ?? string.Empty)
            : new HyperlinkDialogInitialState(
                HyperlinkDialogTargetKind.Slide,
                string.Empty,
                current.TargetSlideId,
                current.Tooltip ?? string.Empty);
    }

    public static HyperlinkDialogResultPlan BuildResult(
        HyperlinkDialogTargetKind targetKind,
        string? urlText,
        string? selectedSlideId,
        string? tooltipText)
    {
        return targetKind switch
        {
            HyperlinkDialogTargetKind.Url => BuildUrlResult(urlText, tooltipText),
            HyperlinkDialogTargetKind.Slide => BuildSlideResult(selectedSlideId, tooltipText),
            _ => throw new ArgumentOutOfRangeException(nameof(targetKind), targetKind, null)
        };
    }

    public static string? NullIfWhiteSpace(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    public static bool IsSupportedExternalUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https" or "mailto";
    }

    private static HyperlinkDialogResultPlan BuildUrlResult(
        string? urlText,
        string? tooltipText)
    {
        var url = urlText?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(url))
        {
            return Invalid(MissingUrlMessage, HyperlinkDialogField.Url);
        }

        if (!IsSupportedExternalUrl(url))
        {
            return Invalid(UnsupportedUrlMessage, HyperlinkDialogField.Url);
        }

        return new HyperlinkDialogResultPlan(
            true,
            new Hyperlink
            {
                Url = url,
                Tooltip = NullIfWhiteSpace(tooltipText)
            },
            null);
    }

    private static HyperlinkDialogResultPlan BuildSlideResult(
        string? selectedSlideId,
        string? tooltipText)
    {
        var slideId = NullIfWhiteSpace(selectedSlideId);
        if (slideId is null)
        {
            return Invalid(MissingSlideMessage, HyperlinkDialogField.Slide);
        }

        return new HyperlinkDialogResultPlan(
            true,
            new Hyperlink
            {
                TargetSlideId = slideId,
                Tooltip = NullIfWhiteSpace(tooltipText)
            },
            null);
    }

    private static HyperlinkDialogResultPlan Invalid(
        string message,
        HyperlinkDialogField focusField)
        => new(
            false,
            null,
            new HyperlinkDialogValidationMessage(Caption, message, focusField));
}
