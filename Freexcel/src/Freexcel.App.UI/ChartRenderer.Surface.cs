using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using Freexcel.Core.Model;

namespace Freexcel.App.UI;

public static partial class ChartRenderer
{
    private static PlotModel BuildSurfaceModel(
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
        var seriesNames = new List<string>();
        for (uint col = dataStartCol; col <= endCol; col++)
        {
            seriesNames.Add(chart.FirstRowIsHeader && cellLookup.TryGetValue((headerRow, col), out var header)
                ? header.DisplayText
                : $"Series {seriesNames.Count + 1}");
        }

        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Title = chart.XAxisTitle,
            Minimum = -0.5,
            Maximum = Math.Max(0.5, categories.Count - 0.5),
            MajorStep = 1,
            MinorStep = 1,
            LabelFormatter = value =>
            {
                var index = (int)Math.Round(value);
                return index >= 0 && index < categories.Count ? categories[index] : "";
            }
        });
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = chart.YAxisTitle,
            Minimum = -0.5,
            Maximum = Math.Max(0.5, seriesNames.Count - 0.5),
            MajorStep = 1,
            MinorStep = 1,
            LabelFormatter = value =>
            {
                var index = (int)Math.Round(value);
                return index >= 0 && index < seriesNames.Count ? seriesNames[index] : "";
            }
        });

        var surfaceValues = new List<(int CategoryIndex, int SeriesIndex, double Value)>();
        var scanSeriesIndex = 0;
        for (uint col = dataStartCol; col <= endCol; col++, scanSeriesIndex++)
        {
            var categoryIndex = 0;
            for (uint row = dataStartRow; row <= endRow; row++, categoryIndex++)
            {
                if (cellLookup.TryGetValue((row, col), out var cell) &&
                    double.TryParse(cell.DisplayText, out var value))
                    surfaceValues.Add((categoryIndex, scanSeriesIndex, value));
            }
        }

        var minValue = surfaceValues.Count == 0 ? 0 : surfaceValues.Min(item => item.Value);
        var maxValue = surfaceValues.Count == 0 ? 0 : surfaceValues.Max(item => item.Value);
        var surfaceSeries = new RectangleBarSeries { Title = chart.Title ?? "Surface" };
        ApplyRectangleBarFormat(surfaceSeries, GetSeriesFormat(chart, 0), theme);

        foreach (var (categoryIndex, seriesIndex, value) in surfaceValues)
        {
            surfaceSeries.Items.Add(new RectangleBarItem(
                categoryIndex - 0.45,
                seriesIndex - 0.45,
                categoryIndex + 0.45,
                seriesIndex + 0.45)
            {
                Color = GetSurfaceCellColor(value, minValue, maxValue)
            });
        }

        model.Series.Add(surfaceSeries);
        return model;
    }

    private static OxyColor GetSurfaceCellColor(double value, double minValue, double maxValue)
    {
        var t = maxValue <= minValue ? 0.5 : Math.Clamp((value - minValue) / (maxValue - minValue), 0, 1);
        var red = (byte)Math.Round(68 + (255 - 68) * t);
        var green = (byte)Math.Round(114 + (192 - 114) * t);
        var blue = (byte)Math.Round(196 + (0 - 196) * t);
        return OxyColor.FromRgb(red, green, blue);
    }
}
