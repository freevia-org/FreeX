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

    private static ViewportModel Viewport() =>
        new(
            [],
            [new RowMetric(1, 20, 0), new RowMetric(2, 20, 20), new RowMetric(3, 20, 40)],
            [new ColMetric(1, 60, 0), new ColMetric(2, 80, 60), new ColMetric(3, 60, 140)]);
}
