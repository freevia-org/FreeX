using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R75-render-selection-marquee-4-3: a Ctrl+click multi-area copy (e.g. A1:A2 + C1:C2) drew ONE
/// marching-ants marquee spanning the bounding box (A1:C2) -- sweeping ants through the untouched
/// gap column B -- instead of a separate marquee around each copied area. GridView.ClipboardRanges
/// (GridView.Properties.cs) now carries every copied area, and RenderMarchingAnts
/// (GridView.Overlays.cs) strokes ants around each one individually when more than one is set.
/// </summary>
public sealed class GridViewClipboardMarqueeMultiAreaTests
{
    // 3 columns x 2 rows, each cell 20x20 logical px, no headers -- matching the minimal-scene
    // pattern used by GridViewClipboardMarqueeSheetTests so the only black-ish pixels the render
    // can ever produce are the marching-ants pens themselves.
    private static GridView CreateGrid(
        SheetId sheetId,
        GridRange clipboardRange,
        IReadOnlyList<GridRange>? clipboardRanges)
    {
        var grid = new GridView
        {
            Width = 60,
            Height = 40,
            ShowHeaders = false,
            ShowGridLines = false,
            ActiveSheetId = sheetId,
            ClipboardRange = clipboardRange,
            ClipboardRanges = clipboardRanges,
            ClipboardIsCut = false,
            Viewport = new ViewportModel(
                [],
                [
                    new RowMetric(1, 20, 0),
                    new RowMetric(2, 20, 20)
                ],
                [
                    new ColMetric(1, 20, 0),
                    new ColMetric(2, 20, 20),
                    new ColMetric(3, 20, 40)
                ])
        };

        grid.Measure(new Size(60, 40));
        grid.Arrange(new Rect(0, 0, 60, 40));
        grid.UpdateLayout();
        return grid;
    }

    private static RenderTargetBitmap RenderGridToBitmap(GridView grid)
    {
        var bitmap = new RenderTargetBitmap(60, 40, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(grid);
        return bitmap;
    }

    /// <summary>
    /// True when any pixel in the region is near-black -- the marching-ants marquee's outer pen is
    /// solid black, and (with no grid lines, headers, or cell content) is the only black-ish thing
    /// this minimal scene can ever draw.
    /// </summary>
    private static bool AnyBlackishPixelInRegion(BitmapSource bitmap, Int32Rect region)
    {
        var clampedX = Math.Max(0, region.X);
        var clampedY = Math.Max(0, region.Y);
        var clampedWidth = Math.Min(bitmap.PixelWidth - clampedX, region.Width - (clampedX - region.X));
        var clampedHeight = Math.Min(bitmap.PixelHeight - clampedY, region.Height - (clampedY - region.Y));
        if (clampedWidth <= 0 || clampedHeight <= 0) return false;

        var clamped = new Int32Rect(clampedX, clampedY, clampedWidth, clampedHeight);
        var stride = clamped.Width * 4;
        var pixels = new byte[stride * clamped.Height];
        bitmap.CopyPixels(clamped, pixels, stride, 0);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var blue = pixels[i];
            var green = pixels[i + 1];
            var red = pixels[i + 2];
            var alpha = pixels[i + 3];
            if (alpha > 10 && red < 100 && green < 100 && blue < 100)
                return true;
        }
        return false;
    }

    [Fact]
    public void RenderMarchingAnts_TwoAreaCopy_RendersTwoRects_ColumnBUnmarqueed()
    {
        WpfTestThread.Run(() =>
        {
            var sheet = SheetId.New();

            // Column A (col 1) and column C (col 3) are copied; column B (col 2, x 20..40) is the
            // untouched gap that a single bounding-box marquee (A1:C2) would incorrectly sweep
            // through.
            var areaA = new GridRange(new CellAddress(sheet, 1, 1), new CellAddress(sheet, 2, 1));
            var areaC = new GridRange(new CellAddress(sheet, 1, 3), new CellAddress(sheet, 2, 3));
            var bounding = new GridRange(new CellAddress(sheet, 1, 1), new CellAddress(sheet, 2, 3));

            var grid = CreateGrid(sheet, bounding, [areaA, areaC]);
            var bitmap = RenderGridToBitmap(grid);

            // Column A's own right border (x=20) and column C's own left border (x=40) must each be
            // drawn -- they only exist if the two areas are stroked as SEPARATE rects; a single
            // A1:C2 bounding box has no border there at all (its only vertical edges are x=0/x=60).
            AnyBlackishPixelInRegion(bitmap, new Int32Rect(17, 0, 6, 40)).Should().BeTrue(
                "column A's own right-edge marquee border (x=20) must render as its own rect");
            AnyBlackishPixelInRegion(bitmap, new Int32Rect(37, 0, 6, 40)).Should().BeTrue(
                "column C's own left-edge marquee border (x=40) must render as its own rect");

            // Deep inside the gap column B (x 24..36), near the top and bottom canvas edges: a single
            // bounding-box marquee's top/bottom border would run the FULL width (0..60), sweeping
            // through here; two separate area rects must leave this strip untouched.
            AnyBlackishPixelInRegion(bitmap, new Int32Rect(24, 0, 12, 4)).Should().BeFalse(
                "the untouched gap column B must not be swept by a top marquee border");
            AnyBlackishPixelInRegion(bitmap, new Int32Rect(24, 36, 12, 4)).Should().BeFalse(
                "the untouched gap column B must not be swept by a bottom marquee border");
        });
    }

    [Fact]
    public void RenderMarchingAnts_SingleRangeCopy_Unchanged_NoRegression()
    {
        WpfTestThread.Run(() =>
        {
            var sheet = SheetId.New();

            // A single-area copy (ClipboardRanges left null) must still render exactly one marquee
            // around the copied range, matching pre-existing behavior.
            var range = new GridRange(new CellAddress(sheet, 1, 1), new CellAddress(sheet, 2, 1));
            var grid = CreateGrid(sheet, range, clipboardRanges: null);
            var bitmap = RenderGridToBitmap(grid);

            AnyBlackishPixelInRegion(bitmap, new Int32Rect(0, 0, 3, 40)).Should().BeTrue(
                "the single copied range's left-edge border must still render");
            AnyBlackishPixelInRegion(bitmap, new Int32Rect(17, 0, 6, 40)).Should().BeTrue(
                "the single copied range's right-edge border (x=20) must still render");
        });
    }
}
