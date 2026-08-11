using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;
using CellHAlign = FreeX.Core.Model.HorizontalAlignment;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R71-render-text-overflow-4-1: DrawPrintedCellText computed <c>canOverflow</c> via
/// GridView.CanOverflowCellText (which allows Right/Center alignment, not just Left/General) but
/// then unconditionally extended the available draw width with ComputePrintedOverflowWidth, which
/// only ever walks RIGHTWARD (colIndex + 1, colIndex + 2, ...). For a Right-aligned long label
/// whose right neighbor is occupied, the rightward walk breaks immediately (zero extension), so
/// the FormattedText stayed clamped to the cell's own narrow width and got ellipsis-trimmed at its
/// own column -- even though the correct Excel/GridView.Rendering.cs behavior is to spill LEFTWARD
/// into the empty cell(s) on that side. The fix adds a leftward counterpart
/// (ComputePrintedOverflowWidthLeft) and picks direction(s) by the resolved horizontal alignment:
/// Right -> extend left only, Center -> extend both sides, Left/General -> extend right only
/// (unchanged). These tests render a 3-column row (100px each -- wide enough that WPF's
/// CharacterEllipsis trimming behaves in its ordinary (non-degenerate near-zero) range, so the
/// pre-fix/post-fix draw widths differ by a large, unambiguous margin) via the private
/// DrawPrintedGridCells entry point and read back the rasterized pixels to confirm ink actually
/// lands in the correct neighboring column.
/// </summary>
public sealed class R71_PrintedCellLeftwardOverflowTests
{
    private const double ColumnWidth = 100.0;
    private const double RowHeight = 30.0;
    private const int ColumnCount = 3;

    [Fact]
    public void DrawPrintedGridCells_RightAlignedLongLabel_SpillsLeftIntoEmptyNeighbor()
    {
        StaTestRunner.Run(() =>
        {
            var longText = new string('W', 40);

            // Col A (left neighbor of B) is blank; col B holds the long Right-aligned label; col C
            // (B's right neighbor) is occupied, so the OLD rightward-only scan finds zero room and
            // would ellipsis-trim the label inside column B alone.
            var bitmap = RenderThreeCellRow(
                aText: string.Empty, aStyle: null,
                bText: longText, bStyle: new CellStyle { HorizontalAlignment = CellHAlign.Right },
                cText: "Z", cStyle: null);

            var inkInColumnA = CountNearBlackPixels(bitmap, left: 0, top: 0, width: (int)ColumnWidth, height: (int)RowHeight);

            // Pre-fix, the rightward-only ComputePrintedOverflowWidth call breaks on the occupied
            // column C immediately, leaving maxTextWidth at the cell's own narrow (96px) width -- the
            // Right-aligned FormattedText then ellipsis-trims to fit almost entirely inside column B
            // (at most a couple of stray anti-aliased edge pixels can bleed past the boundary), so
            // column A (index 0) never receives any SUBSTANTIAL ink. Post-fix, the new leftward scan
            // sees column A is blank and extends the draw width to ~196px, so the label's ink spills
            // deep into column A -- a large, unambiguous pixel count, not just edge noise.
            inkInColumnA.Should().BeGreaterThan(30,
                "a Right-aligned long label with a blocked right neighbor must spill LEFTWARD into the empty left neighbor instead of ellipsis-trimming at its own column");
        });
    }

    [Fact]
    public void DrawPrintedGridCells_CenterAlignedLongLabel_SpillsLeftWhenRightNeighborBlocked()
    {
        StaTestRunner.Run(() =>
        {
            var longText = new string('W', 40);

            // Same blocked-right-neighbor setup as above, but Center-aligned this time: Center must
            // also be able to draw on the leftward extension (not just Right), so this exercises
            // the "Center -> extend both" branch specifically via its leftward half.
            var bitmap = RenderThreeCellRow(
                aText: string.Empty, aStyle: null,
                bText: longText, bStyle: new CellStyle { HorizontalAlignment = CellHAlign.Center },
                cText: "Z", cStyle: null);

            var inkInColumnA = CountNearBlackPixels(bitmap, left: 0, top: 0, width: (int)ColumnWidth, height: (int)RowHeight);

            inkInColumnA.Should().BeGreaterThan(30,
                "a Center-aligned long label with a blocked right neighbor must still spill into the empty left neighbor");
        });
    }

    [Fact]
    public void DrawPrintedGridCells_LeftAlignedLongLabel_StillSpillsRight_NoRegression()
    {
        StaTestRunner.Run(() =>
        {
            var longText = new string('W', 40);

            // Sibling/no-regression case: General/Left-aligned overflow (the pre-existing,
            // already-working direction) must be unaffected by adding the leftward scan. Column A
            // (B's LEFT neighbor) is occupied so any accidental leftward extension would be a bug;
            // column C (B's right neighbor) is blank so the rightward spill must still occur.
            var bitmap = RenderThreeCellRow(
                aText: "Z", aStyle: null,
                bText: longText, bStyle: null,
                cText: string.Empty, cStyle: null);

            var inkInColumnC = CountNearBlackPixels(bitmap, left: (int)ColumnWidth * 2, top: 0, width: (int)ColumnWidth, height: (int)RowHeight);

            inkInColumnC.Should().BeGreaterThan(0,
                "a default (General/Left-aligned) long label must still spill RIGHTWARD into its empty right neighbor, unchanged by the new leftward-overflow logic");
        });
    }

    [Fact]
    public void DrawPrintedGridCells_TooWideNumber_NeverSpillsEitherDirection()
    {
        StaTestRunner.Run(() =>
        {
            // A too-narrow numeric column shows Excel's "######" indicator instead of overflowing --
            // GridView.CanOverflowCellText excludes NumberValue/DateTimeValue from overflow
            // entirely, so this must stay true after adding the leftward/both-ways branches: numbers
            // must never spill into either blank neighbor.
            var bitmap = RenderThreeCellRow(
                aText: string.Empty, aStyle: null,
                bText: "######", bStyle: null, bRawValue: new NumberValue(123456789),
                cText: string.Empty, cStyle: null);

            var inkInColumnA = CountNearBlackPixels(bitmap, left: 0, top: 0, width: (int)ColumnWidth, height: (int)RowHeight);
            var inkInColumnC = CountNearBlackPixels(bitmap, left: (int)ColumnWidth * 2, top: 0, width: (int)ColumnWidth, height: (int)RowHeight);

            inkInColumnA.Should().Be(0, "a too-wide number must never spill leftward -- it shows Excel's ###### indicator instead");
            inkInColumnC.Should().Be(0, "a too-wide number must never spill rightward -- it shows Excel's ###### indicator instead");
        });
    }

    private static byte[] RenderThreeCellRow(
        string aText, CellStyle? aStyle,
        string bText, CellStyle? bStyle,
        string cText, CellStyle? cStyle,
        ScalarValue? bRawValue = null)
    {
        var cellA = new DisplayCell(
            Row: 1, Col: 1,
            RawValue: string.IsNullOrEmpty(aText) ? BlankValue.Instance : new TextValue(aText),
            DisplayText: aText,
            Formula: null, StyleId: default, Error: null, Style: aStyle);
        var cellB = new DisplayCell(
            Row: 1, Col: 2,
            RawValue: bRawValue ?? (string.IsNullOrEmpty(bText) ? BlankValue.Instance : new TextValue(bText)),
            DisplayText: bText,
            Formula: null, StyleId: default, Error: null, Style: bStyle);
        var cellC = new DisplayCell(
            Row: 1, Col: 3,
            RawValue: string.IsNullOrEmpty(cText) ? BlankValue.Instance : new TextValue(cText),
            DisplayText: cText,
            Formula: null, StyleId: default, Error: null, Style: cStyle);

        var cellLookup = new Dictionary<(uint Row, uint Col), DisplayCell>
        {
            [(1u, 1u)] = cellA,
            [(1u, 2u)] = cellB,
            [(1u, 3u)] = cellC,
        };

        var measurement = new PrintGridMeasurement(0, 0, ColumnWidth, RowHeight);
        var pageRows = new uint[] { 1u };
        var pageColumns = new uint[] { 1u, 2u, 3u };

        var textOverlays = new List<PdfTextOverlay>();
        var linkOverlays = new List<PdfLinkOverlay>();
        var cellDestinationOverlays = new List<PdfCellDestinationOverlay>();

        var hyperlinkLookup = new Dictionary<(uint Row, uint Col), WorksheetPrintHyperlinkPlan>();
        var cellDestinationLookup = new Dictionary<(uint Row, uint Col), CellAddress>();

        var method = typeof(PrintRenderer).GetMethod(
            "DrawPrintedGridCells",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var width = (int)(ColumnWidth * ColumnCount);
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
                new Workbook(),
                false,
                null,
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
        const int bitmapWidth = (int)(ColumnWidth * ColumnCount);
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
