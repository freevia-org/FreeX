using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// Round-71 View&gt;Split render-origin fixes:
/// R71-render-frozen-panes-4-1 (the main/BottomRight viewport's cells/borders/text render at the
/// plain header origin instead of the split-divider-relative origin), and
/// R71-render-frozen-panes-4-2 (the header gutter never shows labels for the pinned
/// TopRows/LeftColumns bands).
/// </summary>
public sealed class GridViewSplitPaneMainViewportRenderTests
{
    private static readonly CellColor Marker = new(255, 0, 0);

    // SplitRow = 10 -> pinned TopRows are rows 1-9 (9 rows @ 20px = 180px).
    // SplitColumn = 3 -> pinned LeftColumns are cols 1-2 (2 cols @ 64px = 128px).
    // horizontalY = ColHeaderHeight(18) + 180 = 198; verticalX = RowHeaderWidth(30) + 128 = 158.
    private const double ExpectedHorizontalY = GridView.ColHeaderHeight + 180;
    private const double ExpectedVerticalX = GridView.RowHeaderWidth + 128;

    private static RowMetric[] BuildTopRows()
    {
        var rows = new RowMetric[9];
        for (var i = 0; i < 9; i++)
            rows[i] = new RowMetric((uint)(i + 1), 20, i * 20);
        return rows;
    }

    private static ColMetric[] BuildLeftColumns() =>
    [
        new ColMetric(1, 64, 0),
        new ColMetric(2, 64, 64),
    ];

    // The main (BottomRight) pane's metrics are deliberately sized to run past the 320x320 test
    // canvas even from the OLD (unshifted, header-relative) origin -- so RenderViewportContinuation
    // (which still computes its own "past the last real row/column" bounds from the plain
    // ActualRowHeaderWidth/EffectiveColHeaderHeight origin, independent of the divider fix under
    // test here) never paints its blank continuation area over the now divider-shifted cells. This
    // mirrors a real, generously-sized viewport request, where the same is true in production.
    private static RowMetric[] BuildMainRows()
    {
        var rows = new RowMetric[20];
        for (var i = 0; i < 20; i++)
            rows[i] = new RowMetric((uint)(10 + i), 20, i * 20);
        return rows;
    }

    private static ColMetric[] BuildMainColumns()
    {
        var cols = new ColMetric[6];
        for (var i = 0; i < 6; i++)
            cols[i] = new ColMetric((uint)(3 + i), 64, i * 64);
        return cols;
    }

    private static GridView CreateSplitGridView(bool includeMarkerCell = true)
    {
        var mainRows = BuildMainRows();
        var mainCols = BuildMainColumns();

        var cells = includeMarkerCell
            ? new[] { new DisplayCell(10, 3, null, "", null, default, null, new CellStyle { FillColor = Marker }) }
            : [];

        var grid = new GridView
        {
            Width = 320,
            Height = 320,
            ShowHeaders = true,
            ShowGridLines = false,
            Viewport = new ViewportModel(
                cells,
                mainRows,
                mainCols,
                SplitPanes: new SplitPaneState(
                    10,
                    3,
                    BuildTopRows(),
                    BuildLeftColumns())),
        };

        grid.Measure(new Size(320, 320));
        grid.Arrange(new Rect(0, 0, 320, 320));
        grid.UpdateLayout();
        return grid;
    }

    private static GridView CreateNoSplitGridView()
    {
        var cells = new[] { new DisplayCell(1, 1, null, "", null, default, null, new CellStyle { FillColor = Marker }) };
        var grid = new GridView
        {
            Width = 200,
            Height = 200,
            ShowHeaders = true,
            ShowGridLines = false,
            Viewport = new ViewportModel(
                cells,
                [new RowMetric(1, 20, 0)],
                [new ColMetric(1, 64, 0)]),
        };

        grid.Measure(new Size(200, 200));
        grid.Arrange(new Rect(0, 0, 200, 200));
        grid.UpdateLayout();
        return grid;
    }

    private static RenderTargetBitmap RenderGridToBitmap(GridView grid)
    {
        var bitmap = new RenderTargetBitmap((int)grid.Width, (int)grid.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(grid);
        return bitmap;
    }

    /// <summary>True when the pixel at (x, y) is clearly red-dominant (the marker cell's fill).</summary>
    private static bool IsMarkerPixel(BitmapSource bitmap, int x, int y)
    {
        if (x < 0 || y < 0 || x >= bitmap.PixelWidth || y >= bitmap.PixelHeight)
            return false;

        var pixels = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixels, 4, 0);
        var blue = pixels[0];
        var green = pixels[1];
        var red = pixels[2];
        var alpha = pixels[3];
        return alpha > 10 && red > 150 && red - green > 30 && red - blue > 30;
    }

    [Fact]
    public void SplitActive_MainViewportCellRendersAtDividerRelativeOrigin_NotHeaderOrigin()
    {
        WpfTestThread.Run(() =>
        {
            var grid = CreateSplitGridView();
            var bitmap = RenderGridToBitmap(grid);

            IsMarkerPixel(bitmap, (int)ExpectedVerticalX + 10, (int)ExpectedHorizontalY + 10).Should().BeTrue(
                "the main (BottomRight) viewport cell must render at the split-divider-relative origin (verticalX/horizontalY), matching HitTestViewportCell's geometry");

            IsMarkerPixel(bitmap, (int)GridView.RowHeaderWidth + 10, (int)GridView.ColHeaderHeight + 10).Should().BeFalse(
                "the main viewport cell must NOT render anchored directly under the header once a split is active");
        });
    }

    [Fact]
    public void NoSplit_MainViewportCellRendersAtPlainHeaderOrigin_NoRegression()
    {
        WpfTestThread.Run(() =>
        {
            var grid = CreateNoSplitGridView();
            var bitmap = RenderGridToBitmap(grid);

            IsMarkerPixel(bitmap, (int)GridView.RowHeaderWidth + 10, (int)GridView.ColHeaderHeight + 10).Should().BeTrue(
                "without a split, the cell must still render at the plain header origin");
        });
    }

    /// <summary>
    /// Invokes the private instance method <c>GridView.RenderHeaderBase</c> directly on a bare
    /// <see cref="DrawingVisual"/>, bypassing <c>OnRender</c> entirely (no layout, no
    /// RenderViewportContinuation, no cell painting) so the header-gutter positions under test are
    /// governed ONLY by RenderHeaderBase's own logic, with no confounding overlap from unrelated
    /// render passes.
    /// </summary>
    private static void InvokeRenderHeaderBase(
        DrawingContext dc,
        ViewportModel viewport,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double rowOutlineWidth,
        double columnOutlineHeight,
        double visibleBottom,
        double pixelsPerDip)
    {
        var method = typeof(GridView).GetMethod("RenderHeaderBase", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();
        method!.Invoke(new GridView(), [dc, viewport, rowHeaderWidth, columnHeaderHeight, rowOutlineWidth, columnOutlineHeight, visibleBottom, pixelsPerDip]);
    }

    private static RenderTargetBitmap RenderHeaderBaseToBitmap(ViewportModel viewport, int width, int height)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            InvokeRenderHeaderBase(dc, viewport, GridView.RowHeaderWidth, GridView.ColHeaderHeight, 0, 0, 5000, 1.0);
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    [Fact]
    public void SplitActive_HeaderGutter_ShowsBothPinnedBandAndMainPaneLabels()
    {
        WpfTestThread.Run(() =>
        {
            // Main row/col metrics are pushed far away (TopOffset/LeftOffset = 200) so their
            // unshifted position (200 + RowHeaderWidth/ColHeaderHeight) and their divider-shifted
            // position (200 + verticalX/horizontalY) never coincide with each other OR with the
            // pinned bands' own check positions below -- every checked pixel can only be painted by
            // exactly one specific code path, so the test cannot pass by accident.
            var mainRows = new[] { new RowMetric(10, 20, 200) };
            var mainCols = new[] { new ColMetric(3, 64, 200) };
            var viewport = new ViewportModel(
                [],
                mainRows,
                mainCols,
                SplitPanes: new SplitPaneState(10, 3, BuildTopRows(), BuildLeftColumns()));

            var bitmap = RenderHeaderBaseToBitmap(viewport, 500, 500);

            // Pinned TopRows band: row 5 (the 5th pinned row, TopOffset=80) must show its OWN
            // row-number label at the un-shifted origin (y = ColHeaderHeight(18) + 80 = 98..118),
            // regardless of where the main pane's row is pushed.
            HasNonBackgroundPixelInRow(bitmap, x: 15, y: 108)
                .Should().BeTrue("the pinned TopRows band must show its own row-number labels");

            // Main-pane row 10 must show its label at the DIVIDER-shifted position
            // (horizontalY(198) + 200 = 398..418), not the un-shifted position (18 + 200 = 218..238).
            HasNonBackgroundPixelInRow(bitmap, x: 15, y: 408)
                .Should().BeTrue("the main pane's rows must show labels shifted past the divider");

            // Pinned LeftColumns band: col 1 must show its OWN column-letter label at the
            // un-shifted origin (x = RowHeaderWidth(30) + 0 = 30..94).
            HasNonBackgroundPixelInRow(bitmap, x: 62, y: 9)
                .Should().BeTrue("the pinned LeftColumns band must show its own column-letter labels");

            // Main-pane col 3 must show its label at the DIVIDER-shifted position
            // (verticalX(158) + 200 = 358..422), not the un-shifted position (30 + 200 = 230..294).
            HasNonBackgroundPixelInRow(bitmap, x: 390, y: 9)
                .Should().BeTrue("the main pane's columns must show labels shifted past the divider");
        });
    }

    [Fact]
    public void NoSplit_HeaderGutter_RendersOnlyMainPaneLabels_NoRegression()
    {
        WpfTestThread.Run(() =>
        {
            var viewport = new ViewportModel(
                [],
                [new RowMetric(1, 20, 0)],
                [new ColMetric(1, 64, 0)]);

            var bitmap = RenderHeaderBaseToBitmap(viewport, 200, 200);

            HasNonBackgroundPixelInRow(bitmap, x: 15, y: (int)GridView.ColHeaderHeight + 10)
                .Should().BeTrue("without a split, the single row must still render its label at the plain header origin");
            HasNonBackgroundPixelInRow(bitmap, x: (int)GridView.RowHeaderWidth + 32, y: 9)
                .Should().BeTrue("without a split, the single column must still render its label at the plain header origin");
        });
    }

    /// <summary>
    /// True when any pixel in a small neighborhood around (x, y) differs noticeably from the plain
    /// header-background gray, i.e. a text glyph was drawn there.
    /// </summary>
    private static bool HasNonBackgroundPixelInRow(BitmapSource bitmap, int x, int y)
    {
        const int radius = 6;
        var minX = Math.Max(0, x - radius);
        var maxXExclusive = Math.Min(bitmap.PixelWidth, x + radius + 1);
        var minY = Math.Max(0, y - radius);
        var maxYExclusive = Math.Min(bitmap.PixelHeight, y + radius + 1);
        if (maxXExclusive <= minX || maxYExclusive <= minY)
            return false;

        var width = maxXExclusive - minX;
        var height = maxYExclusive - minY;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        bitmap.CopyPixels(new Int32Rect(minX, minY, width, height), pixels, stride, 0);

        // Header background is a light, near-uniform gray; text glyphs are dark strokes. Flag any
        // pixel that's clearly darker than the background as evidence a label was drawn.
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var blue = pixels[i];
            var green = pixels[i + 1];
            var red = pixels[i + 2];
            var alpha = pixels[i + 3];
            if (alpha > 10 && red < 150 && green < 150 && blue < 150)
                return true;
        }

        return false;
    }
}
