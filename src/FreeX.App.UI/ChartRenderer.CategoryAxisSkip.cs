using OxyPlot;
using OxyPlot.Axes;

using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;

namespace FreeX.App.UI;

/// <summary>
/// R90-render-chart-axis-titles-5-2: Excel's Format Axis &gt; Labels &gt; "Interval between labels"
/// (<c>&lt;c:tickLblSkip&gt;</c>) and "Interval between tick marks" (<c>&lt;c:tickMarkSkip&gt;</c>) were
/// read, stored, and written back correctly (XlsxChartAxisReader / XlsxChartXmlWriter.Axes) but no
/// renderer ever consulted them, so a chart authored with "every 5th label" still drew all of them.
/// The category axes this renderer builds now thin their tick labels and tick marks accordingly.
/// </summary>
public static partial class ChartRenderer
{
    /// <summary>
    /// Category axes that can thin their labels/tick marks. OxyPlot has no built-in skip concept, so
    /// the axes filter their own tick values -- that keeps the skip out of the ~20 axis construction
    /// sites and applies uniformly to whichever axis a chart family put the categories on.
    /// </summary>
    /// <remarks>
    /// OxyPlot draws major gridlines at the same values it draws major tick marks at, so a tick-mark
    /// skip also thins this renderer's category gridlines. The portable layout keeps the two separate
    /// (its gridline pass iterates every tick), so that one nuance differs between the shells.
    /// </remarks>
    private interface ICategorySkipAxis
    {
        int LabelSkipInterval { get; set; }
        int TickMarkSkipInterval { get; set; }
    }

    /// <summary>
    /// Keeps every <paramref name="interval"/>'th entry of <paramref name="values"/>, anchored on the
    /// first, matching Excel (interval 5 keeps the 1st, 6th, 11th ... category).
    /// </summary>
    private static IList<double> FilterToSkipInterval(IList<double> values, int interval)
    {
        if (interval <= 1)
            return values;

        var kept = new List<double>((values.Count / interval) + 1);
        for (var i = 0; i < values.Count; i++)
        {
            if (ChartCategoryAxisSkip.IsShown(i, interval))
                kept.Add(values[i]);
        }

        return kept;
    }

    /// <summary>The <see cref="CategoryAxis"/> used by the bar family (categories on the left).</summary>
    private sealed class SkipAwareCategoryAxis : CategoryAxis, ICategorySkipAxis
    {
        public int LabelSkipInterval { get; set; } = 1;
        public int TickMarkSkipInterval { get; set; } = 1;

        public override void GetTickValues(
            out IList<double> majorLabelValues,
            out IList<double> majorTickValues,
            out IList<double> minorTickValues)
        {
            base.GetTickValues(out majorLabelValues, out majorTickValues, out minorTickValues);
            majorLabelValues = FilterToSkipInterval(majorLabelValues, LabelSkipInterval);
            majorTickValues = FilterToSkipInterval(majorTickValues, TickMarkSkipInterval);
        }
    }

    /// <summary>
    /// The index-valued <see cref="LinearAxis"/> the column/line/area/stock/surface families use for
    /// categories (labels come from a <see cref="Axis.LabelFormatter"/> over the category list).
    /// </summary>
    private sealed class SkipAwareIndexedCategoryAxis : LinearAxis, ICategorySkipAxis
    {
        public int LabelSkipInterval { get; set; } = 1;
        public int TickMarkSkipInterval { get; set; } = 1;

        public override void GetTickValues(
            out IList<double> majorLabelValues,
            out IList<double> majorTickValues,
            out IList<double> minorTickValues)
        {
            base.GetTickValues(out majorLabelValues, out majorTickValues, out minorTickValues);
            // LinearAxis hands back the same list instance for labels and ticks; filter into fresh
            // lists so a label skip cannot silently drop tick marks (or vice versa).
            majorLabelValues = FilterToSkipInterval(majorLabelValues, LabelSkipInterval);
            majorTickValues = FilterToSkipInterval(majorTickValues, TickMarkSkipInterval);
        }
    }

    /// <summary>
    /// Applies <see cref="ChartModel.XAxisLabelSkip"/>/<see cref="ChartModel.XAxisTickMarkSkip"/> to the
    /// chart's category axis. Both live on the X* model fields no matter which side the category axis
    /// ended up on -- XlsxChartAxisReader always writes them there, even for the bar family whose
    /// category axis is vertical -- so this resolves the category axis by kind, not by position.
    /// </summary>
    private static void ApplyCategoryAxisSkip(PlotModel model, ChartModel chart)
    {
        var labelInterval = ChartCategoryAxisSkip.ResolveInterval(chart.XAxisLabelSkip);
        var tickInterval = ChartCategoryAxisSkip.ResolveInterval(chart.XAxisTickMarkSkip);
        if (labelInterval <= 1 && tickInterval <= 1)
            return;

        if (FindCategoryAxis(model) is not { } categoryAxis)
            return;

        categoryAxis.LabelSkipInterval = labelInterval;
        categoryAxis.TickMarkSkipInterval = tickInterval;
    }

    private static ICategorySkipAxis? FindCategoryAxis(PlotModel model)
    {
        // Bar family: categories are a CategoryAxis on the left.
        foreach (var axis in model.Axes)
        {
            if (axis is SkipAwareCategoryAxis categoryAxis)
                return categoryAxis;
        }

        // Everything else: an indexed axis on the bottom. Surface charts additionally build an indexed
        // axis on the LEFT for the series axis (<c:serAx>), which carries its own skip settings in
        // Excel and must not inherit the category axis's -- hence the position filter.
        foreach (var axis in model.Axes)
        {
            if (axis is SkipAwareIndexedCategoryAxis indexed &&
                indexed.Position is AxisPosition.Bottom or AxisPosition.Top)
                return indexed;
        }

        return null;
    }
}
