using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R46-meta-1: r45's WrapText print/PDF fix (PrintRenderer.GridCells.cs) let a wrapped cell's
/// FormattedText grow to as many lines as needed (removing the old forced MaxLineCount=1 cap)
/// but never added a per-cell vertical clip -- FreeX has no automatic row-grow-on-WrapText
/// anywhere, so a WrapText cell whose row height was never manually resized to fit the wrapped
/// text now bleeds its overflow lines into the row below on print/PDF output. The interactive
/// grid (GridView.Rendering.cs) avoids this via CellTextOrientationLayoutPlanner.ShouldClip
/// (clip when wrapText && textHeight > clipRect.Height + tolerance) before drawing. The fix adds
/// the equivalent dc.PushClip(cell rect) to the print path's WrapText branch.
/// </summary>
public sealed class R46_PrintedWrapTextVerticalClipTests
{
    private const double ColumnWidth = 40.0;
    private const double RowHeight = 16.0;
    private const int RowCount = 2;

    [Fact]
    public void DrawPrintedGridCells_TallWrappedCell_ClipsToRowInsteadOfBleedingIntoRowBelow()
    {
        StaTestRunner.Run(() =>
        {
            // Many short words in a narrow column, at the default 11pt font, wrap into far more
            // lines than the tiny 16px row can hold -- exactly the scenario the finding describes
            // (WrapText=true, row height never manually grown to fit).
            var wrappedText = string.Join(' ', Enumerable.Repeat("Word", 20));

            var bitmap = RenderWrappedCellAboveBlankRow(wrappedText);

            // Row 2 (the row directly below the wrapped cell) must contain no drawn text pixels --
            // pre-fix, the unclipped multi-line FormattedText block (centered using its own much
            // taller height) painted straight through into this row.
            var blackInRowBelow = CountNearBlackPixels(
                bitmap,
                left: 0,
                top: (int)RowHeight,
                width: (int)ColumnWidth,
                height: (int)RowHeight);

            blackInRowBelow.Should().Be(0,
                "the wrapped cell's overflow text must be clipped to its own row, not bleed into the row below");
        });
    }

    [Fact]
    public void DrawPrintedGridCells_ShortWrappedTextThatFitsTheRow_StillRendersVisibly()
    {
        // Sibling/no-regression case: a WrapText cell whose (single-line) text comfortably fits
        // within the row height must still be drawn -- the new clip must not be so aggressive
        // that it hides text that never actually overflows the row.
        StaTestRunner.Run(() =>
        {
            var bitmap = RenderWrappedCellAboveBlankRow("Hi");

            var blackInRow1 = CountNearBlackPixels(
                bitmap,
                left: 0,
                top: 0,
                width: (int)ColumnWidth,
                height: (int)RowHeight);

            blackInRow1.Should().BeGreaterThan(0, "short text that fits within the row must still be visible");
        });
    }

    private static byte[] RenderWrappedCellAboveBlankRow(string text)
    {
        var wrapStyle = new CellStyle { WrapText = true };
        var cellA1 = new DisplayCell(
            Row: 1,
            Col: 1,
            RawValue: new TextValue(text),
            DisplayText: text,
            Formula: null,
            StyleId: default,
            Error: null,
            Style: wrapStyle);
        var cellA2 = new DisplayCell(
            Row: 2,
            Col: 1,
            RawValue: BlankValue.Instance,
            DisplayText: string.Empty,
            Formula: null,
            StyleId: default,
            Error: null,
            Style: null);

        var cellLookup = new Dictionary<(uint Row, uint Col), DisplayCell>
        {
            [(1u, 1u)] = cellA1,
            [(2u, 1u)] = cellA2,
        };

        var measurement = new PrintGridMeasurement(0, 0, ColumnWidth, RowHeight);
        var pageRows = new uint[] { 1u, 2u };
        var pageColumns = new uint[] { 1u };

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

        var width = (int)ColumnWidth;
        var height = (int)RowHeight * RowCount;
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
        const int bitmapWidth = (int)ColumnWidth;
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
