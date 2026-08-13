using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;

namespace FreeX.App.UI;

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
        var seriesCount = (int)Math.Min(int.MaxValue, endCol - dataStartCol + 1);
        var categoryCount = (int)Math.Min(int.MaxValue, endRow - dataStartRow + 1);
        var seriesNames = new List<string>(seriesCount);
        for (uint col = dataStartCol; col <= endCol; col++)
        {
            seriesNames.Add(chart.FirstRowIsHeader && cellLookup.TryGetValue((headerRow, col), out var header)
                ? header.DisplayText
                : $"Series {seriesNames.Count + 1}");
        }

        model.Axes.Add(CreateCenteredIndexedCategoryAxis(AxisPosition.Bottom, chart.XAxisTitle, categories));
        model.Axes.Add(CreateCenteredIndexedCategoryAxis(AxisPosition.Left, chart.YAxisTitle, seriesNames));

        var surfaceValueCapacity = categoryCount > 0 && seriesCount <= int.MaxValue / categoryCount
            ? seriesCount * categoryCount
            : 0;
        var surfaceValues = new List<(int CategoryIndex, int SeriesIndex, double Value)>(surfaceValueCapacity);
        var minValue = 0d;
        var maxValue = 0d;
        var scanSeriesIndex = 0;
        for (uint col = dataStartCol; col <= endCol; col++, scanSeriesIndex++)
        {
            var categoryIndex = 0;
            for (uint row = dataStartRow; row <= endRow; row++, categoryIndex++)
            {
                if (cellLookup.TryGetValue((row, col), out var cell) &&
                    TryGetChartNumericValue(cell, out var value))
                {
                    if (surfaceValues.Count == 0)
                    {
                        minValue = value;
                        maxValue = value;
                    }
                    else
                    {
                        minValue = Math.Min(minValue, value);
                        maxValue = Math.Max(maxValue, value);
                    }

                    surfaceValues.Add((categoryIndex, scanSeriesIndex, value));
                }
            }
        }

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
                Color = ToOxyColor(ChartRenderPolicyPlanner.ResolveSurfaceCellColor(value, minValue, maxValue))
                    ?? OxyColors.Transparent
            });
        }

        model.Series.Add(surfaceSeries);
        return model;
    }

}
