using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R74-render-gridlines-borders-4-3: PrintRenderer.GridCells.cs's DrawPrintedCellBorders drew a
/// diagonal border on a merged cell spanning only the merge ANCHOR's own single-cell footprint,
/// not the full merged rectangle -- unlike the on-screen grid (GridView.Rendering.cs), which widens
/// diagonalW/diagonalH by summing the merged columns' widths/rows' heights before drawing. The fix
/// mirrors that: when the printed cell is a merge anchor with a diagonal border, the diagonal
/// endpoint is expanded to the merge's true extent before drawing corner-to-corner.
/// </summary>
public sealed class R74_PrintedDiagonalMergeExtentTests
{
    private const double ColumnWidth = 40.0;
    private const double RowHeight = 20.0;

    [Fact]
    public void DiagonalOnMergedAnchor_SpansTheFullMergedRectangle()
    {
        StaTestRunner.Run(() =>
        {
            // B2:D5 merged (3 columns wide, 4 rows tall); the anchor (B2) carries a
            // BorderDiagonalDown. Rendered with pageRows/pageColumns restricted to exactly the
            // merge's own extent, the anchor's un-merged single-cell box is (0,0)-(40,20), while
            // the true merged rectangle is (0,0)-(120,80).
            var pixels = RenderMergedDiagonal(merge: true);

            // Deep inside the full merge's own diagonal line (near its true bottom-right corner)
            // but far outside the anchor's own un-merged single-cell footprint -- this column of
            // pixels is entirely blank pre-fix (nothing is ever drawn past x=40,y=20) and must show
            // paint post-fix, since the diagonal now reaches all the way to the merge's true extent.
            const int x = 110;
            CountPaintedPixelsInColumnBand(pixels, x, yFrom: 65, yTo: 80).Should().BeGreaterThan(0,
                "the diagonal border on a merged anchor must span the full merged rectangle, not just its own single-cell box");
        });
    }

    [Fact]
    public void DiagonalOnNonMergedCell_StillSpansOnlyItsOwnBox_NoRegression()
    {
        StaTestRunner.Run(() =>
        {
            var pixels = RenderMergedDiagonal(merge: false);

            // Sibling/no-regression case: with no merge at all, the diagonal must still be confined
            // to the cell's own single-cell box, exactly as before this fix -- nothing should appear
            // way out past that box.
            const int x = 110;
            CountPaintedPixelsInColumnBand(pixels, x, yFrom: 0, yTo: 80).Should().Be(0,
                "an un-merged diagonal border must remain confined to its own single-cell footprint");

            // ...but the diagonal still paints within its own (unmerged) single-cell box.
            CountPaintedPixelsInColumnBand(pixels, x: 20, yFrom: 0, yTo: 20).Should().BeGreaterThan(0,
                "the un-merged diagonal border must still print within its own single-cell box");
        });
    }

    private static byte[] RenderMergedDiagonal(bool merge)
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var anchorStyle = new CellStyle
        {
            BorderDiagonalDown = new CellBorder(BorderStyle.Thin, CellColor.Black),
        };

        IReadOnlyList<uint> pageRows;
        IReadOnlyList<uint> pageColumns;
        var cellLookup = new Dictionary<(uint Row, uint Col), DisplayCell>();

        if (merge)
        {
            sheet.AddMergedRegion(new GridRange(
                new CellAddress(sheet.Id, 2, 2),
                new CellAddress(sheet.Id, 5, 4)));

            pageRows = new uint[] { 2u, 3u, 4u, 5u };
            pageColumns = new uint[] { 2u, 3u, 4u };

            cellLookup[(2u, 2u)] = new DisplayCell(
                Row: 2, Col: 2, RawValue: BlankValue.Instance, DisplayText: string.Empty,
                Formula: null, StyleId: default, Error: null, Style: anchorStyle);
            // Non-anchor merge members carry no style of their own (the anchor alone carries the
            // merged range's formatting), matching how the engine populates DisplayCell.Style.
            for (var r = 2u; r <= 5u; r++)
            for (var c = 2u; c <= 4u; c++)
            {
                if (r == 2u && c == 2u) continue;
                cellLookup[(r, c)] = new DisplayCell(
                    Row: r, Col: c, RawValue: BlankValue.Instance, DisplayText: string.Empty,
                    Formula: null, StyleId: default, Error: null, Style: null);
            }
        }
        else
        {
            pageRows = new uint[] { 2u };
            pageColumns = new uint[] { 2u };
            cellLookup[(2u, 2u)] = new DisplayCell(
                Row: 2, Col: 2, RawValue: BlankValue.Instance, DisplayText: string.Empty,
                Formula: null, StyleId: default, Error: null, Style: anchorStyle);
        }

        var measurement = new PrintGridMeasurement(0, 0, ColumnWidth, RowHeight);

        var textOverlays = new List<PdfTextOverlay>();
        var linkOverlays = new List<PdfLinkOverlay>();
        var cellDestinationOverlays = new List<PdfCellDestinationOverlay>();

        var hyperlinkLookup = new Dictionary<(uint Row, uint Col), WorksheetPrintHyperlinkPlan>();
        var cellDestinationLookup = new Dictionary<(uint Row, uint Col), CellAddress>();

        var method = typeof(PrintRenderer).GetMethod(
            "DrawPrintedGridCells",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        const int width = 160;
        const int height = 100;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            method!.Invoke(null,
            [
                dc,
                textOverlays,
                linkOverlays,
                cellDestinationOverlays,
                measurement,
                pageRows,
                pageColumns,
                cellLookup,
                hyperlinkLookup,
                cellDestinationLookup,
                false,
                WorksheetPrintErrorValue.Displayed,
                0.0,
                0.0,
                new Workbook(),
                false,
                sheet,
            ]);
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    private static int CountPaintedPixelsInColumnBand(byte[] pixels, int x, int yFrom, int yTo)
    {
        const int width = 160;
        var count = 0;
        for (var y = yFrom; y < yTo; y++)
        {
            var i = (y * width + x) * 4;
            var blue = pixels[i];
            var green = pixels[i + 1];
            var red = pixels[i + 2];
            var alpha = pixels[i + 3];
            if (alpha > 10 && (red < 245 || green < 245 || blue < 245))
                count++;
        }
        return count;
    }
}
