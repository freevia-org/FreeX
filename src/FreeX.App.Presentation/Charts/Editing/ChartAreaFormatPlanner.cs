using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts.Editing;

/// <summary>
/// The chart-area / plot-area fill-and-border state read from a chart and edited back through the Format
/// Chart Area dialog. <c>null</c> fill colors mean "no explicit fill" (use the theme/default); the plot-area
/// border thickness is always carried so a chosen width round-trips.
/// </summary>
public readonly record struct ChartAreaFormatInput(
    CellColor? ChartAreaFillColor,
    CellColor? PlotAreaFillColor,
    CellColor? PlotAreaBorderColor,
    double PlotAreaBorderThickness);

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
    /// <summary>The plot-area border width bounds Core clamps to (see <c>ApplyOptions</c>).</summary>
    public const double MinBorderThickness = 0;
    public const double MaxBorderThickness = 10;

    /// <summary>Reads the chart's current chart-area / plot-area fill-and-border state into the dialog input shape.</summary>
    public static ChartAreaFormatInput Read(ChartModel chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return new ChartAreaFormatInput(
            chart.ChartAreaFillColor,
            chart.PlotAreaFillColor,
            chart.PlotAreaBorderColor,
            chart.PlotAreaBorderThickness);
    }

    /// <summary>
    /// Validates the edited input. The only constrained field is the plot-area border width, which must be
    /// a finite value within the Core-clamped range. Returns null when valid, else an English message.
    /// </summary>
    public static string? Validate(ChartAreaFormatInput input)
    {
        if (!double.IsFinite(input.PlotAreaBorderThickness)
            || input.PlotAreaBorderThickness < MinBorderThickness
            || input.PlotAreaBorderThickness > MaxBorderThickness)
        {
            return $"Enter a plot-area border width between {MinBorderThickness} and {MaxBorderThickness}.";
        }

        return null;
    }

    /// <summary>
    /// Builds the <see cref="ChartLayoutOptions"/> delta for the edited chart-area / plot-area format. Fill
    /// colors are passed through (null leaves the existing fill untouched in Core); the plot-area border
    /// color and width are always set so a cleared/changed border round-trips.
    /// </summary>
    public static ChartLayoutOptions Plan(ChartAreaFormatInput input) =>
        new(
            ChartAreaFillColor: input.ChartAreaFillColor,
            PlotAreaFillColor: input.PlotAreaFillColor,
            PlotAreaBorderColor: input.PlotAreaBorderColor,
            PlotAreaBorderThickness: input.PlotAreaBorderThickness);
}
