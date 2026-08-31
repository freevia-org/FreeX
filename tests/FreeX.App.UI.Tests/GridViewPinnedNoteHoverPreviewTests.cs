using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R58-render-comment-indicator-6-2: a cell whose note is pinned always-visible (Excel's "Show
/// Comment" / "Always Show", surfaced here via <see cref="GridView.PinnedNoteAddresses"/> and the
/// independent <c>_pinnedNoteBorders</c> overlay built by RefreshPinnedNoteBoxes) must not ALSO raise
/// the transient hover-preview border when the mouse moves over it -- the note is already shown, so a
/// second, independently-positioned popup for the identical note is a bug.
/// </summary>
public sealed class GridViewPinnedNoteHoverPreviewTests
{
    private static GridView CreateGridWithComment(out CellCommentDisplay comment)
    {
        comment = new CellCommentDisplay(CellCommentDisplayKind.Note, "Note", "Body");
        var cells = new DisplayCell[]
        {
            new(1, 1, null, "", null, default, null, HasComment: true, CommentDisplay: comment)
        };

        var grid = new GridView
        {
            Width = 160,
            Height = 40,
            ShowHeaders = false,
            ShowGridLines = false,
            CommentOverlayHost = new Canvas(),
            Viewport = new ViewportModel(
                cells,
                [new RowMetric(1, 40, 0)],
                [new ColMetric(1, 80, 0)])
        };

        grid.Measure(new Size(160, 40));
        grid.Arrange(new Rect(0, 0, 160, 40));
        grid.UpdateLayout();

        // A render pass is required before invoking the hover/preview helpers below: the private
        // merge lookup consulted by the hit-test path is only rebuilt from OnRender.
        var bitmap = new RenderTargetBitmap(160, 40, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(grid);
        return grid;
    }

    private static void InvokeUpdateCommentPreviewForPointer(GridView grid, Point pos)
    {
        grid.UpdateCommentPreviewForPointer(pos);
    }

    private static Border? GetCommentPreviewBorder(GridView grid)
    {
        var field = typeof(GridView).GetField("_commentPreviewBorder", BindingFlags.NonPublic | BindingFlags.Instance);
        field.Should().NotBeNull();
        return (Border?)field!.GetValue(grid);
    }

    [Fact]
    public void HoverOverPinnedNoteCell_DoesNotRaiseHoverPreviewBorder()
    {
        WpfTestThread.Run(() =>
        {
            var grid = CreateGridWithComment(out _);
            grid.PinnedNoteAddresses = new HashSet<(uint Row, uint Col)> { (1u, 1u) };

            // HitTestViewportCell always assumes the standard 30px row-header / 18px column-header
            // hit-test origin regardless of ShowHeaders (matching GridViewMergedCommentIndicatorTests),
            // so (50, 30) is the pointer position that resolves to cell (1,1).
            InvokeUpdateCommentPreviewForPointer(grid, new Point(50, 30));

            var border = GetCommentPreviewBorder(grid);
            (border is null || border.Visibility == Visibility.Collapsed).Should().BeTrue(
                "the note is already shown via the always-visible pinned overlay, so hovering it must not " +
                "also pop a second, overlapping transient preview");
        });
    }

    [Fact]
    public void HoverOverUnpinnedNoteCell_StillRaisesHoverPreviewBorder_NoRegression()
    {
        WpfTestThread.Run(() =>
        {
            var grid = CreateGridWithComment(out var comment);

            InvokeUpdateCommentPreviewForPointer(grid, new Point(50, 30));

            var border = GetCommentPreviewBorder(grid);
            border.Should().NotBeNull();
            border!.Visibility.Should().Be(
                Visibility.Visible,
                "a plain (non-pinned) note must still show the transient hover preview, matching pre-existing behavior");
        });
    }
}
