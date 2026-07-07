using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// Regression coverage for cleanup batch MED11 finding P32: the WPF sheet background tile brush must
/// stay anchored to cell A1 as the grid scrolls, instead of always anchoring to the fixed viewport rect
/// (which glued the tiles to the window and made them appear not to scroll with the cells).
/// </summary>
public sealed class FreeXCleanupMED11Tests
{
    [Fact]
    public void WorksheetBackgroundBrush_ScrolledViewport_ShiftsTileOriginByScrolledOffCellExtent()
    {
        WpfTestThread.Run(() =>
        {
            var grid = new GridView
            {
                ShowHeaders = false,
                SheetDefaultRowHeight = 20,
                SheetDefaultColumnWidth = 64
            };

            var image = new RenderTargetBitmap(40, 20, 96, 96, PixelFormats.Pbgra32);
            var background = new WorksheetBackgroundImage([1, 2, 3], "image/png", "bg.png");

            // Viewport scrolled so row 1 is not visible (first visible row is 6, first visible
            // column is 4). Row/col metrics for a scrolled viewport never include row 1/col A, so
            // the brush must derive the scrolled-off pixel extent from the default row/col size.
            var scrolledViewport = new ViewportModel(
                [],
                [new RowMetric(6, 20, 0)],
                [new ColMetric(4, 64, 0)]);
            SetViewport(grid, scrolledViewport);

            var scrolledBrush = InvokeGetWorksheetBackgroundBrush(grid, background, image);

            // Off-screen extent above row 1: (6-1) rows * 20px = 100px; mod tile height (20) = 0,
            // so the Y phase is unaffected here. Off-screen extent left of col A: (4-1) cols * 64px
            // = 192px; mod tile width (40) = 12px, so the tile origin must shift left by 12px
            // (ActualRowHeaderWidth 0 minus that 12px phase) to keep the pattern anchored to col A
            // instead of staying glued to the fixed (0,0) viewport-relative origin.
            double Mod(double value, double modulus)
            {
                var r = value % modulus;
                return r < 0 ? r + modulus : r;
            }

            var expectedX = 0 - Mod((4 - 1) * 64.0, image.Width);
            var expectedY = 0 - Mod((6 - 1) * 20.0, image.Height);

            scrolledBrush.Viewport.X.Should().BeApproximately(expectedX, 0.001);
            scrolledBrush.Viewport.Y.Should().BeApproximately(expectedY, 0.001);

            // Anchored (unscrolled, row 1 / col A visible) viewport must reproduce the original
            // fixed-rect behaviour: tile origin exactly at the header/body boundary.
            var unscrolledViewport = new ViewportModel(
                [],
                [new RowMetric(1, 20, 0)],
                [new ColMetric(1, 64, 0)]);
            SetViewport(grid, unscrolledViewport);
            var unscrolledBrush = InvokeGetWorksheetBackgroundBrush(grid, background, image);

            unscrolledBrush.Viewport.X.Should().Be(0);
            unscrolledBrush.Viewport.Y.Should().Be(0);

            // The scrolled brush's tile origin must differ from the unscrolled one: this is the
            // crux of the bug (pre-fix, both cache keys collapsed to the same fixed header rect and
            // the tile pattern never moved as the sheet scrolled).
            (scrolledBrush.Viewport.X != unscrolledBrush.Viewport.X ||
             scrolledBrush.Viewport.Y != unscrolledBrush.Viewport.Y).Should().BeTrue();
        });
    }

    private static void SetViewport(GridView grid, ViewportModel viewport)
    {
        var property = typeof(GridView).GetProperty(nameof(GridView.Viewport));
        property.Should().NotBeNull();
        property!.SetValue(grid, viewport);
    }

    private static ImageBrush InvokeGetWorksheetBackgroundBrush(GridView grid, WorksheetBackgroundImage background, ImageSource image)
    {
        var method = typeof(GridView).GetMethod("GetWorksheetBackgroundBrush", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return (ImageBrush)method!.Invoke(grid, [background, image])!;
    }
}
