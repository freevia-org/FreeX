using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace FreeX.App.UI.Tests;

public sealed partial class GridViewSplitPaneLayoutTests
{
    [Fact]
    public void CalculateFormulaTraceArrowLayouts_ReturnsCenterPointsForVisibleSameSheetCells()
    {
        var sheetId = SheetId.New();
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0), new RowMetric(2, 20, 20)],
            [new ColMetric(1, 64, 0), new ColMetric(2, 64, 64)],
            null,
            []);

        var arrows = GridView.CalculateFormulaTraceArrowLayouts(
            viewport,
            [new FormulaTraceArrow(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2))],
            sheetId);

        arrows.Should().ContainSingle().Which.Should().Be(
            new FormulaTraceArrowLayout(
                new Point(GridView.RowHeaderWidth + 32, GridView.ColHeaderHeight + 10),
                new Point(GridView.RowHeaderWidth + 64 + 32, GridView.ColHeaderHeight + 20 + 10)));
    }

    [Fact]
    public void FormulaTraceLayoutPlanner_ReturnsCenterPointsOutsideGridView()
    {
        var sheetId = SheetId.New();
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0), new RowMetric(2, 20, 20)],
            [new ColMetric(1, 64, 0), new ColMetric(2, 64, 64)],
            null,
            []);

        var arrows = FormulaTraceLayoutPlanner.CalculateLayouts(
            viewport,
            [new FormulaTraceArrow(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2))],
            sheetId);

        arrows.Should().ContainSingle().Which.Should().Be(
            new FormulaTraceArrowLayout(
                new Point(GridView.RowHeaderWidth + 32, GridView.ColHeaderHeight + 10),
                new Point(GridView.RowHeaderWidth + 64 + 32, GridView.ColHeaderHeight + 20 + 10)));
    }

    [Fact]
    public void FormulaTraceLayoutPlanner_ReturnsMultipleLayoutsWithMetricLookups()
    {
        var sheetId = SheetId.New();
        var otherSheetId = SheetId.New();
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0), new RowMetric(2, 24, 20), new RowMetric(4, 30, 44)],
            [new ColMetric(1, 64, 0), new ColMetric(2, 80, 64), new ColMetric(4, 100, 144)],
            null,
            []);

        var arrows = FormulaTraceLayoutPlanner.CalculateLayouts(
            viewport,
            [
                new FormulaTraceArrow(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2)),
                new FormulaTraceArrow(new CellAddress(sheetId, 4, 4), new CellAddress(sheetId, 8, 4)),
                new FormulaTraceArrow(new CellAddress(otherSheetId, 1, 1), new CellAddress(sheetId, 1, 1))
            ],
            sheetId);

        arrows.Should().Equal(
            new FormulaTraceArrowLayout(
                new Point(GridView.RowHeaderWidth + 32, GridView.ColHeaderHeight + 10),
                new Point(GridView.RowHeaderWidth + 64 + 40, GridView.ColHeaderHeight + 20 + 12)),
            new FormulaTraceArrowLayout(
                new Point(GridView.RowHeaderWidth + 144 + 50, GridView.ColHeaderHeight + 44 + 15),
                new Point(GridView.RowHeaderWidth + 144 + 50, GridView.ColHeaderHeight + 44 + 15),
                FormulaTraceArrowLayoutKind.OffscreenMarker,
                new CellAddress(sheetId, 8, 4)),
            new FormulaTraceArrowLayout(
                new Point(GridView.RowHeaderWidth + 32, GridView.ColHeaderHeight + 10),
                new Point(GridView.RowHeaderWidth + 32, GridView.ColHeaderHeight + 10),
                FormulaTraceArrowLayoutKind.CrossSheetMarker,
                new CellAddress(otherSheetId, 1, 1)));
    }

    [Fact]
    public void FormulaTraceLayoutPlanner_VisitLayouts_MatchesCalculateLayouts()
    {
        var sheetId = SheetId.New();
        var otherSheetId = SheetId.New();
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0), new RowMetric(2, 24, 20), new RowMetric(4, 30, 44)],
            [new ColMetric(1, 64, 0), new ColMetric(2, 80, 64), new ColMetric(4, 100, 144)],
            null,
            []);
        FormulaTraceArrow[] arrows =
        [
            new(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2)),
            new(new CellAddress(sheetId, 4, 4), new CellAddress(sheetId, 8, 4)),
            new(new CellAddress(otherSheetId, 1, 1), new CellAddress(sheetId, 1, 1))
        ];

        var expected = FormulaTraceLayoutPlanner.CalculateLayouts(viewport, arrows, sheetId);
        var consumer = new CollectingFormulaTraceArrowLayoutConsumer();

        FormulaTraceLayoutPlanner.VisitLayouts(viewport, arrows, sheetId, ref consumer);

        consumer.Layouts.Should().Equal(expected);
    }

    [Fact]
    public void CalculateFormulaTraceArrowLayouts_ReturnsMarkersForCrossSheetAndOffscreenCells()
    {
        var sheetId = SheetId.New();
        var otherSheetId = SheetId.New();
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0)],
            [new ColMetric(1, 64, 0)],
            null,
            []);

        var arrows = GridView.CalculateFormulaTraceArrowLayouts(
            viewport,
            [
                new FormulaTraceArrow(new CellAddress(otherSheetId, 1, 1), new CellAddress(sheetId, 1, 1)),
                new FormulaTraceArrow(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 1))
            ],
            sheetId);

        arrows.Should().Equal(
            new FormulaTraceArrowLayout(
                new Point(GridView.RowHeaderWidth + 32, GridView.ColHeaderHeight + 10),
                new Point(GridView.RowHeaderWidth + 32, GridView.ColHeaderHeight + 10),
                FormulaTraceArrowLayoutKind.CrossSheetMarker,
                new CellAddress(otherSheetId, 1, 1)),
            new FormulaTraceArrowLayout(
                new Point(GridView.RowHeaderWidth + 32, GridView.ColHeaderHeight + 10),
                new Point(GridView.RowHeaderWidth + 32, GridView.ColHeaderHeight + 10),
                FormulaTraceArrowLayoutKind.OffscreenMarker,
                new CellAddress(sheetId, 2, 1)));
    }

    [Fact]
    public void HitTestFormulaTraceMarker_ReturnsHiddenCellNavigationTarget()
    {
        var sheetId = SheetId.New();
        var otherSheetId = SheetId.New();
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0)],
            [new ColMetric(1, 64, 0)],
            null,
            []);
        var visible = new CellAddress(sheetId, 1, 1);
        var offscreen = new CellAddress(sheetId, 2, 1);
        var crossSheet = new CellAddress(otherSheetId, 1, 1);
        var markerPoint = new Point(GridView.RowHeaderWidth + 32, GridView.ColHeaderHeight + 10);

        GridView.HitTestFormulaTraceMarker(
                viewport,
                [new FormulaTraceArrow(visible, offscreen)],
                sheetId,
                markerPoint)
            .Should().Be(offscreen);

        GridView.HitTestFormulaTraceMarker(
                viewport,
                [new FormulaTraceArrow(crossSheet, visible)],
                sheetId,
                markerPoint)
            .Should().Be(crossSheet);

        GridView.HitTestFormulaTraceMarker(
                viewport,
                [new FormulaTraceArrow(visible, visible)],
                sheetId,
                markerPoint)
            .Should().BeNull();
    }

    [Fact]
    public void HitTestFormulaTraceMarker_StreamsArrowsWithoutBuildingLayouts()
    {
        var sheetId = SheetId.New();
        var viewport = new ViewportModel(
            [],
            Enumerable.Range(1, 80).Select(row => new RowMetric((uint)row, 20, (row - 1) * 20)).ToList(),
            Enumerable.Range(1, 20).Select(col => new ColMetric((uint)col, 64, (col - 1) * 64)).ToList(),
            null,
            []);
        var visible = new CellAddress(sheetId, 1, 1);
        var arrows = Enumerable.Range(1, 5_000)
            .Select(row => new FormulaTraceArrow(visible, new CellAddress(sheetId, (uint)(200 + row), 1)))
            .ToList();
        var markerPoint = new Point(GridView.RowHeaderWidth + 32, GridView.ColHeaderHeight + 10);

        FormulaTraceLayoutPlanner.HitTestMarker(viewport, arrows, sheetId, markerPoint)
            .Should().Be(new CellAddress(sheetId, 201, 1));

        var elapsed = Stopwatch.StartNew();
        for (var i = 0; i < 500; i++)
            FormulaTraceLayoutPlanner.HitTestMarker(viewport, arrows, sheetId, markerPoint);
        elapsed.Stop();

        elapsed.ElapsedMilliseconds.Should().BeLessThan(1_500);
    }

    [Fact]
    public void FormulaTraceLayoutPlanner_StopsSingleMetricLookupsOnceSortedMetricsPassAddress()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "FormulaTraceLayoutPlanner.cs"));
        var metricLookup = source[
            source.IndexOf("private readonly struct FormulaTraceMetricLookup", StringComparison.Ordinal)..
            source.IndexOf("private static bool TryGetMarkerHit", StringComparison.Ordinal)];

        metricLookup.Should().Contain("_firstRow = _hasRows ? _rows[0].Row : 0;");
        metricLookup.Should().Contain("_lastRow = _hasRows ? _rows[^1].Row : 0;");
        metricLookup.Should().Contain("_firstCol = _hasColumns ? _columns[0].Col : 0;");
        metricLookup.Should().Contain("_lastCol = _hasColumns ? _columns[^1].Col : 0;");
        metricLookup.Should().Contain("row < _firstRow || row > _lastRow");
        metricLookup.Should().Contain("col < _firstCol || col > _lastCol");
        metricLookup.Should().Contain("FormulaTraceLayoutPlanner.FindRowMetric(_rowArray, row, _firstRow)");
        metricLookup.Should().Contain("FormulaTraceLayoutPlanner.FindColMetric(_colArray, col, _firstCol)");
        source.Should().Contain("var index = row - firstRow;");
        source.Should().Contain("var index = col - firstCol;");
        source.Should().Contain("while (low <= high)");
        source.Should().NotContain("Dictionary<uint, RowMetric>");
        source.Should().NotContain("Dictionary<uint, ColMetric>");
        source.Should().NotContain("BuildRowMetricLookup");
        source.Should().NotContain("BuildColMetricLookup");
        source.Should().NotContain("TryGetValue");
    }
}
