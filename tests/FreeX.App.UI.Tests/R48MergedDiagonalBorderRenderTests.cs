using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R48-render-borders-precedence-3-2: a diagonal border (BorderDiagonalDown/BorderDiagonalUp) on a
/// merged range must span the FULL merged rectangle, matching Excel and matching how the fill/
/// selection/comment-indicator passes in GridView.Rendering.cs already widen to the merged extent.
/// Before the fix, GridView.Rendering.cs drew the diagonal only across the anchor cell's own
/// un-merged column/row footprint, leaving a short diagonal tucked in one corner of the merged block
/// instead of a single line running corner-to-corner across the whole merge.
/// </summary>
public sealed class R48MergedDiagonalBorderRenderTests
{
    private static GridView CreateGridWithCells(
        IReadOnlyList<DisplayCell> cells,
        IReadOnlyList<GridRange>? mergedRegions,
        double width)
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

    private static RenderTargetBitmap RenderGridToBitmap(GridView grid, int width, int height = 40)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(grid);
        return bitmap;
    }

    /// <summary>True when the pixel at (x, y) is clearly red-dominant (i.e. painted by the red diagonal pen).</summary>
    private static bool IsReddishPixel(BitmapSource bitmap, int x, int y)
    {
        if (x < 0 || x >= bitmap.PixelWidth || y < 0 || y >= bitmap.PixelHeight)
            return false;

        var pixels = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixels, 4, 0);
        var blue = pixels[0];
        var green = pixels[1];
        var red = pixels[2];
        var alpha = pixels[3];
        return alpha > 10 && red > 150 && red - green > 30 && red - blue > 30;
    }

    /// <summary>True when any pixel within a small box around (x, y) is red-dominant (tolerates anti-aliasing/pen thickness).</summary>
    private static bool AnyReddishPixelNear(BitmapSource bitmap, int x, int y, int radius = 3)
    {
        for (var dy = -radius; dy <= radius; dy++)
        for (var dx = -radius; dx <= radius; dx++)
            if (IsReddishPixel(bitmap, x + dx, y + dy))
                return true;
        return false;
    }

    [Fact]
    public void DiagonalDownBorder_OnMergedRange_SpansFullMergedRectangle()
    {
        WpfTestThread.Run(() =>
        {
            var sheet = SheetId.New();
            var merge = new GridRange(new CellAddress(sheet, 1, 1), new CellAddress(sheet, 1, 2));

            // A1:B1 merged into one 160x40 cell with a diagonal-down border on the anchor (A1).
            // Real Excel draws one line from the merged rect's true top-left (0,0) to its true
            // bottom-right (160,40).
            var anchorStyle = new CellStyle { BorderDiagonalDown = new CellBorder(BorderStyle.Thick, new CellColor(255, 0, 0)) };
            var cells = new DisplayCell[]
            {
                new(1, 1, null, "", null, default, null, anchorStyle),
                new(1, 2, null, "", null, default, null)
            };

            var grid = CreateGridWithCells(cells, [merge], width: 160);
            var bitmap = RenderGridToBitmap(grid, 160);

            // The true diagonal (0,0)-(160,40) passes through (120,30). The un-merged anchor-only
            // diagonal (the pre-fix bug) would only ever span (0,0)-(80,40), which never reaches
            // x=120 at all -- so painted red there proves the line was widened to the full merge.
            AnyReddishPixelNear(bitmap, x: 120, y: 30).Should().BeTrue(
                "the diagonal border on a merged range must reach the merged rectangle's true bottom-right area, not stop at the anchor cell's own un-merged width");

            // The true diagonal also passes near its own top-left corner (0,0) and bottom-right
            // corner (160,40) -- sanity-check the line still starts/ends at the merge's real extent.
            AnyReddishPixelNear(bitmap, x: 155, y: 36).Should().BeTrue(
                "the diagonal border must reach the merged range's true bottom-right corner");
        });
    }

    [Fact]
    public void DiagonalDownBorder_OnNonMergedCell_SpansOwnCellRectangle_NoRegression()
    {
        WpfTestThread.Run(() =>
        {
            var style = new CellStyle { BorderDiagonalDown = new CellBorder(BorderStyle.Thick, new CellColor(255, 0, 0)) };
            var cells = new DisplayCell[]
            {
                new(1, 1, null, "", null, default, null, style),
                new(1, 2, null, "", null, default, null)
            };

            // No merge: the diagonal on A1 must still span exactly A1's own 80x40 footprint, as
            // before this fix -- it must NOT bleed into B1's footprint.
            var grid = CreateGridWithCells(cells, mergedRegions: null, width: 160);
            var bitmap = RenderGridToBitmap(grid, 160);

            AnyReddishPixelNear(bitmap, x: 40, y: 20).Should().BeTrue(
                "an un-merged cell's own diagonal border must still be drawn across its own footprint");
            AnyReddishPixelNear(bitmap, x: 120, y: 30).Should().BeFalse(
                "an un-merged cell's diagonal border must not extend into the neighboring cell's footprint");
        });
    }
}
