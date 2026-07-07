using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

// Regression coverage for cleanup batch group 4.
public sealed class FreeXCleanupB4Tests
{
    private static StructuredTableModel CreateTotalsTable(Sheet sheet, bool totalsRowShown = true)
    {
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Sales",
            DisplayName = "Sales",
            Range = totalsRowShown
                ? new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2))
                : new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            TotalsRowShown = totalsRowShown,
            Columns =
            {
                new StructuredTableColumnModel(1, "Region"),
                new StructuredTableColumnModel(2, "Amount", TotalsRowFunction: "sum")
            }
        };

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(5));
        if (totalsRowShown)
        {
            sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("Total"));
            sheet.SetCell(new CellAddress(sheet.Id, 5, 2), Cell.FromFormula("SUBTOTAL(109,[Amount])"));
        }

        return table;
    }

    // P105: typing into the row directly below a shown totals row must never auto-expand the
    // table — Excel suppresses the auto-expand gesture entirely there (the user must use the
    // Resize Table handle/dialog). Before the fix, TryGetAutoExpandRange treated
    // range.End.Row (the totals row) + 1 as a valid downward-expand gesture, which grew the
    // table, relocated the totals row down to row 6, and then RefreshStructuredTableTotalsCommand
    // overwrote the user's freshly typed value with a regenerated totals cell.
    [Fact]
    public void EditCellsCommand_TypingBelowShownTotalsRow_DoesNotAutoExpandTableOrDestroyTypedValue()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var table = CreateTotalsTable(sheet);
        sheet.StructuredTables.Add(table);
        var ctx = new TestCommandContext(wb);

        var typedAddress = new CellAddress(sheet.Id, 6, 2);
        var command = EditCellsCommand.ForValue(sheet.Id, typedAddress, new NumberValue(7));

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();

        // The table must not have grown...
        var unchangedTable = sheet.StructuredTables.Single(candidate => candidate.Id == table.Id);
        unchangedTable.Range.Should().Be(table.Range);
        unchangedTable.TotalsRowShown.Should().BeTrue();

        // ...the totals row must still be exactly where it was, still holding its live formula...
        sheet.GetCell(5, 2)!.FormulaText.Should().Be("SUBTOTAL(109,[Amount])");

        // ...and the user's typed value in row 6 must survive untouched.
        sheet.GetValue(6, 2).Should().Be(new NumberValue(7));
    }

    // P105 (auto-expand helper unit coverage): TryGetAutoExpandRange must return null for the
    // row directly below a shown totals row, but must still recognize the ordinary downward
    // auto-expand gesture once the totals row is hidden.
    [Fact]
    public void TryGetAutoExpandRange_RowBelowShownTotalsRow_ReturnsNull()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var table = CreateTotalsTable(sheet);
        sheet.StructuredTables.Add(table);

        var result = StructuredTableDesignCommandHelpers.TryGetAutoExpandRange(
            sheet, table, new CellAddress(sheet.Id, 6, 2));

        result.Should().BeNull();
    }

    [Fact]
    public void TryGetAutoExpandRange_RowBelowTableWithoutTotalsRow_StillExpands()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var table = CreateTotalsTable(sheet, totalsRowShown: false);

        var result = StructuredTableDesignCommandHelpers.TryGetAutoExpandRange(
            sheet, table, new CellAddress(sheet.Id, 5, 2));

        result.Should().Be(new GridRange(table.Range.Start, new CellAddress(sheet.Id, 5, 2)));
    }

    // P106: a table's built-in totalsRowFunction aggregate must be materialized as a live
    // =SUBTOTAL(10x,[Column]) formula, not a precomputed static number — otherwise growing the
    // table (auto-expand or Resize Table) freezes the total at a stale value computed before the
    // newly-grown calculated-column formula cells have even been recalculated, and the saved
    // XLSX round-trips as a dead constant instead of a live Excel total.
    [Fact]
    public void ResizeStructuredTableCommand_GrowingPastShownTotalsRow_RegeneratesLiveSubtotalFormula()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var table = CreateTotalsTable(sheet);
        sheet.StructuredTables.Add(table);
        var ctx = new TestCommandContext(wb);

        // Grow the table by one row: Excel turns the old totals row (5) into an ordinary data
        // row and moves the totals row down to the new last row (6).
        var newRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 2));
        var command = new ResizeStructuredTableCommand(sheet.Id, table.Id, newRange);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        var resizedTable = sheet.StructuredTables.Single(candidate => candidate.Id == table.Id);
        resizedTable.Range.End.Row.Should().Be(6);
        resizedTable.TotalsRowShown.Should().BeTrue();

        // The former totals row (row 5) was relocated: it must no longer hold "Total"/the old
        // formula, since it is now an ordinary (blank) data-body row.
        sheet.GetValue(5, 1).Should().Be(BlankValue.Instance);
        sheet.GetValue(5, 2).Should().Be(BlankValue.Instance);

        // The new totals row (row 6) holds a live SUBTOTAL formula — not a stale precomputed sum
        // computed before recalc — so it stays correct as the data body evolves.
        sheet.GetCell(6, 2)!.FormulaText.Should().Be("SUBTOTAL(109,[Amount])");

        command.Revert(ctx);

        var revertedTable = sheet.StructuredTables.Single(candidate => candidate.Id == table.Id);
        revertedTable.Range.Should().Be(table.Range);
        sheet.GetCell(5, 2)!.FormulaText.Should().Be("SUBTOTAL(109,[Amount])");
    }
}
