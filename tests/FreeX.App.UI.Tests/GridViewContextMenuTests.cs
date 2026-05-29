using FluentAssertions;
using System.IO;

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
        inputSource.Should().Contain("HeaderContextMenuRequested?.Invoke(GridHeaderContextMenuTarget.Column, cm.Col, pos)");
        inputSource.Should().Contain("HeaderContextMenuRequested?.Invoke(GridHeaderContextMenuTarget.Row, rm.Row, pos)");
        inputSource.Should().Contain("if (pos.Y <= EffectiveColHeaderHeight && pos.X >= ActualRowHeaderWidth)");
        inputSource.Should().Contain("if (pos.X <= ActualRowHeaderWidth && pos.Y >= EffectiveColHeaderHeight)");
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
