using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

// freex-table-structured F1: FreeX has no dedicated "rename table column" command -- an ordinary
// EditCellsCommand edit to a header cell is enough, and nothing validates the new text against the
// table's other live header texts (unlike real Excel, which refuses to create a duplicate column
// name). So a header rename can leave two columns sharing one live header text.
// RefreshStructuredTableTotalsCommand.ResolveTotalsCell used to always build an unqualified
// SUBTOTAL(n,[<live header text>]) selector; StructuredReferenceResolver.FindColumnIndex resolves
// such a selector to the FIRST column with that live header text, so the LATER (renamed) column's
// own totals formula silently resolved back to the EARLIER column's data.
public sealed class R151_StructuredTableTotalsDuplicateHeaderTests
{
    private static void SeedTotalsTable(Sheet sheet)
    {
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("South"));
    }

    private static StructuredTableModel BuildSalesTable(Sheet sheet) => new()
    {
        Id = 5,
        Name = "SalesTable",
        DisplayName = "SalesTable",
        Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
        TotalsRowShown = true,
        Columns =
        {
            new StructuredTableColumnModel(1, "Sales", TotalsRowFunction: "sum"),
            new StructuredTableColumnModel(2, "Region", TotalsRowFunction: "count")
        }
    };

    [Fact]
    public void RefreshStructuredTableTotalsCommand_ColumnRenamedToDuplicateEarlierHeader_ComputesOwnColumnsAggregate()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTotalsTable(sheet);
        var table = BuildSalesTable(sheet);
        sheet.StructuredTables.Add(table);

        // The user gesture: an ordinary header-cell edit retypes column B's header ("Region") to
        // duplicate column A's live header ("Sales"). FreeX has no rename command and no
        // duplicate-name guard, so this ordinary edit succeeds silently.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));

        var ctx = new TestCommandContext(wb);
        var outcome = new RefreshStructuredTableTotalsCommand(sheet.Id, table.Id).Apply(ctx);

        outcome.Success.Should().BeTrue();

        // Column A's totals formula is unaffected: it is still the unambiguous, canonical owner of
        // the "Sales" text (its own stored name still equals its live header).
        sheet.GetCell(4, 1)!.FormulaText.Should().Be("SUBTOTAL(109,[Sales])");

        // Column B's totals formula must no longer be an ambiguous [Sales] selector -- that would
        // resolve back to column A (FindColumnIndex's first-match-wins rule) and silently report
        // column A's SUM instead of column B's own COUNT. It must address column B's own data body
        // directly instead.
        var columnBFormula = sheet.GetCell(4, 2)!.FormulaText;
        columnBFormula.Should().NotBe("SUBTOTAL(103,[Sales])");
        columnBFormula.Should().Be("SUBTOTAL(103,B2:B3)");

        // The evaluated aggregate must reflect column B's OWN data (SUBTOTAL(103,...) is Excel's
        // "count" totals-row function, i.e. COUNTA -- 2 non-blank text rows -> 2) rather than being
        // coerced into equalling column A's SUM (30).
        var evaluator = new FormulaEvaluator();
        var columnAFormula = sheet.GetCell(4, 1)!.FormulaText!;
        var columnAValue = evaluator.Evaluate(columnAFormula, sheet, wb, new CellAddress(sheet.Id, 4, 1));
        var columnBValue = evaluator.Evaluate(columnBFormula, sheet, wb, new CellAddress(sheet.Id, 4, 2));

        columnAValue.Should().Be(new NumberValue(30));
        columnBValue.Should().Be(new NumberValue(2));
        columnBValue.Should().NotBe(columnAValue);
    }

    [Fact]
    public void RefreshStructuredTableTotalsCommand_NoDuplicateHeaders_StillUsesStructuredReferenceAsBefore()
    {
        // No-regression sibling: when every column's live header text is unique (the common,
        // non-colliding case), the totals formula is still the ordinary structured reference --
        // unaffected by the F1 fix, which only engages when a duplicate live header is detected.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTotalsTable(sheet);
        var table = BuildSalesTable(sheet);
        sheet.StructuredTables.Add(table);

        var ctx = new TestCommandContext(wb);
        var outcome = new RefreshStructuredTableTotalsCommand(sheet.Id, table.Id).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetCell(4, 1)!.FormulaText.Should().Be("SUBTOTAL(109,[Sales])");
        sheet.GetCell(4, 2)!.FormulaText.Should().Be("SUBTOTAL(103,[Region])");
    }
}
