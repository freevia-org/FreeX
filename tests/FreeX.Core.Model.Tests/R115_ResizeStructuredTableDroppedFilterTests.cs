using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R115: ResizeStructuredTableCommand reconciles a shrinking table's own FilterColumns down to the
/// new (narrower) column span, but a column that carried an active filter criterion also needs its
/// sheet-wide row-hiding contribution released the moment it falls out of the table -- Excel scopes a
/// table's AutoFilter state to its CURRENT column set, and the column's dropdown (the only supported
/// UI to clear a filter) stops rendering for a column no longer inside the table's range, so without
/// this cleanup any row hidden solely by the dropped column's filter would stay hidden forever with no
/// UI path left to un-hide it. Covers both sheet-wide filter mechanisms a table column can drive:
/// <see cref="Sheet.ActiveValueFilterColumns"/> (a plain value-list AutoFilter criterion) and
/// <see cref="Sheet.ColumnFilterOwnedRows"/> (Top10/Above-Average/custom-condition/color filters).
/// </summary>
public sealed class R115_ResizeStructuredTableDroppedFilterTests
{
    private static (Workbook Workbook, Sheet Sheet, StructuredTableModel Table) SeedThreeColumnTable()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Status"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Priority"));

        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Open"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("High"));

        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Open"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new TextValue("Low"));

        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new TextValue("Closed"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new TextValue("Low"));

        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), BlankValue.Instance);
        sheet.SetCell(new CellAddress(sheet.Id, 5, 3), new TextValue("Low"));

        var table = new StructuredTableModel
        {
            Id = 7,
            Name = "Sales",
            DisplayName = "Sales",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3)),
            HasAutoFilter = true,
            Columns =
            {
                new StructuredTableColumnModel(1, "Region"),
                new StructuredTableColumnModel(2, "Status"),
                new StructuredTableColumnModel(3, "Priority")
            }
        };
        sheet.StructuredTables.Add(table);
        return (wb, sheet, table);
    }

    /// <summary>
    /// THE bug: a value-list filter on the column being dropped by the shrink must stop hiding rows
    /// the moment the resize commits -- while a surviving filter on a column that stays in the table
    /// must keep hiding whatever it still legitimately excludes. Before the fix, row 3 (hidden solely
    /// by the dropped Priority="High" filter) stayed hidden forever; rows 4/5 (also failing the
    /// surviving Status="Open" filter) must remain hidden throughout.
    /// </summary>
    [Fact]
    public void ResizeStructuredTableCommand_DroppedColumnValueFilter_UnhidesItsOwnRowsButKeepsSurvivingFilterRowsHidden()
    {
        var (wb, sheet, table) = SeedThreeColumnTable();
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(1, ["Open"]));
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(2, ["High"]));
        var ctx = new TestCommandContext(wb);

        // Simulate the table's dropdowns already having been applied (as the real UI path would do)
        // before the resize -- both criteria contribute to the current hidden-row set.
        new ApplyStructuredTableFiltersCommand(sheet.Id, table.Id).Apply(ctx).Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 4u, 5u]);

        var newRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2));
        var command = new ResizeStructuredTableCommand(sheet.Id, table.Id, newRange);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        var resized = sheet.StructuredTables.Should().ContainSingle().Subject;
        resized.FilterColumns.Should().ContainSingle().Which.ColumnId.Should().Be(1);

        // Row 3 only ever failed the dropped Priority filter -- it must reappear now that Priority is
        // no longer part of the table. Rows 4/5 still fail the SURVIVING Status="Open" filter and must
        // stay hidden.
        sheet.FilterHiddenRows.Should().BeEquivalentTo([4u, 5u]);

        command.Revert(ctx);

        sheet.StructuredTables.Should().ContainSingle().Which.Should().BeSameAs(table);
        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 4u, 5u]);
    }

    /// <summary>
    /// Sibling mechanism: a Top10/Above-Average/custom-condition-style filter on the dropped column
    /// never populates <see cref="Sheet.ActiveValueFilterColumns"/> -- it owns its hidden rows directly
    /// via <see cref="Sheet.ColumnFilterOwnedRows"/>. Dropping the column must relinquish exactly the
    /// rows THAT column's mechanism owns while leaving a different column's ownership (still inside
    /// the table) completely untouched.
    /// </summary>
    [Fact]
    public void ResizeStructuredTableCommand_DroppedColumnConditionFilter_ReleasesOwnedRowsButKeepsOtherColumnOwnership()
    {
        var (wb, sheet, table) = SeedThreeColumnTable();
        // A non-value-list criterion (Top10/condition/color) round-trips through NativeFilterXmls, not
        // Values -- ApplyStructuredTableFiltersCommand.BuildFilters deliberately skips these entries
        // and defers entirely to Sheet.ColumnFilterOwnedRows for their row-hiding, exactly like a real
        // Top-10 filter would.
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(1, [], NativeFilterXml: "<condition/>"));
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(2, [], NativeFilterXml: "<top10 val=\"1\"/>"));

        // Status (absolute col 2) owns row 3 hidden; Priority (absolute col 3, about to be dropped)
        // owns rows 4 and 5 hidden -- mirroring how TopBottomFilterCommand/FilterConditionCommand
        // record ownership and hide rows directly.
        sheet.ColumnFilterOwnedRows[2] = [3u];
        sheet.ColumnFilterOwnedRows[3] = [4u, 5u];
        sheet.FilterHiddenRows.UnionWith([3u, 4u, 5u]);

        var ctx = new TestCommandContext(wb);
        var newRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2));
        var command = new ResizeStructuredTableCommand(sheet.Id, table.Id, newRange);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();

        // Rows 4/5 were owned solely by the now-dropped Priority column's filter and must reappear;
        // row 3 is still owned by Status (which stays in the table) and must remain hidden.
        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u]);
        // ClearColumnOwnedRange empties the dropped column's owned-row set in place rather than
        // removing the dictionary key -- either way it no longer owns (or hides) any row.
        sheet.ColumnFilterOwnedRows.GetValueOrDefault(3u, []).Should().BeEmpty();
        sheet.ColumnFilterOwnedRows[2].Should().BeEquivalentTo([3u]);

        command.Revert(ctx);

        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 4u, 5u]);
        sheet.ColumnFilterOwnedRows[2].Should().BeEquivalentTo([3u]);
        sheet.ColumnFilterOwnedRows[3].Should().BeEquivalentTo([4u, 5u]);
    }

    /// <summary>
    /// No-regression sibling: when the resize does not drop any column that carries a FilterColumns
    /// criterion (here, growing the table), the whole filter-cleanup path must be a complete no-op --
    /// sheet.FilterHiddenRows, sheet.ActiveValueFilterColumns and sheet.ColumnFilterOwnedRows must be
    /// left byte-for-byte untouched, including rows hidden for reasons entirely unrelated to this
    /// table.
    /// </summary>
    [Fact]
    public void ResizeStructuredTableCommand_GrowWithNoDroppedFilterColumns_LeavesFilterStateUntouched()
    {
        var (wb, sheet, table) = SeedThreeColumnTable();
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(1, ["Open"]));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("Extra"));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 1), new TextValue("West"));

        var ctx = new TestCommandContext(wb);
        new ApplyStructuredTableFiltersCommand(sheet.Id, table.Id).Apply(ctx).Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEquivalentTo([4u, 5u]);

        // Unrelated sheet-wide filter bookkeeping that must survive completely untouched.
        sheet.ActiveValueFilterColumns[99] = ["Unrelated"];
        sheet.ColumnFilterOwnedRows[99] = [42u];
        sheet.FilterHiddenRows.Add(42u);

        var previousFilterHiddenRows = new HashSet<uint>(sheet.FilterHiddenRows);
        var previousActiveValueFilterColumns = sheet.ActiveValueFilterColumns.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        var previousColumnFilterOwnedRows = sheet.ColumnFilterOwnedRows.ToDictionary(kvp => kvp.Key, kvp => new HashSet<uint>(kvp.Value));

        // Grow the table both wider (add the new column) and taller (add the new row) -- no existing
        // FilterColumns entry falls out of range, so nothing should be released.
        var newRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 4));
        var command = new ResizeStructuredTableCommand(sheet.Id, table.Id, newRange);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.StructuredTables.Should().ContainSingle().Subject.FilterColumns.Should().ContainSingle().Which.ColumnId.Should().Be(1);
        sheet.FilterHiddenRows.Should().BeEquivalentTo(previousFilterHiddenRows);
        sheet.ActiveValueFilterColumns.Should().BeEquivalentTo(previousActiveValueFilterColumns);
        sheet.ColumnFilterOwnedRows.Keys.Should().BeEquivalentTo(previousColumnFilterOwnedRows.Keys);
        foreach (var (col, owned) in previousColumnFilterOwnedRows)
            sheet.ColumnFilterOwnedRows[col].Should().BeEquivalentTo(owned);

        command.Revert(ctx);

        sheet.FilterHiddenRows.Should().BeEquivalentTo(previousFilterHiddenRows);
    }
}
