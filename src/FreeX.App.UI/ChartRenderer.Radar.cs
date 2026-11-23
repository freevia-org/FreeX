using System.Globalization;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public static partial class ChartRenderer
{
    private const string RadarAngleAxisKey = "RadarAngle";
    private const string RadarMagnitudeAxisKey = "RadarMagnitude";

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
            Key = RadarAngleAxisKey,
            StartAngle = 90,
            EndAngle = -270, // clockwise from 12 o'clock, as Excel lays the categories out
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
        model.Axes.Add(new MagnitudeAxis { Key = RadarMagnitudeAxisKey, Minimum = 0, Title = chart.YAxisTitle });

        for (uint col = dataStartCol; col <= endCol; col++)
        {
            var seriesIndex = GetSeriesIndex(chart, col, dataStartCol);
            var seriesName = chart.FirstRowIsHeader && cellLookup.TryGetValue((headerRow, col), out var header)
                ? header.DisplayText
                : $"Series {seriesIndex + 1}";
            var series = CreateLineSeries(chart, seriesName, seriesIndex, theme);
            series.MarkerType = MarkerType.Circle;
            if (chart.RadarFilled == true)
            {
                // Excel's filled radar has no markers and an outline-less fill; OxyPlot's polar plot cannot fill a
                // polygon, so the series is drawn as its coloured outline instead of vanishing.
                series.MarkerType = MarkerType.None;
                if (series.LineStyle == LineStyle.None)
                    series.LineStyle = LineStyle.Solid;
            }
            // OxyPlot's polar plots take the magnitude from the X axis and the angle from the Y axis ("angle axis
            // should always be the y-axis"); a point built the other way round lands at a huge radius.
            series.XAxisKey = RadarMagnitudeAxisKey;
            series.YAxisKey = RadarAngleAxisKey;

            DataPoint? firstPoint = null;
            var i = 0;
            for (uint row = dataStartRow; row <= endRow; row++, i++)
            {
                if (!cellLookup.TryGetValue((row, col), out var cell) ||
                    !TryGetChartNumericValue(cell, out var value))
                    continue;

                var point = new DataPoint(value, i * 360.0 / pointCount);
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
