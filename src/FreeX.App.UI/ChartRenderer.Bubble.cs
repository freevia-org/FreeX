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
        uint dataStartCol,
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
        // chart.DataRange.Start.Col directly rather than the dataStartCol parameter (which shifts
        // when FirstColIsCategories is set). R113-render-chart-embedded-fallback-all-types:
        // BuildEmbeddedCellLookup places its synthesized shared-X column at column 1, matching the
        // 1x1 placeholder DataRange (Start.Col == 1) every embedded-fallback reader sets, so this
        // still resolves correctly for a fallback-loaded chart -- except the rare case where a
        // Bubble chart's series is an unresolvable named range AND references a cross-sheet range
        // whose union DataRange happens to start at a column other than 1 (TryReadCrossSheetEmbeddedData);
        // that combination is not reproduced exactly by this fallback.
        var xCol = chart.DataRange.Start.Col;

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
                var size = BubbleRadius(Math.Abs(rawSize), maxSize, chart.BubbleSizeRepresents) * bubbleScale;
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

    // Mirrors ChartLayoutEngine.BubbleRadius (Avalonia renderer) so WPF and Avalonia draw bubbles at
    // the same relative sizes: Area representation keeps bubble area proportional to the size value
    // (radius scales with the square root of the size fraction), while Width scales the radius linearly.
    private const double MaxBubbleRadius = 20.0;
    private const double MinBubbleRadius = 1.0;

    private static double BubbleRadius(double size, double maxSize, ChartBubbleSizeRepresents represents)
    {
        if (maxSize <= 0)
            return MinBubbleRadius;

        var fraction = Math.Clamp(size / maxSize, 0, 1);
        var radiusFraction = represents == ChartBubbleSizeRepresents.Width ? fraction : Math.Sqrt(fraction);
        return Math.Max(MinBubbleRadius, MaxBubbleRadius * radiusFraction);
    }
}
