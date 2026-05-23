using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using Freexcel.Core.Model;

namespace Freexcel.App.UI;

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
            AddStockVolumeSeries(model, cellLookup, dataStartRow, endRow, dataStartCol);

        var openCol = hasOpenColumn ? dataStartCol + volumeOffset : (uint?)null;
        var highCol = dataStartCol + volumeOffset + (hasOpenColumn ? 1u : 0u);
        var lowCol = highCol + 1;
        var closeCol = highCol + 2;
        if (valueColumnCount < 3 || closeCol > endCol)
            return model;

        var series = new HighLowSeries
        {
            Title = "Stock",
            StrokeThickness = 1.5,
            Color = OxyColors.Black
        };

        for (uint row = dataStartRow; row <= endRow; row++)
        {
            var index = row - dataStartRow;
            if (!TryGetNumericCell(cellLookup, row, highCol, out var high) ||
                !TryGetNumericCell(cellLookup, row, lowCol, out var low) ||
                !TryGetNumericCell(cellLookup, row, closeCol, out var close))
                continue;

            var open = openCol is { } parsedOpenCol && TryGetNumericCell(cellLookup, row, parsedOpenCol, out var parsedOpen)
                ? parsedOpen
                : close;
            series.Items.Add(new HighLowItem(index, high, low, open, close));
        }

        model.Series.Add(series);
        return model;
    }

    private static void AddStockVolumeSeries(
        PlotModel model,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        uint dataStartRow,
        uint endRow,
        uint volumeCol)
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
            if (TryGetNumericCell(cellLookup, row, volumeCol, out var volume))
                series.Items.Add(new RectangleBarItem(i - 0.35, 0, i + 0.35, volume));
        }

        model.Series.Add(series);
    }
}
