using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts.Editing;

/// <summary>
/// One editable row in the Combo Chart dialog: a single chart series, the per-series treatment it should
/// receive (clustered/column vs. a line overlay) and whether it plots on the secondary axis. Mirrors the
/// classic Excel "Change Chart Type ▸ Combo" grid, one line per series.
/// </summary>
public sealed record ChartComboSeriesInput(int SeriesIndex, bool AsLine, bool OnSecondaryAxis);

/// <summary>The whole combo edit: one row per series, in series-index order.</summary>
public readonly record struct ChartComboInput(IReadOnlyList<ChartComboSeriesInput> Series);

/// <summary>
/// Portable (no UI) planner for the "Combo Chart" editing dialog. Single-sources the per-series combo rules
/// the classic Excel combo dialog enforces and projects an edited <see cref="ChartComboInput"/> onto the
/// <see cref="ChartLayoutOptions"/> the shell hands to the Core <see cref="SetChartLayoutCommand"/>:
/// <list type="bullet">
///   <item>The first series (index 0) is always the base plot type and can never be a line overlay or
///   moved to the secondary axis — Excel keeps it anchored, and Core's
///   <see cref="ChartLayoutOptions.ComboLineSeriesIndexes"/> / <c>SecondaryAxisSeriesIndexes</c> filters
///   discard index 0 anyway.</item>
///   <item>"Plot as line" maps to <see cref="ChartModel.ComboLineSeriesIndexes"/> +
///   <see cref="ChartModel.UseComboLineForSecondarySeries"/>; "secondary axis" maps to
///   <see cref="ChartModel.SecondaryAxisSeriesIndexes"/> + <see cref="ChartModel.ShowSecondaryAxis"/>.</item>
/// </list>
/// Core already represents per-series line/secondary-axis treatment, so no Core change is needed; this
/// planner just keeps the dialog honest and avoids inventing behavior the renderer cannot paint. Reused
/// across every shell.
/// </summary>
public static class ChartComboPlanner
{
    /// <summary>
    /// True when the chart can host a combo (line) overlay: a column/area family with at least two data
    /// series. The same predicate the WPF host uses before opening the combo dialog.
    /// </summary>
    public static bool SupportsCombo(ChartModel chart) => ChartTypeSupport.SupportsComboLineOverlay(chart);

    /// <summary>The number of data series the combo grid should offer (always at least one row).</summary>
    public static int GetSeriesCount(ChartModel chart) => Math.Max(1, ChartTypeSupport.GetDataSeriesCount(chart));

    /// <summary>
    /// Reads the chart's current per-series combo treatment into the dialog input shape, one row per
    /// series in index order. Series 0 always reads as the base type (never line/secondary).
    /// </summary>
    public static ChartComboInput Read(ChartModel chart)
    {
        var count = GetSeriesCount(chart);
        var lineSet = new HashSet<int>(chart.ComboLineSeriesIndexes);
        var secondarySet = new HashSet<int>(chart.SecondaryAxisSeriesIndexes);

        var rows = new List<ChartComboSeriesInput>(count);
        for (var index = 0; index < count; index++)
        {
            var asLine = index > 0 && lineSet.Contains(index);
            var onSecondary = index > 0 && secondarySet.Contains(index);
            rows.Add(new ChartComboSeriesInput(index, asLine, onSecondary));
        }

        return new ChartComboInput(rows);
    }

    /// <summary>
    /// Builds the <see cref="ChartLayoutOptions"/> delta for the edited combo grid. Series 0 is always
    /// dropped from the line/secondary sets (it is the base plot); the remaining selected indexes are
    /// projected as the line-overlay set and the secondary-axis set. When no series is marked as a line the
    /// overlay flag is cleared so the chart reverts to a plain single-type plot.
    /// </summary>
    public static ChartLayoutOptions Plan(ChartComboInput input)
    {
        var series = input.Series ?? [];
        var lineIndexes = series
            .Where(row => row.SeriesIndex > 0 && row.AsLine)
            .Select(row => row.SeriesIndex)
            .Distinct()
            .Order()
            .ToList();
        var secondaryIndexes = series
            .Where(row => row.SeriesIndex > 0 && row.OnSecondaryAxis)
            .Select(row => row.SeriesIndex)
            .Distinct()
            .Order()
            .ToList();

        return new ChartLayoutOptions(
            UseComboLineForSecondarySeries: lineIndexes.Count > 0,
            ComboLineSeriesIndexes: lineIndexes,
            ShowSecondaryAxis: secondaryIndexes.Count > 0,
            SecondaryAxisSeriesIndexes: secondaryIndexes);
    }
}
