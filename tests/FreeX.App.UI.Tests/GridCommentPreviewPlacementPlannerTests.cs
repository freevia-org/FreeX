using System.Windows;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

public sealed class GridCommentPreviewPlacementPlannerTests
{
    [Fact]
    public void Calculate_PlacesPreviewToRightOfCellWhenSpaceAllows()
    {
        var display = new CellCommentDisplay(CellCommentDisplayKind.Note, "Note", "Short note");

        var placement = GridCommentPreviewPlacementPlanner.Calculate(
            new Rect(50, 40, 64, 20),
            new Size(800, 500),
            display);

        placement.HorizontalOffset.Should().Be(120);
        placement.VerticalOffset.Should().Be(40);
        placement.Width.Should().Be(GridCommentPreviewPlacementPlanner.MinWidth);
        placement.MaxHeight.Should().Be(GridCommentPreviewPlacementPlanner.MinHeight);
    }

    [Fact]
    public void Calculate_FlipsPreviewLeftNearRightViewportEdge()
    {
        var display = new CellCommentDisplay(CellCommentDisplayKind.Note, "Note", "Short note");

        var placement = GridCommentPreviewPlacementPlanner.Calculate(
            new Rect(720, 40, 64, 20),
            new Size(800, 500),
            display);

        placement.HorizontalOffset.Should().BeLessThan(720);
        (placement.HorizontalOffset + placement.Width).Should().BeLessThanOrEqualTo(792);
    }

    [Fact]
    public void Calculate_ClampsPreviewInsideSmallViewportAndConstrainsHeight()
    {
        var longBody = string.Join(Environment.NewLine, Enumerable.Repeat("Long comment body", 40));
        var display = new CellCommentDisplay(CellCommentDisplayKind.ThreadedComment, "Comment", longBody);

        var placement = GridCommentPreviewPlacementPlanner.Calculate(
            new Rect(90, 130, 50, 20),
            new Size(150, 160),
            display);

        placement.HorizontalOffset.Should().Be(8);
        placement.Width.Should().Be(134);
        placement.VerticalOffset.Should().Be(8);
        placement.MaxHeight.Should().Be(144);
    }

    [Fact]
    public void GridViewCommentPreviewSurface_UsesHoverSelectionScrollablePopupAndEscapeDismissal()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources(
            "GridView.CommentPreview.cs",
            "GridView.Input.cs",
            "GridView.Properties.cs");

        source.Should().Contain("UpdateCommentPreviewForPointer(pos)");
        source.Should().Contain("UpdateCommentPreviewForSelection()");
        source.Should().Contain("new ScrollViewer");
        source.Should().Contain("VerticalScrollBarVisibility = ScrollBarVisibility.Auto");
        source.Should().Contain("Placement = PlacementMode.Relative");
        source.Should().Contain("if (e.Key == Key.Escape && _activeCommentPreviewKey.HasValue)");
        source.Should().Contain("DismissCommentPreview();");
    }
}
