using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R74-render-gridlines-borders-4-1: PrintRenderer.GridCells.cs's DrawPrintedGridCells drew each
/// cell's own border edges independently, left-to-right/top-to-bottom, so a shared physical edge
/// described differently by its two neighboring cells (e.g. A1.BorderRight=Double and
/// B1.BorderLeft=Thin) simply had the later-drawn cell's line painted on top of the earlier one --
/// unlike the on-screen grid (GridView.Rendering.cs's borderStyleLookup + ResolveBorderEdgeWinner),
/// which resolves shared edges deterministically to the heavier style regardless of draw order.
/// The fix looks up the actual neighboring cell's opposing border and resolves the winner via the
/// SAME <see cref="GridView.ResolveBorderEdgeWinner"/> the interactive grid uses, before drawing.
/// </summary>
public sealed class R74_PrintedBorderEdgePrecedenceTests
{
    private const double ColumnWidth = 60.0;
    private const double RowHeight = 40.0;
    private const int Dpi = 768; // 8x scale over 96 DPI so the 1.0-DIP Double-border gap resolves cleanly.
    private const double Scale = Dpi / 96.0;

    [Fact]
    public void DoubleOnLeftCell_ThinOnRightCell_SharedEdgeResolvesToDouble()
    {
        StaTestRunner.Run(() =>
        {
            var aStyle = new CellStyle { BorderRight = new CellBorder(BorderStyle.Double, CellColor.Black) };
            var bStyle = new CellStyle { BorderLeft = new CellBorder(BorderStyle.Thin, CellColor.Black) };

            var pixels = RenderTwoCellRow(aStyle, bStyle);

            AssertSharedEdgeIsDouble(pixels);
        });
    }

    [Fact]
    public void ThinOnLeftCell_DoubleOnRightCell_SharedEdgeStillResolvesToDouble_OrderIndependent()
    {
        StaTestRunner.Run(() =>
        {
            // Same conflict, but the heavier (Double) style now lives on the SECOND (right) cell --
            // the winner must still be Double, proving resolution is based on style weight, not on
            // which cell happens to be enumerated first during the border pass.
            var aStyle = new CellStyle { BorderRight = new CellBorder(BorderStyle.Thin, CellColor.Black) };
            var bStyle = new CellStyle { BorderLeft = new CellBorder(BorderStyle.Double, CellColor.Black) };

            var pixels = RenderTwoCellRow(aStyle, bStyle);

            AssertSharedEdgeIsDouble(pixels);
        });
    }

    [Fact]
    public void LoneBorder_NoNeighbor_StillRendersAsASingleUnbrokenLine_NoRegression()
    {
        StaTestRunner.Run(() =>
        {
            // Sibling/no-regression case: A1 has a Thin BorderRight and B1 carries no border at
            // all, so there is no neighboring edge to resolve against -- the nominal boundary must
            // still print as a single, unbroken Thin line (no Double-style gap), exactly as before
            // this fix.
            var aStyle = new CellStyle { BorderRight = new CellBorder(BorderStyle.Thin, CellColor.Black) };
            var bStyle = new CellStyle();

            var pixels = RenderTwoCellRow(aStyle, bStyle);

            var x = (int)(ColumnWidth * Scale);
            var centerY = (int)(RowHeight / 2 * Scale);

            IsPaintedPixel(pixels, x, centerY).Should().BeTrue(
                "a lone Thin border with no conflicting neighbor must still paint a single unbroken line at the nominal edge");
        });
    }

    private static void AssertSharedEdgeIsDouble(byte[] pixels)
    {
        var x = (int)(ColumnWidth * Scale);
        var centerY = (int)(RowHeight / 2 * Scale);

        // Excel's Double border straddles the nominal edge with a clear gap between two thin
        // strands; the exact nominal-edge pixel column must therefore be unpainted, while columns
        // a few device pixels to either side (each strand of the double line) must be painted.
        // Pre-fix, the "losing" cell's Thin line was drawn directly over the nominal-edge gap
        // (whichever cell happened to be enumerated last), so this exact assertion fails before
        // the change and passes after it.
        IsPaintedPixel(pixels, x, centerY).Should().BeFalse(
            "the resolved Double border must leave a gap exactly at the shared nominal edge, not a Thin line drawn over it");

        var leftHasPaint = Enumerable.Range(1, 6).Any(d => IsPaintedPixel(pixels, x - d, centerY));
        var rightHasPaint = Enumerable.Range(1, 6).Any(d => IsPaintedPixel(pixels, x + d, centerY));
        leftHasPaint.Should().BeTrue("the Double border's first strand should be painted just left of the shared edge");
        rightHasPaint.Should().BeTrue("the Double border's second strand should be painted just right of the shared edge");
    }

    private static byte[] RenderTwoCellRow(CellStyle aStyle, CellStyle bStyle)
    {
        var cellA = new DisplayCell(
            Row: 1, Col: 1, RawValue: BlankValue.Instance, DisplayText: string.Empty,
            Formula: null, StyleId: default, Error: null, Style: aStyle);
        var cellB = new DisplayCell(
            Row: 1, Col: 2, RawValue: BlankValue.Instance, DisplayText: string.Empty,
            Formula: null, StyleId: default, Error: null, Style: bStyle);

        var cellLookup = new Dictionary<(uint Row, uint Col), DisplayCell>
        {
            [(1u, 1u)] = cellA,
            [(1u, 2u)] = cellB,
        };

        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var measurement = new PrintGridMeasurement(0, 0, ColumnWidth, RowHeight);
        var pageRows = new uint[] { 1u };
        var pageColumns = new uint[] { 1u, 2u };

        var textOverlays = new List<PdfTextOverlay>();
        var linkOverlays = new List<PdfLinkOverlay>();
        var cellDestinationOverlays = new List<PdfCellDestinationOverlay>();

        var linkTargetType = typeof(PrintRenderer).GetNestedType("PdfLinkTarget", BindingFlags.NonPublic)!;
        var hyperlinkLookupType = typeof(Dictionary<,>).MakeGenericType(typeof(ValueTuple<uint, uint>), linkTargetType);
        var hyperlinkLookup = Activator.CreateInstance(hyperlinkLookupType)!;
        var cellDestinationLookup = new Dictionary<(uint Row, uint Col), CellAddress>();

        var method = typeof(PrintRenderer).GetMethod(
            "DrawPrintedGridCells",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var width = (int)(ColumnWidth * 2 * Scale);
        var height = (int)(RowHeight * Scale);
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

        var bitmap = new RenderTargetBitmap(width, height, Dpi, Dpi, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    private static bool IsPaintedPixel(byte[] pixels, int x, int y)
    {
        var width = (int)(ColumnWidth * 2 * Scale);
        var height = (int)(RowHeight * Scale);
        if (x < 0 || x >= width || y < 0 || y >= height) return false;
        var i = (y * width + x) * 4;
        var blue = pixels[i];
        var green = pixels[i + 1];
        var red = pixels[i + 2];
        var alpha = pixels[i + 3];
        return alpha > 10 && (red < 245 || green < 245 || blue < 245);
    }
}
