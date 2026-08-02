using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R53-fix-one-path-miss-twin-sweep-2: DrawPrintedCellText built its FormattedText and drew it
/// straight to the DrawingContext with zero references to TextDecoration/Underline/Strikethrough
/// anywhere in the method, so Format Cells &gt; Font &gt; Underline/Strikethrough/Double-underline
/// were silently dropped on print/PDF output even though the interactive grid
/// (GridView.Rendering.cs's DrawCellText) applies them via CellTextDecorationPlanner.Build (single
/// underline/strikethrough) plus two manual stroke lines for double-underline. The fix reuses the
/// same CellTextDecorationPlanner.Build helper and mirrors the manual double-underline strokes.
/// </summary>
public sealed class R53_PrintedCellDecorationTests
{
    private const double ColumnWidth = 120.0;
    private const double RowHeight = 40.0;

    [Fact]
    public void DrawPrintedGridCells_UnderlineStyle_AddsVisibleInkComparedToPlainText()
    {
        StaTestRunner.Run(() =>
        {
            // "AAA" has no descenders, so any extra ink below the glyph baseline can only be the
            // underline stroke itself, not incidental glyph pixels.
            var plainBitmap = RenderSingleCell("AAA", style: null);
            var underlinedBitmap = RenderSingleCell("AAA", new CellStyle { Underline = true });

            var plainInk = CountNearBlackPixels(plainBitmap);
            var underlinedInk = CountNearBlackPixels(underlinedBitmap);

            // Pre-fix, DrawPrintedCellText never applied any decoration, so an Underline=true cell
            // rendered pixel-for-pixel identical ink to a plain cell (the test would fail: not
            // strictly greater). Post-fix, CellTextDecorationPlanner.Build adds the underline
            // TextDecoration, so the underlined render must contain strictly more ink.
            underlinedInk.Should().BeGreaterThan(plainInk,
                "Format Cells > Font > Underline must add a visible underline stroke on print/PDF output");
        });
    }

    [Fact]
    public void DrawPrintedGridCells_StrikethroughStyle_AddsVisibleInkComparedToPlainText()
    {
        StaTestRunner.Run(() =>
        {
            var plainBitmap = RenderSingleCell("AAA", style: null);
            var strikethroughBitmap = RenderSingleCell("AAA", new CellStyle { Strikethrough = true });

            var plainInk = CountNearBlackPixels(plainBitmap);
            var strikethroughInk = CountNearBlackPixels(strikethroughBitmap);

            strikethroughInk.Should().BeGreaterThan(plainInk,
                "Format Cells > Font > Strikethrough must add a visible strikethrough stroke on print/PDF output");
        });
    }

    [Fact]
    public void DrawPrintedGridCells_DoubleUnderlineStyle_AddsVisibleInkComparedToPlainText()
    {
        StaTestRunner.Run(() =>
        {
            var plainBitmap = RenderSingleCell("AAA", style: null);
            var doubleUnderlineBitmap = RenderSingleCell("AAA", new CellStyle { DoubleUnderline = true });

            var plainInk = CountNearBlackPixels(plainBitmap);
            var doubleUnderlineInk = CountNearBlackPixels(doubleUnderlineBitmap);

            doubleUnderlineInk.Should().BeGreaterThan(plainInk,
                "Format Cells > Font > Double-underline must draw its two manual stroke lines on print/PDF output");
        });
    }

    [Fact]
    public void DrawPrintedGridCells_PlainTextWithoutDecoration_StillRendersNormally()
    {
        // Sibling/no-regression case: ordinary undecorated text (the overwhelming majority of real
        // cells) must still render visibly after threading CellTextDecorationPlanner.Build through
        // DrawPrintedCellText -- the decoration lookup itself must not suppress or corrupt plain text.
        StaTestRunner.Run(() =>
        {
            var bitmap = RenderSingleCell("Hello", style: null);

            CountNearBlackPixels(bitmap).Should().BeGreaterThan(0,
                "plain undecorated text must still be visible after adding decoration support");
        });
    }

    private static byte[] RenderSingleCell(string text, CellStyle? style)
    {
        var cell = new DisplayCell(
            Row: 1,
            Col: 1,
            RawValue: new TextValue(text),
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

    private static int CountNearBlackPixels(byte[] pixels)
    {
        const int bitmapWidth = (int)ColumnWidth;
        const int bitmapHeight = (int)RowHeight;
        var count = 0;
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
                    count++;
            }
        }

        return count;
    }
}
