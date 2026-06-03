using System.Globalization;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
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
        WorkbookTheme theme)
    {
        var (positiveTotals, negativeTotals) = normalizeToPercent
            ? CalculateStackedPercentTotals(cellLookup, categories.Count, dataStartRow, endRow, dataStartCol, endCol)
            : ([], []);
        var (percentAxisMinimum, percentAxisMaximum) =
            GetStackedPercentAxisBounds(normalizeToPercent, positiveTotals, negativeTotals);

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
        for (uint col = dataStartCol; col <= endCol; col++)
        {
            var seriesIndex = (int)(col - dataStartCol);
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

                    lineSeries.Points.Add(new DataPoint(pointIndex, value));
                }
                AddLineDataLabelAnnotations(model, chart, theme, lineSeries, seriesName, seriesIndex, categories);
                model.Series.Add(lineSeries);
                continue;
            }

            var series = new RectangleBarSeries
            {
                Title = seriesName,
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
                series.Items.Add(new RectangleBarItem(i - 0.35, Math.Min(start, end), i + 0.35, Math.Max(start, end)));
                if (displayValue >= 0)
                    positiveBases[i] = end;
                else
                    negativeBases[i] = end;
                if (ShouldUseAnnotationLabels(chart))
                    AddDataLabelAnnotation(model, chart, theme, seriesName, seriesIndex, i, ChartDataLabelFormatter.GetCategory(categories, i), i, end, GetStackedLabelValue(chart, normalizeToPercent, value, displayValue));
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
        WorkbookTheme theme)
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
        for (uint col = dataStartCol; col <= endCol; col++)
        {
            var seriesIndex = (int)(col - dataStartCol);
            var seriesName = chart.FirstRowIsHeader && cellLookup.TryGetValue((headerRow, col), out var hdr)
                ? hdr.DisplayText
                : $"Series {seriesIndex + 1}";
            var series = new RectangleBarSeries
            {
                Title = seriesName,
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
                series.Items.Add(new RectangleBarItem(Math.Min(start, end), i - 0.35, Math.Max(start, end), i + 0.35));
                if (displayValue >= 0)
                    positiveBases[i] = end;
                else
                    negativeBases[i] = end;
                if (ShouldUseAnnotationLabels(chart))
                    AddDataLabelAnnotation(model, chart, theme, seriesName, seriesIndex, i, ChartDataLabelFormatter.GetCategory(categories, i), end, i, GetStackedLabelValue(chart, normalizeToPercent, value, displayValue));
            }

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
        normalizeToPercent && ChartDataLabelFormatter.ShouldRenderPercentageLabels(chart)
            ? displayValue / 100
            : sourceValue;
}
