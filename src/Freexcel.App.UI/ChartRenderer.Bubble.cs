using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using Freexcel.Core.Model;

namespace Freexcel.App.UI;

public static partial class ChartRenderer
{
    private static PlotModel BuildBubbleModel(
        ChartModel chart,
        PlotModel model,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        IReadOnlyList<string> categories,
        uint dataStartRow,
        uint endRow,
        uint dataStartCol,
        uint endCol,
        uint headerRow,
        WorkbookTheme theme,
        out List<DataPoint> trendPoints)
    {
        model.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Title = chart.XAxisTitle });
        model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = chart.YAxisTitle });
        trendPoints = [];

        var xCol = chart.DataRange.Start.Col;
        var seriesIndex = 0;
        for (var yCol = xCol + 1; yCol <= endCol; yCol += 2)
        {
            var sizeCol = yCol + 1;
            if (sizeCol > endCol)
                continue;

            var seriesName = chart.FirstRowIsHeader && cellLookup.TryGetValue((headerRow, yCol), out var hdr)
                ? hdr.DisplayText
                : $"Series {seriesIndex + 1}";
            var series = new ScatterSeries
            {
                Title = seriesName,
                MarkerType = MarkerType.Circle,
                LabelFormatString = ChartDataLabelFormatter.GetNativeValueLabelFormat(chart, 1),
                LabelMargin = ToLabelMargin(chart.DataLabelPosition)
            };
            ApplyScatterFormat(series, GetSeriesFormat(chart, seriesIndex), theme);
            ApplyNativeDataLabelStyle(series, chart, theme);

            var fallbackIndex = 0;
            for (uint row = dataStartRow; row <= endRow; row++, fallbackIndex++)
            {
                if (!TryGetNumericCell(cellLookup, row, xCol, out var x))
                    x = fallbackIndex;
                if (!TryGetNumericCell(cellLookup, row, yCol, out var y))
                    continue;
                var size = TryGetNumericCell(cellLookup, row, sizeCol, out var rawSize)
                    ? Math.Max(1, Math.Abs(rawSize))
                    : 5;
                series.Points.Add(new ScatterPoint(x, y, size));
                if (seriesIndex == 0)
                    trendPoints.Add(new DataPoint(x, y));
                if (ShouldUseAnnotationLabels(chart))
                    AddDataLabelAnnotation(model, chart, theme, seriesName, seriesIndex, fallbackIndex, ChartDataLabelFormatter.GetCategory(categories, fallbackIndex), x, y, y);
            }

            model.Series.Add(series);
            seriesIndex++;
        }

        return model;
    }
}
