using FluentAssertions;
using FreeX.App.Presentation.GridInteraction;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.GridInteraction;

public sealed class GridSelectionMovePlannerTests
{
    [Fact]
    public void IsOnMoveBorder_RecognizesSelectionBorderButNotInteriorOrAutofillHandle()
    {
        var sheet = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheet, 2, 2),
            new CellAddress(sheet, 3, 3));
        var viewport = CreateViewport();

        GridSelectionMovePlanner.IsOnMoveBorder(
                viewport,
                range,
                null,
                new GridPoint(100, 38),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18)
            .Should()
            .BeTrue();

        GridSelectionMovePlanner.IsOnMoveBorder(
                viewport,
                range,
                null,
                new GridPoint(100, 58),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18)
            .Should()
            .BeFalse();

        GridSelectionMovePlanner.IsOnMoveBorder(
                viewport,
                range,
                null,
                new GridPoint(150, 78),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18)
            .Should()
            .BeFalse("the autofill handle keeps priority over move-border dragging");
    }

    [Fact]
    public void IsOnMoveBorder_IgnoresMultiRangeSelections()
    {
        var sheet = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheet, 2, 2),
            new CellAddress(sheet, 3, 3));

        GridSelectionMovePlanner.IsOnMoveBorder(
                CreateViewport(),
                range,
                [range],
                new GridPoint(100, 38),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void IsOnMoveBorder_UsesScaledMetricsAndTheRenderedHandleHitTarget()
    {
        var sheet = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheet, 2, 2),
            new CellAddress(sheet, 3, 3));
        var viewport = CreateViewport();

        GridSelectionMovePlanner.IsOnMoveBorder(
                viewport,
                range,
                null,
                new GridPoint(150, 48),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18,
                metricScale: 1.5,
                handleSize: 10,
                handleHitPadding: 6)
            .Should()
            .BeTrue("the scaled top edge remains draggable");

        GridSelectionMovePlanner.IsOnMoveBorder(
                viewport,
                range,
                null,
                new GridPoint(210, 108),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18,
                metricScale: 1.5,
                handleSize: 10,
                handleHitPadding: 6)
            .Should()
            .BeFalse("the visible fill handle and its padded hit target keep priority");
    }

    [Fact]
    public void IsOnMoveBorder_ReturnsFalseWhenViewportOrSelectionIsMissing()
    {
        var sheet = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheet, 2, 2),
            new CellAddress(sheet, 3, 3));

        GridSelectionMovePlanner.IsOnMoveBorder(
                null,
                range,
                null,
                new GridPoint(100, 38),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18)
            .Should()
            .BeFalse();

        GridSelectionMovePlanner.IsOnMoveBorder(
                CreateViewport(),
                null,
                null,
                new GridPoint(100, 38),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void ClampDragStartCell_ClampsAnchorIntoSourceRange()
    {
        var sheet = SheetId.New();
        var source = new GridRange(
            new CellAddress(sheet, 2, 2),
            new CellAddress(sheet, 5, 5));

        GridSelectionMovePlanner.ClampDragStartCell(source, new CellAddress(sheet, 9, 1))
            .Should()
            .Be(new CellAddress(sheet, 5, 2));
    }

    [Fact]
    public void CalculateTargetRange_PreservesGrabbedCellOffset()
    {
        var sheet = SheetId.New();
        var source = new GridRange(
            new CellAddress(sheet, 2, 2),
            new CellAddress(sheet, 3, 3));

        GridSelectionMovePlanner.CalculateTargetRange(
                source,
                new CellAddress(sheet, 3, 3),
                new CellAddress(sheet, 5, 5))
            .Should()
            .Be(new GridRange(
                new CellAddress(sheet, 4, 4),
                new CellAddress(sheet, 5, 5)));
    }

    [Fact]
    public void CalculateTargetRange_ReturnsNullWhenMoveWouldLeaveWorksheet()
    {
        var sheet = SheetId.New();
        var source = new GridRange(
            new CellAddress(sheet, 2, 2),
            new CellAddress(sheet, 3, 3));

        GridSelectionMovePlanner.CalculateTargetRange(
                source,
                new CellAddress(sheet, 3, 3),
                new CellAddress(sheet, 1, 1))
            .Should()
            .BeNull();
    }

    private static ViewportModel CreateViewport() =>
        new(
            [],
            [
                new RowMetric(1, 20, 0),
                new RowMetric(2, 20, 20),
                new RowMetric(3, 20, 40),
                new RowMetric(4, 20, 60),
                new RowMetric(5, 20, 80)
            ],
            [
                new ColMetric(1, 40, 0),
                new ColMetric(2, 40, 40),
                new ColMetric(3, 40, 80),
                new ColMetric(4, 40, 120),
                new ColMetric(5, 40, 160)
            ]);
}
