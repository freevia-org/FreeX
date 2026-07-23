using System.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R78-io-table-listobject-5-1: a column insert/delete inside a ListObject must rewrite the cell
/// references INSIDE any surviving column's own CalculatedColumnFormula/TotalsRowFormula anchor
/// text, the same way RowColumnShiftHelpers.RewriteAllFormulas already rewrites ordinary live
/// sheet-cell formulas -- otherwise that metadata keeps pointing at the pre-shift column and later
/// corrupts auto-filled rows (FillGrownCalculatedColumns) and the persisted XLSX
/// (calculatedColumnFormula/totalsRowFormula).
/// </summary>
public sealed class R78_TableColumnFormulaShiftTests
{
    private static (Workbook Workbook, Sheet Sheet, ICommandContext Context) Setup()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet, new TestCommandContext(workbook));
    }

    private static StructuredTableModel LiveTable(Sheet sheet, int tableId) =>
        sheet.StructuredTables.Single(t => t.Id == tableId);

    // Table1 A1:C4 (header row 1; data rows 2-4): A=Category, B=Price, C=Total (a calculated column
    // whose anchor formula "A2*B2" is stored on column C's CalculatedColumnFormula, matching every
    // live cell C2:C4). Inserting a blank column before B (Price) pushes Price to C and Total to D --
    // the live cell formulas are already correctly rewritten to "A2*C2" etc. by RewriteAllFormulas,
    // but before this fix the surviving Total column's CalculatedColumnFormula metadata stayed the
    // stale "A2*B2", now referencing the blank inserted column instead of the real (shifted) Price
    // column.
    [Fact]
    public void InsertColumns_RewritesSurvivingCalculatedColumnFormulaForShiftedColumnReference()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Price"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Total"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(3));
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 3), "A2*B2");
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(4));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(5));
        sheet.SetFormula(new CellAddress(sheet.Id, 3, 3), "A3*B3");
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(6));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(7));
        sheet.SetFormula(new CellAddress(sheet.Id, 4, 3), "A4*B4");

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3)),
            HeaderRowCount = 1,
            Columns =
            {
                new StructuredTableColumnModel(1, "Category"),
                new StructuredTableColumnModel(2, "Price"),
                new StructuredTableColumnModel(3, "Total", CalculatedColumnFormula: "A2*B2")
            }
        };
        sheet.StructuredTables.Add(table);

        var command = new InsertColumnsCommand(sheet.Id, beforeCol: 2, count: 1);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue();

        var reconciled = LiveTable(sheet, 1);
        reconciled.Range.Should().Be(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 4)));
        reconciled.Columns.Should().HaveCount(4);

        // The fix under test: the surviving Total column (now the table's 4th column) must have its
        // CalculatedColumnFormula rewritten in lockstep with the column insert, matching the live
        // cell formula that RewriteAllFormulas already produces.
        reconciled.Columns[3].Name.Should().Be("Total");
        reconciled.Columns[3].CalculatedColumnFormula.Should().Be("A2*C2",
            "the calculated-column anchor must follow the same column shift as the live sheet-cell formula");
        sheet.GetCell(new CellAddress(sheet.Id, 2, 4))!.FormulaText.Should().Be("A2*C2",
            "sanity check: the live cell formula this anchor must match");

        // Undo must restore the original (pre-insert) stale-but-correct table wholesale.
        command.Revert(ctx);
        var reverted = LiveTable(sheet, 1);
        reverted.Columns[2].CalculatedColumnFormula.Should().Be("A2*B2");
    }

    // Sibling/regression case: TotalsRowFormula must be rewritten the same way as
    // CalculatedColumnFormula, on the DELETE-columns path too, while a column with no formula
    // metadata at all is left completely unaffected.
    [Fact]
    public void DeleteColumns_RewritesSurvivingTotalsRowFormulaForShiftedColumnReferenceAndLeavesPlainColumnsUntouched()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Extra"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Price"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("Total"));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 4)),
            HeaderRowCount = 1,
            Columns =
            {
                new StructuredTableColumnModel(1, "Extra"),
                new StructuredTableColumnModel(2, "Category"),
                new StructuredTableColumnModel(3, "Price"),
                new StructuredTableColumnModel(
                    4, "Total",
                    TotalsRowFunction: "custom",
                    TotalsRowFormula: "B2+C2")
            }
        };
        sheet.StructuredTables.Add(table);

        var command = new DeleteColumnsCommand(sheet.Id, startCol: 1, count: 1);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue();

        var reconciled = LiveTable(sheet, 1);
        reconciled.Range.Should().Be(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3)));
        reconciled.Columns.Should().HaveCount(3);

        // Category (was col B) and Price (was col C) shifted left one column but carry no formula
        // metadata -- they must remain completely unaffected by the fix.
        reconciled.Columns[0].Name.Should().Be("Category");
        reconciled.Columns[0].CalculatedColumnFormula.Should().BeNull();
        reconciled.Columns[1].Name.Should().Be("Price");
        reconciled.Columns[1].CalculatedColumnFormula.Should().BeNull();

        // The fix under test: Total's TotalsRowFormula ("B2+C2") must shift left in lockstep with
        // the column delete, becoming "A2+B2".
        reconciled.Columns[2].Name.Should().Be("Total");
        reconciled.Columns[2].TotalsRowFormula.Should().Be("A2+B2",
            "the totals-row custom formula must follow the same column shift as an ordinary live formula");
    }
}
