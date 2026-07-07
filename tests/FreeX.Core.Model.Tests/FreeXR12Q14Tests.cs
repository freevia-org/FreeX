using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Round-12 fix bucket Q14: applying a structured-table filter interactively must not hide the
/// table's totals row, matching Excel and matching FreeX's own load-time filter materialization
/// path (XlsxStructuredTableModelMapper.MaterializeFilters), which already excludes it.
/// </summary>
public sealed class FreeXR12Q14Tests
{
    [Fact]
    public void R12_xlsx_tables_2_ApplyStructuredTableFiltersCommand_DoesNotHideTotalsRow()
    {
        // Table1 = A1:C4 (header + 2 data rows + totals row) with TotalsRowShown = true.
        // Row 4 (the totals row) holds SUBTOTAL-style aggregates / a "Total" label, and must
        // never be added to FilterHiddenRows regardless of whether its cell text/value happens
        // to match the filter's allowed-value set.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        // Totals row: label text is "East" so that, absent the fix, it would satisfy the filter
        // and NOT get caught accidentally by a "doesn't match" heuristic -- this proves the
        // command must skip the totals row structurally, not just by content mismatch.
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            HasAutoFilter = true,
            TotalsRowShown = true,
            TotalsRowCount = 1,
            Columns =
            {
                new StructuredTableColumnModel(1, "Region", TotalsRowLabel: "Total"),
                new StructuredTableColumnModel(2, "Sales", TotalsRowFunction: "sum")
            }
        };
        // Filter column A to keep only "West" -- the totals row's own "East" label would fail
        // this filter and, pre-fix, get hidden.
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(0, ["West"]));
        sheet.StructuredTables.Add(table);

        var ctx = new TestCommandContext(wb);
        var command = new ApplyStructuredTableFiltersCommand(sheet.Id, table.Id);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().Contain(2u); // "East" data row does not match "West" filter.
        sheet.FilterHiddenRows.Should().NotContain(3u); // "West" data row matches.
        sheet.FilterHiddenRows.Should().NotContain(4u); // Totals row must stay visible like Excel.

        command.Revert(ctx);

        sheet.FilterHiddenRows.Should().BeEmpty();
    }
}
