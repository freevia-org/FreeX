using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression coverage for R23-meta-1: the round-22 hidden-merge-anchor fix
/// (<see cref="ViewportService"/>'s <c>IsHiddenMergeAnchorRowWithVisibleRemainder</c>/
/// <c>...ColWithVisibleRemainder</c>, which keeps a zero-size RowMetric/ColMetric for the hidden
/// anchor row/column so the merge's own value/style stay addressable) must NOT leak every other,
/// unrelated cell that happens to share that hidden row/column into the viewport. Only the merge's
/// own anchor cell may be exposed; everything else in the hidden row/column stays hidden, exactly
/// as it did before the round-22 fix.
/// </summary>
public sealed class R23_MergedCellsHiddenAnchorViewportLeakTests
{
    [Fact]
    public void GetViewport_VerticalMergeWithHiddenAnchorRow_DoesNotLeakUnrelatedCellInSameHiddenRow()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        // Merge B2:B5 (vertical, 4 rows). Anchor is B2.
        sheet.AddMergedRegion(new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 5, 2)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Total"));

        // Unrelated data in column A of the SAME hidden row -- must never be surfaced.
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("SECRET"));

        // Hide only the anchor row (row 2). Rows 3-5 remain visible.
        sheet.HiddenRows.Add(2);

        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(1, 1, 400, 400));

        // The merge's own value must still render at its anchor cell.
        viewport.Cells.Should().ContainSingle(c => c.Row == 2 && c.Col == 2)
            .Which.DisplayText.Should().Be("Total");

        // But the unrelated cell in the same hidden row must NOT leak into the viewport.
        viewport.Cells.Should().NotContain(c => c.Row == 2 && c.Col == 1);
    }

    [Fact]
    public void GetViewport_HorizontalMergeWithHiddenAnchorColumn_DoesNotLeakUnrelatedCellInSameHiddenColumn()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        // Merge B2:E2 (horizontal, 4 columns). Anchor is B2.
        sheet.AddMergedRegion(new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 2, 5)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Header"));

        // Unrelated data in row 5 of the SAME hidden column -- must never be surfaced.
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new TextValue("SECRET"));

        // Hide only the anchor column (col B / col 2). Columns C-E remain visible.
        sheet.HiddenCols.Add(2);

        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(1, 1, 400, 400));

        // The merge's own value must still render at its anchor cell.
        viewport.Cells.Should().ContainSingle(c => c.Row == 2 && c.Col == 2)
            .Which.DisplayText.Should().Be("Header");

        // But the unrelated cell in the same hidden column must NOT leak into the viewport.
        viewport.Cells.Should().NotContain(c => c.Row == 5 && c.Col == 2);
    }

    [Fact]
    public void GetViewport_VerticalMergeWithHiddenAnchorRow_DenseCartesianLoopPath_DoesNotLeakUnrelatedCell()
    {
        // ShouldScanOccupiedViewportCells only picks the sparse "occupied cell scan" path when the
        // sheet's total CellCount is small relative to the viewport's visible cell-slot count.
        // Padding the sheet with enough occupied cells (even far outside the viewport) pushes
        // CellCount past that threshold, so GetViewport falls back to the dense, small-sheet
        // Cartesian row x col loop (ViewportService.cs's AddDisplayCell call site) instead -- the
        // other cell-enumeration path that must independently guard against the same leak.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.AddMergedRegion(new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 5, 2)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Total"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("SECRET"));

        for (uint row = 1000; row < 1200; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

        sheet.HiddenRows.Add(2);

        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(1, 1, 400, 400));

        viewport.Cells.Should().ContainSingle(c => c.Row == 2 && c.Col == 2)
            .Which.DisplayText.Should().Be("Total");
        viewport.Cells.Should().NotContain(c => c.Row == 2 && c.Col == 1);
    }
}
