using System.Diagnostics;
using System.Reflection;
using FreeX.App.UI;
using FreeX.Core.Model;
using FluentAssertions;
using OxyPlot;

namespace FreeX.App.UI.Tests;

public sealed class ChartRendererPerformanceMeasurementTests
{
    [BenchmarkFact]
    public void Benchmark_BuildPlotModelWithDenseDataLabelFormats_ReportsTiming()
    {
        const int iterations = 64;
        var (chart, viewport) = CreateDenseFormattedLineChart();
        BuildPlotModel(chart, viewport).Series.Should().HaveCount(8);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var annotationCount = 0;
        for (var i = 0; i < iterations; i++)
            annotationCount += BuildPlotModel(chart, viewport).Annotations.Count;

        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Console.WriteLine(
            "PERF CHART_BUILD_DENSE_FORMAT_LOOKUPS " +
            $"steps={iterations} total_ms={stopwatch.Elapsed.TotalMilliseconds:F2} " +
            $"mean_ms={stopwatch.Elapsed.TotalMilliseconds / iterations:F4} " +
            $"allocated_bytes={allocatedBytes:N0}");

        annotationCount.Should().Be(8 * 48 * iterations);
        stopwatch.Elapsed.TotalMilliseconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public void BuildPlotModelWithTinyRangeInLargeViewport_BoundsLookupAllocation()
    {
        const int iterations = 32;
        var (chart, viewport) = CreateTinyChartRangeInLargeViewport();
        BuildPlotModel(chart, viewport).Series.Should().HaveCount(1);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var pointCount = 0;
        for (var i = 0; i < iterations; i++)
            pointCount += ((OxyPlot.Series.LineSeries)BuildPlotModel(chart, viewport).Series[0]).Points.Count;

        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Console.WriteLine(
            "PERF CHART_BUILD_TINY_RANGE_LARGE_VIEWPORT " +
            $"viewport_cells={viewport.Cells.Count:N0} steps={iterations} " +
            $"total_ms={stopwatch.Elapsed.TotalMilliseconds:F2} " +
            $"mean_ms={stopwatch.Elapsed.TotalMilliseconds / iterations:F4} " +
            $"allocated_bytes={allocatedBytes:N0}");

        pointCount.Should().Be(12 * iterations);
        allocatedBytes.Should().BeLessThan(4_000_000);
    }

    private static (ChartModel Chart, ViewportModel Viewport) CreateDenseFormattedLineChart()
    {
        const int seriesCount = 8;
        const int pointCount = 48;
        var sheetId = SheetId.New();
        var cells = new List<DisplayCell>((pointCount + 1) * (seriesCount + 1));
        cells.Add(Cell(1, 1, "Month"));
        for (uint col = 2; col <= seriesCount + 1; col++)
            cells.Add(Cell(1, col, $"Series {col - 1}"));

        for (uint row = 2; row <= pointCount + 1; row++)
        {
            cells.Add(Cell(row, 1, $"M{row - 1}"));
            for (uint col = 2; col <= seriesCount + 1; col++)
            {
                var value = ((row - 1) * 10) + col;
                cells.Add(new DisplayCell(
                    row,
                    col,
                    new NumberValue(value),
                    value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    null,
                    StyleId.Default,
                    null));
            }
        }

        var chart = new ChartModel
        {
            Type = ChartType.Line,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, pointCount + 1, seriesCount + 1)),
            ShowDataLabels = true,
            ShowDataLabelSeriesName = true,
            DataLabelFillColor = new CellColor(255, 255, 255),
            DataLabelBorderThickness = 1,
            Width = 640,
            Height = 360
        };

        for (var i = 0; i < seriesCount; i++)
        {
            chart.SeriesFormats.Add(new ChartSeriesFormat(
                i,
                FillColor: new CellColor((byte)(80 + i), 110, 180),
                StrokeColor: new CellColor((byte)(40 + i), 90, 160),
                StrokeThickness: 1.5));

            for (var point = 0; point < pointCount; point++)
            {
                chart.PointDataLabelFormats.Add(new ChartPointDataLabelFormat(
                    i,
                    point,
                    TextColor: new CellColor((byte)(30 + i), (byte)(30 + point % 50), 30),
                    FontSize: 9 + point % 3));
            }
        }

        return (chart, new ViewportModel(cells, [], []));
    }

    private static (ChartModel Chart, ViewportModel Viewport) CreateTinyChartRangeInLargeViewport()
    {
        var sheetId = SheetId.New();
        var cells = new List<DisplayCell>(60_000);
        for (uint row = 200; row < 800; row++)
        {
            for (uint col = 20; col < 120; col++)
            {
                var value = row + col;
                cells.Add(new DisplayCell(
                    row,
                    col,
                    new NumberValue(value),
                    value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    null,
                    StyleId.Default,
                    null));
            }
        }

        cells.Add(Cell(1, 1, "Month"));
        cells.Add(Cell(1, 2, "Sales"));
        for (uint row = 2; row <= 13; row++)
        {
            cells.Add(Cell(row, 1, $"M{row - 1}"));
            cells.Add(new DisplayCell(
                row,
                2,
                new NumberValue(row * 10),
                (row * 10).ToString(System.Globalization.CultureInfo.InvariantCulture),
                null,
                StyleId.Default,
                null));
        }

        var chart = new ChartModel
        {
            Type = ChartType.Line,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 13, 2)),
            Width = 640,
            Height = 360
        };

        return (chart, new ViewportModel(cells, [], []));
    }

    private static PlotModel BuildPlotModel(ChartModel chart, ViewportModel viewport)
    {
        return ChartRenderer.BuildPlotModel(chart, viewport).Should().BeOfType<PlotModel>().Subject;
    }

    private static DisplayCell Cell(uint row, uint col, string text) =>
        new(row, col, new TextValue(text), text, null, StyleId.Default, null);
}
