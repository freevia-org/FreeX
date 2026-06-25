using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts.Editing;

/// <summary>
/// The chart-area / plot-area fill-and-border state plus the legend state read from a chart and edited
/// back through the Format Chart Area dialog. <c>null</c> fill colors mean "no explicit fill" (use the
/// theme/default); the plot-area border thickness is always carried so a chosen width round-trips. The
/// legend fields mirror the WPF <c>ChartAreaLegendDialog</c> "Legend" group so the dialog reaches full
/// parity with Windows (show/position/overlay + text/fill/border colors, border width, font size).
/// </summary>
public readonly record struct ChartAreaFormatInput(
    CellColor? ChartAreaFillColor,
    CellColor? PlotAreaFillColor,
    CellColor? PlotAreaBorderColor,
    double PlotAreaBorderThickness,
    bool ShowLegend = true,
    ChartLegendPosition LegendPosition = ChartLegendPosition.Right,
    bool LegendOverlay = false,
    CellColor? LegendTextColor = null,
    CellColor? LegendFillColor = null,
    CellColor? LegendBorderColor = null,
    double LegendBorderThickness = 0,
    double LegendFontSize = 12);

/// <summary>
/// Portable (no UI) planner for the "Format Chart Area" dialog: chart-area fill plus plot-area fill and
/// border (color + width). Single-sources the read/validate/project rules and maps an edited
/// <see cref="ChartAreaFormatInput"/> onto the <see cref="ChartLayoutOptions"/> the shell hands to the Core
/// <see cref="SetChartLayoutCommand"/>. Every field here is already represented on
/// <see cref="ChartModel"/> and applied by <c>ApplyOptions</c>, so no Core change is needed. Reused across
/// every shell. (The WPF host's <c>ChartAreaLegendDialog</c> is the behavior reference for the chart/plot
/// area fields.)
/// </summary>
public static class ChartAreaFormatPlanner
{
    /// <summary>The plot-area / legend border width bounds Core clamps to (see <c>ApplyOptions</c>).</summary>
    public const double MinBorderThickness = 0;
    public const double MaxBorderThickness = 10;

    /// <summary>The legend font-size bounds Core clamps to.</summary>
    public const double MinLegendFontSize = 6;
    public const double MaxLegendFontSize = 72;

    /// <summary>
    /// Reads the chart's current chart-area / plot-area fill-and-border state plus the legend state into
    /// the dialog input shape.
    /// </summary>
    public static ChartAreaFormatInput Read(ChartModel chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return new ChartAreaFormatInput(
            chart.ChartAreaFillColor,
            chart.PlotAreaFillColor,
            chart.PlotAreaBorderColor,
            chart.PlotAreaBorderThickness,
            chart.ShowLegend,
            chart.LegendPosition == ChartLegendPosition.None ? ChartLegendPosition.Right : chart.LegendPosition,
            chart.LegendOverlay,
            chart.LegendTextColor,
            chart.LegendFillColor,
            chart.LegendBorderColor,
            chart.LegendBorderThickness,
            chart.LegendFontSize);
    }

    /// <summary>
    /// Validates the edited input: the plot-area border width and legend border width must be finite and
    /// within the Core-clamped range, and the legend font size within its range. Returns null when valid,
    /// else an English message.
    /// </summary>
    public static string? Validate(ChartAreaFormatInput input)
    {
        if (!IsInBorderRange(input.PlotAreaBorderThickness))
            return $"Enter a plot-area border width between {MinBorderThickness} and {MaxBorderThickness}.";

        if (!IsInBorderRange(input.LegendBorderThickness))
            return $"Enter a legend border width between {MinBorderThickness} and {MaxBorderThickness}.";

        if (!double.IsFinite(input.LegendFontSize)
            || input.LegendFontSize < MinLegendFontSize
            || input.LegendFontSize > MaxLegendFontSize)
        {
            return $"Enter a legend font size between {MinLegendFontSize} and {MaxLegendFontSize}.";
        }

        return null;
    }

    private static bool IsInBorderRange(double value) =>
        double.IsFinite(value) && value >= MinBorderThickness && value <= MaxBorderThickness;

    /// <summary>
    /// Builds the <see cref="ChartLayoutOptions"/> delta for the edited chart-area / plot-area format and
    /// legend. Fill colors are passed through (null leaves the existing fill untouched in Core); the
    /// plot-area border color and width are always set so a cleared/changed border round-trips. The legend
    /// fields mirror the WPF <c>ChartAreaLegendDialogResult.ToOptions()</c>.
    /// </summary>
    public static ChartLayoutOptions Plan(ChartAreaFormatInput input) =>
        new(
            ChartAreaFillColor: input.ChartAreaFillColor,
            PlotAreaFillColor: input.PlotAreaFillColor,
            PlotAreaBorderColor: input.PlotAreaBorderColor,
            PlotAreaBorderThickness: input.PlotAreaBorderThickness,
            ShowLegend: input.ShowLegend,
            LegendPosition: input.LegendPosition,
            LegendOverlay: input.LegendOverlay,
            LegendTextColor: input.LegendTextColor,
            LegendFillColor: input.LegendFillColor,
            LegendBorderColor: input.LegendBorderColor,
            LegendBorderThickness: input.LegendBorderThickness,
            LegendFontSize: input.LegendFontSize);
}
