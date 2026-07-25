using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Round-90 fixes to PrintRenderer.GridCells.cs's text/overflow layout so print/PDF matches the
/// interactive grid (GridView.Rendering.cs) for merged cells:
///
/// - R90-render-cell-overflow-clip-5-1: the text pass laid a merged cell's text out against only
///   its single anchor column/row width instead of the full merge span (GridView.Rendering.cs's
///   Pass 3 sums every merged column/row before building its rect). Fixed by widening the printed
///   cell rect for a merge anchor the same way the border pass's diagonal-widening helpers already
///   do (SumPrintedMergedColumnWidth/SumPrintedMergedRowHeight).
///
/// - R90-render-cell-overflow-clip-5-2: ComputePrintedOverflowWidth/-Left only tested a neighbor
///   cell's DisplayText for emptiness, so a neighbor that was blank but a member of an unrelated
///   merge (or formula/icon/data-bar bearing) never blocked the spill, unlike the screen's
///   CellTextOverflowPlanner.IsOverflowOccupied. Fixed by calling that same occupancy check.
///
/// - R90-render-cell-overflow-clip-5-3: DrawPrintedCellText always called
///   GridView.CanOverflowCellText with merge: null, so a merged cell could be granted overflow
///   eligibility the screen would never grant it (CanOverflowCellText requires !merge.HasValue).
///   Fixed by threading the cell's real merge region through.
/// </summary>
public sealed class R90_PrintRendererMergedCellOverflowTests
{
    private const string LongText = "A very long value that overflows the column";

    // ---- R90-render-cell-overflow-clip-5-1 -------------------------------------------------

    [Fact]
    public void RenderWorksheet_MergedShrinkToFitCell_UsesFullMergeSpanWidthNotJustAnchorColumn()
    {
        StaTestRunner.Run(() =>
        {
            var mergedOverlay = RenderShrinkToFitOverlay(merge: true);
            var plainOverlay = RenderShrinkToFitOverlay(merge: false);

            // Both cells share the identical narrow anchor-column width (4.0) and the same
            // requested 20pt font. Only the merged cell has four extra (default-width) columns
            // folded into its available text width, so ShrinkToFit must shrink it LESS (a larger
            // resulting font) than the single narrow non-merged cell. Pre-fix, the print path
            // never looked up the merge at all, so both cells measured against the same
            // single-column rect and shrank identically -- this comparison was never true.
            mergedOverlay.FontSize.Should().BeGreaterThan(plainOverlay.FontSize,
                "a merged cell's printed text must be laid out against its full merge span, " +
                "not just its single anchor column, matching the interactive grid");
        });
    }

    [Fact]
    public void RenderWorksheet_NonMergedNarrowShrinkToFitCell_StillShrinksTowardMinimumFontSize()
    {
        // No-regression sibling: an ordinary (non-merged) narrow ShrinkToFit cell must still
        // shrink aggressively toward the font floor -- the merge-span widening must never leak
        // into plain single-cell layout.
        StaTestRunner.Run(() =>
        {
            var overlay = RenderShrinkToFitOverlay(merge: false);
            var requestedDip = 20.0 * 96.0 / 72.0;
            var minimumDip = 6.0 * 96.0 / 72.0;

            overlay.FontSize.Should().BeLessThan(requestedDip);
            overlay.FontSize.Should().BeGreaterThanOrEqualTo(minimumDip - 0.01);
        });
    }

    private static PdfTextOverlay RenderShrinkToFitOverlay(bool merge)
    {
        var workbook = new Workbook($"Merge shrink print {merge}");
        var sheet = workbook.AddSheet("Sheet1");
        var style = workbook.RegisterStyle(new CellStyle { FontSize = 20, ShrinkToFit = true });
        var cell = Cell.FromValue(new TextValue(LongText));
        cell.StyleId = style;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);
        sheet.ColumnWidths[1] = 4.0;

        if (merge)
        {
            sheet.AddMergedRegion(new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 5)));
        }

        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 5));

        var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
        var page = document.Pages[0].GetPageRoot(forceReload: false)!;
        return PdfTextOverlayExtractor.Extract(page).Should().ContainSingle().Subject;
    }

    // ---- R90-render-cell-overflow-clip-5-2 -------------------------------------------------

    [Fact]
    public void RenderWorksheet_OverflowNeighborIsBlankMergedMemberCell_BlocksPrintedOverflow()
    {
        StaTestRunner.Run(() =>
        {
            var overlay = RenderNeighborOverflowOverlay(neighborMerged: true);

            // Column C is a blank member of an unrelated merge (C1:D1). Excel's own occupied-cell
            // rule (mirrored by CellTextOverflowPlanner.IsOverflowOccupied) treats ANY merge
            // membership as occupied regardless of DisplayText, so B1's overflow must stop at C1's
            // left edge and get ellipsis-truncated at its own narrow column width.
            overlay.Text.Should().Contain("…",
                "a blank cell that is a member of an unrelated merge must still block overflow, " +
                "matching the interactive grid's occupied-cell rule");
        });
    }

    [Fact]
    public void RenderWorksheet_OverflowIntoPlainBlankNeighbor_StillSpillsAcrossItUnblocked()
    {
        // No-regression sibling: a genuinely blank, non-merged neighbor must still allow the
        // overflow to spill across it exactly as before -- the fix only tightens occupancy for
        // merge/formula/icon/databar signals, not ordinary blank cells.
        StaTestRunner.Run(() =>
        {
            var overlay = RenderNeighborOverflowOverlay(neighborMerged: false);

            overlay.Text.Should().NotContain("…",
                "an ordinary blank non-merged neighbor must still be transparent to overflow");
        });
    }

    private static PdfTextOverlay RenderNeighborOverflowOverlay(bool neighborMerged)
    {
        var workbook = new Workbook($"Neighbor overflow print {neighborMerged}");
        var sheet = workbook.AddSheet("Sheet1");
        var style = workbook.RegisterStyle(new CellStyle { FontSize = 20 });
        var cell = Cell.FromValue(new TextValue(LongText));
        cell.StyleId = style;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), cell); // B1
        sheet.ColumnWidths[2] = 4.0;

        if (neighborMerged)
        {
            // C1:D1 merged, deliberately left blank (no cell value set on either member).
            sheet.AddMergedRegion(new GridRange(
                new CellAddress(sheet.Id, 1, 3),
                new CellAddress(sheet.Id, 1, 4)));
        }

        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 1, 2),
            new CellAddress(sheet.Id, 1, 5));

        var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
        var page = document.Pages[0].GetPageRoot(forceReload: false)!;
        return PdfTextOverlayExtractor.Extract(page).Should().ContainSingle().Subject;
    }

    // ---- R90-render-cell-overflow-clip-5-3 -------------------------------------------------

    [Fact]
    public void RenderWorksheet_RightAlignedMergedCell_NeverOverflowsIntoBlankNeighborLikeScreen()
    {
        StaTestRunner.Run(() =>
        {
            var overlay = RenderRightAlignedOverlay(merge: true);

            // The merge (B1:C1) must never be granted overflow eligibility at all -- Excel's own
            // rule (CanOverflowCellText requires !merge.HasValue) confines a merged cell's text to
            // its own merge rect, so it must stay ellipsis-truncated instead of spilling into the
            // blank column A to its left.
            overlay.Text.Should().Contain("…",
                "a merged cell must never overflow into a neighboring blank cell, matching the " +
                "interactive grid's merge/overflow contract");
        });
    }

    [Fact]
    public void RenderWorksheet_RightAlignedNonMergedNarrowCell_StillOverflowsIntoBlankLeftNeighbor()
    {
        // No-regression sibling: an ordinary (non-merged) right-aligned narrow cell must still be
        // allowed to spill its overflow text leftward into a blank neighbor -- the merge-eligibility
        // fix must not touch plain single-cell overflow.
        StaTestRunner.Run(() =>
        {
            var overlay = RenderRightAlignedOverlay(merge: false);

            overlay.Text.Should().NotContain("…",
                "a non-merged right-aligned cell must still overflow into a blank left neighbor");
        });
    }

    private static PdfTextOverlay RenderRightAlignedOverlay(bool merge)
    {
        var workbook = new Workbook($"Right aligned merge print {merge}");
        var sheet = workbook.AddSheet("Sheet1");
        var style = workbook.RegisterStyle(new CellStyle
        {
            FontSize = 20,
            HorizontalAlignment = HorizontalAlignment.Right
        });
        var cell = Cell.FromValue(new TextValue(LongText));
        cell.StyleId = style;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), cell); // B1 (anchor when merged)
        sheet.ColumnWidths[1] = 80.0; // A1: wide blank neighbor to the left (fits the full text)
        sheet.ColumnWidths[2] = 4.0;  // B1: narrow
        sheet.ColumnWidths[3] = 4.0;  // C1: narrow (merge partner when merged)

        if (merge)
        {
            sheet.AddMergedRegion(new GridRange(
                new CellAddress(sheet.Id, 1, 2),
                new CellAddress(sheet.Id, 1, 3)));
        }

        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 3));

        var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
        var page = document.Pages[0].GetPageRoot(forceReload: false)!;
        return PdfTextOverlayExtractor.Extract(page).Should().ContainSingle().Subject;
    }
}
