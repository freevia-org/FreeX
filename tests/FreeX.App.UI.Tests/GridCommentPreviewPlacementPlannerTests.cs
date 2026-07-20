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
    public void CalculateConnector_AnchorsTopRightCellCornerWhenBoxPlacedToTheRight()
    {
        // R50-render-comment-hover-card-3-3: a pinned note box placed to the right of its cell must
        // get a leader line from the cell's top-right corner to the box's top-left corner, so the
        // box doesn't float as an unconnected rectangle with a bare CellGap in between.
        var cellRect = new Rect(50, 40, 64, 20);
        var placement = GridCommentPreviewPlacementPlanner.Calculate(
            cellRect,
            new Size(800, 500),
            new Size(120, 60));

        var connector = GridCommentPreviewPlacementPlanner.CalculateConnector(cellRect, placement);

        connector.Start.Should().Be(new Point(cellRect.Right, cellRect.Top));
        connector.End.Should().Be(new Point(placement.HorizontalOffset, placement.VerticalOffset));
    }

    [Fact]
    public void CalculateConnector_AnchorsTopLeftCellCornerWhenBoxPlacedToTheLeft()
    {
        // Sibling no-regression case: near the right viewport edge Calculate() flips the box to the
        // left of the cell, so the connector must flip to the cell's top-left corner and the box's
        // top-right corner instead of always assuming the "placed right" anchors.
        var cellRect = new Rect(720, 40, 64, 20);
        var placement = GridCommentPreviewPlacementPlanner.Calculate(
            cellRect,
            new Size(800, 500),
            new Size(120, 60));

        var connector = GridCommentPreviewPlacementPlanner.CalculateConnector(cellRect, placement);

        connector.Start.Should().Be(new Point(cellRect.Left, cellRect.Top));
        connector.End.Should().Be(new Point(placement.HorizontalOffset + placement.Width, placement.VerticalOffset));
    }

    [Fact]
    public void GridViewCommentPreviewSurface_UsesHoverSelectionScrollableInWindowPopupAndInlineEditing()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources(
            "GridView.CommentPreview.cs",
            "GridView.Input.cs",
            "GridView.Properties.cs");

        source.Should().Contain("UpdateCommentPreviewForPointer(pos)");
        source.Should().Contain("UpdateCommentPreviewForSelection()");
        source.Should().Contain("new ScrollViewer");
        source.Should().Contain("VerticalScrollBarVisibility = ScrollBarVisibility.Auto");
        source.Should().Contain("CommentOverlayHostProperty");
        source.Should().Contain("CommentOverlayHost.Children.Add(_commentPreviewBorder)");
        source.Should().Contain("GridCommentInWindowPopup");
        source.Should().Contain("BeginNoteInlineEdit(");
        source.Should().Contain("BeginThreadedCommentInlineEdit(");
        source.Should().Contain("SubmitNoteInlineEdit");
        source.Should().Contain("SubmitThreadedCommentInlineEdit");
        source.Should().Contain("Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Enter");
        source.Should().Contain("CancelCommentInlineEdit();");
        source.Should().Contain("if (e.Key == Key.Escape && _activeCommentPreviewKey.HasValue)");
        source.Should().Contain("DismissCommentPreview();");
        source.Should().NotContain("Placement = PlacementMode.Relative");
        source.Should().NotContain("System.Windows.Controls.Primitives");
    }
}
