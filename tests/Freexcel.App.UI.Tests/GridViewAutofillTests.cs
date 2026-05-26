using FluentAssertions;
using Freexcel.App.UI;
using Freexcel.Core.Model;

namespace Freexcel.App.UI.Tests;

public sealed class GridViewAutofillTests
{
    [Fact]
    public void ConstrainAutofillTarget_PrefersVerticalAxisWhenDragExtendsFartherDown()
    {
        var sheet = SheetId.New();
        var source = new GridRange(
            new CellAddress(sheet, 2, 2),
            new CellAddress(sheet, 3, 3));

        var target = GridView.ConstrainAutofillTarget(
            source,
            new CellAddress(sheet, 8, 6));

        target.Should().Be(new CellAddress(sheet, 8, 3));
        GridAutofillPlanner.ConstrainTarget(source, new CellAddress(sheet, 8, 6))
            .Should()
            .Be(target);
    }

    [Fact]
    public void ConstrainAutofillTarget_PrefersHorizontalAxisWhenDragExtendsFartherRight()
    {
        var sheet = SheetId.New();
        var source = new GridRange(
            new CellAddress(sheet, 2, 2),
            new CellAddress(sheet, 3, 3));

        var target = GridView.ConstrainAutofillTarget(
            source,
            new CellAddress(sheet, 5, 9));

        target.Should().Be(new CellAddress(sheet, 3, 9));
    }

    [Fact]
    public void CalculateAutofillEdgeScrollIntent_RequestsHorizontalScrollNearRightEdge()
    {
        GridView.CalculateAutofillEdgeScrollIntent(
                pointerX: 795,
                pointerY: 120,
                width: 800,
                height: 600,
                rowHeaderWidth: 48,
                columnHeaderHeight: 24)
            .Should()
            .Be(new GridAutoScrollRequest(1, 0));
        GridAutofillPlanner.CalculateEdgeScrollIntent(
                pointerX: 795,
                pointerY: 120,
                width: 800,
                height: 600,
                rowHeaderWidth: 48,
                columnHeaderHeight: 24)
            .Should()
            .Be(new GridAutoScrollRequest(1, 0));
    }

    [Fact]
    public void CalculateAutofillEdgeScrollIntent_IgnoresPointerAwayFromEdges()
    {
        GridView.CalculateAutofillEdgeScrollIntent(
                pointerX: 400,
                pointerY: 300,
                width: 800,
                height: 600,
                rowHeaderWidth: 48,
                columnHeaderHeight: 24)
            .Should()
            .Be(new GridAutoScrollRequest(0, 0));
        GridAutofillPlanner.CalculateEdgeScrollIntent(
                pointerX: 400,
                pointerY: 300,
                width: 800,
                height: 600,
                rowHeaderWidth: 48,
                columnHeaderHeight: 24)
            .Should()
            .Be(new GridAutoScrollRequest(0, 0));
    }

    [Fact]
    public void CalculateDragTarget_ReturnsFarthestVisibleCellWithinSourceAndPointerBounds()
    {
        var sheet = SheetId.New();
        var viewport = CreateViewport();
        var source = new GridRange(
            new CellAddress(sheet, 2, 2),
            new CellAddress(sheet, 3, 3));

        GridAutofillPlanner.CalculateDragTarget(
                viewport,
                source,
                new System.Windows.Point(240, 130),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18)
            .Should()
            .Be(new CellAddress(default, 5, 5));
    }

    [Fact]
    public void CalculateDragTarget_ReturnsNullWhenSourceMetricsAreNotVisible()
    {
        var sheet = SheetId.New();
        var source = new GridRange(
            new CellAddress(sheet, 99, 2),
            new CellAddress(sheet, 100, 3));

        GridAutofillPlanner.CalculateDragTarget(
                CreateViewport(),
                source,
                new System.Windows.Point(240, 130),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18)
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
