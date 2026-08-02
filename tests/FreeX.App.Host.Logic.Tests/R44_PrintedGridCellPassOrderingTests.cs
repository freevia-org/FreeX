using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R44-meta-2: WPF print's DrawPrintedGridCells used to draw fill+border+text inline per cell in
/// a single nested loop, so the NEXT column's fill (drawn on the following loop iteration) painted
/// over text the PREVIOUS cell had already overflowed into that column, clipping it. The fix splits
/// the single loop into three passes -- all fills, then all borders, then all text (including
/// overflow) -- mirroring the interactive grid's multi-pass architecture (GridView.Rendering.cs), so
/// overflow text always layers on top of every fill regardless of column draw order.
/// </summary>
public sealed class R44_PrintedGridCellPassOrderingTests
{
    private const double ColumnWidth = 60.0;
    private const double RowHeight = 20.0;

    [Fact]
    public void DrawPrintedGridCells_OverflowTextSurvivesNeighborCellFill()
    {
        StaTestRunner.Run(() =>
        {
            var longText = new string('W', 40);

            var baseline = RenderTwoCellRow(longText, aStyle: null, bStyle: null);
            var withNeighborFill = RenderTwoCellRow(
                longText,
                aStyle: null,
                bStyle: new CellStyle { FillColor = CellColor.FromArgb(255, 255, 0) }); // opaque yellow fill on the overflow target

            var baselineBlackInB = CountNearBlackPixels(baseline, left: (int)ColumnWidth, top: 0, width: (int)ColumnWidth, height: (int)RowHeight);
            var withFillBlackInB = CountNearBlackPixels(withNeighborFill, left: (int)ColumnWidth, top: 0, width: (int)ColumnWidth, height: (int)RowHeight);

            // Sanity: the long unwrapped text in A1 actually overflows into the blank column B when
            // nothing there blocks it, so this scenario exercises the overflow path at all.
            baselineBlackInB.Should().BeGreaterThan(0);

            // Regression: column B's opaque fill must not erase the overflow text pixels that land
            // under it. Pre-fix (single per-cell pass), B's fill was drawn AFTER A's overflow text
            // and painted over it -- verified directly: reverting to the old single-pass draw order
            // makes this count exactly 0 (total wipeout) for this same scenario. Post-fix (fill
            // pass, then border pass, then text pass) the text is always drawn last, so a
            // substantial share of the overflow text pixels remain visible. (An exact pixel-count
            // match against the no-fill baseline is NOT expected: anti-aliased glyph edges blend
            // toward the destination background color, so a black-on-yellow edge pixel can shift
            // out of a strict "near black" threshold in a way a black-on-white edge pixel does not
            // -- that's a subpixel-rendering artifact, not the overflow-clipping bug this test
            // guards against.)
            withFillBlackInB.Should().BeGreaterThan(baselineBlackInB / 4);
        });
    }

    [Fact]
    public void DrawPrintedGridCells_NonOverflowingTextStillRendersOverItsOwnFill()
    {
        StaTestRunner.Run(() =>
        {
            // Sibling/no-regression case: short (non-overflowing) text drawn over its OWN cell's
            // fill, with a differently-filled neighbor, must still be visible after splitting the
            // single per-cell loop into fill/border/text passes -- confirming the ordinary
            // (non-overflow) multi-cell render is unchanged by the pass restructuring.
            var bitmap = RenderTwoCellRow(
                "Hi",
                aStyle: new CellStyle { FillColor = CellColor.FromArgb(0, 0, 255) },
                bStyle: new CellStyle { FillColor = CellColor.FromArgb(255, 0, 0) });

            var blackInA = CountNearBlackPixels(bitmap, left: 0, top: 0, width: (int)ColumnWidth, height: (int)RowHeight);

            blackInA.Should().BeGreaterThan(0);
        });
    }

    private static byte[] RenderTwoCellRow(string aText, CellStyle? aStyle, CellStyle? bStyle)
    {
        var cellA = new DisplayCell(
            Row: 1,
            Col: 1,
            RawValue: new TextValue(aText),
            DisplayText: aText,
            Formula: null,
            StyleId: default,
            Error: null,
            Style: aStyle);
        var cellB = new DisplayCell(
            Row: 1,
            Col: 2,
            RawValue: BlankValue.Instance,
            DisplayText: string.Empty,
            Formula: null,
            StyleId: default,
            Error: null,
            Style: bStyle);

        var cellLookup = new Dictionary<(uint Row, uint Col), DisplayCell>
        {
            [(1u, 1u)] = cellA,
            [(1u, 2u)] = cellB,
        };

        var workbook = new Workbook();
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var measurement = new PrintGridMeasurement(0, 0, ColumnWidth, RowHeight);
        var pageRows = new uint[] { 1u };
        var pageColumns = new uint[] { 1u, 2u };

        var textOverlays = new List<PdfTextOverlay>();
        var linkOverlays = new List<PdfLinkOverlay>();
        var cellDestinationOverlays = new List<PdfCellDestinationOverlay>();

        // PdfLinkTarget is a private nested record of PrintRenderer -- build the (empty)
        // dictionary via reflection since the type name isn't accessible from test code.
        var linkTargetType = typeof(PrintRenderer).GetNestedType("PdfLinkTarget", BindingFlags.NonPublic)!;
        var hyperlinkLookupType = typeof(Dictionary<,>).MakeGenericType(typeof(ValueTuple<uint, uint>), linkTargetType);
        var hyperlinkLookup = Activator.CreateInstance(hyperlinkLookupType)!;
        var cellDestinationLookup = new Dictionary<(uint Row, uint Col), CellAddress>();

        var method = typeof(PrintRenderer).GetMethod(
            "DrawPrintedGridCells",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var width = (int)(ColumnWidth * 2);
        var height = (int)RowHeight;
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
                workbook,
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

    private static int CountNearBlackPixels(byte[] pixels, int left, int top, int width, int height)
    {
        const int bitmapWidth = (int)(ColumnWidth * 2);
        var count = 0;
        for (var y = top; y < top + height; y++)
        {
            for (var x = left; x < left + width; x++)
            {
                var i = (y * bitmapWidth + x) * 4;
                var blue = pixels[i];
                var green = pixels[i + 1];
                var red = pixels[i + 2];
                var alpha = pixels[i + 3];
                if (alpha > 0 && red < 100 && green < 100 && blue < 100)
                    count++;
            }
        }

        return count;
    }
}
