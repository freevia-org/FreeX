using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

// Regression coverage for R58-render-comment-indicator-6-1: a merged, noted cell that crosses a
// View>Split divider must only expose its comment/note in the anchor's own (indicator-bearing)
// pane quadrant. The non-anchor quadrant's DisplayCell is stripped of DisplayText/HasComment/
// ConditionalIcon/ConditionalDataBar (see StripContentForSecondaryMergeQuadrant) so its content is
// never drawn twice -- but pre-fix, CommentDisplay itself survived the strip, so the hover hit-test
// in GridView.CommentPreview.cs (which checks CommentDisplay, not HasComment) still matched the
// indicator-less quadrant and popped the note preview there too.
public sealed partial class GridViewSplitPaneLayoutTests
{
    [Fact]
    public void CalculateSplitPaneCellLayouts_MergeCrossingVerticalSplit_WithComment_SecondaryQuadrantHasNoCommentDisplay()
    {
        var sheetId = SheetId.New();
        var comment = new CellCommentDisplay(CellCommentDisplayKind.Note, "Note", "Body");
        var cells = new DisplayCell[]
        {
            new(1, 1, null, "merged", null, default, null, HasComment: true, CommentDisplay: comment)
        };
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 18, 0)],
            [new ColMetric(1, 64, 0), new ColMetric(2, 80, 64), new ColMetric(3, 64, 144), new ColMetric(4, 80, 208)],
            SplitPanes: new SplitPaneState(
                2,
                3,
                [new RowMetric(1, 18, 0)],
                [new ColMetric(1, 64, 0), new ColMetric(2, 80, 64)],
                cells,
                [new ColMetric(3, 64, 0), new ColMetric(4, 80, 64)]));
        var mergedRegions = new[]
        {
            new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 1, 4))
        };

        var layouts = GridView.CalculateSplitPaneCellLayouts(viewport, mergedRegions);

        // Primary (anchor) quadrant: keeps both the render-gate flag AND the popup content.
        var leftBox = layouts.Should().ContainSingle(l => l.Region == SplitPaneRegion.TopLeft).Subject;
        leftBox.Cell.HasComment.Should().BeTrue();
        leftBox.Cell.CommentDisplay.Should().Be(comment);

        // Secondary (non-indicator) quadrant: no triangle is drawn (HasComment already false,
        // pre-existing behavior) AND, with the fix, no CommentDisplay either -- so the hover
        // hit-test (which keys off CommentDisplay) can no longer match here and pop the preview
        // over an indicator-less pane.
        var rightBox = layouts.Should().ContainSingle(l => l.Region == SplitPaneRegion.TopRight).Subject;
        rightBox.Cell.HasComment.Should().BeFalse();
        rightBox.Cell.CommentDisplay.Should().BeNull(
            "the secondary quadrant must not carry comment content once its indicator has been stripped, " +
            "else the hover popup still opens over a pane with no visible triangle");
    }

    // Sibling no-regression case: a merge with a comment that does NOT cross the split keeps behaving
    // exactly as before -- one layout entry, and it keeps its full comment content.
    [Fact]
    public void CalculateSplitPaneCellLayouts_MergeNotCrossingSplit_WithComment_RetainsCommentDisplay_NoRegression()
    {
        var sheetId = SheetId.New();
        var comment = new CellCommentDisplay(CellCommentDisplayKind.Note, "Note", "Body");
        var cells = new DisplayCell[]
        {
            new(1, 1, null, "merged", null, default, null, HasComment: true, CommentDisplay: comment)
        };
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 18, 0)],
            [new ColMetric(1, 64, 0), new ColMetric(2, 80, 64), new ColMetric(3, 64, 144), new ColMetric(4, 80, 208)],
            SplitPanes: new SplitPaneState(
                2,
                3,
                [new RowMetric(1, 18, 0)],
                [new ColMetric(1, 64, 0), new ColMetric(2, 80, 64)],
                cells,
                [new ColMetric(3, 64, 0), new ColMetric(4, 80, 64)]));
        var mergedRegions = new[]
        {
            new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 1, 2))
        };

        var layouts = GridView.CalculateSplitPaneCellLayouts(viewport, mergedRegions);

        layouts.Should().ContainSingle();
        var box = layouts[0];
        box.Region.Should().Be(SplitPaneRegion.TopLeft);
        box.Cell.HasComment.Should().BeTrue();
        box.Cell.CommentDisplay.Should().Be(comment);
    }
}
