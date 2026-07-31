using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R21-autofilter-sort-state-2: ApplyStructuredTableFiltersCommand.RemoveExistingFilterRows used to
/// blanket-clear every row in the table's data range from sheet.FilterHiddenRows before recomputing
/// value-list filters, with no regard for sheet.ColumnFilterOwnedRows. That silently un-hid rows a
/// Top-10/Above-Average/condition/color filter on a SIBLING column had hidden (e.g. via a table
/// slicer on a different column re-applying the table's value-list filters), while
/// ColumnFilterOwnedRows for that sibling column kept claiming the rows were still hidden --
/// desyncing the two mechanisms. This test reproduces that exact scenario: a Top-3 filter on the
/// "Score" column, followed by a value-list filter change on the "Region" column, and asserts the
/// Top-3-hidden rows stay hidden and ColumnFilterOwnedRows remains consistent with FilterHiddenRows.
/// </summary>
public sealed class R21_StructuredTableFilter_PreservesColumnOwnedRows
{
    [Fact]
    public void ApplyStructuredTableFiltersCommand_DoesNotDiscardSiblingColumnOwnedTopBottomFilter()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        // Header row 1, data rows 2-6. Column A = Region (text), Column B = Score (numeric).
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Score"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(90));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(80));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(70));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 2), new NumberValue(60));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 2));
        var table = new StructuredTableModel
        {
            Id = 42,
            Name = "Sales",
            DisplayName = "Sales",
            Range = range,
            HasAutoFilter = true,
            Columns =
            {
                new StructuredTableColumnModel(1, "Region"),
                new StructuredTableColumnModel(2, "Score")
            }
        };
        sheet.StructuredTables.Add(table);

        // Apply a Top-3 filter on the Score column (offset 1): keeps rows 2,3,4 (100,90,80),
        // hides rows 5,6 (70,60) and records the ownership in sheet.ColumnFilterOwnedRows[colB].
        var topBottom = new TopBottomFilterCommand(sheet.Id, range, filterColOffset: 1, count: 3, top: true);
        topBottom.Apply(ctx).Success.Should().BeTrue();

        var scoreCol = range.Start.Col + 1;
        sheet.FilterHiddenRows.Should().BeEquivalentTo([5u, 6u]);
        sheet.ColumnFilterOwnedRows[scoreCol].Should().BeEquivalentTo([5u, 6u]);

        // R106-commands-autofilter-table-sync-1: TopBottomFilterCommand.Apply now mirrors its
        // criterion into the table's own FilterColumns model too (this test's Score-column Top-3
        // filter is applied against the table's own Range, so it matches), the same copy-on-write
        // way FilterCommand.ApplyToStructuredTableIfMatched already replaces
        // sheet.StructuredTables[i] with a new StructuredTableModel instance rather than mutating
        // the original in place (StructuredTableModel's properties are init-only) -- re-fetch the
        // live instance rather than mutating the now-stale local `table` reference captured before
        // that Apply call.
        table = sheet.StructuredTables.Single(t => t.Id == table.Id);

        // Now a table slicer/dropdown changes the Region column's value-list filter to "North" and
        // re-applies the table's structured filters (mirrors PivotTableSlicerCommands.ApplyTableSlicer).
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(0, ["North"]));
        var applyFilters = new ApplyStructuredTableFiltersCommand(sheet.Id, table.Id);
        applyFilters.Apply(ctx).Success.Should().BeTrue();

        // Row 3 (South) and row 5 (South) fail the Region="North" filter -> hidden.
        // Row 6 (North) passes the Region filter but is still owned/hidden by the Top-3 Score
        // filter -> must remain hidden, not silently un-hidden.
        // Rows 2 and 4 (North, in the top 3 scores) remain visible.
        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 5u, 6u]);

        // ColumnFilterOwnedRows for the Score column must stay in sync with FilterHiddenRows --
        // every row it claims to own must actually still be hidden.
        sheet.ColumnFilterOwnedRows[scoreCol].Should().BeEquivalentTo([5u, 6u]);
        foreach (var ownedRow in sheet.ColumnFilterOwnedRows[scoreCol])
            sheet.FilterHiddenRows.Should().Contain(ownedRow);

        applyFilters.Revert(ctx);

        // Reverting the structured-table filter application must restore the Top-3 filter's rows
        // exactly as they were (5 and 6 hidden, ownership untouched).
        sheet.FilterHiddenRows.Should().BeEquivalentTo([5u, 6u]);
        sheet.ColumnFilterOwnedRows[scoreCol].Should().BeEquivalentTo([5u, 6u]);
    }
}
