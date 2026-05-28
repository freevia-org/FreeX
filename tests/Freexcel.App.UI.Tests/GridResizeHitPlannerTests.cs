using FluentAssertions;
using Freexcel.App.UI;
using Freexcel.Core.Model;
using System.Windows;

namespace Freexcel.App.UI.Tests;

public sealed class GridResizeHitPlannerTests
{
    [Fact]
    public void HitTest_ReturnsColumnWhenPointerIsNearColumnHeaderRightEdge()
    {
        GridResizeHitPlanner.HitTest(
                CreateViewport(),
                new Point(30 + 40 + 2, 8),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18,
                hitZone: 4)
            .Should()
            .Be(new GridResizeHit(GridResizeHitTarget.Column, 1, 40));
    }

    [Fact]
    public void HitTest_ReturnsRowWhenPointerIsNearRowHeaderBottomEdge()
    {
        GridResizeHitPlanner.HitTest(
                CreateViewport(),
                new Point(12, 18 + 20 - 2),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18,
                hitZone: 4)
            .Should()
            .Be(new GridResizeHit(GridResizeHitTarget.Row, 1, 20));
    }

    [Fact]
    public void HitTest_IncludesHeaderBoundaryForResizeEdges()
    {
        GridResizeHitPlanner.HitTest(
                CreateViewport(),
                new Point(30 + 40, 18),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18,
                hitZone: 4)
            .Should()
            .Be(new GridResizeHit(GridResizeHitTarget.Column, 1, 40));

        GridResizeHitPlanner.HitTest(
                CreateViewport(),
                new Point(30, 18 + 20),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18,
                hitZone: 4)
            .Should()
            .Be(new GridResizeHit(GridResizeHitTarget.Row, 1, 20));
    }

    [Fact]
    public void HitTest_ReturnsNoneAwayFromHeadersOrWhenViewportIsMissing()
    {
        GridResizeHitPlanner.HitTest(
                CreateViewport(),
                new Point(120, 80),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18,
                hitZone: 4)
            .Should()
            .Be(new GridResizeHit(GridResizeHitTarget.None, 0, 0));

        GridResizeHitPlanner.HitTest(
                null,
                new Point(32, 8),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18,
                hitZone: 4)
            .Should()
            .Be(new GridResizeHit(GridResizeHitTarget.None, 0, 0));
    }

    private static ViewportModel CreateViewport() =>
        new(
            [],
            [new RowMetric(1, 20, 0), new RowMetric(2, 24, 20)],
            [new ColMetric(1, 40, 0), new ColMetric(2, 60, 40)]);
}
