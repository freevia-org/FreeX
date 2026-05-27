using FluentAssertions;
using System.IO;

namespace Freexcel.App.UI.Tests;

public sealed class GridViewContextMenuTests
{
    [Fact]
    public void GridViewRightClick_RoutesRowAndColumnHeadersToHeaderContextMenuEvent()
    {
        var inputSource = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.App.UI", "GridView.Input.cs"));
        var eventsSource = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.App.UI", "GridView.Events.cs"));

        eventsSource.Should().Contain("HeaderContextMenuRequested");
        inputSource.Should().Contain("HeaderContextMenuRequested?.Invoke(GridHeaderContextMenuTarget.Column, cm.Col, pos)");
        inputSource.Should().Contain("HeaderContextMenuRequested?.Invoke(GridHeaderContextMenuTarget.Row, rm.Row, pos)");
    }

    [Fact]
    public void GridViewDoubleClickResizeBorder_RoutesToAutoFitEventsBeforeDragResize()
    {
        var inputSource = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.App.UI", "GridView.Input.cs"));
        var eventsSource = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.App.UI", "GridView.Events.cs"));
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
