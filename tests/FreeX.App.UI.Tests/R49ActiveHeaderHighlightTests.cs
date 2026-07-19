using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R49-render-header-frozen-corner-3-3: within a multi-cell selection, every selected row/column
/// header used to paint with the exact same flat HeaderHighlightBrush, with no reference to
/// ActiveCell at all -- so the active cell's own row/column header looked identical to every other
/// selected header. Real Excel gives the active cell's row/column header a visibly stronger tint so
/// users can locate the active cell inside a large selection at a glance. These tests render the
/// actual composited pixels for the column headers of a multi-column selection and assert the
/// active column's header pixel differs from (and is distinguishable from) the other selected
/// columns' header pixels.
/// </summary>
public sealed class R49ActiveHeaderHighlightTests
{
    private const double ColumnWidth = 80;
    private const double RowHeight = 24;

    private static GridView CreateThreeColumnGrid(SheetId sheet, GridRange selectedRange, CellAddress? activeCell)
    {
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, RowHeight, 0)],
            [
                new ColMetric(1, ColumnWidth, 0),
                new ColMetric(2, ColumnWidth, ColumnWidth),
                new ColMetric(3, ColumnWidth, ColumnWidth * 2)
            ]);

        var width = GridView.RowHeaderWidth + ColumnWidth * 3;
        var height = GridView.ColHeaderHeight + RowHeight;

        var grid = new GridView
        {
            Width = width,
            Height = height,
            ShowHeaders = true,
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

    private static Color GetColumnHeaderPixel(GridView grid, int columnIndexZeroBased)
    {
        var bitmap = new RenderTargetBitmap(
            (int)grid.Width, (int)grid.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(grid);

        // Sample near the left edge of the column's header cell (well away from the centered
        // column-letter glyph and the thin grid-line border) so we read the flat fill color.
        var x = (int)(GridView.RowHeaderWidth + columnIndexZeroBased * ColumnWidth + 10);
        var y = (int)(GridView.ColHeaderHeight / 2);

        var pixels = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixels, 4, 0);
        // Pbgra32: B, G, R, A
        return Color.FromRgb(pixels[2], pixels[1], pixels[0]);
    }

    [Fact]
    public void MultiColumnSelection_ActiveColumnHeaderIsDistinctFromOtherSelectedColumnHeaders()
    {
        WpfTestThread.Run(() =>
        {
            var sheet = SheetId.New();
            // A1:C1 selected, active cell is B1 (the middle column).
            var selectedRange = new GridRange(new CellAddress(sheet, 1, 1), new CellAddress(sheet, 1, 3));
            var grid = CreateThreeColumnGrid(sheet, selectedRange, new CellAddress(sheet, 1, 2));

            var columnAHeader = GetColumnHeaderPixel(grid, columnIndexZeroBased: 0);
            var columnBHeader = GetColumnHeaderPixel(grid, columnIndexZeroBased: 1);
            var columnCHeader = GetColumnHeaderPixel(grid, columnIndexZeroBased: 2);

            // The active column (B) must render with a visibly different (stronger) tint than the
            // other selected-but-not-active columns (A and C).
            columnBHeader.Should().NotBe(columnAHeader,
                "the active cell's column header must be visually distinct from a merely-selected column header");
            columnBHeader.Should().NotBe(columnCHeader,
                "the active cell's column header must be visually distinct from a merely-selected column header");

            // The two non-active selected columns must still share the same (regular) highlight --
            // only the active one should stand out.
            columnAHeader.Should().Be(columnCHeader,
                "non-active selected column headers must still render identically to each other");
        });
    }

    [Fact]
    public void MultiColumnSelection_WithoutExplicitActiveCell_FallsBackToSelectionStartAsActive_NoRegression()
    {
        WpfTestThread.Run(() =>
        {
            var sheet = SheetId.New();
            // A1:C1 selected, ActiveCell left unset -- must fall back to the selection's start (A1),
            // matching the same fallback GridView already uses for the fill-hole border
            // (ActiveCell ?? SelectedRange?.Start). Non-active columns (B, C) must still receive the
            // ordinary selected-header highlight, i.e. the base multi-selection highlighting behavior
            // must be unaffected by the active-cell distinction added for R49-render-header-3-3.
            var selectedRange = new GridRange(new CellAddress(sheet, 1, 1), new CellAddress(sheet, 1, 3));
            var grid = CreateThreeColumnGrid(sheet, selectedRange, activeCell: null);

            var columnAHeader = GetColumnHeaderPixel(grid, columnIndexZeroBased: 0);
            var columnBHeader = GetColumnHeaderPixel(grid, columnIndexZeroBased: 1);
            var columnCHeader = GetColumnHeaderPixel(grid, columnIndexZeroBased: 2);

            columnAHeader.Should().NotBe(columnBHeader,
                "the fallback active column (selection start, A) must still stand out from the other selected columns");
            columnBHeader.Should().Be(columnCHeader,
                "non-active selected column headers must still render identically to each other");
        });
    }
}
