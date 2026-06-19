using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts.Editing;

/// <summary>The bar/column gap-width and overlap state read from a chart and edited back through the dialog.</summary>
public readonly record struct ChartBarFormatInput(int BarGapWidth, int BarOverlap);

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
        return new ChartBarFormatInput(chart.BarGapWidth ?? 150, chart.BarOverlap ?? 0);
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

    /// <summary>Builds the <see cref="ChartLayoutOptions"/> delta for the edited bar/column format.</summary>
    public static ChartLayoutOptions Plan(ChartBarFormatInput input) =>
        new(
            BarGapWidth: Math.Clamp(input.BarGapWidth, MinGapWidth, MaxGapWidth),
            BarOverlap: Math.Clamp(input.BarOverlap, MinOverlap, MaxOverlap));
}
