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
/// rtl-localization-F1: DrawPrintedCellText (the WPF Print/Print Preview/PDF text-layout path)
/// hardcoded FlowDirection.LeftToRight and omitted isEffectivelyRightToLeft on both
/// CellTextOrientationLayoutPlanner.CalculateLayout calls, so it always took the method's
/// isEffectivelyRightToLeft:false default regardless of the sheet's own
/// <see cref="Sheet.IsRightToLeft"/> flag. The interactive grid (GridView.Rendering.cs) resolves
/// this correctly via CellTextOrientationLayoutPlanner.ResolveIsEffectivelyRightToLeft(style.ReadingOrder,
/// sheet.IsRightToLeft) before calling the same CalculateLayout -- so a General-aligned numeric cell
/// that renders LEFT-anchored on an RTL sheet on screen printed/exported RIGHT-anchored instead. The
/// fix threads the same resolution through DrawPrintedGridCells/DrawPrintedCellText (and the
/// conditional-icon-set layout call in the same file), using the `sheet` parameter that was already
/// available on both methods.
/// </summary>
public sealed class R146_PrintedRightToLeftGeneralAlignmentTests
{
    private const double ColumnWidth = 200.0;
    private const double RowHeight = 40.0;

    [Fact]
    public void DrawPrintedGridCells_RightToLeftSheet_GeneralAlignedNumberRendersNearLeftEdge()
    {
        StaTestRunner.Run(() =>
        {
            var sheet = new Sheet(SheetId.New(), "Sheet1") { IsRightToLeft = true };

            // General alignment (no explicit HorizontalAlignment override) on a numeric value: Excel
            // anchors this to the RIGHT on an LTR sheet but to the LEFT on an RTL sheet. Pre-fix, the
            // print path always resolved as if the sheet were LTR, so this rendered flush-right even
            // on an RTL sheet.
            var bitmap = RenderSingleCell("100", style: null, sheet);

            var (minX, maxX) = FindInkHorizontalExtent(bitmap);

            minX.Should().BeLessThan((int)(ColumnWidth / 2),
                "a General-aligned numeric cell on a right-to-left sheet must print anchored to the " +
                "LEFT edge, matching what the interactive grid renders on screen");
            maxX.Should().BeLessThan((int)ColumnWidth,
                "the text must still be fully inside the cell");
        });
    }

    [Fact]
    public void DrawPrintedGridCells_LeftToRightSheet_GeneralAlignedNumberStillRendersNearRightEdge()
    {
        // Sibling/no-regression case: the overwhelmingly common LTR sheet (sheet: null, matching
        // every pre-existing print/PDF caller) must keep resolving General-aligned numeric content to
        // the RIGHT edge exactly as before -- the RTL fix must not touch the default LTR path.
        StaTestRunner.Run(() =>
        {
            var bitmap = RenderSingleCell("100", style: null, sheet: null);

            var (minX, maxX) = FindInkHorizontalExtent(bitmap);

            minX.Should().BeGreaterThan((int)(ColumnWidth / 2),
                "a General-aligned numeric cell on an ordinary left-to-right sheet must still print " +
                "anchored to the right edge, unchanged by the RTL fix");
            maxX.Should().BeLessThan((int)ColumnWidth,
                "the text must still be fully inside the cell");
        });
    }

    [Fact]
    public void DrawPrintedGridCells_ExplicitLeftToRightReadingOrder_IgnoresRightToLeftSheet()
    {
        // Sibling/no-regression: an explicit per-cell Format Cells > Alignment > Text direction
        // override (CellReadingOrder.LeftToRight) must keep forcing LTR even when the sheet itself is
        // RTL -- ResolveIsEffectivelyRightToLeft only falls back to the sheet's flag for the default
        // Context reading order.
        StaTestRunner.Run(() =>
        {
            var sheet = new Sheet(SheetId.New(), "Sheet1") { IsRightToLeft = true };
            var style = new CellStyle { ReadingOrder = CellReadingOrder.LeftToRight };

            var bitmap = RenderSingleCell("100", style, sheet);

            var (minX, _) = FindInkHorizontalExtent(bitmap);

            minX.Should().BeGreaterThan((int)(ColumnWidth / 2),
                "an explicit LeftToRight reading-order override must anchor General-aligned numeric " +
                "content to the right edge even on a right-to-left sheet");
        });
    }

    private static byte[] RenderSingleCell(string text, CellStyle? style, Sheet? sheet)
    {
        var cell = new DisplayCell(
            Row: 1,
            Col: 1,
            RawValue: new NumberValue(double.Parse(text)),
            DisplayText: text,
            Formula: null,
            StyleId: default,
            Error: null,
            Style: style);

        var cellLookup = new Dictionary<(uint Row, uint Col), DisplayCell>
        {
            [(1u, 1u)] = cell,
        };

        var measurement = new PrintGridMeasurement(0, 0, ColumnWidth, RowHeight);
        var pageRows = new uint[] { 1u };
        var pageColumns = new uint[] { 1u };

        var textOverlays = new List<PdfTextOverlay>();
        var linkOverlays = new List<PdfLinkOverlay>();
        var cellDestinationOverlays = new List<PdfCellDestinationOverlay>();

        var hyperlinkLookup = new Dictionary<(uint Row, uint Col), WorksheetPrintHyperlinkPlan>();
        var cellDestinationLookup = new Dictionary<(uint Row, uint Col), CellAddress>();

        var method = typeof(PrintRenderer).GetMethod(
            "DrawPrintedGridCells",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var width = (int)ColumnWidth;
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
                sheet,
            ]);
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    private static (int MinX, int MaxX) FindInkHorizontalExtent(byte[] pixels)
    {
        const int bitmapWidth = (int)ColumnWidth;
        const int bitmapHeight = (int)RowHeight;
        var minX = int.MaxValue;
        var maxX = int.MinValue;
        for (var y = 0; y < bitmapHeight; y++)
        {
            for (var x = 0; x < bitmapWidth; x++)
            {
                var i = (y * bitmapWidth + x) * 4;
                var blue = pixels[i];
                var green = pixels[i + 1];
                var red = pixels[i + 2];
                var alpha = pixels[i + 3];
                if (alpha > 0 && red < 100 && green < 100 && blue < 100)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                }
            }
        }

        minX.Should().NotBe(int.MaxValue, "the rendered cell must contain some visible text ink to measure");
        return (minX, maxX);
    }
}
