using FluentAssertions;
using Freexcel.App.UI;
using Freexcel.Core.Model;
using System.Windows;

namespace Freexcel.App.UI.Tests;

public sealed class GridViewSelectionLayoutTests
{
    [Fact]
    public void CalculateVisibleSelectionRect_ReturnsNullWhenSelectedRowIsNotVisible()
    {
        var sheetId = SheetId.New();
        var viewport = Viewport();
        var range = new GridRange(
            new CellAddress(sheetId, 5, 2),
            new CellAddress(sheetId, 5, 2));

        var rect = GridView.CalculateVisibleSelectionRect(
            viewport,
            range,
            GridView.RowHeaderWidth,
            GridView.ColHeaderHeight);

        rect.Should().BeNull();
    }

    [Fact]
    public void CalculateVisibleSelectionRect_ReturnsNullWhenSelectedColumnIsNotVisible()
    {
        var sheetId = SheetId.New();
        var viewport = Viewport();
        var range = new GridRange(
            new CellAddress(sheetId, 2, 5),
            new CellAddress(sheetId, 2, 5));

        var rect = GridView.CalculateVisibleSelectionRect(
            viewport,
            range,
            GridView.RowHeaderWidth,
            GridView.ColHeaderHeight);

        rect.Should().BeNull();
    }

    [Fact]
    public void CalculateVisibleSelectionRect_UsesVisibleIntersectionForWholeRowSelection()
    {
        var sheetId = SheetId.New();
        var viewport = Viewport();
        var range = new GridRange(
            new CellAddress(sheetId, 2, 1),
            new CellAddress(sheetId, 2, CellAddress.MaxCol));

        var rect = GridView.CalculateVisibleSelectionRect(
            viewport,
            range,
            GridView.RowHeaderWidth,
            GridView.ColHeaderHeight);

        rect.Should().Be(new Rect(30, 38, 200, 20));
    }

    [Fact]
    public void CalculateVisibleSelectionRect_DoesNotTreatNonContiguousFrozenMetricsAsContinuous()
    {
        var sheetId = SheetId.New();
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0), new RowMetric(20, 20, 20)],
            [new ColMetric(1, 64, 0), new ColMetric(20, 64, 64)]);
        var range = new GridRange(
            new CellAddress(sheetId, 10, 10),
            new CellAddress(sheetId, 10, 10));

        var rect = GridView.CalculateVisibleSelectionRect(
            viewport,
            range,
            GridView.RowHeaderWidth,
            GridView.ColHeaderHeight);

        rect.Should().BeNull();
    }

    [Fact]
    public void CalculateClipboardMarquee_ReturnsVisibleRectangle_ForCopiedRange()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 2, 2),
            new CellAddress(sheetId, 3, 3));
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0), new RowMetric(2, 20, 20), new RowMetric(3, 20, 40)],
            [new ColMetric(1, 64, 0), new ColMetric(2, 64, 64), new ColMetric(3, 64, 128)]);

        var rect = GridView.CalculateClipboardMarquee(
            viewport,
            range,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18);

        rect.Should().Be(new Rect(94, 38, 128, 40));
    }

    [Fact]
    public void CalculateClipboardMarquee_ReturnsNull_WhenRangeIsOutsideViewport()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 10, 10),
            new CellAddress(sheetId, 11, 11));
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0), new RowMetric(2, 20, 20)],
            [new ColMetric(1, 64, 0), new ColMetric(2, 64, 64)]);

        GridView.CalculateClipboardMarquee(
            viewport,
            range,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18).Should().BeNull();
    }

    [Fact]
    public void CalculateQuickAnalysisPreviewRect_ReturnsVisibleRectangle_ForHoverRange()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 2, 2),
            new CellAddress(sheetId, 3, 3));
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0), new RowMetric(2, 20, 20), new RowMetric(3, 20, 40)],
            [new ColMetric(1, 64, 0), new ColMetric(2, 64, 64), new ColMetric(3, 64, 128)]);

        var rect = GridView.CalculateQuickAnalysisPreviewRect(
            viewport,
            range,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18);

        rect.Should().Be(new Rect(94, 38, 128, 40));
    }

    [Fact]
    public void CalculateQuickAnalysisDataBarPreviewRects_ReturnsProportionalBarsForVisibleNumericCells()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 2, 2));
        var viewport = new ViewportModel(
            [
                new DisplayCell(1, 1, new NumberValue(10), "10", null, StyleId.Default, null),
                new DisplayCell(1, 2, new NumberValue(20), "20", null, StyleId.Default, null),
                new DisplayCell(2, 1, new TextValue("n/a"), "n/a", null, StyleId.Default, null),
                new DisplayCell(2, 2, new NumberValue(0), "0", null, StyleId.Default, null)
            ],
            [new RowMetric(1, 20, 0), new RowMetric(2, 20, 20)],
            [new ColMetric(1, 64, 0), new ColMetric(2, 64, 64)]);

        var rects = GridView.CalculateQuickAnalysisDataBarPreviewRects(
            viewport,
            range,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18);

        rects.Should().Equal(
            new Rect(33, 22, 29, 12),
            new Rect(97, 22, 58, 12),
            new Rect(97, 42, 0, 12));
    }

    [Fact]
    public void CalculateQuickAnalysisCellPreviewRects_ReturnsVisibleCellRects_ForOverlayVisuals()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 2, 2));
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0), new RowMetric(2, 20, 20), new RowMetric(4, 20, 40)],
            [new ColMetric(1, 64, 0), new ColMetric(2, 64, 64), new ColMetric(4, 64, 128)]);

        var rects = GridView.CalculateQuickAnalysisCellPreviewRects(
            viewport,
            range,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18);

        rects.Should().Equal(
            new Rect(33, 21, 58, 14),
            new Rect(97, 21, 58, 14),
            new Rect(33, 41, 58, 14),
            new Rect(97, 41, 58, 14));
    }

    [Fact]
    public void CalculateQuickAnalysisSparklinePreviewRects_ReturnsCompactRectPerVisibleTargetRow()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 1, 3),
            new CellAddress(sheetId, 3, 3));
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0), new RowMetric(2, 24, 20), new RowMetric(4, 20, 44)],
            [new ColMetric(1, 64, 0), new ColMetric(3, 72, 64)]);

        var rects = GridView.CalculateQuickAnalysisSparklinePreviewRects(
            viewport,
            range,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18);

        rects.Should().Equal(
            new Rect(100, 25, 60, 6),
            new Rect(100, 46, 60, 8));
    }

    [Fact]
    public void CalculateQuickAnalysisSparklinePreviewRects_SkipsTinyTargetsThatCannotContainPreviewBars()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 1, 3),
            new CellAddress(sheetId, 1, 3));
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0)],
            [new ColMetric(3, 10, 64)]);

        var rects = GridView.CalculateQuickAnalysisSparklinePreviewRects(
            viewport,
            range,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18);

        rects.Should().BeEmpty();
    }

    private static ViewportModel Viewport() =>
        new(
            [],
            [new RowMetric(1, 20, 0), new RowMetric(2, 20, 20), new RowMetric(3, 20, 40)],
            [new ColMetric(1, 60, 0), new ColMetric(2, 80, 60), new ColMetric(3, 60, 140)]);
}
