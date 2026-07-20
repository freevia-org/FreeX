using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R53-render-cell-text-overflow-3-3: a plain hidden column has NO entry in the viewport's
/// ColMetrics at all (ViewportService.Metrics.BuildColMetrics skips it entirely rather than giving
/// it a zero-width entry), so the overflow-extension scan in GridView.Rendering.cs must treat a
/// missing colLookup entry as "this column is hidden, keep going" rather than "stop here" -- Excel
/// treats a hidden column as transparent to overflow. Verified end-to-end via the real WPF render
/// pipeline (RenderTargetBitmap), since the fix lives inline in RenderCells's overflow-extension
/// loop rather than in a separately callable helper.
/// </summary>
public sealed class GridViewHiddenColumnOverflowTests
{
    private const string OverflowText = "WWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWW";
    private static readonly CellColor Red = new(255, 0, 0);

    private static GridView CreateGridWithHiddenColumnGap(bool includeStopCell)
    {
        var cells = new List<DisplayCell>
        {
            // A1 (col 1): long overflowing text, styled red so it's trivially distinguishable from
            // the default-black "Data" stop cell. Column 2 is hidden -- deliberately absent from
            // both the cell list and (crucially) the ColMetrics below.
            new(1, 1, new TextValue(OverflowText), OverflowText, null, StyleId.Default, null,
                new CellStyle { FontColor = Red }),
            // Column 3: ordinary visible blank cell (transparent to overflow either way).
            new(1, 3, null, "", null, StyleId.Default, null),
        };
        if (includeStopCell)
            cells.Add(new DisplayCell(1, 4, new TextValue("Data"), "Data", null, StyleId.Default, null));

        var grid = new GridView
        {
            Width = 260,
            Height = 30,
            ShowHeaders = false,
            ShowGridLines = false,
            Viewport = new ViewportModel(
                cells,
                [new RowMetric(1, 24, 0)],
                [
                    // Column 2 (hidden) has NO entry -- col 3's LeftOffset (64) is exactly what
                    // ViewportService.Metrics.BuildColMetrics would compute for a hidden column
                    // contributing zero width.
                    new ColMetric(1, 64, 0),
                    new ColMetric(3, 64, 64),
                    new ColMetric(4, 64, 128),
                ])
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
    public void Overflow_SkipsOverHiddenColumnGapIntoNextVisibleBlankColumn()
    {
        WpfTestThread.Run(() =>
        {
            var grid = CreateGridWithHiddenColumnGap(includeStopCell: true);
            var bitmap = RenderToBitmap(grid);

            // Column 3 spans x=[64,128). Pre-fix, the overflow scan hit the missing ColMetric entry
            // for hidden column 2 and stopped dead, clipping A1's overflow to its own 64px width --
            // so no red pixel would ever reach into column 3's span. Post-fix, the hidden column is
            // transparent and the red overflow text continues across it into column 3.
            AnyPixelInRangeIsRed(bitmap, 64, 128).Should().BeTrue(
                "Excel lets overflow text slide over a hidden column and continue into the next visible blank column");
        });
    }

    [Fact]
    public void Overflow_StillStopsAtTheNextOccupiedVisibleColumn_NoRegression()
    {
        WpfTestThread.Run(() =>
        {
            var grid = CreateGridWithHiddenColumnGap(includeStopCell: true);
            var bitmap = RenderToBitmap(grid);

            // Column 4 ("Data", x=[128,192)) is a real, non-blank cell -- the overflow scan must
            // still stop before it, exactly as it did before this fix (hidden-column transparency
            // must not make the scan barrel through genuinely occupied cells too).
            AnyPixelInRangeIsRed(bitmap, 128, 192).Should().BeFalse(
                "overflow must still stop at the next occupied visible cell, unaffected by the hidden-column fix");
        });
    }
}
