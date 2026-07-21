using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R57-render-cell-overflow-clip-5-1: when the sheet has a frozen column and is scrolled far to
/// the right, the combined viewport ColMetrics list has entries only for columns 1..FrozenCols
/// (the pinned pane) and bodyStart..end (the visible scrollable pane) -- everything strictly
/// between (the merely scrolled-off, NOT hidden, columns) has no ColMetrics entry at all. That is
/// the exact same "colLookup miss" signature the overflow-extension scan uses to detect a
/// genuinely-hidden column, so pre-fix the scan tunneled straight across the frozen/scrolled-body
/// seam and painted the frozen column's overflow text into the first visible scrollable column.
/// Real Excel renders the frozen pane as a separate clip region, so overflow must stop dead at the
/// pane boundary regardless of what is or isn't in the (offscreen) scrolled-past columns.
/// </summary>
public sealed class GridViewFrozenSeamOverflowTests
{
    private const string OverflowText = "WWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWW";
    private static readonly CellColor Red = new(255, 0, 0);

    private static GridView CreateGridWithFrozenSeamGap(bool frozen)
    {
        var cells = new List<DisplayCell>
        {
            // A1 (col 1): long overflowing text, styled red. When frozen, this is the sole (and
            // last) frozen column.
            new(1, 1, new TextValue(OverflowText), OverflowText, null, StyleId.Default, null,
                new CellStyle { FontColor = Red }),
            // The first visible scrollable column, rendered immediately after the pinned column
            // (CombineColumnsWithOffset always places it there visually) even though its real
            // sheet column number (53) is far away -- columns 2..52 were scrolled off and are
            // simply absent from ColMetrics, not hidden.
            new(1, 53, null, "", null, StyleId.Default, null),
        };

        var grid = new GridView
        {
            Width = 128,
            Height = 30,
            ShowHeaders = false,
            ShowGridLines = false,
            Viewport = new ViewportModel(
                cells,
                [new RowMetric(1, 24, 0)],
                [
                    new ColMetric(1, 64, 0),
                    new ColMetric(53, 64, 64),
                ],
                FrozenPanes: frozen ? new FrozenPaneState(0, 1) : null)
        };

        grid.Measure(new Size(grid.Width, grid.Height));
        grid.Arrange(new Rect(0, 0, grid.Width, grid.Height));
        grid.UpdateLayout();
        return grid;
    }

    private static RenderTargetBitmap RenderToBitmap(GridView grid)
    {
        var bitmap = new RenderTargetBitmap((int)grid.Width, (int)grid.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(grid);
        return bitmap;
    }

    /// <summary>True when any pixel in the given X range (across the bitmap's full height) is clearly red-dominant.</summary>
    private static bool AnyPixelInRangeIsRed(BitmapSource bitmap, int minX, int maxXExclusive)
    {
        minX = Math.Max(0, minX);
        maxXExclusive = Math.Min(bitmap.PixelWidth, maxXExclusive);
        var width = maxXExclusive - minX;
        if (width <= 0) return false;

        var stride = width * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(new Int32Rect(minX, 0, width, bitmap.PixelHeight), pixels, stride, 0);

        for (var i = 0; i < pixels.Length; i += 4)
        {
            var blue = pixels[i];
            var green = pixels[i + 1];
            var red = pixels[i + 2];
            var alpha = pixels[i + 3];
            if (alpha > 10 && red > 150 && green < 100 && blue < 100)
                return true;
        }

        return false;
    }

    [Fact]
    public void Overflow_DoesNotBleedAcrossFrozenSeam_WhenScrolledPastHiddenGap()
    {
        WpfTestThread.Run(() =>
        {
            var grid = CreateGridWithFrozenSeamGap(frozen: true);
            var bitmap = RenderToBitmap(grid);

            // Column 53 spans x=[64,128) in the combined viewport. Pre-fix, the scan treated the
            // scrolled-off gap (cols 2..52, absent from ColMetrics) exactly like a hidden column
            // and tunneled straight across it, painting red overflow text into column 53's span.
            // Post-fix, the scan must stop at the end of the frozen column (x=64) instead.
            AnyPixelInRangeIsRed(bitmap, 64, 128).Should().BeFalse(
                "Excel clips overflow at the freeze-pane boundary -- it must never bleed across the " +
                "scrolled-off gap into the first visible scrollable column");
        });
    }

    [Fact]
    public void Overflow_StillBleedsIntoNextColumn_WhenNoFreezePaneIsSet_NoRegression()
    {
        WpfTestThread.Run(() =>
        {
            // Identical column layout/gap, but with no frozen pane at all: this is not a seam,
            // just an ordinary (if unusual) viewport, so the pre-existing "gap is transparent"
            // overflow behavior for a genuinely blank neighboring column must be unaffected by the
            // frozen-seam clamp.
            var grid = CreateGridWithFrozenSeamGap(frozen: false);
            var bitmap = RenderToBitmap(grid);

            AnyPixelInRangeIsRed(bitmap, 64, 128).Should().BeTrue(
                "without a frozen pane there is no seam to stop at, so overflow must still slide " +
                "across into the next blank column exactly as before this fix");
        });
    }
}
