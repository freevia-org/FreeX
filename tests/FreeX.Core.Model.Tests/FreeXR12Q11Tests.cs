using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Round-12 bucket Q11 regression tests.
/// </summary>
public sealed class FreeXR12Q11Tests
{
    // R12-sort-filter-1: a condition/average/top-bottom/color filter on one column must not un-hide
    // a row that another column's active value filter already hid — Excel ANDs AutoFilter criteria
    // across every active column (a row is hidden if it fails ANY active column's filter).
    [Fact]
    public void FilterConditionCommand_DoesNotUnhideRowHiddenByAnotherColumnsValueFilter()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        // Column A (offset 0): "X" everywhere except row 3, which is "Y".
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("ColA"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("X"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Y"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("X"));
        // Column B (offset 1): every data row is a positive number (so a ">0" filter keeps every row,
        // including row 3).
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("ColB"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(7));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(9));
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 2));
        var ctx = new TestCommandContext(wb);

        // Value filter on column A keeps only "X" -> hides row 3.
        new FilterCommand(sheet.Id, range, 0, ["X"]).Apply(ctx).Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u]);

        // Condition filter on column B (">0") matches every row, including row 3 -- but row 3 must
        // STAY hidden because column A's value filter still excludes it (AND across columns).
        var conditionCommand = new FilterConditionCommand(
            sheet.Id, range, filterColOffset: 1, new NumberGreaterThanFilterCriterion(0));
        conditionCommand.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u], "column A's value filter still excludes row 3");

        // Undo the condition filter: column A's hidden row must still be intact.
        conditionCommand.Revert(ctx);
        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u]);
    }

    // R12-xlsx-tables-3: a table totals-row SUBTOTAL formula must escape '[', ']', '#', and "'" in
    // the column name (not just double ']') so the structured reference round-trips through FreeX's
    // own formula lexer/resolver instead of resolving to #NAME? (or the wrong column).
    [Theory]
    [InlineData("A[B")]
    [InlineData("Rate#Q1")]
    [InlineData("Weird]Name")]
    [InlineData("It's Total")]
    public void RefreshStructuredTableTotalsCommand_SubtotalFormulaResolvesColumnWithSpecialCharacters(string columnName)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(columnName));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(30));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "SpecialTbl",
            DisplayName = "SpecialTbl",
            // Header row 1, data rows 2-4, totals row 5.
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            TotalsRowShown = true,
            Columns = { new StructuredTableColumnModel(1, columnName, TotalsRowFunction: "sum") }
        };
        sheet.StructuredTables.Add(table);
        var ctx = new TestCommandContext(wb);

        var outcome = new RefreshStructuredTableTotalsCommand(sheet.Id, table.Id).Apply(ctx);
        outcome.Success.Should().BeTrue();

        var totalsCell = sheet.GetCell(5, 1);
        totalsCell.Should().NotBeNull();
        totalsCell!.FormulaText.Should().NotBeNull();

        // The formula must actually resolve back to this column and compute the real sum (60), not
        // #NAME? — proving the escaped column name round-trips through the lexer/structured
        // reference resolver, matching Excel's exact result for a totals-row SUBTOTAL.
        var evaluator = new FormulaEvaluator();
        var totalsAddress = new CellAddress(sheet.Id, 5, 1);
        var result = evaluator.Evaluate(totalsCell.FormulaText!, sheet, wb, totalsAddress);

        result.Should().Be(new NumberValue(60));
    }
}
