using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// Round-42 merged-cell comment/note indicator fixes:
/// R42-render-comment-note-indicator-3-1 (GridView.CommentPreview.cs) -- hovering a merged cell only
/// showed the note/comment popup over the anchor's own single-cell footprint, not the full visible
/// merged block, because the hover/selection lookup matched (row, col) literally instead of resolving
/// to the merge's anchor first.
/// R42-render-comment-note-indicator-3-2 (GridView.Rendering.cs) -- the note/comment triangle on a
/// merged cell's anchor was drawn using only the anchor's own column width/row height, landing at an
/// interior gridline instead of the merged range's true top-right corner.
/// </summary>
public sealed class GridViewMergedCommentIndicatorTests
{
    private static GridView CreateGridWithCells(
        IReadOnlyList<DisplayCell> cells,
        IReadOnlyList<GridRange>? mergedRegions,
        double width = 160)
    {
        var grid = new GridView
        {
            Width = width,
            Height = 40,
            ShowHeaders = false,
            ShowGridLines = false,
            MergedRegions = mergedRegions,
            Viewport = new ViewportModel(
                cells,
                [new RowMetric(1, 40, 0)],
                [
                    new ColMetric(1, 80, 0),
                    new ColMetric(2, 80, 80)
                ])
        };

        grid.Measure(new Size(width, 40));
        grid.Arrange(new Rect(0, 0, width, 40));
        grid.UpdateLayout();
        return grid;
    }

    /// <summary>
    /// Renders the grid to a bitmap. This is also required before invoking the hover/preview
    /// helpers below: the private merge lookup (`_mergeLookup`, consulted by `FindMerge`) is only
    /// rebuilt from `OnRender` (via `RebuildMergeLookup`), so a render pass must happen first.
    /// </summary>
    private static RenderTargetBitmap RenderGridToBitmap(GridView grid, int width, int height = 40)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(grid);
        return bitmap;
    }

    private static bool InvokeTryGetCommentPreviewForCell(GridView grid, uint row, uint col, out DisplayCell cell, out Rect rect)
    {
        var method = typeof(GridView).GetMethod("TryGetCommentPreviewForCell", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();
        var args = new object?[] { row, col, null, null };
        var result = (bool)method!.Invoke(grid, args)!;
        cell = (DisplayCell)args[2]!;
        rect = (Rect)args[3]!;
        return result;
    }

    private static bool InvokeTryGetCommentPreviewAt(GridView grid, Point pos, out DisplayCell cell, out Rect rect)
    {
        var method = typeof(GridView).GetMethod("TryGetCommentPreviewAt", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();
        var args = new object?[] { pos, null, null };
        var result = (bool)method!.Invoke(grid, args)!;
        cell = (DisplayCell)args[1]!;
        rect = (Rect)args[2]!;
        return result;
    }

    /// <summary>True when any pixel within <paramref name="xRadius"/> device pixels of <paramref name="centerX"/> is clearly red-dominant.</summary>
    private static bool AnyPixelNearXIsReddish(BitmapSource bitmap, int centerX, int xRadius, int height)
    {
        var minX = Math.Max(0, centerX - xRadius);
        var maxXExclusive = Math.Min(bitmap.PixelWidth, centerX + xRadius + 1);
        var width = maxXExclusive - minX;
        if (width <= 0) return false;

        var clampedHeight = Math.Min(height, bitmap.PixelHeight);
        var stride = width * 4;
        var pixels = new byte[stride * clampedHeight];
        bitmap.CopyPixels(new Int32Rect(minX, 0, width, clampedHeight), pixels, stride, 0);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var blue = pixels[i];
            var green = pixels[i + 1];
            var red = pixels[i + 2];
            var alpha = pixels[i + 3];
            if (alpha > 10 && red > 150 && red - green > 30 && red - blue > 30)
                return true;
        }
        return false;
    }

    // --- R42-render-comment-note-indicator-3-1: hover hit-region ---

    [Fact]
    public void HoverOverNonAnchorPartOfMergedCell_FindsCommentAndFullMergedRect()
    {
        WpfTestThread.Run(() =>
        {
            var sheet = SheetId.New();
            var merge = new GridRange(new CellAddress(sheet, 1, 1), new CellAddress(sheet, 1, 2));
            var comment = new CellCommentDisplay(CellCommentDisplayKind.Note, "Note", "Body");

            var cells = new DisplayCell[]
            {
                new(1, 1, null, "", null, default, null, HasComment: true, CommentDisplay: comment),
                new(1, 2, null, "", null, default, null)
            };

            var grid = CreateGridWithCells(cells, [merge]);
            RenderGridToBitmap(grid, 160);

            // HitTestViewportCell (GridView.SplitPanes.cs) always assumes the standard 30px
            // row-header / 18px column-header hit-test origin, regardless of ShowHeaders -- so
            // pointer positions must be expressed in that space. x=150 (>= 30 + 80) is squarely
            // inside column B (the merge's non-anchor half); it is NOT within the anchor A1's own
            // single-cell hit-test footprint (x in [30,110)) but IS within the visually-merged
            // block, which spans columns A:B.
            var found = InvokeTryGetCommentPreviewAt(grid, new Point(150, 30), out var hitCell, out var hitRect);

            found.Should().BeTrue(
                "hovering anywhere over the visually-merged block must surface the note/comment, matching Excel");
            hitCell.Row.Should().Be(1u);
            hitCell.Col.Should().Be(1u);
            hitCell.CommentDisplay.Should().Be(comment);
            hitRect.Width.Should().Be(160,
                "the hit rect must cover the full merged footprint, not just the anchor's own 80-wide column");
        });
    }

    [Fact]
    public void HoverOverAnchorOfMergedCell_StillFindsComment_NoRegression()
    {
        WpfTestThread.Run(() =>
        {
            var sheet = SheetId.New();
            var merge = new GridRange(new CellAddress(sheet, 1, 1), new CellAddress(sheet, 1, 2));
            var comment = new CellCommentDisplay(CellCommentDisplayKind.Note, "Note", "Body");

            var cells = new DisplayCell[]
            {
                new(1, 1, null, "", null, default, null, HasComment: true, CommentDisplay: comment),
                new(1, 2, null, "", null, default, null)
            };

            var grid = CreateGridWithCells(cells, [merge]);
            RenderGridToBitmap(grid, 160);

            // x=50 (within [30,110), i.e. the standard hit-test origin plus column A's own width)
            // lands on the merge anchor cell itself.
            var found = InvokeTryGetCommentPreviewAt(grid, new Point(50, 30), out var hitCell, out var hitRect);

            found.Should().BeTrue();
            hitCell.Row.Should().Be(1u);
            hitCell.Col.Should().Be(1u);
            hitRect.Width.Should().Be(160);
        });
    }

    [Fact]
    public void HoverOverPlainNonMergedCommentCell_UsesSingleCellRect_NoRegression()
    {
        WpfTestThread.Run(() =>
        {
            var comment = new CellCommentDisplay(CellCommentDisplayKind.Note, "Note", "Body");
            var cells = new DisplayCell[]
            {
                new(1, 1, null, "", null, default, null, HasComment: true, CommentDisplay: comment),
                new(1, 2, null, "", null, default, null)
            };

            var grid = CreateGridWithCells(cells, mergedRegions: null);
            RenderGridToBitmap(grid, 160);

            var found = InvokeTryGetCommentPreviewForCell(grid, 1, 1, out _, out var hitRect);

            found.Should().BeTrue();
            hitRect.Width.Should().Be(80,
                "an un-merged cell's comment rect must remain its own single-column width");

            var foundNeighbor = InvokeTryGetCommentPreviewForCell(grid, 1, 2, out _, out _);
            foundNeighbor.Should().BeFalse("the neighboring un-merged cell legitimately carries no comment of its own");
        });
    }

    // --- R42-render-comment-note-indicator-3-2: triangle position ---

    [Fact]
    public void CommentTriangle_OnMergedCellAnchor_DrawsAtMergedRangeTopRightCorner()
    {
        WpfTestThread.Run(() =>
        {
            var sheet = SheetId.New();
            var merge = new GridRange(new CellAddress(sheet, 1, 1), new CellAddress(sheet, 1, 2));
            var comment = new CellCommentDisplay(CellCommentDisplayKind.Note, "Note", "Body");

            var cells = new DisplayCell[]
            {
                new(1, 1, null, "", null, default, null, HasComment: true, CommentDisplay: comment),
                new(1, 2, null, "", null, default, null)
            };

            var grid = CreateGridWithCells(cells, [merge], width: 200);
            var bitmap = RenderGridToBitmap(grid, 200);

            // The merged range spans x=[0,160); its true top-right corner is at x=160.
            AnyPixelNearXIsReddish(bitmap, centerX: 160, xRadius: 4, height: 10).Should().BeTrue(
                "the note triangle on a merged cell's anchor must be drawn at the merged range's top-right corner");

            // x=80 is the interior boundary between the merge's two columns -- Excel never shows the
            // triangle at an interior gridline once cells are merged.
            AnyPixelNearXIsReddish(bitmap, centerX: 80, xRadius: 4, height: 10).Should().BeFalse(
                "the note triangle must not land at the merged range's interior gridline");
        });
    }

    [Fact]
    public void CommentTriangle_OnNonMergedCell_DrawsAtOwnTopRightCorner_NoRegression()
    {
        WpfTestThread.Run(() =>
        {
            var comment = new CellCommentDisplay(CellCommentDisplayKind.Note, "Note", "Body");
            var cells = new DisplayCell[]
            {
                new(1, 1, null, "", null, default, null, HasComment: true, CommentDisplay: comment),
                new(1, 2, null, "", null, default, null)
            };

            var grid = CreateGridWithCells(cells, mergedRegions: null, width: 200);
            var bitmap = RenderGridToBitmap(grid, 200);

            AnyPixelNearXIsReddish(bitmap, centerX: 80, xRadius: 4, height: 10).Should().BeTrue(
                "an un-merged cell's own top-right corner (x=80) must still get the note triangle");
        });
    }
}
