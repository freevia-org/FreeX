using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R69-render-active-cell-selection-6-1: the active cell's own locator box was never drawn as a
/// dedicated rectangle -- only the whole-selection perimeter edges (HasTopEdge/HasBottomEdge/
/// HasLeftEdge/HasRightEdge of the outer *range*) were drawn. Whenever the range's own perimeter
/// isn't visible on screen (e.g. after Select All and the sheet is scrolled to an interior view, or
/// the active cell sits at an interior position of the selected range after a Tab/Enter wrap),
/// Excel still always draws a crisp box tightly around the active cell -- but FreeX drew nothing at
/// all there. These tests render actual composited pixels and assert a dedicated active-cell box
/// is painted around the active cell independent of the outer range's edges.
/// </summary>
public sealed class R69ActiveCellBoxRenderTests
{
    private static Color GetPixel(RenderTargetBitmap bitmap, int x, int y)
    {
        var pixels = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixels, 4, 0);
        // Pbgra32: B, G, R, A
        return Color.FromArgb(pixels[3], pixels[2], pixels[1], pixels[0]);
    }

    private static RenderTargetBitmap RenderGrid(GridView grid)
    {
        var bitmap = new RenderTargetBitmap(
            (int)grid.Width, (int)grid.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(grid);
        return bitmap;
    }

    private static GridView CreateGrid(double width, double height, ViewportModel viewport, GridRange selectedRange, CellAddress activeCell)
    {
        var grid = new GridView
        {
            Width = width,
            Height = height,
            ShowHeaders = false,
            ShowGridLines = false,
            Viewport = viewport,
            SelectedRange = selectedRange,
            ActiveCell = activeCell
        };

        grid.Measure(new Size(width, height));
        grid.Arrange(new Rect(0, 0, width, height));
        grid.UpdateLayout();
        return grid;
    }

    [Fact]
    public void SelectAllStyleSelection_ActiveCellAwayFromRangeEdges_DrawsDedicatedActiveCellBox()
    {
        WpfTestThread.Run(() =>
        {
            var sheet = SheetId.New();

            // Simulates "Select All" (range spans the entire sheet) scrolled to an interior view:
            // none of the visible rows/columns are the range's Start (1,1) or End (Max,Max), so the
            // range's own perimeter edges (HasTopEdge/HasBottomEdge/HasLeftEdge/HasRightEdge) are
            // all false and, pre-fix, RenderSelectionRange drew no border at all anywhere on screen.
            var viewport = new ViewportModel(
                [],
                [new RowMetric(5, 20, 0), new RowMetric(6, 20, 20), new RowMetric(7, 20, 40)],
                [new ColMetric(5, 60, 0), new ColMetric(6, 60, 60), new ColMetric(7, 60, 120)]);
            var selectAll = new GridRange(
                new CellAddress(sheet, 1, 1),
                new CellAddress(sheet, CellAddress.MaxRow, CellAddress.MaxCol));
            var activeCell = new CellAddress(sheet, 6, 6); // interior cell: rect (60,20)-(120,40)

            var grid = CreateGrid(180, 60, viewport, selectAll, activeCell);
            var bitmap = RenderGrid(grid);

            // Active-cell box: the pen is centered on the rect edges, so the top edge's stroke
            // spans a couple of device pixels straddling y=20. It must be solidly (opaquely)
            // painted in the dark selection-border color -- the range itself has no edges visible
            // anywhere in this viewport, so this paint can only be coming from the dedicated
            // active-cell box.
            var topBorder = GetPixel(bitmap, 90, 19);
            topBorder.R.Should().BeLessThan(150,
                "Excel always draws a crisp, opaque box around the active cell, even when the " +
                "selection range's own perimeter edges are off-screen (e.g. after Select All)");

            // Well inside the active cell (away from its border): Excel never tints the active
            // cell itself, so this must stay plain white (unfilled).
            var activeCellInterior = GetPixel(bitmap, 90, 30);
            activeCellInterior.R.Should().BeGreaterThanOrEqualTo(250,
                "the active cell interior must stay unfilled, matching Excel's plain-border look");

            // Elsewhere in the (off-screen-perimeter) selection, away from the active cell: this
            // must show the faint selection-fill tint over white, clearly lighter than the
            // active-cell box's solid dark stroke but darker than untinted white.
            var generalFill = GetPixel(bitmap, 10, 10);
            generalFill.R.Should().BeInRange(150, 249,
                "the rest of the selected range keeps its faint fill tint, distinct from both plain white and the solid active-cell box");
        });
    }

    [Fact]
    public void SmallSelection_InteriorActiveCell_GetsOwnBox()
    {
        WpfTestThread.Run(() =>
        {
            var sheet = SheetId.New();

            // A1:C3 selected; active cell B2 is interior to the range, so only the *outer*
            // perimeter (around all of A1:C3) was drawn pre-fix -- B2 itself never got its own box.
            var viewport = new ViewportModel(
                [],
                [new RowMetric(1, 20, 0), new RowMetric(2, 20, 20), new RowMetric(3, 20, 40)],
                [new ColMetric(1, 60, 0), new ColMetric(2, 60, 60), new ColMetric(3, 60, 120)]);
            var selection = new GridRange(
                new CellAddress(sheet, 1, 1),
                new CellAddress(sheet, 3, 3));
            var activeCell = new CellAddress(sheet, 2, 2); // B2: rect (60,20)-(120,40)

            var grid = CreateGrid(180, 60, viewport, selection, activeCell);
            var bitmap = RenderGrid(grid);

            // B2's own top edge (y=20) is strictly interior to the outer A1:C3 perimeter (whose
            // edges sit at y=0 and y=60) -- any dark, opaque paint here can only be B2's dedicated
            // box.
            var activeCellTopBorder = GetPixel(bitmap, 90, 19);
            activeCellTopBorder.R.Should().BeLessThan(150,
                "the active cell (B2) must get its own locator box even when it sits at an interior " +
                "position within a larger selected range");

            var activeCellInterior = GetPixel(bitmap, 90, 30);
            activeCellInterior.R.Should().BeGreaterThanOrEqualTo(250,
                "the active cell interior must stay unfilled");
        });
    }

    [Fact]
    public void SingleCellSelection_StillShowsActiveCellBox_NoRegression()
    {
        WpfTestThread.Run(() =>
        {
            var sheet = SheetId.New();

            var viewport = new ViewportModel(
                [],
                [new RowMetric(1, 20, 0)],
                [new ColMetric(1, 60, 0)]);
            var selection = new GridRange(
                new CellAddress(sheet, 1, 1),
                new CellAddress(sheet, 1, 1));
            var activeCell = new CellAddress(sheet, 1, 1);

            var grid = CreateGrid(60, 20, viewport, selection, activeCell);
            var bitmap = RenderGrid(grid);

            var topBorder = GetPixel(bitmap, 30, 0);
            topBorder.R.Should().BeLessThan(150,
                "a plain single-cell selection must still show a border box around the active cell");
        });
    }
}
