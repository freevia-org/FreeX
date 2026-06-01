using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using System.IO;
using System.Windows;

namespace FreeX.App.UI.Tests;

public sealed class GridViewContextMenuTests
{
    [Fact]
    public void GridViewRightClick_RoutesRowAndColumnHeadersToHeaderContextMenuEvent()
    {
        var inputSource = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "GridView.Input.cs"));
        var eventsSource = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "GridView.Events.cs"));

        eventsSource.Should().Contain("HeaderContextMenuRequested");
        inputSource.Should().Contain("GridHeaderContextMenuHitPlanner.HitTest(Viewport, pos, ActualRowHeaderWidth, EffectiveColHeaderHeight)");
        inputSource.Should().Contain("HeaderContextMenuRequested?.Invoke(headerHit.Target, headerHit.Index, pos)");
        inputSource.Should().NotContain("HeaderContextMenuRequested?.Invoke(GridHeaderContextMenuTarget.Column, cm.Col, pos)");
        inputSource.Should().NotContain("HeaderContextMenuRequested?.Invoke(GridHeaderContextMenuTarget.Row, rm.Row, pos)");
    }

    [Fact]
    public void HeaderContextMenuHitPlanner_ReturnsColumnOrRowHeaderTargets()
    {
        var viewport = CreateViewport();

        GridHeaderContextMenuHitPlanner.HitTest(
                viewport,
                new Point(30 + 40 + 5, 8),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18)
            .Should()
            .Be(new GridHeaderContextMenuHit(GridHeaderContextMenuTarget.Column, 2));

        GridHeaderContextMenuHitPlanner.HitTest(
                viewport,
                new Point(12, 18 + 20 + 5),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18)
            .Should()
            .Be(new GridHeaderContextMenuHit(GridHeaderContextMenuTarget.Row, 2));
    }

    [Fact]
    public void HeaderContextMenuHitPlanner_ReturnsNullOutsideHeadersOrBeforeVisibleMetrics()
    {
        var viewport = CreateViewport();

        GridHeaderContextMenuHitPlanner.HitTest(
                viewport,
                new Point(12, 8),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18)
            .Should()
            .BeNull();

        GridHeaderContextMenuHitPlanner.HitTest(
                viewport,
                new Point(30, 18),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18)
            .Should()
            .BeNull();

        GridHeaderContextMenuHitPlanner.HitTest(
                viewport,
                new Point(-1, 24),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18)
            .Should()
            .BeNull();

        GridHeaderContextMenuHitPlanner.HitTest(
                viewport,
                new Point(40, -1),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18)
            .Should()
            .BeNull();

        GridHeaderContextMenuHitPlanner.HitTest(
                viewport,
                new Point(80, 40),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18)
            .Should()
            .BeNull();

        GridHeaderContextMenuHitPlanner.HitTest(
                viewport,
                new Point(155, 8),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18)
            .Should()
            .BeNull();

        GridHeaderContextMenuHitPlanner.HitTest(
                viewport: null,
                new Point(70, 8),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18)
            .Should()
            .BeNull();
    }

    [Fact]
    public void GridViewRightClick_RoutesCellContextMenuThroughSplitAwareViewportHitTesting()
    {
        var inputSource = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "GridView.Input.cs"));
        var cellFallbackStart = inputSource.IndexOf("if (HitTestViewportCell(Viewport, default, pos) is { } contextCell)", StringComparison.Ordinal);
        var rightClickBlock = inputSource[
            cellFallbackStart..
            inputSource.IndexOf("protected override void OnMouseLeftButtonUp", StringComparison.Ordinal)];

        rightClickBlock.Should().Contain("HitTestViewportCell(Viewport, default, pos)");
        rightClickBlock.Should().Contain("ContextMenuRequested?.Invoke(contextCell, pos);");
        rightClickBlock.Should().NotContain("foreach (var rm in Viewport.RowMetrics)");
        rightClickBlock.Should().NotContain("foreach (var cm in Viewport.ColMetrics)");
    }

    [Fact]
    public void GridViewRightClick_RoutesDrawingObjectContextMenuBeforeCellFallback()
    {
        var inputSource = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "GridView.Input.cs"));
        var rightClickBlock = inputSource[
            inputSource.IndexOf("protected override void OnMouseRightButtonDown", StringComparison.Ordinal)..
            inputSource.IndexOf("protected override void OnMouseLeftButtonUp", StringComparison.Ordinal)];

        rightClickBlock.Should().Contain("var objectHit = HitTestDrawingObject(pos);");
        rightClickBlock.Should().Contain("SelectedObjectId = objectHit.Id;");
        rightClickBlock.Should().Contain("SelectedObjectKind = objectHit.Kind;");
        rightClickBlock.Should().Contain("ContextMenuRequested?.Invoke(objectHit.Anchor, pos);");
        rightClickBlock.IndexOf("var objectHit = HitTestDrawingObject(pos);", StringComparison.Ordinal)
            .Should()
            .BeLessThan(rightClickBlock.IndexOf("if (HitTestViewportCell(Viewport, default, pos) is { } contextCell)", StringComparison.Ordinal));
        rightClickBlock.IndexOf("ContextMenuRequested?.Invoke(objectHit.Anchor, pos);", StringComparison.Ordinal)
            .Should()
            .BeLessThan(rightClickBlock.IndexOf("ContextMenuRequested?.Invoke(contextCell, pos);", StringComparison.Ordinal));
    }

    [Fact]
    public void GridViewRightClick_IgnoresContextMenuWhileCapturedDragIsActive()
    {
        var inputSource = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "GridView.Input.cs"));
        var rightClickBlock = inputSource[
            inputSource.IndexOf("protected override void OnMouseRightButtonDown", StringComparison.Ordinal)..
            inputSource.IndexOf("protected override void OnMouseLeftButtonUp", StringComparison.Ordinal)];

        rightClickBlock.Should().Contain("if (HasActiveCapturedGridDrag())");
        rightClickBlock.Should().Contain("e.Handled = true;");
        rightClickBlock.IndexOf("if (HasActiveCapturedGridDrag())", StringComparison.Ordinal)
            .Should()
            .BeLessThan(rightClickBlock.IndexOf("HitTestPivotChartFieldButton", StringComparison.Ordinal));
        rightClickBlock.IndexOf("if (HasActiveCapturedGridDrag())", StringComparison.Ordinal)
            .Should()
            .BeLessThan(rightClickBlock.IndexOf("ContextMenuRequested?.Invoke", StringComparison.Ordinal));
        rightClickBlock.IndexOf("e.Handled = true;", StringComparison.Ordinal)
            .Should()
            .BeLessThan(rightClickBlock.IndexOf("HitTestPivotChartFieldButton", StringComparison.Ordinal));
    }

    [Fact]
    public void GridViewDoubleClickResizeBorder_RoutesToAutoFitEventsBeforeDragResize()
    {
        var inputSource = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "GridView.Input.cs"));
        var eventsSource = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "GridView.Events.cs"));
        var resizeStart = inputSource[
            inputSource.IndexOf("var (target, index, size) = HitTestResize(pos);", StringComparison.Ordinal)..
            inputSource.IndexOf("_resizeTarget    = target;", StringComparison.Ordinal)];

        eventsSource.Should().Contain("ColumnAutoFitRequested");
        eventsSource.Should().Contain("RowAutoFitRequested");
        resizeStart.Should().Contain("if (e.ClickCount >= 2)");
        resizeStart.Should().Contain("ColumnAutoFitRequested?.Invoke(index)");
        resizeStart.Should().Contain("RowAutoFitRequested?.Invoke(index)");
    }

    private static ViewportModel CreateViewport() =>
        new(
            [],
            [
                new RowMetric(1, 20, 0),
                new RowMetric(2, 20, 20),
                new RowMetric(3, 20, 40)
            ],
            [
                new ColMetric(1, 40, 0),
                new ColMetric(2, 40, 40),
                new ColMetric(3, 40, 80)
            ]);

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
