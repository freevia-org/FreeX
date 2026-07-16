using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R21-autofilter-sort-state-1: SortCommand permutes FilterHiddenRows/ValueFilterHiddenRows
/// per-row but never touched sheet.ColumnFilterOwnedRows, desyncing condition/Top-N/Average/
/// color filter ownership after a sort.
///
/// R45-commands-sort-filter-interaction-3-1 superseded this scenario's original expectations:
/// real Excel's Sort documentation states hidden rows in a filtered range are not sorted at all,
/// so a row a column-owned filter is hiding must stay pinned at its own physical row rather than
/// being permuted to a new one alongside the rows that are actually re-sorted. The scenario below
/// now demonstrates the corrected behavior — column A (col 1) over A2:A6 holds 100,90,80,70,60,
/// where a Top-10-style mechanism has hidden rows 5 and 6 (values 70, 60) and recorded that
/// ownership in sheet.ColumnFilterOwnedRows[1] = {5,6}/sheet.FilterHiddenRows = {5,6}. Sorting
/// A2:A6 ascending must leave rows 5 and 6 completely untouched (they are excluded from the sort
/// entirely) and only reorder the three VISIBLE rows (100, 90, 80 at rows 2-4) among themselves —
/// which, already being ascending-ordered pairwise, actually reverses to 80,90,100.
/// </summary>
public sealed class R21_SortColumnFilterOwnedRowsTests
{
    [Fact]
    public void Apply_LeavesFilterHiddenRowsAndTheirColumnFilterOwnershipUntouchedByTheSort()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var sid = sheet.Id;

        // Column A, rows 2-6: 100, 90, 80, 70, 60.
        sheet.SetCell(new CellAddress(sid, 2, 1), new NumberValue(100));
        sheet.SetCell(new CellAddress(sid, 3, 1), new NumberValue(90));
        sheet.SetCell(new CellAddress(sid, 4, 1), new NumberValue(80));
        sheet.SetCell(new CellAddress(sid, 5, 1), new NumberValue(70));
        sheet.SetCell(new CellAddress(sid, 6, 1), new NumberValue(60));

        // A Top-10 (or Average/condition/color) filter on column A hid rows 5 and 6 and recorded
        // that ownership — a sort of the range must leave this pair of rows completely alone.
        sheet.FilterHiddenRows.Add(5);
        sheet.FilterHiddenRows.Add(6);
        sheet.ColumnFilterOwnedRows[1] = [5, 6];

        var range = new GridRange(new CellAddress(sid, 2, 1), new CellAddress(sid, 6, 1));
        var cmd = new SortCommand(sid, range, sortByColOffset: 0, ascending: true);
        cmd.Apply(ctx).Success.Should().BeTrue();

        // Only the three VISIBLE rows (100, 90, 80) are reordered among their own slots
        // (rows 2-4) — ascending flips them to 80, 90, 100.
        sheet.GetValue(2, 1).Should().Be(new NumberValue(80));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(90));
        sheet.GetValue(4, 1).Should().Be(new NumberValue(100));

        // The filter-hidden rows' data must never move: 70 and 60 stay exactly at rows 5 and 6.
        sheet.GetValue(5, 1).Should().Be(new NumberValue(70));
        sheet.GetValue(6, 1).Should().Be(new NumberValue(60));

        // FilterHiddenRows and ColumnFilterOwnedRows must still name rows 5 and 6 — they were
        // never candidates for the sort in the first place, so nothing to permute.
        sheet.FilterHiddenRows.Should().BeEquivalentTo([5u, 6u]);
        sheet.ColumnFilterOwnedRows.Should().ContainKey(1u);
        sheet.ColumnFilterOwnedRows[1].Should().BeEquivalentTo([5u, 6u],
            "a row a column-owned filter is hiding must stay pinned at its own physical row, matching Excel's documented Sort behavior");
    }

    [Fact]
    public void Revert_RestoresOriginalRowOrderAndColumnFilterOwnedRows()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var sid = sheet.Id;

        sheet.SetCell(new CellAddress(sid, 2, 1), new NumberValue(100));
        sheet.SetCell(new CellAddress(sid, 3, 1), new NumberValue(90));
        sheet.SetCell(new CellAddress(sid, 4, 1), new NumberValue(80));
        sheet.SetCell(new CellAddress(sid, 5, 1), new NumberValue(70));
        sheet.SetCell(new CellAddress(sid, 6, 1), new NumberValue(60));

        sheet.FilterHiddenRows.Add(5);
        sheet.FilterHiddenRows.Add(6);
        sheet.ColumnFilterOwnedRows[1] = [5, 6];

        var range = new GridRange(new CellAddress(sid, 2, 1), new CellAddress(sid, 6, 1));
        var cmd = new SortCommand(sid, range, sortByColOffset: 0, ascending: true);
        cmd.Apply(ctx).Success.Should().BeTrue();
        sheet.ColumnFilterOwnedRows[1].Should().BeEquivalentTo([5u, 6u]);

        cmd.Revert(ctx);

        sheet.GetValue(2, 1).Should().Be(new NumberValue(100));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(90));
        sheet.GetValue(4, 1).Should().Be(new NumberValue(80));
        sheet.GetValue(5, 1).Should().Be(new NumberValue(70));
        sheet.GetValue(6, 1).Should().Be(new NumberValue(60));
        sheet.ColumnFilterOwnedRows.Should().ContainKey(1u);
        sheet.ColumnFilterOwnedRows[1].Should().BeEquivalentTo([5u, 6u],
            "undo must restore the pre-sort state exactly");
    }
}
