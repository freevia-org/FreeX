using System.Globalization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public enum PageSetupGeometryMode
{
    PortraitInputSwappedWhenLandscape,
    NormalizeToOrientation
}

public enum PageSetupValidationProfile
{
    UnifiedDialog,
    CompactDialog
}

public readonly record struct PageSetupDialogThickness(
    double Left,
    double Top,
    double Right,
    double Bottom);

public sealed record PageSetupDialogValidationPolicy(
    PageSetupGeometryMode GeometryMode,
    PageSetupValidationProfile ValidationProfile,
    bool UseSelectedPaperPreset,
    string Message);

public sealed record PageSetupDialogPresentationMetrics
{
    public double WindowWidth { get; init; } = 420;
    public double RowInset { get; init; } = 4;
    public double LabelFieldSpacing { get; init; } = 8;
    public double LabelColumnWidth { get; init; } = 93;
    public double NumberBoxMinWidth { get; init; } = 120;
    public double ComboBoxMinWidth { get; init; } = 180;
    public double ActionButtonWidth { get; init; } = 72;
    public double LauncherButtonWidth { get; init; } = 110;
    public double CheckGroupTopSpacing { get; init; } = 8;
    public double LauncherTopSpacing { get; init; } = 10;
    public double LauncherSpacing { get; init; } = 8;
    public PageSetupDialogThickness TabMargin { get; init; } = new(14, 14, 14, 0);
    public PageSetupDialogThickness ActionRowMargin { get; init; } = new(14, 12, 14, 12);
    public PageSetupDialogThickness SecondCheckMargin { get; init; } = new(0, 4, 0, 0);
    public PageSetupDialogThickness TabContentMargin { get; init; } = new(14, 14, 14, 14);
    public PageSetupDialogThickness TabPaneMargin { get; init; } = new(-12, -2, -12, 0);
    public IReadOnlyList<string> TabNames { get; init; } = ["Margins", "Paper", "Layout"];
    public PageSetupDialogValidationPolicy Validation { get; init; } =
        new(
            PageSetupGeometryMode.PortraitInputSwappedWhenLandscape,
            PageSetupValidationProfile.UnifiedDialog,
            UseSelectedPaperPreset: false,
            "Enter non-negative margins/distances and a positive page width and height (in points).");
}

public sealed record PageSetupPaperOption(
    string HostLabel,
    string AvaloniaLabel,
    double WidthPt,
    double HeightPt)
{
    public bool IsCustom => WidthPt <= 0 || HeightPt <= 0;
}

public sealed record PageSetupInitialState(
    string MarginTopText,
    string MarginBottomText,
    string MarginLeftText,
    string MarginRightText,
    string GutterText,
    int OrientationIndex,
    int MultiplePagesIndex,
    string WidthText,
    string HeightText,
    int PaperSizeIndex,
    int SectionStartIndex,
    bool DifferentFirstPage,
    bool DifferentOddEvenPages,
    string HeaderDistanceText,
    string FooterDistanceText,
    int VerticalAlignmentIndex,
    int GutterPositionIndex = 0);

public sealed record PageSetupDialogInput(
    string? MarginTopText,
    string? MarginBottomText,
    string? MarginLeftText,
    string? MarginRightText,
    string? GutterText,
    int OrientationIndex,
    int MultiplePagesIndex,
    string? WidthText,
    string? HeightText,
    int PaperSizeIndex,
    int SectionStartIndex,
    bool DifferentFirstPage,
    bool DifferentOddEvenPages,
    string? HeaderDistanceText,
    string? FooterDistanceText,
    int VerticalAlignmentIndex,
    bool UseSelectedPaperPreset,
    PageSetupGeometryMode GeometryMode,
    PageSetupValidationProfile ValidationProfile,
    int GutterPositionIndex = 0);

public sealed record PageSetupDialogResult(
    double MarginTopPt,
    double MarginBottomPt,
    double MarginLeftPt,
    double MarginRightPt,
    double GutterPt,
    bool Landscape,
    bool MirrorMargins,
    double WidthPt,
    double HeightPt,
    SectionBreakKind SectionStart,
    bool DifferentFirstPage,
    bool DifferentOddEvenPages,
    double HeaderDistancePt,
    double FooterDistancePt,
    PageVerticalAlignment VerticalAlignment,
    bool GutterAtTop = false);

public static class PageSetupDialogPlanner
{
    public static PageSetupDialogPresentationMetrics PresentationMetrics { get; } = new();

    public const string Title = "Page Setup";
    public const string MarginsSectionLabel = "Margins (points)";
    public const string TopMarginLabel = "Top (pt):";
    public const string BottomMarginLabel = "Bottom (pt):";
    public const string LeftMarginLabel = "Left (pt):";
    public const string RightMarginLabel = "Right (pt):";
    public const string GutterLabel = "Gutter (pt):";
    public const string GutterPositionLabel = "Gutter position:";
    public const string OrientationLabel = "Orientation:";
    public const string MultiplePagesLabel = "Multiple pages:";
    public const string ApplyToLabel = "Apply to:";
    public static readonly IReadOnlyList<string> GutterPositionNames = ["Left", "Top"];
    public const string OrientationSectionLabel = "Orientation";
    public const string PaperSizeSectionLabel = "Paper Size";
    public const string PaperSizeLabel = "Paper size:";
    public const string CustomWidthLabel = "Width (pt):";
    public const string CustomHeightLabel = "  Height (pt):";
    public const string SectionStartLabel = "Section start:";
    public const string VerticalAlignmentLabel = "Vertical alignment:";
    public const string HeaderDistanceLabel = "Header from edge (pt):";
    public const string FooterDistanceLabel = "Footer from edge (pt):";
    public const string DifferentFirstPageLabel = "Different first page";
    public const string DifferentOddEvenLabel = "Different odd and even";
    public const string LineNumbersLabel = "Line Numbers\u2026";
    public const string BordersLabel = "Borders\u2026";
    public const string OkButton = "OK";
    public const string CancelButton = "Cancel";
    public const string UnifiedValidationMessage =
        "Enter non-negative margins/distances and a positive page width and height (in points).";

    public static readonly IReadOnlyList<PageSetupPaperOption> HostPaperOptions =
    [
        new("Letter (8.5\" x 11\")", "Letter (8.5 \u00d7 11 in)", 612, 792),
        new("Legal (8.5\" x 14\")", "Legal (8.5 \u00d7 14 in)", 612, 1008),
        new("Tabloid (11\" x 17\")", "Tabloid (11 \u00d7 17 in)", 792, 1224),
        new("A3 (29.7cm x 42cm)", "A3 (297 \u00d7 420 mm)", 841.9, 1190.55),
        new("A4 (21cm x 29.7cm)", "A4 (210 \u00d7 297 mm)", 595.3, 841.9),
        new("A5 (14.8cm x 21cm)", "A5 (148 \u00d7 210 mm)", 419.55, 595.3),
        new("B4 (25cm x 35.3cm)", "B4 (250 \u00d7 353 mm)", 708.7, 1000.65),
        new("B5 (17.6cm x 25cm)", "B5 (176 \u00d7 250 mm)", 498.9, 708.7),
        new("Custom", "Custom", 0, 0),
    ];

    public static readonly IReadOnlyList<PageSetupPaperOption> AvaloniaPaperOptions =
    [
        new("Letter (8.5\" x 11\")", "Letter (8.5 \u00d7 11 in)", 612, 792),
        new("Legal (8.5\" x 14\")", "Legal (8.5 \u00d7 14 in)", 612, 1008),
        new("A4 (21cm x 29.7cm)", "A4 (210 \u00d7 297 mm)", 595.3, 841.9),
        new("A3 (29.7cm x 42cm)", "A3 (297 \u00d7 420 mm)", 841.9, 1190.6),
        new("A5 (14.8cm x 21cm)", "A5 (148 \u00d7 210 mm)", 419.5, 595.3),
        new("Executive (7.25\" x 10.5\")", "Executive (7.25 \u00d7 10.5 in)", 522, 756),
        new("Custom", "Custom", 0, 0),
    ];

    public static readonly IReadOnlyList<string> OrientationNames = ["Portrait", "Landscape"];
    public static readonly IReadOnlyList<string> MultiplePagesNames = ["Normal", "Mirror margins"];
    public static readonly IReadOnlyList<string> ApplyToNames = ["Whole document", "This section"];
    public static readonly IReadOnlyList<string> SectionStartNames = ["Continuous", "New page", "Even page", "Odd page"];
    public static readonly IReadOnlyList<SectionBreakKind> SectionStartValues =
        [SectionBreakKind.Continuous, SectionBreakKind.NextPage, SectionBreakKind.EvenPage, SectionBreakKind.OddPage];
    public static readonly IReadOnlyList<string> VerticalAlignmentNames = ["Top", "Center", "Justified", "Bottom"];
    public static readonly IReadOnlyList<PageVerticalAlignment> VerticalAlignmentValues =
        [PageVerticalAlignment.Top, PageVerticalAlignment.Center, PageVerticalAlignment.Justified, PageVerticalAlignment.Bottom];

    public static PageSetupInitialState BuildInitialState(
        PageSettings page,
        SectionBreakKind sectionStart,
        IReadOnlyList<PageSetupPaperOption> paperOptions,
        PageSetupGeometryMode geometryMode,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(paperOptions);
        ArgumentNullException.ThrowIfNull(culture);

        var (displayWidth, displayHeight) = DisplayGeometry(page, geometryMode);
        return new PageSetupInitialState(
            MarginTopText: FormatPoints(page.MarginTopPt, culture),
            MarginBottomText: FormatPoints(page.MarginBottomPt, culture),
            MarginLeftText: FormatPoints(page.MarginLeftPt, culture),
            MarginRightText: FormatPoints(page.MarginRightPt, culture),
            GutterText: FormatPoints(page.GutterPt, culture),
            OrientationIndex: page.Landscape ? 1 : 0,
            MultiplePagesIndex: page.MirrorMargins ? 1 : 0,
            WidthText: FormatPoints(displayWidth, culture),
            HeightText: FormatPoints(displayHeight, culture),
            PaperSizeIndex: geometryMode == PageSetupGeometryMode.NormalizeToOrientation
                ? PaperIndexForNormalized(paperOptions, displayWidth, displayHeight)
                : PaperIndexFor(paperOptions, displayWidth, displayHeight),
            SectionStartIndex: Math.Max(0, IndexOf(SectionStartValues, sectionStart)),
            DifferentFirstPage: page.DifferentFirstPage,
            DifferentOddEvenPages: page.DifferentOddEvenPages,
            HeaderDistanceText: FormatPoints(page.HeaderDistancePt > 0 ? page.HeaderDistancePt : 36, culture),
            FooterDistanceText: FormatPoints(page.FooterDistancePt > 0 ? page.FooterDistancePt : 36, culture),
            VerticalAlignmentIndex: Math.Max(0, IndexOf(VerticalAlignmentValues, page.VerticalAlignment)),
            GutterPositionIndex: page.GutterAtTop ? 1 : 0);
    }

    public static int PaperIndexFor(
        IReadOnlyList<PageSetupPaperOption> paperOptions,
        double widthPt,
        double heightPt,
        double tolerancePt = 1)
    {
        ArgumentNullException.ThrowIfNull(paperOptions);

        for (var i = 0; i < paperOptions.Count; i++)
        {
            var option = paperOptions[i];
            if (option.IsCustom)
                continue;

            if (Math.Abs(option.WidthPt - widthPt) < tolerancePt &&
                Math.Abs(option.HeightPt - heightPt) < tolerancePt)
            {
                return i;
            }
        }

        return CustomIndex(paperOptions);
    }

    public static int PaperIndexForNormalized(
        IReadOnlyList<PageSetupPaperOption> paperOptions,
        double widthPt,
        double heightPt,
        double tolerancePt = 1.5)
    {
        ArgumentNullException.ThrowIfNull(paperOptions);

        var shortPt = Math.Min(widthPt, heightPt);
        var longPt = Math.Max(widthPt, heightPt);
        for (var i = 0; i < paperOptions.Count; i++)
        {
            var option = paperOptions[i];
            if (option.IsCustom)
                continue;

            if (Math.Abs(Math.Min(option.WidthPt, option.HeightPt) - shortPt) < tolerancePt &&
                Math.Abs(Math.Max(option.WidthPt, option.HeightPt) - longPt) < tolerancePt)
            {
                return i;
            }
        }

        return CustomIndex(paperOptions);
    }

    public static (string WidthText, string HeightText)? ApplyPaperPreset(
        IReadOnlyList<PageSetupPaperOption> paperOptions,
        int selectedIndex,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(paperOptions);
        ArgumentNullException.ThrowIfNull(culture);

        if (selectedIndex < 0 || selectedIndex >= paperOptions.Count)
            return null;

        var option = paperOptions[selectedIndex];
        return option.IsCustom
            ? null
            : (FormatPoints(option.WidthPt, culture), FormatPoints(option.HeightPt, culture));
    }

    public static bool TryBuildResult(
        PageSetupDialogInput input,
        IReadOnlyList<PageSetupPaperOption> paperOptions,
        CultureInfo culture,
        out PageSetupDialogResult? result,
        out string? errorMessage)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(paperOptions);
        ArgumentNullException.ThrowIfNull(culture);

        result = null;
        errorMessage = null;

        if (!TryParseNonNegative(input.MarginTopText, "Top margin", input.ValidationProfile, culture, out var top, out errorMessage) ||
            !TryParseNonNegative(input.MarginBottomText, "Bottom margin", input.ValidationProfile, culture, out var bottom, out errorMessage) ||
            !TryParseNonNegative(input.MarginLeftText, "Left margin", input.ValidationProfile, culture, out var left, out errorMessage) ||
            !TryParseNonNegative(input.MarginRightText, "Right margin", input.ValidationProfile, culture, out var right, out errorMessage) ||
            !TryParseNonNegative(input.GutterText, "Gutter", input.ValidationProfile, culture, out var gutter, out errorMessage) ||
            !TryResolvePaperSize(input, paperOptions, culture, out var width, out var height, out errorMessage) ||
            !TryParseNonNegative(input.HeaderDistanceText, "Header distance", input.ValidationProfile, culture, out var headerDistance, out errorMessage) ||
            !TryParseNonNegative(input.FooterDistanceText, "Footer distance", input.ValidationProfile, culture, out var footerDistance, out errorMessage))
        {
            if (input.ValidationProfile == PageSetupValidationProfile.UnifiedDialog)
                errorMessage = UnifiedValidationMessage;
            return false;
        }

        var landscape = input.OrientationIndex == 1;
        var (storedWidth, storedHeight) = StoreGeometry(width, height, landscape, input.GeometryMode);

        result = new PageSetupDialogResult(
            MarginTopPt: top,
            MarginBottomPt: bottom,
            MarginLeftPt: left,
            MarginRightPt: right,
            GutterPt: gutter,
            Landscape: landscape,
            MirrorMargins: input.MultiplePagesIndex == 1,
            WidthPt: storedWidth,
            HeightPt: storedHeight,
            SectionStart: ValueAtOrDefault(SectionStartValues, input.SectionStartIndex),
            DifferentFirstPage: input.DifferentFirstPage,
            DifferentOddEvenPages: input.DifferentOddEvenPages,
            HeaderDistancePt: headerDistance,
            FooterDistancePt: footerDistance,
            VerticalAlignment: ValueAtOrDefault(VerticalAlignmentValues, input.VerticalAlignmentIndex),
            GutterAtTop: input.GutterPositionIndex == 1);
        return true;
    }

    public static void ApplyToPageSettings(PageSettings page, PageSetupDialogResult result)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(result);

        page.MarginTopPt = result.MarginTopPt;
        page.MarginBottomPt = result.MarginBottomPt;
        page.MarginLeftPt = result.MarginLeftPt;
        page.MarginRightPt = result.MarginRightPt;
        page.GutterPt = result.GutterPt;
        page.GutterAtTop = result.GutterAtTop;
        page.Landscape = result.Landscape;
        page.MirrorMargins = result.MirrorMargins;
        page.WidthPt = result.WidthPt;
        page.HeightPt = result.HeightPt;
        page.DifferentFirstPage = result.DifferentFirstPage;
        page.DifferentOddEvenPages = result.DifferentOddEvenPages;
        page.HeaderDistancePt = result.HeaderDistancePt;
        page.FooterDistancePt = result.FooterDistancePt;
        page.VerticalAlignment = result.VerticalAlignment;
    }

    public static string FormatPoints(double value, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return value.ToString("0.##", culture);
    }

    public static string FormatCompactPoints(double value, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return value == 0 ? "0" : value.ToString("G5", culture);
    }

    public static int CustomIndex(IReadOnlyList<PageSetupPaperOption> paperOptions)
    {
        ArgumentNullException.ThrowIfNull(paperOptions);

        for (var i = 0; i < paperOptions.Count; i++)
        {
            if (paperOptions[i].IsCustom)
                return i;
        }

        return Math.Max(0, paperOptions.Count - 1);
    }

    private static bool TryResolvePaperSize(
        PageSetupDialogInput input,
        IReadOnlyList<PageSetupPaperOption> paperOptions,
        CultureInfo culture,
        out double width,
        out double height,
        out string? errorMessage)
    {
        width = 0;
        height = 0;
        errorMessage = null;

        if (input.UseSelectedPaperPreset &&
            input.PaperSizeIndex >= 0 &&
            input.PaperSizeIndex < paperOptions.Count &&
            !paperOptions[input.PaperSizeIndex].IsCustom)
        {
            var option = paperOptions[input.PaperSizeIndex];
            width = Math.Min(option.WidthPt, option.HeightPt);
            height = Math.Max(option.WidthPt, option.HeightPt);
            return true;
        }

        return TryParsePositive(input.WidthText, "Paper width", input.ValidationProfile, culture, out width, out errorMessage) &&
               TryParsePositive(input.HeightText, "Paper height", input.ValidationProfile, culture, out height, out errorMessage);
    }

    private static bool TryParseNonNegative(
        string? text,
        string field,
        PageSetupValidationProfile profile,
        CultureInfo culture,
        out double value,
        out string? errorMessage)
    {
        value = 0;
        errorMessage = null;
        var t = (text ?? string.Empty).Trim();
        if (profile == PageSetupValidationProfile.CompactDialog && t.Length == 0)
            return true;

        if (!double.TryParse(t, NumberStyles.Float, culture, out value) || value < 0)
        {
            errorMessage = profile == PageSetupValidationProfile.CompactDialog
                ? $"Invalid value for {field}: \"{t}\". Enter a non-negative number."
                : UnifiedValidationMessage;
            return false;
        }

        return true;
    }

    private static bool TryParsePositive(
        string? text,
        string field,
        PageSetupValidationProfile profile,
        CultureInfo culture,
        out double value,
        out string? errorMessage)
    {
        value = 1;
        errorMessage = null;
        var t = (text ?? string.Empty).Trim();
        if (!double.TryParse(t, NumberStyles.Float, culture, out value) || value <= 0)
        {
            errorMessage = profile == PageSetupValidationProfile.CompactDialog
                ? $"Invalid value for {field}: \"{t}\". Enter a positive number."
                : UnifiedValidationMessage;
            return false;
        }

        return true;
    }

    private static (double WidthPt, double HeightPt) DisplayGeometry(PageSettings page, PageSetupGeometryMode geometryMode)
    {
        if (geometryMode == PageSetupGeometryMode.PortraitInputSwappedWhenLandscape && page.Landscape)
            return (page.HeightPt, page.WidthPt);

        return (page.WidthPt, page.HeightPt);
    }

    private static (double WidthPt, double HeightPt) StoreGeometry(
        double widthPt,
        double heightPt,
        bool landscape,
        PageSetupGeometryMode geometryMode)
    {
        if (geometryMode == PageSetupGeometryMode.PortraitInputSwappedWhenLandscape)
            return landscape ? (heightPt, widthPt) : (widthPt, heightPt);

        if (landscape && widthPt < heightPt)
            return (heightPt, widthPt);
        if (!landscape && widthPt > heightPt)
            return (heightPt, widthPt);
        return (widthPt, heightPt);
    }

    private static int IndexOf<T>(IReadOnlyList<T> values, T value)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (EqualityComparer<T>.Default.Equals(values[i], value))
                return i;
        }

        return -1;
    }

    private static T ValueAtOrDefault<T>(IReadOnlyList<T> values, int index) =>
        values[Math.Clamp(index, 0, values.Count - 1)];
}
