using System.Globalization;

namespace FreeX.App.Presentation.PageLayout;

public enum PrintPreviewSidesMode
{
    OneSided,
    TwoSidedLongEdge,
    TwoSidedShortEdge
}

public enum PrintPreviewPageRangeMode
{
    AllPages,
    CurrentPage,
    Pages
}

public sealed record PrintPreviewChoice<TValue>(
    string Text,
    TValue Value,
    bool IsEnabled = true,
    bool IsPlaceholder = false);

public sealed record PrintPreviewZoomOption(string Text, double? Percent)
{
    public bool FitToWidth => Percent is null;
}

public sealed record PrintPreviewPageRangeChoice(
    PrintPreviewPageRangeMode Mode,
    string Text,
    bool IsChecked);

public sealed record PrintPreviewPageRangeToolbarPlan(
    IReadOnlyList<PrintPreviewPageRangeChoice> Choices,
    string FromPageText,
    string ToPageText,
    string ToSeparatorText,
    bool PageBoxesEnabled);

public readonly record struct PrintPreviewPageRangePlan(int FromPage, int ToPage);

public static class PrintPreviewToolbarStatePlanner
{
    public const int DefaultZoomOptionIndex = 2;

    public static PrintPreviewNavigationState CreateNavigationState(int currentPage, int totalPages) =>
        PrintPreviewNavigationState.Create(currentPage, totalPages);

    public static IReadOnlyList<PrintPreviewChoice<PrintPreviewSidesMode>> CreateSidesOptions(
        PrintSettingsTextResolver? textResolver = null) =>
        [
            new(Get(textResolver, "PrintPreview_SidesOneSided", "Print One Sided"), PrintPreviewSidesMode.OneSided),
            new(Get(textResolver, "PrintPreview_SidesFlipLongEdge", "Print on Both Sides - Flip pages on long edge"), PrintPreviewSidesMode.TwoSidedLongEdge),
            new(Get(textResolver, "PrintPreview_SidesFlipShortEdge", "Print on Both Sides - Flip pages on short edge"), PrintPreviewSidesMode.TwoSidedShortEdge)
        ];

    public static PrintPreviewSidesMode SidesIndexToMode(int selectedIndex) =>
        selectedIndex switch
        {
            1 => PrintPreviewSidesMode.TwoSidedLongEdge,
            2 => PrintPreviewSidesMode.TwoSidedShortEdge,
            _ => PrintPreviewSidesMode.OneSided
        };

    public static int SidesModeToIndex(PrintPreviewSidesMode mode) =>
        mode switch
        {
            PrintPreviewSidesMode.TwoSidedLongEdge => 1,
            PrintPreviewSidesMode.TwoSidedShortEdge => 2,
            _ => 0
        };

    public static IReadOnlyList<PrintPreviewChoice<bool>> CreateCollationOptions(
        PrintSettingsTextResolver? textResolver = null) =>
        [
            new(Get(textResolver, "PrintPreview_CollatedOption", "Collated"), true),
            new(Get(textResolver, "PrintPreview_UncollatedOption", "Uncollated"), false)
        ];

    public static string CreateToolbarCollatedText(PrintSettingsTextResolver? textResolver = null) =>
        StripAccessKeyMarker(Get(textResolver, "PrintPreview_CollatedLabel", "Collated"));

    public static IReadOnlyList<PrintPreviewZoomOption> CreateZoomOptions(
        PrintSettingsTextResolver? textResolver = null) =>
        [
            new("50%", 50),
            new("75%", 75),
            new("100%", 100),
            new("125%", 125),
            new(Get(textResolver, "PrintPreview_ZoomPageWidth", "Page Width"), null)
        ];

    public static PrintPreviewPageRangeToolbarPlan CreatePageRangeToolbarPlan(
        int totalPages,
        PrintSettingsTextResolver? textResolver = null)
    {
        var normalizedTotalPages = Math.Max(1, totalPages);

        return new PrintPreviewPageRangeToolbarPlan(
            [
                new(
                    PrintPreviewPageRangeMode.AllPages,
                    StripAccessKeyMarker(Get(textResolver, "PrintPreview_AllPagesLabel", "All pages")),
                    IsChecked: true),
                new(
                    PrintPreviewPageRangeMode.CurrentPage,
                    StripAccessKeyMarker(Get(textResolver, "PrintPreview_CurrentPageLabel", "Current page")),
                    IsChecked: false),
                new(
                    PrintPreviewPageRangeMode.Pages,
                    StripAccessKeyMarker(Get(textResolver, "PrintPreview_PagesLabel", "Pages")),
                    IsChecked: false)
            ],
            FromPageText: "1",
            ToPageText: normalizedTotalPages.ToString(CultureInfo.InvariantCulture),
            ToSeparatorText: Get(textResolver, "PrintPreview_PageRangeToText", "to"),
            PageBoxesEnabled: false);
    }

    public static PrintPreviewPageRangePlan? ResolvePageRange(
        PrintPreviewPageRangeMode mode,
        int currentPage,
        int? fromPage = null,
        int? toPage = null) =>
        mode switch
        {
            PrintPreviewPageRangeMode.CurrentPage => new PrintPreviewPageRangePlan(currentPage, currentPage),
            PrintPreviewPageRangeMode.Pages when fromPage is { } from && toPage is { } to => new PrintPreviewPageRangePlan(from, to),
            _ => null
        };

    public static string CreateStatusText(string? printerName, int? copies, int totalPages)
    {
        var copyText = copies is { } count
            ? count == 1 ? "1 copy" : $"{count.ToString(CultureInfo.InvariantCulture)} copies"
            : "invalid copies";
        var pages = totalPages == 1
            ? "1 page"
            : $"{totalPages.ToString(CultureInfo.InvariantCulture)} pages";
        var name = string.IsNullOrWhiteSpace(printerName)
            ? "Windows print dialog"
            : printerName;

        return $"Ready: {name}; {copyText}; {pages}";
    }

    private static string StripAccessKeyMarker(string text) =>
        text.Replace("_", string.Empty, StringComparison.Ordinal);

    private static string Get(PrintSettingsTextResolver? textResolver, string key, string fallback) =>
        textResolver?.Get(key, fallback) ?? fallback;
}
