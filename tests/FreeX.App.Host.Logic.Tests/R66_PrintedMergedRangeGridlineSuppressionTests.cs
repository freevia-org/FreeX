using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R66-render-gridlines-borders-6-2: PrintRenderer.GridCells.cs's DrawPrintedGridCells drew the
/// default gridline rectangle and every explicit border edge per physical cell with no awareness
/// of merge membership, so a bordered/gridlined merged range printed with interior gridlines (and
/// duplicated interior border edges) cutting straight through it -- unlike the on-screen grid
/// (GridView.Rendering.cs Pass 2), which suppresses edges strictly inside a merged region and only
/// ever draws the merge's outer perimeter. The fix looks up each cell's merge region (via
/// Sheet.GetMergeRegion) and skips the gridline/border edges on sides that fall inside the merge.
/// </summary>
public sealed class R66_PrintedMergedRangeGridlineSuppressionTests
{
    private const double ColumnWidth = 60.0;
    private const double RowHeight = 30.0;
    private const int ColumnCount = 3;

    [Fact]
    public void MergedRangeWithOutlineBorderAndGridlines_SuppressesInteriorVerticalEdges()
    {
        StaTestRunner.Run(() =>
        {
            // A1:C1 merged with a border style directly matching an "Outline" authoring that
            // (worst case) stamped a full 4-side border on every physical member cell -- exactly
            // the scenario the fix must defend against, since merge suppression has to hold
            // regardless of exactly which raw per-cell border values are present.
            var borderedStyle = new CellStyle
            {
                BorderTop = new CellBorder(BorderStyle.Thin, CellColor.Black),
                BorderBottom = new CellBorder(BorderStyle.Thin, CellColor.Black),
                BorderLeft = new CellBorder(BorderStyle.Thin, CellColor.Black),
                BorderRight = new CellBorder(BorderStyle.Thin, CellColor.Black),
            };

            var pixels = RenderMergedRow(borderedStyle, merge: true, printGridlines: true);

            // Interior column boundaries (between A1/B1 at x=60, and B1/C1 at x=120) must show no
            // vertical line at all -- neither from the default gridline pass nor from the explicit
            // border pass -- because both physical cells on either side of that boundary are
            // interior to the same merged region. The check is restricted to a middle band of rows,
            // away from the merge's own (still-drawn, non-suppressed) top/bottom outer edges, whose
            // horizontal lines legitimately touch every x-coordinate including x=60/120.
            CountNonWhitePixelsInColumn(pixels, x: 60, topMargin: 5, bottomMargin: 5).Should().Be(0,
                "the A1/B1 boundary is strictly inside the merge and must show no interior gridline/border");
            CountNonWhitePixelsInColumn(pixels, x: 120, topMargin: 5, bottomMargin: 5).Should().Be(0,
                "the B1/C1 boundary is strictly inside the merge and must show no interior gridline/border");

            // The merge's true outer perimeter (left edge of A1, right edge of C1, and the full-width
            // top/bottom) must still print -- suppression must not swallow the whole border.
            CountNonWhitePixelsInColumn(pixels, x: 0, topMargin: 5, bottomMargin: 5).Should().BeGreaterThan(0,
                "the merge's own left outer edge must still print");
            CountNonWhitePixelsInColumn(pixels, x: (int)(ColumnWidth * ColumnCount) - 1, topMargin: 5, bottomMargin: 5).Should().BeGreaterThan(0,
                "the merge's own right outer edge must still print");
            CountNonWhitePixelsInRow(pixels, y: 1).Should().BeGreaterThan(0,
                "the merge's own top outer edge must still print across the full merged width");
        });
    }

    [Fact]
    public void UnmergedBorderedCell_StillPrintsNormally()
    {
        // Sibling no-regression case: an ordinary unmerged bordered cell (no merge on the sheet
        // at all) must keep printing every one of its own four edges exactly as before this fix.
        StaTestRunner.Run(() =>
        {
            var borderedStyle = new CellStyle
            {
                BorderTop = new CellBorder(BorderStyle.Thin, CellColor.Black),
                BorderBottom = new CellBorder(BorderStyle.Thin, CellColor.Black),
                BorderLeft = new CellBorder(BorderStyle.Thin, CellColor.Black),
                BorderRight = new CellBorder(BorderStyle.Thin, CellColor.Black),
            };

            var pixels = RenderMergedRow(borderedStyle, merge: false, printGridlines: true);

            // Every column boundary (60 and 120) is a real, un-merged cell-to-cell edge here, so
            // both must still show their (now-doubled-up, since neither side is suppressed) border
            // lines -- confirming the merge-suppression change left ordinary unmerged cells alone.
            CountNonWhitePixelsInColumn(pixels, x: 60, topMargin: 5, bottomMargin: 5).Should().BeGreaterThan(0,
                "an unmerged cell boundary must still print its own border/gridline edges");
            CountNonWhitePixelsInColumn(pixels, x: 120, topMargin: 5, bottomMargin: 5).Should().BeGreaterThan(0,
                "an unmerged cell boundary must still print its own border/gridline edges");
        });
    }

    private static byte[] RenderMergedRow(CellStyle style, bool merge, bool printGridlines)
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        if (merge)
        {
            sheet.AddMergedRegion(new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, (uint)ColumnCount)));
        }

        var cellLookup = new Dictionary<(uint Row, uint Col), DisplayCell>();
        for (var col = 1u; col <= ColumnCount; col++)
        {
            cellLookup[(1u, col)] = new DisplayCell(
                Row: 1,
                Col: col,
                RawValue: BlankValue.Instance,
                DisplayText: string.Empty,
                Formula: null,
                StyleId: default,
                Error: null,
                Style: style);
        }

        var measurement = new PrintGridMeasurement(0, 0, ColumnWidth, RowHeight);
        var pageRows = new uint[] { 1u };
        var pageColumns = Enumerable.Range(1, ColumnCount).Select(c => (uint)c).ToArray();

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
                printGridlines,
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

    private static int CountNonWhitePixelsInColumn(byte[] pixels, int x, int topMargin = 0, int bottomMargin = 0)
    {
        const int bitmapWidth = (int)(ColumnWidth * ColumnCount);
        const int bitmapHeight = (int)RowHeight;
        var count = 0;
        for (var y = topMargin; y < bitmapHeight - bottomMargin; y++)
        {
            var i = (y * bitmapWidth + x) * 4;
            if (IsLinePixel(pixels, i))
                count++;
        }

        return count;
    }

    private static int CountNonWhitePixelsInRow(byte[] pixels, int y)
    {
        const int bitmapWidth = (int)(ColumnWidth * ColumnCount);
        var count = 0;
        for (var x = 0; x < bitmapWidth; x++)
        {
            var i = (y * bitmapWidth + x) * 4;
            if (IsLinePixel(pixels, i))
                count++;
        }

        return count;
    }

    private static bool IsLinePixel(byte[] pixels, int i)
    {
        var blue = pixels[i];
        var green = pixels[i + 1];
        var red = pixels[i + 2];
        var alpha = pixels[i + 3];
        // Anything visibly darker than white counts as a drawn gridline/border pixel; a generous
        // threshold tolerates anti-aliased edge blending on the thin (0.5 DIP) pen strokes used here.
        return alpha > 0 && (red < 250 || green < 250 || blue < 250);
    }
}
