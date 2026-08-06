using System.Globalization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public sealed record ColumnsDialogPreset(string Label, int ColumnCount, bool UsesUnequalWidths);

public sealed record ColumnsDialogInitialState(
    int PresetIndex,
    string CountText,
    string SpacingText,
    bool LineBetween,
    double ContentWidthPt);

public sealed record ColumnsDialogInput(
    int PresetIndex,
    string? CountText,
    string? SpacingText,
    bool LineBetween,
    double ContentWidthPt);

public sealed record ColumnsDialogResult(
    int Count,
    double SpacingPt,
    bool LineBetween,
    IReadOnlyList<double>? WidthsPt);

public static class ColumnsDialogPlanner
{
    public const string Title = "Columns";
    public const string PresetsLabel = "Presets:";
    public const string CountLabel = "Number of columns:";
    public const string SpacingLabel = "Spacing (pt):";
    public const string LineBetweenLabel = "Line between";
    public const string ValidationMessage =
        "Enter 1-12 columns and a non-negative spacing in points.";
    public const string AutomationId = "ColumnsDialog";
    public const string PresetAutomationId = "ColumnsPreset";
    public const string CountAutomationId = "ColumnsCount";
    public const string SpacingAutomationId = "ColumnsSpacing";
    public const string LineBetweenAutomationId = "ColumnsLineBetween";

    public static readonly IReadOnlyList<ColumnsDialogPreset> Presets =
    [
        new("One", 1, UsesUnequalWidths: false),
        new("Two", 2, UsesUnequalWidths: false),
        new("Three", 3, UsesUnequalWidths: false),
        new("Left", 2, UsesUnequalWidths: true),
        new("Right", 2, UsesUnequalWidths: true),
    ];

    private const int LeftPresetIndex = 3;
    private const int RightPresetIndex = 4;
    private const double MinimumContentWidthPt = 72;
    private const double DefaultSpacingPt = 36;
    private const double NarrowColumnPt = 108;
    private const double MinimumWideColumnPt = 36;

    public static ColumnsDialogInitialState BuildInitialState(PageSettings page, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(culture);

        return new ColumnsDialogInitialState(
            PresetIndex: PresetIndexFor(page),
            CountText: FormatPoints(Math.Max(1, page.ColumnCount), culture),
            SpacingText: FormatPoints(page.ColumnSpacingPt, culture),
            LineBetween: page.ColumnsLineBetween,
            ContentWidthPt: ContentWidthFor(page));
    }

    public static int PresetIndexFor(PageSettings page)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (page.ColumnWidthsPt is { Count: 2 } widths)
            return widths[0] < widths[1] ? LeftPresetIndex : RightPresetIndex;

        return Math.Clamp(page.ColumnCount - 1, 0, 2);
    }

    public static int ColumnCountForPreset(int presetIndex) =>
        Presets[Math.Clamp(presetIndex, 0, Presets.Count - 1)].ColumnCount;

    public static bool TryBuildResult(
        ColumnsDialogInput input,
        CultureInfo culture,
        out ColumnsDialogResult? result,
        out string? errorMessage)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(culture);

        result = null;
        errorMessage = null;

        if (!TryParseCount(input.CountText, culture, out var count) ||
            !TryParseSpacing(input.SpacingText, culture, out var spacing))
        {
            errorMessage = ValidationMessage;
            return false;
        }

        var widths = PlanUnequalWidths(input.PresetIndex, input.ContentWidthPt, spacing);
        if (widths is not null)
            count = widths.Count;

        result = new ColumnsDialogResult(count, spacing, input.LineBetween, widths);
        return true;
    }

    public static IReadOnlyList<double>? PlanUnequalWidths(int presetIndex, double contentWidthPt, double spacingPt)
    {
        if (presetIndex is not LeftPresetIndex and not RightPresetIndex)
            return null;

        var contentWidth = Math.Max(MinimumContentWidthPt, contentWidthPt);
        var spacing = spacingPt >= 0 ? spacingPt : DefaultSpacingPt;
        var widePt = Math.Max(MinimumWideColumnPt, contentWidth - spacing - NarrowColumnPt);
        return presetIndex == LeftPresetIndex
            ? [NarrowColumnPt, widePt]
            : [widePt, NarrowColumnPt];
    }

    public static double ContentWidthFor(PageSettings page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return Math.Max(MinimumContentWidthPt, page.WidthPt - page.MarginLeftPt - page.MarginRightPt);
    }

    public static string FormatPoints(double value, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return value.ToString("0.##", culture);
    }

    private static bool TryParseCount(string? text, CultureInfo culture, out int value)
    {
        var t = (text ?? string.Empty).Trim();
        return int.TryParse(t, NumberStyles.Integer, culture, out value) && value is >= 1 and <= 12;
    }

    private static bool TryParseSpacing(string? text, CultureInfo culture, out double value)
    {
        var t = (text ?? string.Empty).Trim();
        return double.TryParse(t, NumberStyles.Float, culture, out value) && value >= 0;
    }
}
