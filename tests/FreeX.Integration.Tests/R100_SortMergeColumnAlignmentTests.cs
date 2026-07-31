using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R100-commands-sort-merge-column-align-4-1: SortCommand's "uniform merged rows" allowance (the
/// same-count/contained/RowCount==1/ColCount-matches-first checks around line 189-201, and the
/// LeftToRight transpose around line 181-188) never verified that every overlapping merge shares
/// the SAME horizontal (or, for LeftToRight, vertical) position as the first one — only that they
/// are identically SIZED. A range where row 2's merge sits at B2:C2 but row 3's same-width merge
/// sits at C3:D3 (a different column offset) passed the "uniform" gate and the sort proceeded. The
/// sort itself swaps whole grid ROWS through FIXED column indexes (WriteCellPayload writes each
/// column's payload back into the SAME column it read from), never touching MergedRegions, so a
/// misaligned merge's anchor value gets physically written into a COVERED (non-anchor) cell of
/// whichever row it swaps into — a genuine "value hiding in a merged-but-not-anchor cell" corruption
/// that is invisible in the grid, live to formulas/Unmerge, and never produced by Excel's own writer.
/// </summary>
public sealed class R100_sort_merge_column_alignment_Tests
{
    [Fact]
    public void Sort_OverSameSizedMergesAtDifferentColumnOffsets_IsRejected()
    {
        // B2:D6 sort range. Row 2's merge is B2:C2 (cols 2-3); row 3's SAME-WIDTH merge is C3:D3
        // (cols 3-4) — a different horizontal offset. Rows 4-6 also merge at B:C to keep the
        // "one merge per row, identically sized" gate satisfied except for the misalignment.
        // Before the fix, this passed the "uniform" check and the sort proceeded; after the fix,
        // it must be rejected exactly like any other non-uniform merge shape.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var sid = sheet.Id;

        var keys = new[] { 50d, 10d, 30d, 20d, 40d };
        for (uint i = 0; i < 5; i++)
        {
            var row = 2 + i;
            sheet.SetCell(new CellAddress(sid, row, 2), new NumberValue(keys[i]));
            uint startCol = row == 3 ? 3u : 2u; // row 3's merge is shifted one column right
            uint endCol = startCol + 1;
            sheet.AddMergedRegion(new GridRange(
                new CellAddress(sid, row, startCol),
                new CellAddress(sid, row, endCol)));
        }

        var range = new GridRange(new CellAddress(sid, 2, 2), new CellAddress(sid, 6, 4)); // B2:D6
        var cmd = new SortCommand(sid, range, sortByColOffset: 0, ascending: true);

        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeFalse(
            "merges that are identically sized but sit at different column offsets are not a uniform " +
            "per-row unit — sorting would relocate one row's merge anchor into another row's covered cell");
        outcome.ErrorMessage.Should().Be("Cannot sort a range that contains merged cells.");

        // Nothing should have moved — the rejection must happen before any mutation.
        // keys = {50, 10, 30, 20, 40} laid down at rows 2..6 respectively (unsorted order).
        sheet.GetValue(2, 2).Should().Be(new NumberValue(50d));
        sheet.GetValue(3, 2).Should().Be(new NumberValue(10d));
        sheet.GetValue(4, 2).Should().Be(new NumberValue(30d));
        sheet.GetValue(5, 2).Should().Be(new NumberValue(20d));
        sheet.GetValue(6, 2).Should().Be(new NumberValue(40d));
    }

    [Fact]
    public void Sort_LeftToRight_OverSameSizedMergesAtDifferentRowOffsets_IsRejected()
    {
        // Transpose of the above for the LeftToRight (column-swap) branch: B2:B4 merged as the
        // key column's "unit" for column 2, but column 3's same-height merge sits at C3:C5 —
        // shifted down one row. Column 4 also merges at rows 2-4 to keep the rest of the gate
        // satisfied. This must still be rejected under LeftToRight sorting.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var sid = sheet.Id;

        var keys = new[] { 30d, 10d, 20d };
        for (uint i = 0; i < 3; i++)
        {
            var col = 2 + i;
            sheet.SetCell(new CellAddress(sid, 2, col), new NumberValue(keys[i]));
            uint startRow = col == 3 ? 3u : 2u; // column C's merge is shifted one row down
            uint endRow = startRow + 2;
            sheet.AddMergedRegion(new GridRange(
                new CellAddress(sid, startRow, col),
                new CellAddress(sid, endRow, col)));
        }

        var range = new GridRange(new CellAddress(sid, 2, 2), new CellAddress(sid, 4, 4)); // B2:D4
        var cmd = new SortCommand(sid, range, [new SortKey(0, true)], new SortOptions(LeftToRight: true));

        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeFalse(
            "column merges that are identically sized but sit at different row offsets are not a uniform " +
            "per-column unit under a LeftToRight sort");
        outcome.ErrorMessage.Should().Be("Cannot sort a range that contains merged cells.");
    }

    [Fact]
    public void Sort_OverUniformFullWidthRowMerges_StillSucceeds_NoRegression()
    {
        // No-regression sibling: the already-working case where every row's merge shares the same
        // Start.Col (B:C throughout) must still be allowed and must still move each merged unit
        // together — this is the exact scenario R22_SortUniformMergedRowsTests already covers, kept
        // here as a same-file regression guard for the new Start.Col check.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var sid = sheet.Id;

        var keys = new[] { 50d, 10d, 30d, 20d, 40d };
        for (uint i = 0; i < 5; i++)
        {
            var row = 2 + i;
            sheet.SetCell(new CellAddress(sid, row, 2), new NumberValue(keys[i]));
            sheet.SetCell(new CellAddress(sid, row, 3), new TextValue($"tag{keys[i]}"));
            sheet.AddMergedRegion(new GridRange(
                new CellAddress(sid, row, 2),
                new CellAddress(sid, row, 3)));
        }

        var range = new GridRange(new CellAddress(sid, 2, 2), new CellAddress(sid, 6, 3));
        var cmd = new SortCommand(sid, range, sortByColOffset: 0, ascending: true);

        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue("every row's merge shares the same Start.Col, so the range is genuinely uniform");

        var expectedKeys = new[] { 10d, 20d, 30d, 40d, 50d };
        for (uint i = 0; i < 5; i++)
        {
            var row = 2 + i;
            sheet.GetValue(row, 2).Should().Be(new NumberValue(expectedKeys[i]));
            sheet.GetValue(row, 3).Should().Be(new TextValue($"tag{expectedKeys[i]}"));
        }
    }
}
