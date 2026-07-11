using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R21-autofilter-sort-state-1: SortCommand permutes FilterHiddenRows/ValueFilterHiddenRows
/// per-row but never touched sheet.ColumnFilterOwnedRows, desyncing condition/Top-N/Average/
/// color filter ownership after a sort.
///
/// Scenario: column A (col 1) over A2:A6 holds 100,90,80,70,60. A Top-10-style mechanism has
/// hidden the two lowest values — rows 5 and 6 — and recorded that ownership in
/// sheet.ColumnFilterOwnedRows[1] = {5,6} alongside sheet.FilterHiddenRows = {5,6}. Sorting
/// A2:A6 ascending moves the two lowest values (70, 60 — originally at rows 5 and 6) to the top
/// of the range (rows 3 and 2). FilterHiddenRows correctly becomes {2,3}, but before the fix
/// ColumnFilterOwnedRows[1] stayed stale at {5,6} — rows that are now actually visible — while
/// the truly-hidden rows (2,3) were left unowned.
/// </summary>
public sealed class R21_SortColumnFilterOwnedRowsTests
{
    [Fact]
    public void Apply_PermutesColumnFilterOwnedRows_InLockstepWithFilterHiddenRows()
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
        // that ownership — this is the pre-existing state the sort must not desync.
        sheet.FilterHiddenRows.Add(5);
        sheet.FilterHiddenRows.Add(6);
        sheet.ColumnFilterOwnedRows[1] = [5, 6];

        var range = new GridRange(new CellAddress(sid, 2, 1), new CellAddress(sid, 6, 1));
        var cmd = new SortCommand(sid, range, sortByColOffset: 0, ascending: true);
        cmd.Apply(ctx).Success.Should().BeTrue();

        // Sanity-check the actual data reorder: ascending puts 60 then 70 on top.
        sheet.GetValue(2, 1).Should().Be(new NumberValue(60));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(70));

        // FilterHiddenRows was already known to permute correctly.
        sheet.FilterHiddenRows.Should().BeEquivalentTo([2u, 3u]);

        // The bug: ColumnFilterOwnedRows must move with the same rows, not stay at {5,6}.
        sheet.ColumnFilterOwnedRows.Should().ContainKey(1u);
        sheet.ColumnFilterOwnedRows[1].Should().BeEquivalentTo([2u, 3u],
            "the Top-10 filter's ownership must follow the rows it hid to their new post-sort position");
    }

    [Fact]
    public void Revert_RestoresOriginalColumnFilterOwnedRows()
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
        sheet.ColumnFilterOwnedRows[1].Should().BeEquivalentTo([2u, 3u]);

        cmd.Revert(ctx);

        sheet.GetValue(5, 1).Should().Be(new NumberValue(70));
        sheet.GetValue(6, 1).Should().Be(new NumberValue(60));
        sheet.ColumnFilterOwnedRows.Should().ContainKey(1u);
        sheet.ColumnFilterOwnedRows[1].Should().BeEquivalentTo([5u, 6u],
            "undo must restore the pre-sort ownership, not leave the permuted post-sort rows behind");
    }
}
