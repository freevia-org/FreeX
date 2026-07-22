using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class StructuredTableCommandTests
{
    // R74-io-tables-4-1: hiding a shown totals row makes SetStructuredTableTotalsRowCommand capture
    // whatever SUBTOTAL(n,[Column]) aggregate is actually sitting in the totals-row cell right now
    // (CaptureManualTotalsEdits -> ReconcileColumnFromTotalsCell) and re-map the SUBTOTAL number back
    // to a totalsRowFunction token via SubtotalNumberToTotalsRowFunction. That map must produce the
    // exact ECMA-376 18.3.1.90 camelCase tokens ("countNums", "stdDev", ...) because
    // XlsxStructuredTableSchemaNormalizer.ValidTotalsRowFunctions is a case-sensitive set -- a
    // lowercase "countnums"/"stddev" is not a recognized token and gets stripped entirely on save,
    // losing the column's totals-row metadata (Excel then shows "None").
    private static Sheet SeedFourColumnTotalsTableSheet(Workbook wb)
    {
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Orders"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("Quantity"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 4), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(15));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 4), new NumberValue(7));
        return sheet;
    }

    [Fact]
    public void SetStructuredTableTotalsRowCommand_Hide_ReconcilesCountNumsAndStdDevWithEcma376CamelCase()
    {
        var wb = new Workbook("test");
        var sheet = SeedFourColumnTotalsTableSheet(wb);
        // The totals row still holds the live SUBTOTAL formulas that were showing before hide -- this
        // is what CaptureManualTotalsEdits reads back while reconciling.
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Total"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), Cell.FromFormula("SUBTOTAL(102,[Sales])"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), Cell.FromFormula("SUBTOTAL(107,[Orders])"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), Cell.FromFormula("SUBTOTAL(109,[Quantity])"));
        var table = new StructuredTableModel
        {
            Id = 3,
            Name = "Sales",
            DisplayName = "Sales",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 4)),
            TotalsRowShown = true,
            TotalsRowCount = 1,
            Columns =
            {
                new StructuredTableColumnModel(1, "Region", TotalsRowLabel: "Total"),
                new StructuredTableColumnModel(2, "Sales", TotalsRowFunction: "countNums"),
                new StructuredTableColumnModel(3, "Orders", TotalsRowFunction: "stdDev"),
                new StructuredTableColumnModel(4, "Quantity", TotalsRowFunction: "sum")
            }
        };
        sheet.StructuredTables.Add(table);
        var ctx = new TestCommandContext(wb);
        var command = new SetStructuredTableTotalsRowCommand(sheet.Id, table.Id, showTotalsRow: false);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        var configured = sheet.StructuredTables.Should().ContainSingle().Subject;
        // R74-io-tables-4-1: SUBTOTAL(102,...) / SUBTOTAL(107,...) must reconcile back to the exact
        // ECMA-376 camelCase tokens, not the lowercase "countnums"/"stddev" the SUBTOTAL-number
        // reverse map previously produced.
        configured.Columns[1].TotalsRowFunction.Should().Be("countNums");
        configured.Columns[2].TotalsRowFunction.Should().Be("stdDev");
        configured.Columns[3].TotalsRowFunction.Should().Be("sum");

        command.Revert(ctx);

        sheet.StructuredTables.Should().ContainSingle().Which.Should().BeSameAs(table);
    }

    [Fact]
    public void SetStructuredTableTotalsRowCommand_Hide_ReconcilesOtherSubtotalFunctionsWithCorrectCase()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Orders"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("Quantity"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 4), new NumberValue(5));
        // Totals row: average / max / min / var, one no-regression sibling per remaining SUBTOTAL
        // aggregate that was already correctly-cased before this fix.
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromFormula("SUBTOTAL(101,[Region])"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), Cell.FromFormula("SUBTOTAL(104,[Sales])"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), Cell.FromFormula("SUBTOTAL(105,[Orders])"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 4), Cell.FromFormula("SUBTOTAL(110,[Quantity])"));
        var table = new StructuredTableModel
        {
            Id = 3,
            Name = "Sales",
            DisplayName = "Sales",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 4)),
            TotalsRowShown = true,
            TotalsRowCount = 1,
            Columns =
            {
                new StructuredTableColumnModel(1, "Region", TotalsRowFunction: "average"),
                new StructuredTableColumnModel(2, "Sales", TotalsRowFunction: "max"),
                new StructuredTableColumnModel(3, "Orders", TotalsRowFunction: "min"),
                new StructuredTableColumnModel(4, "Quantity", TotalsRowFunction: "var")
            }
        };
        sheet.StructuredTables.Add(table);
        var ctx = new TestCommandContext(wb);
        var command = new SetStructuredTableTotalsRowCommand(sheet.Id, table.Id, showTotalsRow: false);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        var configured = sheet.StructuredTables.Should().ContainSingle().Subject;
        configured.Columns[0].TotalsRowFunction.Should().Be("average");
        configured.Columns[1].TotalsRowFunction.Should().Be("max");
        configured.Columns[2].TotalsRowFunction.Should().Be("min");
        configured.Columns[3].TotalsRowFunction.Should().Be("var");
    }
}
