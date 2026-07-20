using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R53-fix-one-path-miss-twin-sweep-1: DrawPrintedCellText's non-rotated branch (the vast majority
/// of printed cells) used to hardcode `textPoint = new Point(rect.Left + 2, rect.Top + (rect.Height
/// - ft.Height) / 2)` regardless of the cell's style, so Format Cells &gt; Alignment &gt;
/// Horizontal/Indent were completely ignored on print/PDF even though the interactive grid
/// (GridView.Rendering.cs) honors them. The fix routes the non-rotated branch through the same
/// CellTextOrientationLayoutPlanner.CalculateLayout the rotated branch already used, with a resolved
/// HorizontalAlignment/VerticalAlignment/IndentLevel.
/// </summary>
public sealed class R53_PrintedCellAlignmentIndentTests
{
    [Fact]
    public void RenderWorksheet_RightAlignedCellPrintsFartherRightThanLeftAlignedCell()
    {
        StaTestRunner.Run(() =>
        {
            var leftOverlay = RenderSingleCellOverlay(HorizontalAlignment.Left);
            var rightOverlay = RenderSingleCellOverlay(HorizontalAlignment.Right);

            // Pre-fix, both cells drew their text at the identical `rect.Left + 2` position
            // regardless of style.HorizontalAlignment, so this would fail (rightOverlay.X ==
            // leftOverlay.X). Post-fix, a Right-aligned cell in a wide column must sit well to the
            // right of a Left-aligned cell's text.
            rightOverlay.X.Should().BeGreaterThan(leftOverlay.X + 50.0);
        });
    }

    [Fact]
    public void RenderWorksheet_GeneralAlignedTextCellStillPrintsAtSamePositionAsExplicitLeftAligned()
    {
        // Sibling/no-regression case: Excel General-aligns TEXT content to the left (same visual
        // position as an explicit Left alignment) both before and after this fix -- the old hardcoded
        // rect.Left + 2 position happened to coincide with flush-left, and the new
        // alignment-resolving code path must still land text-General content at the same spot as
        // Left, not silently shift it.
        StaTestRunner.Run(() =>
        {
            var generalOverlay = RenderSingleCellOverlay(HorizontalAlignment.General);
            var leftOverlay = RenderSingleCellOverlay(HorizontalAlignment.Left);

            generalOverlay.X.Should().BeApproximately(leftOverlay.X, 0.01);
        });
    }

    private static PdfTextOverlay RenderSingleCellOverlay(HorizontalAlignment hAlign)
    {
        var workbook = new Workbook($"Aligned {hAlign}");
        var sheet = workbook.AddSheet("Sheet1");
        var style = workbook.RegisterStyle(new CellStyle { HorizontalAlignment = hAlign });
        var cell = Cell.FromValue(new TextValue("Hi"));
        cell.StyleId = style;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);
        sheet.ColumnWidths[1] = 40.0; // wide column so Left vs Right differ unmistakably
        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 1));

        var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
        var page = document.Pages[0].GetPageRoot(forceReload: false)!;
        return PdfTextOverlayExtractor.Extract(page).Should().ContainSingle().Subject;
    }
}
