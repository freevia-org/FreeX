using System.IO;
using FluentAssertions;

namespace FreeX.App.UI.Tests;

public sealed class GridResizeSizePlannerTests
{
    // The pure clamp/cap/line-position math lives in (and is tested against)
    // FreeX.App.Presentation.GridInteraction.GridResizeSizePlanner. This host-specific test only
    // guards that the WPF GridView resize drag still routes its preview and commit sizes through
    // that shared planner instead of reintroducing a local minimum-visual-size clamp.
    [Fact]
    public void GridViewResizeDrag_UsesPlannerForPreviewAndCommitSizes()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.UI", "GridView.Input.cs"));

        source.Should().Contain("GridResizeSizePlanner.ClampColumnSize(_resizeSizeStart + (pos.X - _resizeDragStart))");
        source.Should().Contain("GridResizeSizePlanner.ClampRowSize(_resizeSizeStart + (pos.Y - _resizeDragStart))");
        source.Should().Contain("GridResizeSizePlanner.ClampColumnSize(_resizeSizeStart + delta)");
        source.Should().Contain("GridResizeSizePlanner.ClampRowSize(_resizeSizeStart + delta)");
        source.Should().Contain("_resizeDragStart = _resizeLinePos;");
        source.Should().Contain("GridResizeSizePlanner.CalculateLinePosition(_resizeSizeStart, _resizeDragStart, newWidth)");
        source.Should().NotContain("Math.Max(MinCellSize");
    }
}
