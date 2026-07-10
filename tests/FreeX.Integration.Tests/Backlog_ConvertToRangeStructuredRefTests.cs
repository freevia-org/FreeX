using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// Backlog item "convert-to-range": Excel's real Convert-to-Range lowers every structured
/// reference into the converted table (elsewhere in the workbook, or inside the table's own
/// formulas) into the equivalent absolute A1 reference. Before the fix,
/// <see cref="ConvertStructuredTableToRangeCommand"/> only removed the table metadata, leaving
/// every structured-reference formula pointing at a table that no longer exists — evaluating to
/// #NAME? — instead of an equivalent A1 reference.
/// </summary>
public sealed class Backlog_convert_to_range_Tests
{
    private static StructuredTableModel BuildAmountTable(Sheet sheet)
    {
        // Table1 spans A1:C4 on Sheet1: header row + 3 data rows — Region, Amount, and a blank
        // "Double" calculated column (left empty here; individual tests fill it in to exercise
        // unqualified/this-row structured references from inside the table's own cells).
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Double"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3)),
            Columns =
            {
                new StructuredTableColumnModel(1, "Region"),
                new StructuredTableColumnModel(2, "Amount"),
                new StructuredTableColumnModel(3, "Double")
            }
        };
        sheet.StructuredTables.Add(table);
        return table;
    }

    [Fact]
    public void ConvertToRange_LowersCrossSheetStructuredReference_AndPreservesEvaluatedTotal()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var table = BuildAmountTable(sheet1);
        var sheet2 = wb.AddSheet("Sheet2");
        var formulaAddress = new CellAddress(sheet2.Id, 1, 2); // Sheet2!B1
        sheet2.SetFormula(formulaAddress, "SUM(Table1[Amount])");

        var recalc = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        recalc.RecalculateAllFormulas(wb);
        sheet2.GetValue(1, 2).Should().Be(new NumberValue(60), "pre-conversion sanity check");

        var ctx = new TestCommandContext(wb);
        var command = new ConvertStructuredTableToRangeCommand(sheet1.Id, table.Id);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet1.StructuredTables.Should().BeEmpty();

        var rewritten = sheet2.GetCell(formulaAddress)!.FormulaText;
        rewritten.Should().NotContain("Table1", "the structured reference must be lowered to A1 once the table is gone");
        rewritten.Should().Be("SUM(Sheet1!$B$2:$B$4)");

        recalc.RecalculateAllFormulas(wb);
        sheet2.GetValue(1, 2).Should().Be(new NumberValue(60), "the lowered A1 reference must still evaluate to the same total");

        // Undo must restore both the table and the original structured-reference formula text.
        command.Revert(ctx);
        sheet1.StructuredTables.Should().ContainSingle().Which.Should().BeSameAs(table);
        sheet2.GetCell(formulaAddress)!.FormulaText.Should().Be("SUM(Table1[Amount])");

        recalc.RecalculateAllFormulas(wb);
        sheet2.GetValue(1, 2).Should().Be(new NumberValue(60), "undo must restore the original evaluated total too");
    }

    [Fact]
    public void ConvertToRange_LowersUnqualifiedStructuredReference_InsideTablesOwnFormula()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var table = BuildAmountTable(sheet);

        // A calculated-column-style formula living inside the table itself, using a bare
        // (unqualified) current-row structured reference — e.g. "=[@Amount]*2" in the table's own
        // "Double" column (column C, part of the table's range).
        var innerFormulaAddress = new CellAddress(sheet.Id, 2, 3); // C2, inside the table's own range
        sheet.SetFormula(innerFormulaAddress, "[@Amount]*2");

        var ctx = new TestCommandContext(wb);
        var command = new ConvertStructuredTableToRangeCommand(sheet.Id, table.Id);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        var rewritten = sheet.GetCell(innerFormulaAddress)!.FormulaText;
        rewritten.Should().Be("$B$2*2");

        var recalc = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        recalc.RecalculateAllFormulas(wb);
        sheet.GetValue(2, 3).Should().Be(new NumberValue(20));
    }

    [Fact]
    public void ConvertToRange_LowersThisRowStructuredReference_ToSameSheetSingleCell()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var table = BuildAmountTable(sheet1);
        _ = wb.AddSheet("Sheet2"); // an unrelated sheet, present just to exercise the multi-sheet scan
        // A table-qualified "[#This Row],[Amount]" combined selector, written from inside the
        // table's own "Double" calculated column (column C, row 3 = the "South" data row).
        var thisRowAddress = new CellAddress(sheet1.Id, 3, 3);
        sheet1.SetFormula(thisRowAddress, "Table1[[#This Row],[Amount]]*2");

        var ctx = new TestCommandContext(wb);
        var command = new ConvertStructuredTableToRangeCommand(sheet1.Id, table.Id);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        var rewritten = sheet1.GetCell(thisRowAddress)!.FormulaText;
        rewritten.Should().Be("$B$3*2");

        var recalc = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        recalc.RecalculateAllFormulas(wb);
        sheet1.GetValue(3, 3).Should().Be(new NumberValue(40));
    }
}
