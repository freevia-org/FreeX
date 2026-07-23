using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class StructuredTableCommandTests
{
    // R78-io-table-listobject-5-2: hiding a table's totals row captures whatever is actually sitting
    // in the totals-row cell (ReconcileColumnFromTotalsCell) so re-showing it reproduces the user's
    // last edit. A manually-typed LITERAL scalar (a plain number here, not text and not a formula)
    // fell through every branch to the all-null return, so re-showing the totals row produced a
    // blank cell instead of restoring the number the user typed.
    [Fact]
    public void SetStructuredTableTotalsRowCommand_HideThenShow_PreservesManuallyTypedLiteralNumber()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTotalsTable(sheet);
        // "Orders" column's totals cell is directly overwritten with a bare literal number (100) --
        // no leading '=', not text, and not a recognized SUBTOTAL/custom formula.
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("Total"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(45));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 3), new NumberValue(100));
        var table = new StructuredTableModel
        {
            Id = 3,
            Name = "Sales",
            DisplayName = "Sales",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3)),
            TotalsRowShown = true,
            TotalsRowCount = 1,
            Columns =
            {
                new StructuredTableColumnModel(1, "Region", TotalsRowLabel: "Total"),
                new StructuredTableColumnModel(2, "Sales", TotalsRowFunction: "sum"),
                new StructuredTableColumnModel(3, "Orders")
            }
        };
        sheet.StructuredTables.Add(table);
        var ctx = new TestCommandContext(wb);

        var hide = new SetStructuredTableTotalsRowCommand(sheet.Id, table.Id, showTotalsRow: false);
        hide.Apply(ctx).Success.Should().BeTrue();

        var hidden = sheet.StructuredTables.Should().ContainSingle().Subject;
        // The fix under test: the literal 100 must be captured into the column's totals metadata
        // (round-tripped through the custom-formula slot as a trivial constant "formula"), not
        // silently discarded.
        hidden.Columns[2].TotalsRowFormula.Should().Be("100");
        hidden.Columns[2].TotalsRowFunction.Should().Be("custom");
        hidden.Columns[2].TotalsRowLabel.Should().BeNull();

        var show = new SetStructuredTableTotalsRowCommand(sheet.Id, hidden.Id, showTotalsRow: true);
        show.Apply(ctx).Success.Should().BeTrue();

        // Re-showing the totals row must reconstruct the original literal instead of leaving the
        // cell blank.
        sheet.GetCell(new CellAddress(sheet.Id, 5, 3))!.FormulaText.Should().Be("100",
            "the manually-typed literal must survive a hide/show round trip instead of being wiped");
    }

    // Sibling/regression case: the pre-existing branches (blank, recognized SUBTOTAL aggregate,
    // custom formula, and text label) must still round-trip exactly as before this fix.
    [Fact]
    public void SetStructuredTableTotalsRowCommand_HideThenShow_StillPreservesTextLabelAndSubtotalFunction()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTotalsTable(sheet);
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("Total"));
        // Real SUBTOTAL-backed totals cells (matching what RefreshStructuredTableTotalsCommand
        // itself writes for a built-in totalsRowFunction) -- not bare literals -- so the formula
        // branch of ReconcileColumnFromTotalsCell is what's actually exercised here.
        sheet.SetFormula(new CellAddress(sheet.Id, 5, 2), "SUBTOTAL(109,[Sales])");
        sheet.SetFormula(new CellAddress(sheet.Id, 5, 3), "SUBTOTAL(103,[Orders])");
        var table = new StructuredTableModel
        {
            Id = 3,
            Name = "Sales",
            DisplayName = "Sales",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3)),
            TotalsRowShown = true,
            TotalsRowCount = 1,
            Columns =
            {
                new StructuredTableColumnModel(1, "Region", TotalsRowLabel: "Total"),
                new StructuredTableColumnModel(2, "Sales", TotalsRowFunction: "sum"),
                new StructuredTableColumnModel(3, "Orders", TotalsRowFunction: "count")
            }
        };
        sheet.StructuredTables.Add(table);
        var ctx = new TestCommandContext(wb);

        var hide = new SetStructuredTableTotalsRowCommand(sheet.Id, table.Id, showTotalsRow: false);
        hide.Apply(ctx).Success.Should().BeTrue();
        var hidden = sheet.StructuredTables.Should().ContainSingle().Subject;
        hidden.Columns[0].TotalsRowLabel.Should().Be("Total");
        hidden.Columns[1].TotalsRowFunction.Should().Be("sum");
        hidden.Columns[2].TotalsRowFunction.Should().Be("count");

        var show = new SetStructuredTableTotalsRowCommand(sheet.Id, hidden.Id, showTotalsRow: true);
        show.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(5, 1).Should().Be(new TextValue("Total"));
        sheet.GetCell(new CellAddress(sheet.Id, 5, 2))!.FormulaText.Should().Be("SUBTOTAL(109,[Sales])");
        sheet.GetCell(new CellAddress(sheet.Id, 5, 3))!.FormulaText.Should().Be("SUBTOTAL(103,[Orders])");
    }
}
