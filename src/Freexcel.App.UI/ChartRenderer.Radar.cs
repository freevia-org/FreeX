using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using Freexcel.Core.Model;

namespace Freexcel.App.UI;

public static partial class ChartRenderer
{
    private static PlotModel BuildRadarModel(
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
        model.PlotType = PlotType.Polar;
        var pointCount = Math.Max(1, categories.Count);
        model.Axes.Add(new AngleAxis
        {
            StartAngle = 90,
            EndAngle = 450,
            Minimum = 0,
            Maximum = 360,
            MajorStep = 360.0 / pointCount,
            MinorStep = 360.0 / pointCount,
            LabelFormatter = angle =>
            {
                var index = (int)Math.Round((((angle % 360) + 360) % 360) / (360.0 / pointCount));
                index %= pointCount;
                return index >= 0 && index < categories.Count ? categories[index] : "";
            }
        });
        model.Axes.Add(new MagnitudeAxis { Minimum = 0, Title = chart.YAxisTitle });

        for (uint col = dataStartCol; col <= endCol; col++)
        {
            var seriesIndex = GetSeriesIndex(chart, col, dataStartCol);
            var seriesName = chart.FirstRowIsHeader && cellLookup.TryGetValue((headerRow, col), out var header)
                ? header.DisplayText
                : $"Series {seriesIndex + 1}";
            var series = CreateLineSeries(chart, seriesName, seriesIndex, theme);
            series.MarkerType = MarkerType.Circle;

            DataPoint? firstPoint = null;
            var i = 0;
            for (uint row = dataStartRow; row <= endRow; row++, i++)
            {
                if (!cellLookup.TryGetValue((row, col), out var cell) ||
                    !double.TryParse(cell.DisplayText, out var value))
                    continue;

                var point = new DataPoint(i * 360.0 / pointCount, value);
                firstPoint ??= point;
                series.Points.Add(point);
            }

            if (firstPoint is { } closedPoint && series.Points.Count > 1)
                series.Points.Add(closedPoint);

            model.Series.Add(series);
        }

        return model;
    }
}
