using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R74-render-gridlines-borders-4-4 (print-path twin): PrintRenderer.GridCells.cs's
/// DrawPrintedBorderEdge thickness switch carries the exact same bucket omission as the on-screen
/// GridView.Rendering.CellStyles.cs switch -- <see cref="BorderStyle.SlantDashDot"/> was missing
/// from the medium (1.5 DIP) bucket and fell through to the 0.5 (Thin) default. The fix adds
/// SlantDashDot to the print path's medium-thickness case too, so printed/PDF output matches the
/// screen.
/// </summary>
public sealed class R74_PrintedSlantDashDotThicknessTests
{
    private const int Dpi = 768; // 8x scale over 96 DPI for reliable sub-DIP width measurement.
    private const double Scale = Dpi / 96.0;
    private const double LineLengthDip = 40.0;
    private const int CanvasWidthDip = 40;

    /// <summary>
    /// Renders a single vertical border edge via the print path's private DrawPrintedBorderEdge
    /// and returns, for each device-pixel row, the widest contiguous run of painted pixels centered
    /// near the nominal line X -- taking the max across all rows so a dashed style's "off" segments
    /// (which paint nothing) don't understate the pen's true thickness.
    /// </summary>
    private static int MaxPaintedLineWidthDevicePixels(BorderStyle style)
    {
        var border = new CellBorder(style, CellColor.Black);
        var p1 = new Point(20, 2);
        var p2 = new Point(20, 2 + LineLengthDip);

        var method = typeof(PrintRenderer).GetMethod("DrawPrintedBorderEdge", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            method!.Invoke(null, [dc, border, p1, p2, false]);
        }

        var width = (int)(CanvasWidthDip * Scale);
        var height = (int)((LineLengthDip + 4) * Scale);
        var bitmap = new RenderTargetBitmap(width, height, Dpi, Dpi, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);

        bool IsPainted(int x, int y)
        {
            var i = (y * width + x) * 4;
            var blue = pixels[i];
            var green = pixels[i + 1];
            var red = pixels[i + 2];
            var alpha = pixels[i + 3];
            return alpha > 10 && (red < 245 || green < 245 || blue < 245);
        }

        var maxRun = 0;
        for (var y = 0; y < height; y++)
        {
            var run = 0;
            for (var x = 0; x < width; x++)
            {
                if (IsPainted(x, y))
                    run++;
                else
                {
                    maxRun = Math.Max(maxRun, run);
                    run = 0;
                }
            }
            maxRun = Math.Max(maxRun, run);
        }

        return maxRun;
    }

    [Fact]
    public void SlantDashDotBorder_PrintsAtMediumWeight_NotThin()
    {
        StaTestRunner.Run(() =>
        {
            var slantWidth = MaxPaintedLineWidthDevicePixels(BorderStyle.SlantDashDot);
            var thinWidth = MaxPaintedLineWidthDevicePixels(BorderStyle.Thin);
            var mediumWidth = MaxPaintedLineWidthDevicePixels(BorderStyle.Medium);

            // Thin = 0.5 DIP = 4 device px at 8x scale; Medium/SlantDashDot = 1.5 DIP = 12 device
            // px. Pre-fix, SlantDashDot fell through to the 0.5 default, so slantWidth ~= thinWidth;
            // post-fix it must match Medium's much wider stroke instead.
            slantWidth.Should().BeGreaterThan(thinWidth,
                "SlantDashDot must print visibly wider than Thin now that it is bucketed as medium-weight");
            Math.Abs(slantWidth - mediumWidth).Should().BeLessThanOrEqualTo(2,
                "SlantDashDot's printed thickness should match Medium's (both 1.5 DIP), within anti-aliasing tolerance");
        });
    }

    [Fact]
    public void ThinBorder_StillPrintsAtThinWeight_NoRegression()
    {
        StaTestRunner.Run(() =>
        {
            var thinWidth = MaxPaintedLineWidthDevicePixels(BorderStyle.Thin);
            var mediumWidth = MaxPaintedLineWidthDevicePixels(BorderStyle.Medium);

            thinWidth.Should().BeLessThan(mediumWidth,
                "a Thin border must keep printing narrower than a Medium border");
        });
    }
}
