using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts.Editing;

/// <summary>
/// The stock-chart up/down-bar and high-low-line state read from a chart and edited back through the dialog.
/// <c>null</c> colors mean "use the default"; the high-low line thickness is always carried so a chosen width
/// round-trips.
/// </summary>
public readonly record struct ChartStockFormatInput(
    int UpDownBarGapWidth,
    CellColor? UpBarFillColor,
    CellColor? UpBarBorderColor,
    CellColor? DownBarFillColor,
    CellColor? DownBarBorderColor,
    CellColor? HighLowLineColor,
    double HighLowLineThickness);

/// <summary>
/// Portable (no UI) planner for the "Format Stock Chart" editing dialog: the up/down-bar gap width, the
/// up-bar and down-bar fill/border colors, and the high-low connector line color/thickness. Single-sources
/// the read/validate/project rules and maps an edited <see cref="ChartStockFormatInput"/> onto the
/// <see cref="ChartLayoutOptions"/> the shell hands to the Core <see cref="SetChartLayoutCommand"/>. Every
/// field already exists on <see cref="ChartModel"/> and is clamped by Core's <c>ApplyOptions</c>, so no Core
/// change is needed. Reused across every shell. (The WPF host's <c>ChartStockFormatDialog</c> is the behavior
/// reference.)
/// </summary>
public static class ChartStockFormatPlanner
{
    public const int MinGapWidth = 0;
    public const int MaxGapWidth = 500;

    public const double MinLineThickness = 0.5;
    public const double MaxLineThickness = 10.0;

    /// <summary>True when the chart is a stock chart that has up/down bars and high-low lines.</summary>
    public static bool Supports(ChartModel chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return chart.Type == ChartType.Stock;
    }

    /// <summary>Reads the chart's current stock formatting into the dialog input shape.</summary>
    public static ChartStockFormatInput Read(ChartModel chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return new ChartStockFormatInput(
            chart.UpDownBarGapWidth ?? 150,
            chart.UpBarFillColor,
            chart.UpBarBorderColor,
            chart.DownBarFillColor,
            chart.DownBarBorderColor,
            chart.HighLowLineColor,
            chart.HighLowLineThickness);
    }

    /// <summary>Validates the edited input. Returns null when valid, else an English reason.</summary>
    public static string? Validate(ChartStockFormatInput input)
    {
        if (input.UpDownBarGapWidth < MinGapWidth || input.UpDownBarGapWidth > MaxGapWidth)
            return $"Enter an up/down-bar gap width between {MinGapWidth} and {MaxGapWidth}.";

        if (!double.IsFinite(input.HighLowLineThickness)
            || input.HighLowLineThickness < MinLineThickness
            || input.HighLowLineThickness > MaxLineThickness)
        {
            return $"Enter a high-low line thickness between {MinLineThickness} and {MaxLineThickness}.";
        }

        return null;
    }

    /// <summary>Builds the <see cref="ChartLayoutOptions"/> delta for the edited stock formatting.</summary>
    public static ChartLayoutOptions Plan(ChartStockFormatInput input) =>
        new(
            UpDownBarGapWidth: Math.Clamp(input.UpDownBarGapWidth, MinGapWidth, MaxGapWidth),
            UpBarFillColor: input.UpBarFillColor,
            UpBarBorderColor: input.UpBarBorderColor,
            DownBarFillColor: input.DownBarFillColor,
            DownBarBorderColor: input.DownBarBorderColor,
            HighLowLineColor: input.HighLowLineColor,
            HighLowLineThickness: Math.Clamp(input.HighLowLineThickness, MinLineThickness, MaxLineThickness));
}
