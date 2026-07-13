using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-42 finding R42-meta-1: the round-41 merge-aware width fix (GetMergeAwareTargetWidthPixels)
/// was wired into only ONE of ViewportService's four cell-emission paths -- the sparse
/// occupied-cell scan (AddOccupiedViewportCells/TryGetVisibleColumnTargetWidth). The DENSE
/// default-loop path (ViewportService.cs, the `else` branch iterating rowMetrics x colMetrics) and
/// the split-pane path (BuildSplitPaneCells) still passed the single anchor column's width, so a
/// horizontally-merged cell whose combined merged width comfortably fits a value would still show a
/// false "###"/scientific-notation fallback in the common dense case and in any freeze/split-pane
/// sheet. Fixed by routing all four call sites through GetMergeAwareTargetWidthPixels.
/// </summary>
public sealed class R42_ViewportMergeAwareWidthAllPathsTests
{
    // ── Dense path (default cell loop) ─────────────────────────────────────────────────────────

    [Fact]
    public void DensePath_MergedCell_UsesCombinedMergedWidthNotJustAnchorColumn()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        // Narrow columns (Excel width 2 units each -> EstimateCharacterWidth 2 each).
        sheet.ColumnWidths[1] = 2;
        sheet.ColumnWidths[2] = 2;
        sheet.ColumnWidths[3] = 2;

        sheet.AddMergedRegion(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 3)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(123456789));

        // A cell comment forces ShouldScanOccupiedViewportCells to false, so GetViewport takes the
        // DENSE default-loop path (the `else` branch at ViewportService.cs) instead of the
        // sparse occupied-cell scan.
        sheet.Comments[new CellAddress(sheet.Id, 5, 5)] = "unrelated note";

        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(1, 1, 400, 400));

        var anchorCell = viewport.Cells.Should().ContainSingle(c => c.Row == 1 && c.Col == 1).Subject;

        // Bug: anchor column alone (width 2, digitBudget 5) can only fit "1E+08" (5 chars).
        // Fixed: merged A1:C1 combined width fits the full 9-digit "123456789".
        anchorCell.DisplayText.Should().Be("123456789");
    }

    [Fact]
    public void DensePath_UnmergedCell_SameNarrowColumnWidth_StillFallsBackToScientificNotation()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.ColumnWidths[1] = 2;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(123456789));
        sheet.Comments[new CellAddress(sheet.Id, 5, 5)] = "unrelated note";

        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(1, 1, 400, 400));

        var cell = viewport.Cells.Should().ContainSingle(c => c.Row == 1 && c.Col == 1).Subject;
        cell.DisplayText.Should().Be("1E+08");
    }

    // ── Split-pane path (freeze/split window) ──────────────────────────────────────────────────

    [Fact]
    public void SplitPanePath_MergedCell_UsesCombinedMergedWidthNotJustAnchorColumn()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.ColumnWidths[1] = 2;
        sheet.ColumnWidths[2] = 2;
        sheet.ColumnWidths[3] = 2;

        sheet.AddMergedRegion(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 3)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(123456789));

        // A split window puts the merged cell's row/column in the "left columns" of the split
        // pane, exercising BuildSplitPaneCells rather than the main viewport loop.
        sheet.SplitRow = 5;

        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(1, 1, 400, 400));

        viewport.SplitPanes.Should().NotBeNull();
        var anchorCell = viewport.SplitPanes!.Cells.Should().ContainSingle(c => c.Row == 1 && c.Col == 1).Subject;

        anchorCell.DisplayText.Should().Be("123456789");
    }

    [Fact]
    public void SplitPanePath_UnmergedCell_SameNarrowColumnWidth_StillFallsBackToScientificNotation()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.ColumnWidths[1] = 2;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(123456789));
        sheet.SplitRow = 5;

        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(1, 1, 400, 400));

        viewport.SplitPanes.Should().NotBeNull();
        var cell = viewport.SplitPanes!.Cells.Should().ContainSingle(c => c.Row == 1 && c.Col == 1).Subject;
        cell.DisplayText.Should().Be("1E+08");
    }
}
