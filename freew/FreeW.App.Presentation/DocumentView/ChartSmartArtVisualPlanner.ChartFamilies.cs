using System.Globalization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Scene geometry for the chart families beyond a single clustered plot: stacked / 100% stacked column,
/// bar, line and area; radar; bubble; high-low-close stock; and column + line combinations. Everything is
/// expressed with the existing scene primitives, so both renderers draw these kinds without new code.
/// </summary>
public static partial class ChartSmartArtVisualPlanner
{
    private const double SecondaryAxisLabelWidth = 30;

    /// <summary>
    /// The largest magnitude a chart value is plotted at. A value beyond it (or a non-finite one) would
    /// overflow the axis arithmetic to Infinity, and the axis walk `value += step` would then never end.
    /// </summary>
    private const double LargestPlottableMagnitude = 1e290;

    private static double PlottableValue(double value) =>
        // Accept range; anything outside it (NaN and the infinities included) is pinned or zeroed.
        value >= -LargestPlottableMagnitude && value <= LargestPlottableMagnitude
            ? value
            : double.IsNaN(value) ? 0 : value > 0 ? LargestPlottableMagnitude : -LargestPlottableMagnitude;

    /// <summary>The value axis for a chart, accounting for stacking, the stock price band and secondary series.</summary>
    private static ChartValueAxisPlan BuildValueAxisPlan(Chart chart, IReadOnlyList<ChartSeriesVisualPlan> series)
    {
        switch (chart.EffectiveGrouping)
        {
            case ChartGrouping.PercentStacked:
                return new ChartValueAxisPlan(series.Any(item => item.Values.Any(value => value < 0)) ? -100 : 0, 100);
            case ChartGrouping.Stacked:
            {
                var count = series.Select(item => item.Values.Count).DefaultIfEmpty().Max();
                var positive = new double[count];
                var negative = new double[count];
                foreach (var item in series)
                {
                    for (var index = 0; index < item.Values.Count; index++)
                    {
                        var value = item.Values[index];
                        if (!double.IsFinite(value))
                            continue;
                        if (value >= 0)
                            positive[index] += value;
                        else
                            negative[index] += value;
                    }
                }

                return ChartValueAxisPlan.FromSeries(
                [
                    new ChartSeriesVisualPlan(string.Empty, positive),
                    new ChartSeriesVisualPlan(string.Empty, negative),
                ]);
            }
        }

        if (ChartKindNormalizer.IsStockShaped(chart))
        {
            // Prices rarely sit near zero: fit the axis to the traded band instead of anchoring it at 0.
            var values = series.SelectMany(item => item.Values).Where(double.IsFinite).ToList();
            // ...but only while the band is wide enough for the axis walk (`value += step`) to advance;
            // a band that is negligible next to the price level falls back to the zero-based axis.
            if (values.Count > 0 && values.Min() > 0 && values.Max() - values.Min() > values.Max() * 1e-6)
            {
                var (minimum, maximum) = (values.Min(), values.Max());
                var step = new ChartValueAxisPlan(minimum, maximum).MajorUnit;
                minimum = Math.Floor(minimum / step) * step;
                maximum = Math.Ceiling(maximum / step) * step;
                if (maximum > minimum && double.IsFinite(maximum) && minimum + step > minimum)
                    return new ChartValueAxisPlan(minimum, maximum);
            }
        }

        if (chart.Kind == ChartKind.Combo)
        {
            var primary = series.Where((_, index) => !IsSecondarySeries(chart, index)).ToList();
            if (primary.Count > 0)
                return ChartValueAxisPlan.FromSeries(primary);
        }

        return ChartValueAxisPlan.FromSeries(series);
    }

    private static bool IsSecondarySeries(Chart chart, int index) =>
        chart.Kind == ChartKind.Combo
        && index < chart.Series.Count
        && chart.Series[index] is { PlotAsLine: true, UseSecondaryAxis: true };

    private static bool HasSecondaryAxis(Chart chart) =>
        chart.Kind == ChartKind.Combo
        && chart.Series.Any(series => series is { PlotAsLine: true, UseSecondaryAxis: true })
        && chart.Series.Any(series => !series.PlotAsLine);

    /// <summary>Extra room to the right of the plot for the secondary value axis labels.</summary>
    private static double FamilyRightMargin(Chart chart) => HasSecondaryAxis(chart) ? SecondaryAxisLabelWidth : 0;

    private static bool UsesCartesianValueAxis(Chart chart) =>
        chart.Kind is not (ChartKind.Pie or ChartKind.Doughnut or ChartKind.Radar);

    private static string FormatValueAxisLabel(Chart chart, double value) =>
        value.ToString("G3", CultureInfo.InvariantCulture)
        + (chart.EffectiveGrouping == ChartGrouping.PercentStacked ? "%" : string.Empty);

    /// <summary>
    /// Builds the plot geometry for the families handled here. Returns false for the classic clustered /
    /// unstacked kinds, which the original planner code keeps drawing exactly as before.
    /// </summary>
    private static bool TryBuildFamilyGeometry(
        Chart chart,
        ChartVisualPlan plan,
        ChartSceneRect plot,
        IReadOnlyList<string> palette,
        int categoryCount,
        string axisStroke,
        List<ChartSceneLine> gridLines,
        List<ChartSceneLine> axisLines,
        List<ChartSceneBar> bars,
        List<ChartSceneLineSeries> lineSeries,
        List<ChartSceneMarker> markers,
        List<ChartSceneText> texts)
    {
        var axis = plan.ValueAxis;
        switch (chart.Kind)
        {
            case ChartKind.Radar:
                BuildRadar(chart, plan, plot, palette, categoryCount, gridLines, lineSeries, markers, texts);
                return true;
            case ChartKind.Bubble:
                BuildBubble(chart, plan, plot, palette, axisStroke, axisLines, markers, texts);
                return true;
            case ChartKind.Stock:
                if (ChartKindNormalizer.IsStockShaped(chart))
                    BuildStock(chart, plan, plot, palette, categoryCount, axisLines, markers, texts);
                else
                    BuildLines(chart, plan, plot, palette, categoryCount, Enumerable.Range(0, plan.Series.Count).ToList(), axis, centred: false, lineSeries, markers, texts);
                return true;
            case ChartKind.Combo:
                BuildCombo(chart, plan, plot, palette, categoryCount, bars, lineSeries, markers, texts);
                return true;
        }

        if (chart.EffectiveGrouping == ChartGrouping.Standard)
            return false;

        var stacked = StackSeries(plan.Series, categoryCount, chart.EffectiveGrouping == ChartGrouping.PercentStacked);
        if (chart.Kind is ChartKind.Column or ChartKind.Bar)
            BuildStackedBars(chart, plan, plot, palette, categoryCount, stacked, horizontal: chart.Kind == ChartKind.Bar, bars, texts);
        else
            BuildStackedLines(chart, plan, plot, palette, categoryCount, stacked, fillArea: chart.Kind == ChartKind.Area, lineSeries, markers, texts);
        return true;
    }

    /// <summary>
    /// Per series and category, the [from, to] span of the stacked segment. Positive values stack upwards
    /// from zero, negative values downwards; with <paramref name="percent"/> each category is scaled so
    /// the magnitudes total 100.
    /// </summary>
    public static (double From, double To, double Value)[][] StackSeries(
        IReadOnlyList<ChartSeriesVisualPlan> series,
        int categoryCount,
        bool percent)
    {
        var count = Math.Max(categoryCount, series.Select(item => item.Values.Count).DefaultIfEmpty().Max());
        var result = new (double From, double To, double Value)[series.Count][];
        for (var s = 0; s < series.Count; s++)
            result[s] = new (double, double, double)[count];

        for (var category = 0; category < count; category++)
        {
            var total = 0.0;
            if (percent)
            {
                foreach (var item in series)
                {
                    if (category < item.Values.Count && double.IsFinite(item.Values[category]))
                        total += Math.Abs(item.Values[category]);
                }
            }

            var positive = 0.0;
            var negative = 0.0;
            for (var s = 0; s < series.Count; s++)
            {
                var raw = category < series[s].Values.Count && double.IsFinite(series[s].Values[category])
                    ? series[s].Values[category]
                    : 0;
                var value = percent ? total > 0 ? raw / total * 100 : 0 : raw;
                if (value >= 0)
                {
                    result[s][category] = (positive, positive + value, value);
                    positive += value;
                }
                else
                {
                    result[s][category] = (negative, negative + value, value);
                    negative += value;
                }
            }
        }

        return result;
    }

    private static void BuildStackedBars(
        Chart chart,
        ChartVisualPlan plan,
        ChartSceneRect plot,
        IReadOnlyList<string> palette,
        int categoryCount,
        (double From, double To, double Value)[][] stacked,
        bool horizontal,
        List<ChartSceneBar> bars,
        List<ChartSceneText> texts)
    {
        var axis = plan.ValueAxis;
        var cats = Math.Max(1, categoryCount);
        var percent = chart.EffectiveGrouping == ChartGrouping.PercentStacked;
        var slot = (horizontal ? plot.Height : plot.Width) / cats;
        var pad = Math.Max(1, slot * (horizontal ? 0.15 : 0.3));
        var thickness = Math.Max(1, slot - 2 * pad);
        for (var category = 0; category < cats; category++)
        {
            for (var s = 0; s < stacked.Length; s++)
            {
                if (category >= stacked[s].Length)
                    continue;
                var (from, to, value) = stacked[s][category];
                if (value == 0)
                    continue;
                var low = axis.ValueFraction(Math.Min(from, to));
                var high = axis.ValueFraction(Math.Max(from, to));
                ChartSceneRect bounds;
                if (horizontal)
                {
                    bounds = new ChartSceneRect(
                        plot.X + low * plot.Width,
                        plot.Y + category * slot + pad,
                        Math.Max(1, (high - low) * plot.Width),
                        thickness);
                }
                else
                {
                    bounds = new ChartSceneRect(
                        plot.X + category * slot + pad,
                        plot.Bottom - high * plot.Height,
                        thickness,
                        Math.Max(1, (high - low) * plot.Height));
                }

                bars.Add(new ChartSceneBar(bounds, palette[s % palette.Count]));
                if (plan.ShowDataLabels)
                {
                    texts.Add(new ChartSceneText(
                        FormatChartValue(Math.Round(value, 1)) + (percent ? "%" : string.Empty),
                        bounds.CenterX,
                        bounds.CenterY,
                        ChartSceneTextAnchor.Center,
                        ChartSceneTextKind.DataLabel,
                        "#000000",
                        8));
                }
            }

            if (horizontal)
                AddCategoryText(texts, chart, category, plot.X - 2, plot.Y + category * slot + slot / 2, ChartSceneTextAnchor.CenterRight);
            else
                AddCategoryText(texts, chart, category, plot.X + category * slot + slot / 2, plot.Bottom + 2, ChartSceneTextAnchor.TopCenter);
        }
    }

    private static void BuildStackedLines(
        Chart chart,
        ChartVisualPlan plan,
        ChartSceneRect plot,
        IReadOnlyList<string> palette,
        int categoryCount,
        (double From, double To, double Value)[][] stacked,
        bool fillArea,
        List<ChartSceneLineSeries> lineSeries,
        List<ChartSceneMarker> markers,
        List<ChartSceneText> texts)
    {
        var axis = plan.ValueAxis;
        var cats = Math.Max(2, categoryCount);
        var zeroY = plot.Bottom - Math.Clamp(axis.ZeroFraction, 0, 1) * plot.Height;
        // Areas are painted top layer first so each lower layer covers the part of the one above it.
        var order = Enumerable.Range(0, stacked.Length);
        foreach (var s in fillArea ? order.Reverse() : order)
        {
            var points = new List<(double X, double Y)>();
            var valueCount = s < plan.Series.Count ? plan.Series[s].Values.Count : 0;
            for (var category = 0; category < Math.Min(valueCount, stacked[s].Length); category++)
            {
                var x = plot.X + category * plot.Width / Math.Max(1, cats - 1);
                var y = plot.Bottom - axis.ValueFraction(stacked[s][category].To) * plot.Height;
                points.Add((x, y));
                if (plan.ShowMarkers && !fillArea)
                    markers.Add(new ChartSceneMarker(x, y, 3, ChartSceneMarkerKind.Circle, palette[s % palette.Count]));
                if (plan.ShowDataLabels)
                    AddDataText(texts, Math.Round(stacked[s][category].Value, 1), x + 2, y - 12);
            }

            if (points.Count > 0)
                lineSeries.Add(new ChartSceneLineSeries(points, palette[s % palette.Count], 2, fillArea, zeroY, AreaOpacity: fillArea ? 1 : 0.33));
        }

        AddCategoryLabels(texts, chart, plot, []);
    }

    private static void BuildLines(
        Chart chart,
        ChartVisualPlan plan,
        ChartSceneRect plot,
        IReadOnlyList<string> palette,
        int categoryCount,
        IReadOnlyList<int> seriesIndexes,
        ChartValueAxisPlan axis,
        bool centred,
        List<ChartSceneLineSeries> lineSeries,
        List<ChartSceneMarker> markers,
        List<ChartSceneText> texts)
    {
        var cats = Math.Max(centred ? 1 : 2, categoryCount);
        var zeroY = plot.Bottom - Math.Clamp(axis.ZeroFraction, 0, 1) * plot.Height;
        foreach (var s in seriesIndexes)
        {
            var points = new List<(double X, double Y)>();
            for (var category = 0; category < plan.Series[s].Values.Count; category++)
            {
                var x = centred
                    ? plot.X + (category + 0.5) * plot.Width / cats
                    : plot.X + category * plot.Width / Math.Max(1, cats - 1);
                var y = plot.Bottom - Math.Clamp(axis.ValueFraction(plan.Series[s].Values[category]), -0.05, 1.05) * plot.Height;
                points.Add((x, y));
                if (plan.ShowMarkers || centred)
                    markers.Add(new ChartSceneMarker(x, y, 3, ChartSceneMarkerKind.Circle, palette[s % palette.Count]));
                if (plan.ShowDataLabels)
                    AddDataText(texts, plan.Series[s].Values[category], x + 2, y - 12);
            }

            if (points.Count > 0)
                lineSeries.Add(new ChartSceneLineSeries(points, palette[s % palette.Count], 2, false, zeroY));
        }

        if (!centred)
            AddCategoryLabels(texts, chart, plot, []);
    }

    private static void BuildCombo(
        Chart chart,
        ChartVisualPlan plan,
        ChartSceneRect plot,
        IReadOnlyList<string> palette,
        int categoryCount,
        List<ChartSceneBar> bars,
        List<ChartSceneLineSeries> lineSeries,
        List<ChartSceneMarker> markers,
        List<ChartSceneText> texts)
    {
        var axis = plan.ValueAxis;
        var cats = Math.Max(1, categoryCount);
        var columnSeries = new List<int>();
        var primaryLines = new List<int>();
        var secondaryLines = new List<int>();
        for (var s = 0; s < plan.Series.Count; s++)
        {
            if (s >= chart.Series.Count || !chart.Series[s].PlotAsLine)
                columnSeries.Add(s);
            else if (IsSecondarySeries(chart, s) && HasSecondaryAxis(chart))
                secondaryLines.Add(s);
            else
                primaryLines.Add(s);
        }

        var groupWidth = plot.Width / cats;
        var pad = Math.Max(1, groupWidth * 0.3);
        var seriesWidth = Math.Max(1, (groupWidth - 2 * pad) / Math.Max(1, columnSeries.Count));
        var zeroY = plot.Bottom - Math.Clamp(axis.ZeroFraction, 0, 1) * plot.Height;
        for (var category = 0; category < cats; category++)
        {
            for (var slot = 0; slot < columnSeries.Count; slot++)
            {
                var s = columnSeries[slot];
                if (category >= plan.Series[s].Values.Count)
                    continue;
                var value = plan.Series[s].Values[category];
                var barHeight = Math.Abs(axis.ValueFraction(value) - axis.ZeroFraction) * plot.Height;
                var x = plot.X + category * groupWidth + pad + slot * seriesWidth;
                var y = value >= 0 ? zeroY - barHeight : zeroY;
                bars.Add(new ChartSceneBar(
                    new ChartSceneRect(x, y, Math.Max(1, seriesWidth - 1), Math.Max(1, barHeight)),
                    palette[s % palette.Count]));
                if (plan.ShowDataLabels)
                {
                    texts.Add(new ChartSceneText(
                        FormatChartValue(value), x + seriesWidth / 2, Math.Max(plot.Y, y - 2),
                        ChartSceneTextAnchor.TopCenter, ChartSceneTextKind.DataLabel, "#000000", 8));
                }
            }

            AddCategoryText(texts, chart, category, plot.X + category * groupWidth + groupWidth / 2,
                plot.Bottom + 2, ChartSceneTextAnchor.TopCenter);
        }

        BuildLines(chart, plan, plot, palette, categoryCount, primaryLines, axis, centred: true, lineSeries, markers, texts);
        if (secondaryLines.Count == 0)
            return;

        var secondaryAxis = ChartValueAxisPlan.FromSeries(secondaryLines.Select(index => plan.Series[index]));
        BuildLines(chart, plan, plot, palette, categoryCount, secondaryLines, secondaryAxis, centred: true, lineSeries, markers, texts);
        for (var value = secondaryAxis.Minimum; value <= secondaryAxis.Maximum + secondaryAxis.MajorUnit / 2; value += secondaryAxis.MajorUnit)
        {
            var y = plot.Bottom - secondaryAxis.ValueFraction(value) * plot.Height;
            texts.Add(new ChartSceneText(
                value.ToString("G3", CultureInfo.InvariantCulture), plot.Right + 3, y - 6,
                ChartSceneTextAnchor.TopLeft, ChartSceneTextKind.ValueAxis, "#000000", 9));
        }
    }

    private static void BuildStock(
        Chart chart,
        ChartVisualPlan plan,
        ChartSceneRect plot,
        IReadOnlyList<string> palette,
        int categoryCount,
        List<ChartSceneLine> axisLines,
        List<ChartSceneMarker> markers,
        List<ChartSceneText> texts)
    {
        var axis = plan.ValueAxis;
        var cats = Math.Max(1, categoryCount);
        var slot = plot.Width / cats;
        double Y(double value) => plot.Bottom - Math.Clamp(axis.ValueFraction(value), 0, 1) * plot.Height;
        var (high, low, close) = (plan.Series[0].Values, plan.Series[1].Values, plan.Series[2].Values);
        for (var category = 0; category < cats; category++)
        {
            var x = plot.X + (category + 0.5) * slot;
            if (category < high.Count && category < low.Count)
                axisLines.Add(new ChartSceneLine(x, Y(high[category]), x, Y(low[category]), "#404040", 1.5));
            if (category < close.Count)
            {
                var y = Y(close[category]);
                axisLines.Add(new ChartSceneLine(x, y, x + Math.Min(6, slot / 3), y, "#404040", 1.5));
                markers.Add(new ChartSceneMarker(x, y, 2.5, ChartSceneMarkerKind.Circle, palette[2 % palette.Count]));
                if (plan.ShowDataLabels)
                    AddDataText(texts, close[category], x + 4, y - 12);
            }

            AddCategoryText(texts, chart, category, x, plot.Bottom + 2, ChartSceneTextAnchor.TopCenter);
        }
    }

    private static void BuildBubble(
        Chart chart,
        ChartVisualPlan plan,
        ChartSceneRect plot,
        IReadOnlyList<string> palette,
        string axisStroke,
        List<ChartSceneLine> axisLines,
        List<ChartSceneMarker> markers,
        List<ChartSceneText> texts)
    {
        var axis = plan.ValueAxis;
        var xValues = chart.Categories
            .Select(value => double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                && double.IsFinite(parsed) ? parsed : double.NaN)
            .ToList();
        var xAxis = BuildScatterAxis(xValues);
        var largest = chart.Series
            .SelectMany(series => Enumerable.Range(0, series.Values.Count).Select(index => ChartKindNormalizer.BubbleSizeAt(series, index)))
            .DefaultIfEmpty(0)
            .Max();
        var maximumRadius = Math.Max(4, Math.Min(plot.Width, plot.Height) * 0.12);
        for (var s = 0; s < plan.Series.Count && s < chart.Series.Count; s++)
        {
            for (var point = 0; point < plan.Series[s].Values.Count; point++)
            {
                var xValue = point < xValues.Count && !double.IsNaN(xValues[point]) ? xValues[point] : point + 1;
                var x = plot.X + (xValue - xAxis.Minimum) / (xAxis.Maximum - xAxis.Minimum) * plot.Width;
                var y = plot.Bottom - Math.Clamp(axis.ValueFraction(plan.Series[s].Values[point]), 0, 1) * plot.Height;
                var size = ChartKindNormalizer.BubbleSizeAt(chart.Series[s], point);
                // Bubble AREA is proportional to the size value, as in Word's default bubble charts.
                var radius = largest > 0 ? Math.Max(3, Math.Sqrt(size / largest) * maximumRadius) : 3;
                markers.Add(new ChartSceneMarker(
                    x, y, radius, ChartSceneMarkerKind.Circle, palette[s % palette.Count], FillOpacity: 0.65));
                if (plan.ShowDataLabels)
                    AddDataText(texts, plan.Series[s].Values[point], x + radius + 2, y - 6);
            }
        }

        axisLines.Add(new ChartSceneLine(plot.X, plot.Y, plot.X, plot.Bottom, axisStroke, 1));
        for (var tick = xAxis.Minimum; tick <= xAxis.Maximum + xAxis.Step / 2; tick += xAxis.Step)
        {
            var x = plot.X + (tick - xAxis.Minimum) / (xAxis.Maximum - xAxis.Minimum) * plot.Width;
            axisLines.Add(new ChartSceneLine(x, plot.Bottom, x, plot.Bottom + 4, axisStroke, 1));
            texts.Add(new ChartSceneText(
                FormatChartAxisValue(tick), x, plot.Bottom + 2,
                ChartSceneTextAnchor.TopCenter, ChartSceneTextKind.CategoryAxis, "#000000", 9));
        }
    }

    private static void BuildRadar(
        Chart chart,
        ChartVisualPlan plan,
        ChartSceneRect plot,
        IReadOnlyList<string> palette,
        int categoryCount,
        List<ChartSceneLine> gridLines,
        List<ChartSceneLineSeries> lineSeries,
        List<ChartSceneMarker> markers,
        List<ChartSceneText> texts)
    {
        var axis = plan.ValueAxis;
        var spokes = Math.Max(3, categoryCount);
        var (centerX, centerY) = (plot.CenterX, plot.CenterY);
        var radius = Math.Max(6, Math.Min(plot.Width, plot.Height) / 2 - 14);
        (double X, double Y) At(int spoke, double fraction)
        {
            var angle = -Math.PI / 2 + spoke * 2 * Math.PI / spokes;
            return (centerX + radius * fraction * Math.Cos(angle), centerY + radius * fraction * Math.Sin(angle));
        }

        // Web: one ring per major unit plus the spokes; the value scale runs up the first spoke.
        for (var value = axis.Minimum; value <= axis.Maximum + axis.MajorUnit / 2; value += axis.MajorUnit)
        {
            var fraction = Math.Clamp(axis.ValueFraction(value), 0, 1);
            if (fraction > 0)
            {
                for (var spoke = 0; spoke < spokes; spoke++)
                {
                    var (x1, y1) = At(spoke, fraction);
                    var (x2, y2) = At((spoke + 1) % spokes, fraction);
                    gridLines.Add(new ChartSceneLine(x1, y1, x2, y2, "#D9D9D9", 1));
                }
            }

            texts.Add(new ChartSceneText(
                value.ToString("G3", CultureInfo.InvariantCulture), centerX - 3, centerY - radius * fraction,
                ChartSceneTextAnchor.CenterRight, ChartSceneTextKind.ValueAxis, "#000000", 8));
        }

        for (var spoke = 0; spoke < spokes; spoke++)
        {
            var (x, y) = At(spoke, 1);
            gridLines.Add(new ChartSceneLine(centerX, centerY, x, y, "#D9D9D9", 1));
            var (labelX, labelY) = At(spoke, 1 + 10 / radius);
            if (spoke < chart.Categories.Count && !string.IsNullOrEmpty(chart.Categories[spoke]))
            {
                texts.Add(new ChartSceneText(
                    chart.Categories[spoke], labelX, labelY,
                    ChartSceneTextAnchor.Center, ChartSceneTextKind.CategoryAxis, "#000000", 9));
            }
        }

        for (var s = 0; s < plan.Series.Count; s++)
        {
            var points = new List<(double X, double Y)>();
            for (var spoke = 0; spoke < Math.Min(spokes, plan.Series[s].Values.Count); spoke++)
            {
                var point = At(spoke, Math.Clamp(axis.ValueFraction(plan.Series[s].Values[spoke]), 0, 1));
                points.Add(point);
                if (plan.ShowMarkers)
                    markers.Add(new ChartSceneMarker(point.X, point.Y, 3, ChartSceneMarkerKind.Circle, palette[s % palette.Count]));
                if (plan.ShowDataLabels)
                    AddDataText(texts, plan.Series[s].Values[spoke], point.X + 3, point.Y - 10);
            }

            if (points.Count > 2)
                points.Add(points[0]);
            if (points.Count > 0)
                lineSeries.Add(new ChartSceneLineSeries(points, palette[s % palette.Count], 2, false, centerY));
        }
    }
}
