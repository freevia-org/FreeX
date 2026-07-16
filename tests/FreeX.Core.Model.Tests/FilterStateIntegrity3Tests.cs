using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression coverage for review-3 findings H1, H18, and H40: keeping
/// <see cref="Sheet.ValueFilterHiddenRows"/> in lockstep with <see cref="Sheet.FilterHiddenRows"/>
/// across row-content-permuting commands (Sort, Remove Duplicates), and keeping a structured table's
/// <see cref="StructuredTableModel.FilterColumns"/> in sync with an interactively-applied value filter
/// so it survives an xlsx save/reload.
/// </summary>
public sealed class FilterStateIntegrity3Tests
{
    // ── H1 / R45-commands-sort-filter-interaction-3-1: a row ValueFilterHiddenRows/FilterHiddenRows
    // is hiding must never be moved by Sort at all — real Excel's Sort documentation states hidden
    // rows in a filtered range are not sorted. These scenarios were originally written to pin the
    // (buggy) "hidden flag follows the row's relocated data" contract; they now assert the
    // corrected contract — the hidden rows stay exactly where they were, and only the rows that
    // were actually visible get reordered among themselves.

    [Fact]
    public void Sort_LeavesValueFilterHiddenRowsPinnedInPlaceAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        // A1:B6 — a value-list filter on column A hides rows 3 and 5 (matching the H1 scenario).
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new NumberValue(40));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 1), new NumberValue(5));
        sheet.ActiveValueFilterColumns[1] = ["30", "20", "5"];
        sheet.FilterHiddenRows.UnionWith([3u, 5u]);
        sheet.ValueFilterHiddenRows.UnionWith([3u, 5u]);

        var range = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 6, 2));
        var command = new SortCommand(sheet.Id, range, sortByColOffset: 0, ascending: true);
        command.Apply(ctx).Success.Should().BeTrue();

        // Rows 3 (10) and 5 (40) are filter-hidden and must stay exactly where they are. Only the
        // three VISIBLE rows (30@r2, 20@r4, 5@r6) are reordered among their own three slots,
        // ascending: 5(r2), 20(r4), 30(r6).
        sheet.GetValue(2, 1).Should().Be(new NumberValue(5));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(10)); // hidden — untouched
        sheet.GetValue(4, 1).Should().Be(new NumberValue(20));
        sheet.GetValue(5, 1).Should().Be(new NumberValue(40)); // hidden — untouched
        sheet.GetValue(6, 1).Should().Be(new NumberValue(30));
        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 5u]);
        // ValueFilterHiddenRows must likewise stay put — nothing to permute since the hidden rows
        // were excluded from the sort entirely.
        sheet.ValueFilterHiddenRows.Should().BeEquivalentTo([3u, 5u]);

        command.Revert(ctx);

        sheet.GetValue(2, 1).Should().Be(new NumberValue(30));
        sheet.GetValue(4, 1).Should().Be(new NumberValue(20));
        sheet.GetValue(6, 1).Should().Be(new NumberValue(5));
        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 5u]);
        sheet.ValueFilterHiddenRows.Should().BeEquivalentTo([3u, 5u]);
    }

    [Fact]
    public void Sort_ThenWideningValueFilter_UnhidesRowThatNowPasses()
    {
        // A filter-hidden row must stay at its own physical row through a Sort (Excel never moves
        // it), so widening the value filter afterward to allow that row's value must unhide it at
        // that same, never-changed physical row.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new NumberValue(40));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 1), new NumberValue(5));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 1));
        // Value filter on column A allows everything except 10 and 40 — hides rows 3 and 5.
        new FilterCommand(sheet.Id, range, filterColOffset: 0, ["30", "20", "5"])
            .Apply(ctx).Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 5u]);

        var dataRange = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 6, 1));
        new SortCommand(sheet.Id, dataRange, sortByColOffset: 0, ascending: true)
            .Apply(ctx).Success.Should().BeTrue();
        // Rows 3 (10) and 5 (40) are filter-hidden and stay exactly where they were — Sort never
        // moves them.
        sheet.GetValue(3, 1).Should().Be(new NumberValue(10));
        sheet.GetValue(5, 1).Should().Be(new NumberValue(40));
        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 5u]);

        // Widen the filter to also allow 40 — the row holding 40 (physical row 5, unchanged by the
        // sort) must become visible, leaving only row 3 (value 10, still excluded) hidden.
        new FilterCommand(sheet.Id, range, filterColOffset: 0, ["30", "20", "5", "40"])
            .Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u]);
    }

    // ── H40: RemoveDuplicateRowsCommand must compact FilterHiddenRows/ValueFilterHiddenRows too ────

    [Fact]
    public void RemoveDuplicateRows_CompactsFilterHiddenRowsAlongsideSurvivingRowContentAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        // Column A: X, X (dup), Y, Z. A value filter allows only "Y", hiding rows 2,3,5 (all but Y at row 4).
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("X"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("X"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Y"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("Z"));
        sheet.ActiveValueFilterColumns[1] = ["Y"];
        sheet.FilterHiddenRows.UnionWith([2u, 3u, 5u]);
        sheet.ValueFilterHiddenRows.UnionWith([2u, 3u, 5u]);

        var range = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 5, 1));
        var command = new RemoveDuplicateRowsCommand(sheet.Id, range);
        command.Apply(ctx).Success.Should().BeTrue();

        // Surviving rows compact upward: X(r2), Y(r3), Z(r4); row 5 is vacated/cleared.
        sheet.GetValue(2, 1).Should().Be(new TextValue("X"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("Y"));
        sheet.GetValue(4, 1).Should().Be(new TextValue("Z"));

        // Row 3 now holds "Y" (should be visible — the value filter allows it), and row 4 now holds
        // "Z" (should be hidden). The hidden-row bookkeeping must have moved with the content:
        sheet.FilterHiddenRows.Should().NotContain(3u);
        sheet.ValueFilterHiddenRows.Should().NotContain(3u);
        sheet.FilterHiddenRows.Should().Contain(2u); // X still fails the filter
        sheet.FilterHiddenRows.Should().Contain(4u); // Z (relocated) still fails the filter

        command.Revert(ctx);

        sheet.FilterHiddenRows.Should().BeEquivalentTo([2u, 3u, 5u]);
        sheet.ValueFilterHiddenRows.Should().BeEquivalentTo([2u, 3u, 5u]);
    }

    // ── H18: interactive value filtering on a structured table must sync table.FilterColumns ──────

    [Fact]
    public void FilterCommand_OnStructuredTableRange_SyncsTableFilterColumnsAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("East"));

        var tableRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = tableRange,
            HasAutoFilter = true
        };
        table.Columns.Add(new StructuredTableColumnModel(0, "Region"));
        sheet.StructuredTables.Add(table);

        // Simulate the table's column-header filter dropdown (which resolves to the table's full
        // range) applying a value-list filter.
        var command = new FilterCommand(sheet.Id, tableRange, filterColOffset: 0, ["East"]);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u]);

        // The table model itself (what XlsxStructuredTableWriter serializes) must now carry the
        // filter, or it is silently lost on save/reload.
        var updatedTable = sheet.StructuredTables.Single(t => t.Id == 1);
        updatedTable.FilterColumns.Should().ContainSingle();
        updatedTable.FilterColumns[0].ColumnId.Should().Be(0);
        updatedTable.FilterColumns[0].Values.Should().BeEquivalentTo(["East"]);

        command.Revert(ctx);

        sheet.FilterHiddenRows.Should().BeEmpty();
        var revertedTable = sheet.StructuredTables.Single(t => t.Id == 1);
        revertedTable.FilterColumns.Should().BeEmpty();
    }

    [Fact]
    public void FilterCommand_ClearingTableFilter_RemovesTableFilterColumnsEntry()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));

        var tableRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1));
        var table = new StructuredTableModel
        {
            Id = 7,
            Name = "Table7",
            DisplayName = "Table7",
            Range = tableRange,
            HasAutoFilter = true
        };
        table.Columns.Add(new StructuredTableColumnModel(0, "Region"));
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(0, ["East"]));
        sheet.StructuredTables.Add(table);
        sheet.ActiveValueFilterColumns[1] = ["East"];
        sheet.FilterHiddenRows.Add(3);
        sheet.ValueFilterHiddenRows.Add(3);

        var command = new FilterCommand(sheet.Id, tableRange, filterColOffset: 0, allowedValues: []);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().BeEmpty();
        var updatedTable = sheet.StructuredTables.Single(t => t.Id == 7);
        updatedTable.FilterColumns.Should().BeEmpty();

        command.Revert(ctx);

        var revertedTable = sheet.StructuredTables.Single(t => t.Id == 7);
        revertedTable.FilterColumns.Should().ContainSingle();
        revertedTable.FilterColumns[0].Values.Should().BeEquivalentTo(["East"]);
    }
}
