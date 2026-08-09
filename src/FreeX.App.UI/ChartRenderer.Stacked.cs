using System.Globalization;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public static partial class ChartRenderer
{
    private static PlotModel BuildStackedColumnModel(
        ChartModel chart,
        PlotModel model,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        IReadOnlyList<string> categories,
        uint dataStartRow,
        uint endRow,
        uint dataStartCol,
        uint endCol,
        uint headerRow,
        bool normalizeToPercent,
        WorkbookTheme theme,
        ChartPointDataLabelFormatLookup pointDataLabelFormats)
    {
        var (positiveTotals, negativeTotals) = normalizeToPercent
            ? CalculateStackedPercentTotals(cellLookup, categories.Count, dataStartRow, endRow, dataStartCol, endCol)
            : ([], []);
        var (percentAxisMinimum, percentAxisMaximum) =
            GetStackedPercentAxisBounds(normalizeToPercent, positiveTotals, negativeTotals);

        // R131-render-chart-date-category-axis (WPF-family gap): the main non-stacked Column/Area/Line
        // loop in ChartRenderer.cs plots a date category axis (XAxisIsDateAxis) proportionally to the
        // actual dates; this stacked-column path built its own category axis independently and never
        // consulted it, so an unevenly dated STACKED column chart (e.g. Jan 1, Jan 2, Jan 10) still
        // plotted its segments at the plain evenly-spaced 0,1,2… index. Reuses the same
        // TryBuildDateCategoryAxis helper (ChartRenderer.Axes.cs) the non-stacked path uses, so both
        // paths agree pixel-for-pixel on a date axis; when it fails (not a date axis, or any category
        // isn't parseable) dateCategoryPositions stays null and every call site below falls back to
        // its original index-based behavior unchanged.
        var hasDateCategoryAxis = TryBuildDateCategoryAxis(chart, categories, out var dateCategoryAxisTemplate, out var dateCategoryPositionsArray);
        double[]? dateCategoryPositions = hasDateCategoryAxis ? dateCategoryPositionsArray : null;
        if (hasDateCategoryAxis && dateCategoryAxisTemplate is not null)
            model.Axes.Add(dateCategoryAxisTemplate);
        else
            model.Axes.Add(CreateCenteredIndexedCategoryAxis(AxisPosition.Bottom, chart.XAxisTitle, categories));
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = chart.YAxisTitle,
            Minimum = percentAxisMinimum,
            Maximum = percentAxisMaximum
        });

        var positiveBases = new double[categories.Count];
        var negativeBases = new double[categories.Count];
        // Honor the chart's gapWidth for the stacked column width so e.g. a shaded "target band"
        // (gapWidth=0) reads continuous across categories like Excel, instead of narrow columns
        // with wide inter-category gaps. With no explicit gapWidth this falls back to 0.35.
        var stackedHalfWidth = ColumnBarHalfWidth(chart);
        for (uint col = dataStartCol; col <= endCol; col++)
        {
            if (!ShouldRenderColumnAsSeries(chart, col, dataStartCol, endCol))
                continue;

            var seriesIndex = GetSeriesIndex(chart, col, dataStartCol, endCol);
            var seriesName = chart.FirstRowIsHeader && cellLookup.TryGetValue((headerRow, col), out var hdr)
                ? hdr.DisplayText
                : $"Series {seriesIndex + 1}";

            if (IsComboLineSeries(chart, seriesIndex))
            {
                var lineSeries = CreateLineSeries(chart, seriesName, seriesIndex, theme);
                var pointIndex = 0;
                for (uint row = dataStartRow; row <= endRow; row++, pointIndex++)
                {
                    if (!TryGetNumericCell(cellLookup, row, col, out var value) || pointIndex >= categories.Count)
                        continue;

                    var lineX = dateCategoryPositions is not null && pointIndex < dateCategoryPositions.Length
                        ? dateCategoryPositions[pointIndex]
                        : pointIndex;
                    lineSeries.Points.Add(new DataPoint(lineX, value));
                }
                AddLineDataLabelAnnotations(model, chart, theme, pointDataLabelFormats, lineSeries, seriesName, seriesIndex, categories);
                model.Series.Add(lineSeries);
                continue;
            }

            var series = new RectangleBarSeries
            {
                Title = IsLegendEntryDeleted(chart, seriesIndex) ? "" : seriesName,
                LabelFormatString = ChartDataLabelFormatter.GetNativeValueLabelFormat(chart, 4)
            };
            ApplyRectangleBarFormat(series, GetSeriesFormat(chart, seriesIndex), theme);
            ApplyNativeDataLabelStyle(series, chart, theme);

            var i = 0;
            for (uint row = dataStartRow; row <= endRow; row++, i++)
            {
                if (!TryGetNumericCell(cellLookup, row, col, out var value) || i >= categories.Count)
                    continue;

                var displayValue = NormalizeStackedValue(value, i, positiveTotals, negativeTotals);
                var start = displayValue >= 0 ? positiveBases[i] : negativeBases[i];
                var end = start + displayValue;
                var x = dateCategoryPositions is not null && i < dateCategoryPositions.Length ? dateCategoryPositions[i] : i;
                series.Items.Add(new RectangleBarItem(x - stackedHalfWidth, Math.Min(start, end), x + stackedHalfWidth, Math.Max(start, end)));
                if (displayValue >= 0)
                    positiveBases[i] = end;
                else
                    negativeBases[i] = end;
                if (ShouldUseAnnotationLabels(chart))
                    AddDataLabelAnnotation(model, chart, theme, pointDataLabelFormats, seriesName, seriesIndex, i, ChartDataLabelTextPlanner.GetCategory(categories, i), x, end, GetStackedLabelValue(chart, normalizeToPercent, value, displayValue));
            }

            model.Series.Add(series);
        }

        return model;
    }

    private static PlotModel BuildStackedBarModel(
        ChartModel chart,
        PlotModel model,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        IReadOnlyList<string> categories,
        uint dataStartRow,
        uint endRow,
        uint dataStartCol,
        uint endCol,
        uint headerRow,
        bool normalizeToPercent,
        WorkbookTheme theme,
        ChartPointDataLabelFormatLookup pointDataLabelFormats)
    {
        var (positiveTotals, negativeTotals) = normalizeToPercent
            ? CalculateStackedPercentTotals(cellLookup, categories.Count, dataStartRow, endRow, dataStartCol, endCol)
            : ([], []);
        var (percentAxisMinimum, percentAxisMaximum) =
            GetStackedPercentAxisBounds(normalizeToPercent, positiveTotals, negativeTotals);

        model.Axes.Add(CreateCategoryAxis(AxisPosition.Left, chart.YAxisTitle, categories));
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Title = chart.XAxisTitle,
            Minimum = percentAxisMinimum,
            Maximum = percentAxisMaximum
        });

        var positiveBases = new double[categories.Count];
        var negativeBases = new double[categories.Count];
        var stackedHalfWidth = ColumnBarHalfWidth(chart);
        for (uint col = dataStartCol; col <= endCol; col++)
        {
            if (!ShouldRenderColumnAsSeries(chart, col, dataStartCol, endCol))
                continue;

            var seriesIndex = GetSeriesIndex(chart, col, dataStartCol, endCol);
            var seriesName = chart.FirstRowIsHeader && cellLookup.TryGetValue((headerRow, col), out var hdr)
                ? hdr.DisplayText
                : $"Series {seriesIndex + 1}";

            if (IsComboLineSeries(chart, seriesIndex))
            {
                var lineSeries = CreateLineSeries(chart, seriesName, seriesIndex, theme);
                var pointIdx = 0;
                for (uint row = dataStartRow; row <= endRow; row++, pointIdx++)
                {
                    if (!TryGetNumericCell(cellLookup, row, col, out var value) || pointIdx >= categories.Count)
                        continue;
                    lineSeries.Points.Add(new DataPoint(value, pointIdx));
                }
                model.Series.Add(lineSeries);
                continue;
            }

            var series = new RectangleBarSeries
            {
                Title = IsLegendEntryDeleted(chart, seriesIndex) ? "" : seriesName,
                LabelFormatString = ChartDataLabelFormatter.GetNativeValueLabelFormat(chart, 4)
            };
            ApplyRectangleBarFormat(series, GetSeriesFormat(chart, seriesIndex), theme);
            ApplyNativeDataLabelStyle(series, chart, theme);

            var i = 0;
            for (uint row = dataStartRow; row <= endRow; row++, i++)
            {
                if (!TryGetNumericCell(cellLookup, row, col, out var value) || i >= categories.Count)
                    continue;

                var displayValue = NormalizeStackedValue(value, i, positiveTotals, negativeTotals);
                var start = displayValue >= 0 ? positiveBases[i] : negativeBases[i];
                var end = start + displayValue;
                series.Items.Add(new RectangleBarItem(Math.Min(start, end), i - stackedHalfWidth, Math.Max(start, end), i + stackedHalfWidth));
                if (displayValue >= 0)
                    positiveBases[i] = end;
                else
                    negativeBases[i] = end;
                if (ShouldUseAnnotationLabels(chart))
                    AddDataLabelAnnotation(model, chart, theme, pointDataLabelFormats, seriesName, seriesIndex, i, ChartDataLabelTextPlanner.GetCategory(categories, i), end, i, GetStackedLabelValue(chart, normalizeToPercent, value, displayValue));
            }

            model.Series.Add(series);
        }

        return model;
    }

    /// <summary>
    /// Builds a Stacked Area / 100%-Stacked Area chart: one filled <see cref="AreaSeries"/> band per
    /// data column, each riding on the cumulative baseline of the bands below it (mirroring
    /// <see cref="BuildStackedColumnModel"/>/<see cref="BuildStackedBarModel"/> for the area family).
    /// Without this a stacked area chart renders as independent overlapping areas with no cumulative
    /// baseline. For <paramref name="normalizeToPercent"/> each category's stack is scaled to 100%
    /// via the same per-category totals used by the stacked column/bar path. The band's top polyline
    /// is <see cref="AreaSeries.Points"/> and its bottom (the running stack base) is
    /// <see cref="AreaSeries.Points2"/>, so OxyPlot fills exactly the segment this series contributes.
    /// </summary>
    private static PlotModel BuildStackedAreaModel(
        ChartModel chart,
        PlotModel model,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        IReadOnlyList<string> categories,
        uint dataStartRow,
        uint endRow,
        uint dataStartCol,
        uint endCol,
        uint headerRow,
        bool normalizeToPercent,
        WorkbookTheme theme,
        ChartPointDataLabelFormatLookup pointDataLabelFormats)
    {
        // Area charts plot by point (row) index, so the running-stack bases are sized to the row
        // span rather than the category count (categories may be empty, unlike the column path).
        var pointCount = endRow >= dataStartRow ? (int)(endRow - dataStartRow + 1) : 0;
        var (positiveTotals, negativeTotals) = normalizeToPercent
            ? CalculateStackedPercentTotals(cellLookup, pointCount, dataStartRow, endRow, dataStartCol, endCol)
            : ([], []);
        var (percentAxisMinimum, percentAxisMaximum) =
            GetStackedPercentAxisBounds(normalizeToPercent, positiveTotals, negativeTotals);

        // Same category/value axes as the plain Area path (zero-based indexed categories). Stacked
        // charts do not split series across a secondary axis, so none is added.
        // R131-render-chart-date-category-axis (WPF-family gap): see the matching comment in
        // BuildStackedColumnModel above -- reuses the same TryBuildDateCategoryAxis helper so an
        // unevenly dated STACKED area chart plots proportionally instead of at the plain index.
        var hasDateCategoryAxis = TryBuildDateCategoryAxis(chart, categories, out var dateCategoryAxisTemplate, out var dateCategoryPositionsArray);
        double[]? dateCategoryPositions = hasDateCategoryAxis ? dateCategoryPositionsArray : null;
        if (hasDateCategoryAxis && dateCategoryAxisTemplate is not null)
            model.Axes.Add(dateCategoryAxisTemplate);
        else
            model.Axes.Add(CreateZeroBasedIndexedCategoryAxis(AxisPosition.Bottom, chart.XAxisTitle, categories));
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = chart.YAxisTitle,
            Minimum = percentAxisMinimum,
            Maximum = percentAxisMaximum
        });

        var positiveBases = new double[pointCount];
        var negativeBases = new double[pointCount];
        for (uint col = dataStartCol; col <= endCol; col++)
        {
            if (!ShouldRenderColumnAsSeries(chart, col, dataStartCol, endCol))
                continue;

            var seriesIndex = GetSeriesIndex(chart, col, dataStartCol, endCol);
            var seriesName = chart.FirstRowIsHeader && cellLookup.TryGetValue((headerRow, col), out var hdr)
                ? hdr.DisplayText
                : $"Series {seriesIndex + 1}";

            if (IsComboLineSeries(chart, seriesIndex))
            {
                // A series promoted to a combo line overlay is drawn over the stack and does not
                // participate in the running stack totals (mirrors BuildStackedColumnModel).
                var lineSeries = CreateLineSeries(chart, seriesName, seriesIndex, theme);
                var pointIndex = 0;
                for (uint row = dataStartRow; row <= endRow; row++, pointIndex++)
                {
                    if (TryGetNumericCell(cellLookup, row, col, out var value))
                    {
                        var lineX = dateCategoryPositions is not null && pointIndex < dateCategoryPositions.Length
                            ? dateCategoryPositions[pointIndex]
                            : pointIndex;
                        lineSeries.Points.Add(new DataPoint(lineX, value));
                    }
                }
                AddLineDataLabelAnnotations(model, chart, theme, pointDataLabelFormats, lineSeries, seriesName, seriesIndex, categories);
                model.Series.Add(lineSeries);
                continue;
            }

            var series = new AreaSeries
            {
                Title = IsLegendEntryDeleted(chart, seriesIndex) ? "" : seriesName,
                LabelFormatString = ChartDataLabelFormatter.GetNativeValueLabelFormat(chart, 1)
            };
            ApplyAreaFormat(series, GetSeriesFormat(chart, seriesIndex), theme);
            ApplyNativeDataLabelStyle(series, chart, theme);

            var i = 0;
            for (uint row = dataStartRow; row <= endRow && i < pointCount; row++, i++)
            {
                // A non-numeric/blank cell contributes 0 so the band stays continuous and the layers
                // above keep a well-defined baseline (Excel stacks a blank area point as zero).
                var hasValue = TryGetNumericCell(cellLookup, row, col, out var value);
                var displayValue = hasValue ? NormalizeStackedValue(value, i, positiveTotals, negativeTotals) : 0;
                var start = displayValue >= 0 ? positiveBases[i] : negativeBases[i];
                var end = start + displayValue;
                var x = dateCategoryPositions is not null && i < dateCategoryPositions.Length ? dateCategoryPositions[i] : i;
                series.Points.Add(new DataPoint(x, end));
                series.Points2.Add(new DataPoint(x, start));
                if (displayValue >= 0)
                    positiveBases[i] = end;
                else
                    negativeBases[i] = end;
                if (hasValue && ShouldUseAnnotationLabels(chart))
                    AddDataLabelAnnotation(model, chart, theme, pointDataLabelFormats, seriesName, seriesIndex, i, ChartDataLabelTextPlanner.GetCategory(categories, i), x, end, GetStackedLabelValue(chart, normalizeToPercent, value, displayValue));
            }

            model.Series.Add(series);
        }

        return model;
    }

    /// <summary>
    /// Detects the "progress-bar" idiom used by Excel for a horizontal completion bar (Contextures /
    /// ExcelExamples1 "todo" chart20): N stacked-bar series, each a SINGLE cell stacked within ONE
    /// category, with NO <c:cat> (so the model collapses to one column x N rows and 0 categories).
    /// The normal stacked builder skips every point in this shape (i &gt;= categories.Count == 0) and
    /// renders blank. The signal is: a single data column (<paramref name="dataStartCol"/> ==
    /// <paramref name="endCol"/>), no categories, more than one data row, and an authoritative
    /// series-column mapping with &gt;1 series all pointing at that single column.
    /// </summary>
    private static bool IsSingleColumnStackedSeriesShape(
        ChartModel chart,
        IReadOnlyList<string> categories,
        uint dataStartRow,
        uint endRow,
        uint dataStartCol,
        uint endCol)
    {
        if (categories.Count != 0)
            return false;
        if (dataStartCol != endCol)
            return false;
        if (endRow <= dataStartRow)
            return false;

        var mappings = chart.SeriesColumnMappings;
        if (mappings.Count <= 1)
            return false;

        for (var i = 0; i < mappings.Count; i++)
        {
            if (mappings[i].ValueColumn != dataStartCol)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Renders the single-column stacked "progress-bar" shape (see
    /// <see cref="IsSingleColumnStackedSeriesShape"/>): each data row becomes its own stacked
    /// series contributing one rectangle in the single synthetic category, so the segments stack to
    /// their sum (e.g. 0.30 + 0.15 = 0.45 -&gt; a ~45% progress bar) instead of rendering blank.
    /// </summary>
    private static PlotModel BuildSingleColumnStackedModel(
        ChartModel chart,
        PlotModel model,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        uint dataStartRow,
        uint endRow,
        uint dataColumn,
        uint headerRow,
        bool isBar,
        bool normalizeToPercent,
        WorkbookTheme theme)
    {
        // One synthetic category (the single progress bar).
        var singleCategory = new List<string> { string.Empty };
        // The progress-bar value axis is fixed (e.g. 0..1 = 0..100%) in the chart XML; honor it so
        // the bar reads as its true fraction (~45%) rather than auto-scaling to fill the plot. The
        // value-axis bounds are loaded into YAxis* regardless of bar direction.
        var valueAxisMinimum = chart.YAxisMinimum ?? chart.XAxisMinimum ?? double.NaN;
        var valueAxisMaximum = chart.YAxisMaximum ?? chart.XAxisMaximum ?? double.NaN;
        if (isBar)
        {
            model.Axes.Add(CreateCategoryAxis(AxisPosition.Left, chart.YAxisTitle, singleCategory));
            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Title = chart.XAxisTitle,
                Minimum = valueAxisMinimum,
                Maximum = valueAxisMaximum
            });
        }
        else
        {
            model.Axes.Add(CreateCenteredIndexedCategoryAxis(AxisPosition.Bottom, chart.XAxisTitle, singleCategory));
            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = chart.YAxisTitle,
                Minimum = valueAxisMinimum,
                Maximum = valueAxisMaximum
            });
        }

        // Sum for percent-stacked normalization (each row is one segment in the single category).
        var total = 0.0;
        if (normalizeToPercent)
        {
            for (var row = dataStartRow; row <= endRow; row++)
            {
                if (TryGetNumericCell(cellLookup, row, dataColumn, out var v) && v > 0)
                    total += v;
            }
        }

        var halfWidth = ColumnBarHalfWidth(chart);
        var seriesOrdinal = 0;
        var baseValue = 0.0;
        for (var row = dataStartRow; row <= endRow; row++, seriesOrdinal++)
        {
            if (!TryGetNumericCell(cellLookup, row, dataColumn, out var value))
                continue;

            var seriesIndex = seriesOrdinal < chart.SeriesPlotOrder.Count
                ? chart.SeriesPlotOrder[seriesOrdinal]
                : seriesOrdinal;

            var seriesName = chart.FirstRowIsHeader && cellLookup.TryGetValue((headerRow, dataColumn), out var hdr)
                ? hdr.DisplayText
                : $"Series {seriesIndex + 1}";

            var series = new RectangleBarSeries
            {
                Title = IsLegendEntryDeleted(chart, seriesIndex) ? "" : seriesName,
                LabelFormatString = ChartDataLabelFormatter.GetNativeValueLabelFormat(chart, 4)
            };
            ApplyRectangleBarFormat(series, GetSeriesFormat(chart, seriesIndex), theme);
            ApplyNativeDataLabelStyle(series, chart, theme);

            var displayValue = normalizeToPercent && total > 0 ? value / total * 100 : value;
            var start = baseValue;
            var end = start + displayValue;
            baseValue = end;

            series.Items.Add(isBar
                ? new RectangleBarItem(Math.Min(start, end), -halfWidth, Math.Max(start, end), halfWidth)
                : new RectangleBarItem(-halfWidth, Math.Min(start, end), halfWidth, Math.Max(start, end)));
            model.Series.Add(series);
        }

        return model;
    }

    private static bool TryGetNumericCell(
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        uint row,
        uint col,
        out double value)
    {
        value = 0;
        return cellLookup.TryGetValue((row, col), out var cell) &&
               TryGetChartNumericValue(cell, out value);
    }

    private static (double[] PositiveTotals, double[] NegativeTotals) CalculateStackedPercentTotals(
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        int categoryCount,
        uint dataStartRow,
        uint endRow,
        uint dataStartCol,
        uint endCol)
    {
        var positiveTotals = new double[categoryCount];
        var negativeTotals = new double[categoryCount];
        for (uint col = dataStartCol; col <= endCol; col++)
        {
            var index = 0;
            for (uint row = dataStartRow; row <= endRow && index < categoryCount; row++, index++)
            {
                if (!TryGetNumericCell(cellLookup, row, col, out var value))
                    continue;
                if (value >= 0)
                    positiveTotals[index] += value;
                else
                    negativeTotals[index] += Math.Abs(value);
            }
        }

        return (positiveTotals, negativeTotals);
    }

    private static (double Minimum, double Maximum) GetStackedPercentAxisBounds(
        bool normalizeToPercent,
        IReadOnlyList<double> positiveTotals,
        IReadOnlyList<double> negativeTotals)
    {
        if (!normalizeToPercent)
            return (double.NaN, double.NaN);

        var hasPositive = false;
        for (var index = 0; index < positiveTotals.Count; index++)
        {
            if (positiveTotals[index] <= 0)
                continue;

            hasPositive = true;
            break;
        }

        var hasNegative = false;
        for (var index = 0; index < negativeTotals.Count; index++)
        {
            if (negativeTotals[index] <= 0)
                continue;

            hasNegative = true;
            break;
        }

        return (hasNegative ? -100 : 0, hasPositive || !hasNegative ? 100 : 0);
    }

    private static double NormalizeStackedValue(
        double value,
        int categoryIndex,
        IReadOnlyList<double> positiveTotals,
        IReadOnlyList<double> negativeTotals)
    {
        if (positiveTotals.Count == 0 && negativeTotals.Count == 0)
            return value;

        var total = value >= 0 ? positiveTotals[categoryIndex] : negativeTotals[categoryIndex];
        return total == 0 ? 0 : value / total * 100;
    }

    private static double GetStackedLabelValue(ChartModel chart, bool normalizeToPercent, double sourceValue, double displayValue) =>
        normalizeToPercent && ChartDataLabelTextPlanner.ShouldRenderPercentageLabels(chart)
            ? displayValue / 100
            : sourceValue;
}
