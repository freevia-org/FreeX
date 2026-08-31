using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R66-render-gridlines-borders-6-4: GridView.Rendering.cs's DrawCellSurface stroked every merged
/// cell with the default gray GridPen unconditionally ("dc.DrawRectangle(fill, isMerged ? GridPen
/// : null, rect)"), ignoring both <see cref="GridView.ShowGridLines"/> and whether the merge has
/// its own explicit fill hiding it -- so with gridlines OFF a merge still showed a gray outline,
/// and a filled merge showed a gray outline drawn over its own fill (an unmerged filled cell never
/// gets a matching outline). The fix only strokes the merge's default gridline outline when
/// ShowGridLines is true AND the merge has no explicit (gradient or solid FillColor) fill of its
/// own, mirroring the "if (!ShowGridLines) return;" gate in RenderCellBackgroundBase and the
/// "ShowGridLines ? GridPen : null" gate in RenderSplitPaneCells.
/// </summary>
public sealed class GridViewMergedCellGridlineStrokeTests
{
    private const int Width = 40;
    private const int Height = 20;

    // The default merge gridline (GridLineBrush, GridView.cs) is achromatic RGB(220,220,220), drawn
    // with a 1-DIP pen centered exactly on the rect's own edge -- so at y=0 only the inner half of
    // the stroke is actually inside the canvas (the outer half is clipped), anti-aliasing the
    // sampled pixel to a blend between the gray line and whatever's behind it rather than the pure
    // (220,220,220) pen color. What distinguishes "a gray gridline was drawn here" from "no stroke,
    // just the plain white/fill background" is achromaticity (R==G==B, since GridLineBrush has no
    // color tint) plus being visibly darker than pure white -- the explicit blue fill in the second
    // test is neither achromatic nor anywhere near white, so it can never be mistaken for this.
    private static bool IsWhitePixel(byte r, byte g, byte b) => r >= 250 && g >= 250 && b >= 250;
    private static bool IsGridlineGrayPixel(byte r, byte g, byte b) =>
        r == g && g == b && r is >= 150 and < 250;

    private static (byte R, byte G, byte B, byte A) GetTopEdgePixel(byte[] pixels)
    {
        // Sample the middle of the rect's top edge (y=0), safely away from any corner anti-aliasing.
        const int x = Width / 2;
        const int y = 0;
        var i = (y * Width + x) * 4;
        return (pixels[i + 2], pixels[i + 1], pixels[i], pixels[i + 3]);
    }

    private static void RenderAndAssert(CellStyle? bg, bool isMerged, bool showGridLines, Action<byte, byte, byte, byte> assert)
    {
        WpfTestThread.Run(() =>
        {
            var grid = new GridView { ShowGridLines = showGridLines };
            var rect = new Rect(0, 0, Width, Height);


            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                grid.DrawCellSurface(dc, rect, bg, isMerged, 0.0, 0.0, (double)Width, (double)Height);
            }

            var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var pixels = new byte[Width * Height * 4];
            bitmap.CopyPixels(pixels, Width * 4, 0);

            var (r, g, b, a) = GetTopEdgePixel(pixels);
            assert(r, g, b, a);
        });
    }

    [Fact]
    public void GridlinesOff_UnfilledMerge_DrawsNoOutline()
    {
        // Failure scenario: gridlines are OFF, and the merge carries no explicit border or fill --
        // pre-fix, DrawCellSurface still stroked it with the gray GridPen regardless.
        RenderAndAssert(bg: null, isMerged: true, showGridLines: false, (r, g, b, a) =>
        {
            a.Should().BeGreaterThan(0);
            IsWhitePixel(r, g, b).Should().BeTrue(
                "with ShowGridLines off, a merged cell's own default fallback fill must show no gray outline at all");
        });
    }

    [Fact]
    public void GridlinesOn_FilledMerge_DrawsFillWithNoGrayOutlineOverIt()
    {
        // Failure scenario: gridlines are ON and the merge has its own explicit solid FillColor --
        // pre-fix, the gray GridPen was still drawn ON TOP of that fill, exactly like an unmerged
        // filled cell never shows.
        var style = new CellStyle { FillColor = new CellColor(0, 0, 255) };
        RenderAndAssert(style, isMerged: true, showGridLines: true, (r, g, b, a) =>
        {
            a.Should().BeGreaterThan(0);
            r.Should().Be(0);
            g.Should().Be(0);
            b.Should().Be(255);
            IsGridlineGrayPixel(r, g, b).Should().BeFalse(
                "a merge with its own explicit fill must show that fill undiluted at its own edge, not a gray gridline drawn over it");
        });
    }

    [Fact]
    public void GridlinesOn_UnfilledMerge_StillDrawsOutline_NoRegression()
    {
        // Sibling no-regression case: gridlines ON and NO explicit fill/border authored -- Excel
        // still shows the plain gridline outline around a merge in this case, so the fix must not
        // suppress it too.
        RenderAndAssert(bg: null, isMerged: true, showGridLines: true, (r, g, b, a) =>
        {
            a.Should().BeGreaterThan(0);
            IsGridlineGrayPixel(r, g, b).Should().BeTrue(
                "with ShowGridLines on and no explicit fill, the merge's default gridline outline must still draw, matching Excel");
        });
    }
}
