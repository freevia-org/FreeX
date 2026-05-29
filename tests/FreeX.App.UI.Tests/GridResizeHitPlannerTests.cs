using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using System.IO;
using System.Windows;

namespace FreeX.App.UI.Tests;

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

    [Fact]
    public void HitTest_StopsHeaderScansOnceSortedEdgesPassPointer()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "GridResizeHitPlanner.cs"));

        source.Should().Contain("public readonly record struct GridResizeHit");
        source.Should().Contain("for (var i = 0; i < columns.Count; i++)");
        source.Should().Contain("for (var i = 0; i < rows.Count; i++)");
        source.Should().Contain("if (rightEdge - pointer.X > hitZone)");
        source.Should().Contain("if (bottomEdge - pointer.Y > hitZone)");
        source.Should().NotContain("public sealed record GridResizeHit");
        source.Should().NotContain("foreach (var column in viewport.ColMetrics)");
        source.Should().NotContain("foreach (var row in viewport.RowMetrics)");
    }

    private static ViewportModel CreateViewport() =>
        new(
            [],
            [new RowMetric(1, 20, 0), new RowMetric(2, 24, 20)],
            [new ColMetric(1, 40, 0), new ColMetric(2, 60, 40)]);

    private static string FindWorkspaceFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate workspace file.", Path.Combine(relativeParts));
    }
}
