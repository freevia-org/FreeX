using FreeX.Core.Commands;
using FreeX.Core.Model;
using System.Globalization;

namespace FreeX.App.Presentation.Charts.Editing;

/// <summary>The bar/column gap-width and overlap state read from a chart and edited back through the dialog.</summary>
public readonly record struct ChartBarFormatInput(int BarGapWidth, int BarOverlap);

public enum ChartBarFormatParseIssue
{
    None,
    GapWidth,
    Overlap
}

/// <summary>
/// Portable (no UI) planner for the "Format Bar/Column" editing dialog: the inter-category gap width and the
/// series overlap. Single-sources the read/validate/project rules and maps an edited
/// <see cref="ChartBarFormatInput"/> onto the <see cref="ChartLayoutOptions"/> the shell hands to the Core
/// <see cref="SetChartLayoutCommand"/>. Both fields already exist on <see cref="ChartModel"/> and are clamped
/// by Core's <c>ApplyOptions</c>, so no Core change is needed. Reused across every shell. (The WPF host's
/// <c>ChartBarFormatDialog</c> is the behavior reference.)
/// </summary>
public static class ChartBarFormatPlanner
{
    /// <summary>The gap-width bounds Core clamps to (percent of bar width).</summary>
    public const int MinGapWidth = 0;
    public const int MaxGapWidth = 500;

    /// <summary>The series-overlap bounds Core clamps to (percent).</summary>
    public const int MinOverlap = -100;
    public const int MaxOverlap = 100;

    /// <summary>True when the chart is a bar/column family that has a gap-width / overlap to format.</summary>
    public static bool Supports(ChartModel chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return ChartTypeSupport.SupportsBarGapWidth(chart.Type);
    }

    /// <summary>Reads the chart's current gap-width / overlap (falling back to Excel's 150 / 0 defaults).</summary>
    public static ChartBarFormatInput Read(ChartModel chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return Normalize(new ChartBarFormatInput(chart.BarGapWidth ?? 150, chart.BarOverlap ?? 0));
    }

    /// <summary>Validates the edited input. Returns null when valid, else an English reason.</summary>
    public static string? Validate(ChartBarFormatInput input)
    {
        if (input.BarGapWidth < MinGapWidth || input.BarGapWidth > MaxGapWidth)
            return $"Enter a gap width between {MinGapWidth} and {MaxGapWidth}.";

        if (input.BarOverlap < MinOverlap || input.BarOverlap > MaxOverlap)
            return $"Enter a series overlap between {MinOverlap} and {MaxOverlap}.";

        return null;
    }

    public static bool TryParseDialogInput(
        string gapWidthText,
        string overlapText,
        out ChartBarFormatInput input,
        out ChartBarFormatParseIssue issue)
    {
        if (!TryParseClampedInt(gapWidthText, MinGapWidth, MaxGapWidth, out var gapWidth))
        {
            input = default;
            issue = ChartBarFormatParseIssue.GapWidth;
            return false;
        }

        if (!TryParseClampedInt(overlapText, MinOverlap, MaxOverlap, out var overlap))
        {
            input = default;
            issue = ChartBarFormatParseIssue.Overlap;
            return false;
        }

        input = new ChartBarFormatInput(gapWidth, overlap);
        issue = ChartBarFormatParseIssue.None;
        return true;
    }

    public static ChartBarFormatInput Normalize(ChartBarFormatInput input) =>
        new(
            Math.Clamp(input.BarGapWidth, MinGapWidth, MaxGapWidth),
            Math.Clamp(input.BarOverlap, MinOverlap, MaxOverlap));

    /// <summary>Builds the <see cref="ChartLayoutOptions"/> delta for the edited bar/column format.</summary>
    public static ChartLayoutOptions Plan(ChartBarFormatInput input)
    {
        var normalized = Normalize(input);
        return new(
            BarGapWidth: normalized.BarGapWidth,
            BarOverlap: normalized.BarOverlap);
    }

    private static bool TryParseClampedInt(string text, int min, int max, out int value) =>
        TryParseInt(text, out value) && value >= min && value <= max;

    private static bool TryParseInt(string text, out int value) =>
        int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out value)
        || int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
}
