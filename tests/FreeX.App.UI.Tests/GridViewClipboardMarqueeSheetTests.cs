using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R54-render-copy-cut-marquee-4-2: RenderMarchingAnts (GridView.Overlays.cs) drew the copy/cut
/// marching-ants marquee purely from numeric row/col matches against the active viewport, without
/// ever checking that ClipboardRange.Start/End.Sheet is actually the sheet currently on screen
/// (ActiveSheetId). Because ClipboardRange survives a sheet-tab switch (it is never cleared),
/// switching to any other sheet that happens to have the same row/col numbers reused the stale
/// range and drew a marquee for cells that were never copied on that sheet -- Excel hides the
/// marching ants the moment a different sheet becomes active.
/// </summary>
public sealed class GridViewClipboardMarqueeSheetTests
{
    private static GridView CreateGrid(GridRange clipboardRange, SheetId activeSheetId, bool isCut = false)
    {
        var grid = new GridView
        {
            Width = 60,
            Height = 60,
            ShowHeaders = false,
            ShowGridLines = false,
            ActiveSheetId = activeSheetId,
            ClipboardRange = clipboardRange,
            ClipboardIsCut = isCut,
            Viewport = new ViewportModel(
                [],
                [
                    new RowMetric(1, 20, 0),
                    new RowMetric(2, 20, 20),
                    new RowMetric(3, 20, 40)
                ],
                [
                    new ColMetric(1, 20, 0),
                    new ColMetric(2, 20, 20),
                    new ColMetric(3, 20, 40)
                ])
        };

        grid.Measure(new Size(60, 60));
        grid.Arrange(new Rect(0, 0, 60, 60));
        grid.UpdateLayout();
        return grid;
    }

    private static RenderTargetBitmap RenderGridToBitmap(GridView grid)
    {
        var bitmap = new RenderTargetBitmap(60, 60, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(grid);
        return bitmap;
    }

    /// <summary>
    /// True when any pixel in the region is near-black (the marching-ants marquee's outer pen is
    /// solid black, thickness 2.5, and is the only black-ish thing this minimal scene -- with no
    /// grid lines, headers, or cell content -- can ever draw). The scene's background is opaque
    /// white (RenderCellBackgroundBase), so plain alpha alone can't distinguish "marquee drawn" from
    /// "background painted".
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
    public void RenderMarchingAnts_HidesMarquee_WhenClipboardRangeBelongsToAnotherSheet()
    {
        WpfTestThread.Run(() =>
        {
            var copiedSheet = SheetId.New();
            var otherSheet = SheetId.New();

            // Row 2 / Col 2 (offsets 20..40) is the interior cell whose marquee rect (20,20,20,20)
            // sits fully away from the canvas edges, so the whole bordered region can be scanned.
            var range = new GridRange(
                new CellAddress(copiedSheet, 2, 2),
                new CellAddress(copiedSheet, 2, 2));

            // Active sheet is a DIFFERENT sheet than the one the range was copied from -- Excel
            // would show no marching ants here at all.
            var grid = CreateGrid(range, activeSheetId: otherSheet);
            var bitmap = RenderGridToBitmap(grid);

            var borderRegion = new Int32Rect(17, 17, 26, 26);
            AnyBlackishPixelInRegion(bitmap, borderRegion).Should().BeFalse(
                "the marching-ants marquee must not render on a sheet other than the one the range was copied from");
        });
    }

    [Fact]
    public void RenderMarchingAnts_ShowsMarquee_WhenClipboardRangeBelongsToActiveSheet_NoRegression()
    {
        WpfTestThread.Run(() =>
        {
            var copiedSheet = SheetId.New();

            var range = new GridRange(
                new CellAddress(copiedSheet, 2, 2),
                new CellAddress(copiedSheet, 2, 2));

            // Active sheet IS the sheet the range was copied from -- the marquee must still render.
            var grid = CreateGrid(range, activeSheetId: copiedSheet);
            var bitmap = RenderGridToBitmap(grid);

            var borderRegion = new Int32Rect(17, 17, 26, 26);
            AnyBlackishPixelInRegion(bitmap, borderRegion).Should().BeTrue(
                "the marching-ants marquee must still render around the copied range on the sheet it was copied from");
        });
    }
}
