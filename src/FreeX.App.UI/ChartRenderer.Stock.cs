using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public static partial class ChartRenderer
{
    private static PlotModel BuildStockModel(
        ChartModel chart,
        PlotModel model,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        IReadOnlyList<string> categories,
        uint dataStartRow,
        uint endRow,
        uint dataStartCol,
        uint endCol,
        uint headerRow,
        WorkbookTheme theme)
    {
        var xValues = GetStockXValues(categories, out var dateAxis);
        if (dateAxis is not null)
        {
            dateAxis.Title = chart.XAxisTitle;
            model.Axes.Add(dateAxis);
        }
        else
        {
            model.Axes.Add(CreateCenteredIndexedCategoryAxis(AxisPosition.Bottom, chart.XAxisTitle, categories));
        }
        model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = chart.YAxisTitle });

        var valueColumnCount = endCol >= dataStartCol ? endCol - dataStartCol + 1 : 0;
        var hasVolumeColumn = chart.StockSubtype is StockChartSubtype.VolumeHighLowClose or StockChartSubtype.VolumeOpenHighLowClose;
        var hasOpenColumn = chart.StockSubtype is StockChartSubtype.OpenHighLowClose or StockChartSubtype.VolumeOpenHighLowClose ||
                            (!hasVolumeColumn && valueColumnCount >= 4);
        var volumeOffset = hasVolumeColumn ? 1u : 0u;
        var requiredValueColumns = volumeOffset + (hasOpenColumn ? 4u : 3u);
        if (valueColumnCount < requiredValueColumns)
            return model;

        if (hasVolumeColumn)
            AddStockVolumeSeries(model, cellLookup, dataStartRow, endRow, dataStartCol, xValues);

        var openCol = hasOpenColumn ? dataStartCol + volumeOffset : (uint?)null;
        var highCol = dataStartCol + volumeOffset + (hasOpenColumn ? 1u : 0u);
        var lowCol = highCol + 1;
        var closeCol = highCol + 2;
        if (valueColumnCount < 3 || closeCol > endCol)
            return model;

        var series = CreateStockPriceSeries(chart, hasOpenColumn, theme);

        for (uint row = dataStartRow; row <= endRow; row++)
        {
            var index = row - dataStartRow;
            if (index >= xValues.Count)
                break;

            if (!TryGetNumericCell(cellLookup, row, highCol, out var high) ||
                !TryGetNumericCell(cellLookup, row, lowCol, out var low) ||
                !TryGetNumericCell(cellLookup, row, closeCol, out var close))
                continue;

            var open = openCol is { } parsedOpenCol && TryGetNumericCell(cellLookup, row, parsedOpenCol, out var parsedOpen)
                ? parsedOpen
                : close;
            series.Items.Add(new HighLowItem(xValues[(int)index], high, low, open, close));
        }

        model.Series.Add(series);
        AddHighLowLinesIfRequested(model, chart, theme, series.Items);
        AddDropLinesIfRequested(model, chart, theme, series.Items);
        return model;
    }

    private static HighLowSeries CreateStockPriceSeries(ChartModel chart, bool hasOpenColumn, WorkbookTheme theme)
    {
        if (chart.ShowUpDownBars && hasOpenColumn)
        {
            return new CandleStickSeries
            {
                Title = "Stock",
                StrokeThickness = GetUpDownBarBorderThickness(chart),
                Color = ToOxyColor(GetUpDownBarBorderColor(chart, theme)) ?? OxyColors.Black,
                IncreasingColor = ToOxyColor(chart.UpBarFillThemeColor?.Resolve(theme) ?? chart.UpBarFillColor) ?? OxyColors.White,
                DecreasingColor = ToOxyColor(chart.DownBarFillThemeColor?.Resolve(theme) ?? chart.DownBarFillColor) ?? OxyColors.Black,
                CandleWidth = GetUpDownBarCandleWidth(chart)
            };
        }

        return new HighLowSeries
        {
            Title = "Stock",
            StrokeThickness = 1.5,
            Color = OxyColors.Black
        };
    }

    /// <summary>
    /// Draws Excel's stock/line-chart "High-Low Lines" (<c>&lt;c:hiLowLines&gt;</c>): a vertical
    /// connector between the high and low value at each category, styled from
    /// <see cref="ChartModel.HighLowLineColor"/>/<see cref="ChartModel.HighLowLineThemeColor"/>/
    /// <see cref="ChartModel.HighLowLineThickness"/>/<see cref="ChartModel.HighLowLineDashStyle"/>.
    /// Only drawn when <see cref="ChartModel.ShowHighLowLines"/> is set — the OHLC/HLC price series
    /// itself already draws its own high-low wick unconditionally, so this is an independent overlay
    /// (mirroring the source chart XML, where <c>hiLowLines</c> is a sibling element to the price
    /// series rather than part of it) drawn as one disjoint segment per category in its own
    /// <see cref="LineSeries"/>, matching the <c>AddWhisker</c>/error-bar idiom used elsewhere in this
    /// renderer.
    /// </summary>
    private static void AddHighLowLinesIfRequested(
        PlotModel model,
        ChartModel chart,
        WorkbookTheme theme,
        IReadOnlyList<HighLowItem> items)
    {
        if (!chart.ShowHighLowLines || items.Count == 0)
            return;

        var color = chart.HighLowLineThemeColor?.Resolve(theme) ?? chart.HighLowLineColor;
        var lines = new LineSeries
        {
            Color = ToOxyColor(color) ?? OxyColors.Black,
            StrokeThickness = double.IsFinite(chart.HighLowLineThickness)
                ? Math.Clamp(chart.HighLowLineThickness, 0, 20)
                : 1,
            LineStyle = ToOxyLineStyle(chart.HighLowLineDashStyle),
            MarkerType = MarkerType.None
        };

        foreach (var item in items)
        {
            if (lines.Points.Count > 0)
                lines.Points.Add(DataPoint.Undefined);
            lines.Points.Add(new DataPoint(item.X, item.High));
            lines.Points.Add(new DataPoint(item.X, item.Low));
        }

        model.Series.Add(lines);
    }

    /// <summary>
    /// Draws Excel's stock/line-chart "Drop Lines" (<c>&lt;c:dropLines&gt;</c>): a vertical connector
    /// from each plotted data point down to the category axis, styled from
    /// <see cref="ChartModel.DropLineColor"/>/<see cref="ChartModel.DropLineThemeColor"/>/
    /// <see cref="ChartModel.DropLineThickness"/>/<see cref="ChartModel.DropLineDashStyle"/>. Only
    /// drawn when <see cref="ChartModel.ShowDropLines"/> is set. For the stock chart the anchor value
    /// is the close price at each category; the connector drops to the zero/category-axis line
    /// (matching the RectangleBarItem/AreaSeries baseline convention used elsewhere in this renderer).
    /// </summary>
    private static void AddDropLinesIfRequested(
        PlotModel model,
        ChartModel chart,
        WorkbookTheme theme,
        IReadOnlyList<HighLowItem> items)
    {
        if (!chart.ShowDropLines || items.Count == 0)
            return;

        var color = chart.DropLineThemeColor?.Resolve(theme) ?? chart.DropLineColor;
        var lines = new LineSeries
        {
            Color = ToOxyColor(color) ?? OxyColors.Black,
            StrokeThickness = double.IsFinite(chart.DropLineThickness)
                ? Math.Clamp(chart.DropLineThickness, 0, 20)
                : 1,
            LineStyle = ToOxyLineStyle(chart.DropLineDashStyle),
            MarkerType = MarkerType.None
        };

        foreach (var item in items)
        {
            if (lines.Points.Count > 0)
                lines.Points.Add(DataPoint.Undefined);
            lines.Points.Add(new DataPoint(item.X, Math.Min(0, item.Close)));
            lines.Points.Add(new DataPoint(item.X, item.Close));
        }

        model.Series.Add(lines);
    }

    private static double GetUpDownBarCandleWidth(ChartModel chart) =>
        chart.UpDownBarGapWidth is { } gapWidth
            ? Math.Clamp(100.0 / (100.0 + Math.Max(0, gapWidth)), 0.05, 0.95)
            : 0.55;

    private static double GetUpDownBarBorderThickness(ChartModel chart)
    {
        var thickness = Math.Max(chart.UpBarBorderThickness ?? 1.5, chart.DownBarBorderThickness ?? 1.5);
        return double.IsFinite(thickness) ? Math.Clamp(thickness, 0, 20) : 1.5;
    }

    private static CellColor? GetUpDownBarBorderColor(ChartModel chart, WorkbookTheme theme) =>
        chart.UpBarBorderThemeColor?.Resolve(theme)
        ?? chart.UpBarBorderColor
        ?? chart.DownBarBorderThemeColor?.Resolve(theme)
        ?? chart.DownBarBorderColor;

    private static void AddStockVolumeSeries(
        PlotModel model,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        uint dataStartRow,
        uint endRow,
        uint volumeCol,
        IReadOnlyList<double> xValues)
    {
        var series = new RectangleBarSeries
        {
            Title = "Volume",
            FillColor = OxyColor.FromArgb(90, 91, 155, 213),
            StrokeColor = OxyColor.FromArgb(140, 91, 155, 213),
            StrokeThickness = 0.5
        };

        var i = 0;
        for (uint row = dataStartRow; row <= endRow; row++, i++)
        {
            if (i >= xValues.Count)
                break;

            if (TryGetNumericCell(cellLookup, row, volumeCol, out var volume))
                series.Items.Add(new RectangleBarItem(xValues[i] - 0.35, 0, xValues[i] + 0.35, volume));
        }

        model.Series.Add(series);
    }

    private static IReadOnlyList<double> GetStockXValues(IReadOnlyList<string> categories, out DateTimeAxis? dateAxis)
    {
        dateAxis = null;
        if (categories.Count == 0)
            return [];

        var values = new double[categories.Count];
        var minValue = double.PositiveInfinity;
        var maxValue = double.NegativeInfinity;
        for (var index = 0; index < categories.Count; index++)
        {
            if (!TryParseStockDateCategory(categories[index], out var parsed))
                return BuildStockCategoryIndexes(categories.Count);

            var value = DateTimeAxis.ToDouble(parsed.Date);
            values[index] = value;
            if (value < minValue)
                minValue = value;
            if (value > maxValue)
                maxValue = value;
        }

        dateAxis = new DateTimeAxis
        {
            Position = AxisPosition.Bottom,
            StringFormat = "d",
            IntervalType = DateTimeIntervalType.Days,
            Minimum = minValue - 0.5,
            Maximum = maxValue + 0.5
        };
        return values;
    }

    private static double[] BuildStockCategoryIndexes(int count)
    {
        var values = new double[count];
        for (var index = 0; index < values.Length; index++)
            values[index] = index;
        return values;
    }

    private static bool TryParseStockDateCategory(string category, out DateTime value) =>
        ChartRenderPolicyPlanner.TryParseDateCategory(category, out value);
}
