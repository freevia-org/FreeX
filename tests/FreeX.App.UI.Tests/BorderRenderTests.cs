using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// Round-37 border-render fixes:
/// R37-render-borders-gridlines-2-1 (Double border draws as two parallel lines, not one solid line),
/// R37-render-borders-gridlines-2-3 (merge interior edges are suppressed, not drawn through the merged cell), and
/// R37-render-borders-gridlines-2-4 (adjacent conflicting borders resolve deterministically to the heavier style).
/// </summary>
public sealed class BorderRenderTests
{
    private static void InvokeDrawBorderEdge(
        DrawingContext dc,
        CellBorder border,
        Point p1,
        Point p2,
        double effectivePixelsPerDip = 1.0)
    {
        // Direct call, not reflection: DrawBorderEdge is internal and this assembly has
        // InternalsVisibleTo, so a change to its signature is a build error right here rather than a
        // runtime TargetParameterCountException out of a positional argument array.
        GridView.DrawBorderEdge(dc, border, p1, p2, WorkbookTheme.Office, null, null, effectivePixelsPerDip);
    }

    private static RenderTargetBitmap RenderLineToBitmap(CellBorder border, Point p1, Point p2, int dpi)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            InvokeDrawBorderEdge(dc, border, p1, p2, dpi / 96.0);
        }

        var scale = dpi / 96.0;
        var bitmap = new RenderTargetBitmap(
            (int)(100 * scale),
            (int)(40 * scale),
            dpi,
            dpi,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    private static RenderTargetBitmap RenderHorizontalLinesToBitmap(params double[] centers)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var border = new CellBorder(BorderStyle.Thin, CellColor.Black);
            foreach (var y in centers)
                InvokeDrawBorderEdge(dc, border, new Point(10, y), new Point(90, y));
        }

        var bitmap = new RenderTargetBitmap(100, 40, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    /// <summary>True when the device pixel at (x, y) is meaningfully non-white (i.e. painted).</summary>
    private static bool IsPaintedPixel(RenderTargetBitmap bitmap, int x, int y)
    {
        var pixels = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixels, 4, 0);
        var blue = pixels[0];
        var green = pixels[1];
        var red = pixels[2];
        var alpha = pixels[3];
        return alpha > 10 && (red < 245 || green < 245 || blue < 245);
    }

    private static int CountPaintedRowsNear(RenderTargetBitmap bitmap, int x, int centerY, int radius = 3) =>
        Enumerable.Range(centerY - radius, radius * 2 + 1)
            .Count(y => y >= 0 && y < bitmap.PixelHeight && IsPaintedPixel(bitmap, x, y));

    [Fact]
    public void DoubleBorder_RendersAsTwoParallelLinesWithGapBetweenThem()
    {
        WpfTestThread.Run(() =>
        {
            const int dpi = 768; // 8x scale over 96 DPI so the 1.0-DIP gap resolves to 8 clean device pixels
            var scale = dpi / 96.0;
            var border = new CellBorder(BorderStyle.Double, CellColor.Black);
            var p1 = new Point(10, 20);
            var p2 = new Point(90, 20);

            var bitmap = RenderLineToBitmap(border, p1, p2, dpi);

            var x = (int)(50 * scale);
            var centerY = (int)(20 * scale);

            // Excel's Double border is two thin lines straddling the nominal edge with a clear gap
            // between them; the pixel row exactly at the original edge coordinate must therefore be
            // untouched (white), while rows a few device pixels above and below it (each strut of the
            // double line) must be painted.
            IsPaintedPixel(bitmap, x, centerY).Should().BeFalse(
                "the Double border must leave a visible gap exactly at the nominal edge, not a single solid line");

            var aboveHasPaint = Enumerable.Range(1, 6).Any(d => IsPaintedPixel(bitmap, x, centerY - d));
            var belowHasPaint = Enumerable.Range(1, 6).Any(d => IsPaintedPixel(bitmap, x, centerY + d));
            aboveHasPaint.Should().BeTrue("the Double border's first parallel line should be painted above the nominal edge");
            belowHasPaint.Should().BeTrue("the Double border's second parallel line should be painted below the nominal edge");
        });
    }

    [Fact]
    public void ThinBorder_StillRendersAsASingleUnbrokenLine_NoRegression()
    {
        WpfTestThread.Run(() =>
        {
            const int dpi = 768;
            var scale = dpi / 96.0;
            var border = new CellBorder(BorderStyle.Thin, CellColor.Black);
            var p1 = new Point(10, 20);
            var p2 = new Point(90, 20);

            var bitmap = RenderLineToBitmap(border, p1, p2, dpi);

            var x = (int)(50 * scale);
            var centerY = (int)(20 * scale);

            // A non-Double style must remain a single line drawn exactly at the requested edge
            // (this is the pre-existing, still-correct behavior for every other border style).
            IsPaintedPixel(bitmap, x, centerY).Should().BeTrue(
                "a Thin border should still paint a single unbroken line centered on the requested edge");
        });
    }

    [Fact]
    public void ThinBordersAtDifferentFractionalCenters_RenderSameDevicePixelThickness()
    {
        WpfTestThread.Run(() =>
        {
            var bitmap = RenderHorizontalLinesToBitmap(10.0, 15.5);

            var integerCenteredRows = CountPaintedRowsNear(bitmap, x: 50, centerY: 10);
            var halfCenteredRows = CountPaintedRowsNear(bitmap, x: 50, centerY: 15);

            integerCenteredRows.Should().Be(1,
                "a Thin border on an integer DIP boundary should snap to one crisp device pixel");
            halfCenteredRows.Should().Be(integerCenteredRows,
                "identical Thin borders must not alternate between one and two painted pixel rows based on fractional position");
        });
    }

    private static GridView CreateGridWithCells(
        IReadOnlyList<DisplayCell> cells,
        IReadOnlyList<GridRange>? mergedRegions = null,
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

    private static RenderTargetBitmap RenderGridToBitmap(GridView grid)
    {
        var width = (int)grid.Width;
        var bitmap = new RenderTargetBitmap(width, 40, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(grid);
        return bitmap;
    }

    /// <summary>
    /// True when any pixel within <paramref name="xRadius"/> device pixels of <paramref name="centerX"/>
    /// (across the bitmap's full height) has a clearly red-dominant tint. Anti-aliasing near a straight
    /// pixel-grid-aligned line can soften pure (255,0,0) to a lighter shade, so this checks for the red
    /// channel being distinctly higher than green/blue rather than requiring an exact color match.
    /// </summary>
    private static bool AnyPixelNearXIsReddish(BitmapSource bitmap, int centerX, int xRadius, int height = 40)
    {
        var minX = Math.Max(0, centerX - xRadius);
        var maxXExclusive = Math.Min(bitmap.PixelWidth, centerX + xRadius + 1);
        var width = maxXExclusive - minX;
        if (width <= 0) return false;

        var stride = width * 4;
        var pixels = new byte[stride * height];
        bitmap.CopyPixels(new Int32Rect(minX, 0, width, height), pixels, stride, 0);
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

    // Row 1, Column 1 = "A1"; Row 1, Column 2 = "B1" (same row, adjacent column) throughout.
    [Fact]
    public void MergedCell_SuppressesInteriorBorderInsteadOfDrawingThroughIt()
    {
        WpfTestThread.Run(() =>
        {
            var sheet = SheetId.New();
            var merge = new GridRange(new CellAddress(sheet, 1, 1), new CellAddress(sheet, 1, 2));

            // A1 (the merge anchor) carries a BorderRight that becomes purely interior once
            // A1:B1 is merged -- Excel never draws a line through the middle of a merged cell.
            var anchorStyle = new CellStyle { BorderRight = new CellBorder(BorderStyle.Thick, new CellColor(255, 0, 0)) };
            var cells = new DisplayCell[]
            {
                new(1, 1, null, "", null, default, null, anchorStyle),
                new(1, 2, null, "", null, default, null, null)
            };

            var grid = CreateGridWithCells(cells, [merge]);
            var bitmap = RenderGridToBitmap(grid);

            // The interior boundary between column A and column B sits at x = 80; no red pixels
            // should appear there once the interior edge is suppressed.
            AnyPixelNearXIsReddish(bitmap, centerX: 80, xRadius: 3).Should().BeFalse(
                "a border on the interior edge of a merged region must be suppressed, not drawn through the merged cell");
        });
    }

    [Fact]
    public void MergedCell_StillDrawsItsOuterPerimeterBorder_NoRegression()
    {
        WpfTestThread.Run(() =>
        {
            var sheet = SheetId.New();
            var merge = new GridRange(new CellAddress(sheet, 1, 1), new CellAddress(sheet, 1, 2));

            // B1 is the last column of the merge, so its own BorderRight is the merge's OUTER
            // perimeter edge (at x = 160), not an interior edge -- it must still be drawn.
            var farStyle = new CellStyle { BorderRight = new CellBorder(BorderStyle.Thick, new CellColor(255, 0, 0)) };
            var cells = new DisplayCell[]
            {
                new(1, 1, null, "", null, default, null, null),
                new(1, 2, null, "", null, default, null, farStyle)
            };

            // Widen the viewport past the merge's right edge (x = 160) so the perimeter line
            // isn't half-clipped by the render surface boundary itself.
            var grid = CreateGridWithCells(cells, [merge], width: 200);
            var bitmap = RenderGridToBitmap(grid);

            AnyPixelNearXIsReddish(bitmap, centerX: 160, xRadius: 3).Should().BeTrue(
                "the merge's own outer perimeter edge must still be drawn even though interior edges are suppressed");
        });
    }

    /// <summary>
    /// Reads the raw (B,G,R,A) channels at a single device pixel.
    /// </summary>
    private static (byte Blue, byte Green, byte Red, byte Alpha) GetPixel(BitmapSource bitmap, int x, int y)
    {
        var pixels = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixels, 4, 0);
        return (pixels[0], pixels[1], pixels[2], pixels[3]);
    }

    [Fact]
    public void AdjacentConflictingBorders_HeavierStyleWinsRegardlessOfWhichCellDefinesIt()
    {
        WpfTestThread.Run(() =>
        {
            // A1.BorderRight = Thick/Red and B1.BorderLeft = Thin/Black both describe the same
            // physical boundary at x = 80. Excel resolves this deterministically to the heavier
            // (Thick) style. Without conflict resolution, DrawLine simply overlays both pens: the
            // Thin/Black line (only ~0.5 DIP wide) partially covers the middle of the Thick/Red
            // line (2.5 DIP wide) without fully replacing it, leaving a visibly DIMMED (not pure)
            // red at the shared boundary instead of a clean, fully-saturated red line.
            var leftStyle = new CellStyle { BorderRight = new CellBorder(BorderStyle.Thick, new CellColor(255, 0, 0)) };
            var rightStyle = new CellStyle { BorderLeft = new CellBorder(BorderStyle.Thin, new CellColor(0, 0, 0)) };
            var cells = new DisplayCell[]
            {
                new(1, 1, null, "", null, default, null, leftStyle),
                new(1, 2, null, "", null, default, null, rightStyle)
            };

            var grid = CreateGridWithCells(cells);
            var bitmap = RenderGridToBitmap(grid);

            // The pen is centered exactly on x = 80, so both x = 79 and x = 80 are fully inside
            // the Thick (2.5 DIP) line; a resolved single-style winner paints them fully-saturated
            // pure red (R >= 245, G/B <= 10), whereas an unresolved dual-pen overlay dims/darkens
            // them well below that.
            var (_, green79, red79, _) = GetPixel(bitmap, 79, 20);
            var (_, green80, red80, _) = GetPixel(bitmap, 80, 20);

            (red79 >= 245 && green79 <= 10).Should().BeTrue(
                $"the heavier Thick/Red border should win cleanly with no dimming artifact at x=79 (got R={red79}, G={green79})");
            (red80 >= 245 && green80 <= 10).Should().BeTrue(
                $"the heavier Thick/Red border should win cleanly with no dimming artifact at x=80 (got R={red80}, G={green80})");
        });
    }

    [Fact]
    public void AdjacentConflictingBorders_ResolutionIsOrderIndependent_NoRegression()
    {
        WpfTestThread.Run(() =>
        {
            // Same conflict as above, but with the heavier style now defined on the SECOND cell
            // (B1) instead of the first (A1). The winner must still be a clean, fully-saturated
            // Thick/Red line -- proving the resolution is based on style weight, not on which
            // cell happens to be enumerated first/last during the render pass.
            var leftStyle = new CellStyle { BorderRight = new CellBorder(BorderStyle.Thin, new CellColor(0, 0, 0)) };
            var rightStyle = new CellStyle { BorderLeft = new CellBorder(BorderStyle.Thick, new CellColor(255, 0, 0)) };
            var cells = new DisplayCell[]
            {
                new(1, 1, null, "", null, default, null, leftStyle),
                new(1, 2, null, "", null, default, null, rightStyle)
            };

            var grid = CreateGridWithCells(cells);
            var bitmap = RenderGridToBitmap(grid);

            var (_, green79, red79, _) = GetPixel(bitmap, 79, 20);
            var (_, green80, red80, _) = GetPixel(bitmap, 80, 20);

            (red79 >= 245 && green79 <= 10).Should().BeTrue(
                $"the heavier Thick/Red border should win regardless of which cell defines it (got R={red79}, G={green79} at x=79)");
            (red80 >= 245 && green80 <= 10).Should().BeTrue(
                $"the heavier Thick/Red border should win regardless of which cell defines it (got R={red80}, G={green80} at x=80)");
        });
    }
}
