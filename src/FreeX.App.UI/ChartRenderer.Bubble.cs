using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public static partial class ChartRenderer
{
    private static PlotModel BuildBubbleModel(
        ChartModel chart,
        PlotModel model,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        IReadOnlyList<string> categories,
        uint dataStartRow,
        uint endRow,
        uint sharedXCol,
        uint endCol,
        uint headerRow,
        WorkbookTheme theme,
        ChartPointDataLabelFormatLookup pointDataLabelFormats,
        out List<DataPoint> trendPoints)
    {
        model.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Title = chart.XAxisTitle });
        model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = chart.YAxisTitle });
        trendPoints = [];

        // Bubble deliberately ignores FirstColIsCategories -- the first column of DataRange is
        // ALWAYS the shared X column for a bubble chart (see
        // BubbleRenderer_IgnoresCategoryFlagAndUsesFirstRangeColumnAsXValues) -- so this reads
        // the unshifted start column rather than the FirstColIsCategories-shifted dataStartCol
        // BuildPlotModel passes to most other chart types. R114: that unshifted column is
        // BuildPlotModel's local `startCol`, NOT chart.DataRange.Start.Col -- the two agree for a
        // live (non-fallback) chart (startCol is read straight from chart.DataRange.Start.Col at
        // the top of BuildPlotModel), but a cross-sheet embedded-fallback chart (R113:
        // BuildEmbeddedCellLookup) reassigns startCol to match the column it actually synthesized
        // (1) while chart.DataRange.Start.Col keeps the REAL worksheet column the resolved
        // cross-sheet range started at (e.g. 2 for 'Data'!$B$2:$D$10) -- reading the latter would
        // shift every Y/size column lookup below by one and silently drop every point.
        var xCol = sharedXCol;

        // Matches the Avalonia ChartLayoutEngine.LayoutBubble reference: the bubble radius scale is
        // derived from the largest size value across every series in the chart, not just the current
        // one, so a first pass collects that shared maximum before any points are laid out.
        var maxSize = 0.0;
        for (var sizeScanCol = xCol + 2; sizeScanCol <= endCol; sizeScanCol += 2)
        {
            for (uint row = dataStartRow; row <= endRow; row++)
            {
                if (TryGetNumericCell(cellLookup, row, sizeScanCol, out var scannedSize))
                    maxSize = Math.Max(maxSize, Math.Abs(scannedSize));
            }
        }

        var bubbleScale = Math.Max(0, chart.BubbleScale) / 100.0;

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
                var rawSize = TryGetNumericCell(cellLookup, row, sizeCol, out var sizeValue) ? sizeValue : 1;
                if (rawSize < 0 && !chart.ShowNegativeBubbles)
                    continue;
                var size = ChartRenderPolicyPlanner.ResolveBubbleRadius(
                    Math.Abs(rawSize),
                    maxSize,
                    chart.BubbleSizeRepresents) * bubbleScale;
                series.Points.Add(new ScatterPoint(x, y, size));
                if (seriesIndex == 0)
                    trendPoints.Add(new DataPoint(x, y));
                if (ShouldUseAnnotationLabels(chart))
                    AddDataLabelAnnotation(model, chart, theme, pointDataLabelFormats, seriesName, seriesIndex, fallbackIndex, ChartDataLabelTextPlanner.GetCategory(categories, fallbackIndex), x, y, y);
            }

            model.Series.Add(series);
            seriesIndex++;
        }

        return model;
    }

}
