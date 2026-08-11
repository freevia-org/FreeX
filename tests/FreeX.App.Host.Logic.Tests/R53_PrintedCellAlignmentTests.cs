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
/// R53-fix-one-path-miss-twin-sweep-1: DrawPrintedCellText's non-rotated branch (the vast majority
/// of cells) used to hardcode the drawn text position to
/// <c>new Point(rect.Left + 2, rect.Top + (rect.Height - ft.Height) / 2)</c> regardless of the
/// cell's style -- so Format Cells &gt; Alignment &gt; Horizontal/Vertical/Indent were completely
/// ignored on print/PDF output even though the interactive grid (GridView.Rendering.cs) honors all
/// three. The fix routes the text position through the same
/// <see cref="CellTextOrientationLayoutPlanner.CalculateLayout"/> call the interactive grid uses
/// (and the print path's own previously-rotated-only branch already called), passing the cell's
/// real HorizontalAlignment/VerticalAlignment/IndentLevel instead of a hardcoded flush-left point.
/// </summary>
public sealed class R53_PrintedCellAlignmentTests
{
    private const double ColumnWidth = 200.0;
    private const double RowHeight = 40.0;

    [Fact]
    public void DrawPrintedGridCells_RightAlignedCell_RendersTextNearRightEdgeNotLeftEdge()
    {
        StaTestRunner.Run(() =>
        {
            var style = new CellStyle { HorizontalAlignment = CellHAlign.Right };
            var bitmap = RenderSingleCell("5", style);

            var (minX, maxX) = FindInkHorizontalExtent(bitmap);

            // Pre-fix, DrawPrintedCellText hardcoded textPoint to rect.Left + 2 for every
            // non-rotated cell, so a right-aligned cell's short text would land flush left (minX
            // well under 20px). Post-fix, the text must be anchored to the RIGHT edge of the
            // 200px-wide column, so its ink must start well past the column's midpoint.
            minX.Should().BeGreaterThan((int)(ColumnWidth / 2),
                "a right-aligned cell's text must be drawn near the column's right edge, not flush left");
            maxX.Should().BeLessThan((int)ColumnWidth,
                "the text must still be fully inside the cell, just anchored to its right side");
        });
    }

    [Fact]
    public void DrawPrintedGridCells_DefaultLeftAlignedTextCell_StillRendersNearLeftEdge()
    {
        // Sibling/no-regression case: an ordinary General-aligned text cell (the overwhelming
        // majority of real cells) must still render flush-left after routing textPoint through
        // CalculateLayout -- the refactor must not shift the common default case.
        StaTestRunner.Run(() =>
        {
            var bitmap = RenderSingleCell("Hello", style: null);

            var (minX, _) = FindInkHorizontalExtent(bitmap);

            minX.Should().BeLessThan(20,
                "a default (General-aligned) text cell must still render near the cell's left edge");
        });
    }

    [Fact]
    public void DrawPrintedGridCells_IndentedCell_ShiftsTextRightOfUnindentedCell()
    {
        // R53-fix-one-path-miss-twin-sweep-1 also called out IndentLevel as ignored by the
        // non-rotated branch -- Format Cells > Alignment > "Increase Indent" must visibly push the
        // text away from the left edge on print, just like it does on screen.
        StaTestRunner.Run(() =>
        {
            var plainBitmap = RenderSingleCell("Hi", style: null);
            var indentedBitmap = RenderSingleCell("Hi", new CellStyle { IndentLevel = 3 });

            var (plainMinX, _) = FindInkHorizontalExtent(plainBitmap);
            var (indentedMinX, _) = FindInkHorizontalExtent(indentedBitmap);

            indentedMinX.Should().BeGreaterThan(plainMinX,
                "an indented cell must render its text further from the left edge than the same unindented cell");
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
                null,
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
