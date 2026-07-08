using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class DataTableCommandTests
{
    [Fact]
    public void OneVariableDataTableCommand_FillsResultFormulasAndUndoRestores()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var inputCell = new CellAddress(sheet.Id, 1, 2);
        var formulaCell = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(inputCell, new NumberValue(10));
        sheet.SetFormula(formulaCell, "B1*2");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("Result"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 4), new TextValue("old"));

        var command = new OneVariableDataTableCommand(
            new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 3, 4)),
            formulaCell,
            inputCell);

        command.Apply(ctx).Success.Should().BeTrue();

        // Column D's header (D1) is the plain label "Result", not a formula, so — matching Excel,
        // which never invents a formula for a column that doesn't have one — every body cell in
        // that column just repeats the constant "Result" rather than borrowing column C's formula.
        sheet.GetCell(2, 4)!.FormulaText.Should().BeNull();
        sheet.GetCell(2, 4)!.Value.Should().Be(new TextValue("Result"));
        sheet.GetCell(3, 4)!.FormulaText.Should().BeNull();
        sheet.GetCell(3, 4)!.Value.Should().Be(new TextValue("Result"));

        command.Revert(ctx);

        sheet.GetValue(2, 4).Should().Be(new TextValue("old"));
        sheet.GetCell(3, 4).Should().BeNull();
    }

    [Fact]
    public void OneVariableDataTableCommand_RowInputUsesTopRowTrialValues()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var inputCell = new CellAddress(sheet.Id, 1, 2);
        var formulaCell = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(inputCell, new NumberValue(10));
        sheet.SetFormula(formulaCell, "B1*2");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("old"));

        var command = new OneVariableDataTableCommand(
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 3)),
            formulaCell,
            inputCell,
            DataTableInputOrientation.Row);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetCell(2, 2)!.FormulaText.Should().Be("B1*2");
        sheet.GetCell(2, 3)!.FormulaText.Should().Be("C1*2");

        command.Revert(ctx);

        sheet.GetValue(2, 2).Should().Be(new TextValue("old"));
        sheet.GetCell(2, 3).Should().BeNull();
    }

    [Fact]
    public void TwoVariableDataTableCommand_FillsGridFormulasAndUndoRestores()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var rowInputCell = new CellAddress(sheet.Id, 1, 2);
        var columnInputCell = new CellAddress(sheet.Id, 1, 3);
        var formulaCell = new CellAddress(sheet.Id, 1, 4);
        sheet.SetCell(rowInputCell, new NumberValue(10));
        sheet.SetCell(columnInputCell, new NumberValue(20));
        sheet.SetFormula(formulaCell, "B1+C1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 5), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 6), new NumberValue(200));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 4), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 4), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 5), new TextValue("old"));

        var command = new TwoVariableDataTableCommand(
            new GridRange(new CellAddress(sheet.Id, 1, 4), new CellAddress(sheet.Id, 3, 6)),
            formulaCell,
            rowInputCell,
            columnInputCell);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetCell(2, 5)!.FormulaText.Should().Be("E1+D2");
        sheet.GetCell(2, 6)!.FormulaText.Should().Be("F1+D2");
        sheet.GetCell(3, 5)!.FormulaText.Should().Be("E1+D3");
        sheet.GetCell(3, 6)!.FormulaText.Should().Be("F1+D3");

        command.Revert(ctx);

        sheet.GetValue(2, 5).Should().Be(new TextValue("old"));
        sheet.GetCell(3, 6).Should().BeNull();
    }

    [Fact]
    public void OneVariableDataTableCommand_CrossSheetReferenceIsNotSubstituted()
    {
        // K6 regression: a formula referencing the same A1 coordinates on a DIFFERENT sheet
        // must not have those coordinates rewritten — only the local unqualified input cell
        // should be substituted.
        //
        // Table layout (Column orientation):
        //   formulaCell = B1, formula = "A1+Sheet2!A1"
        //   inputCell   = A1
        //   tableRange  = A1:B3   (body rows = 2..3, body cols = 2)
        //   trial input for row 2 = A2 (tableRange.Start.Col=1, row=2)
        //   expected rewrite of body cell (2,2) = "A2+Sheet2!A1"
        //     - local A1 → A2  (substituted)
        //     - Sheet2!A1 unchanged  (cross-sheet ref must NOT be touched)
        var workbook = new Workbook("test");
        var sheet1 = workbook.AddSheet("Sheet1");
        workbook.AddSheet("Sheet2");
        var ctx = new TestCommandContext(workbook);

        // Input cell A1 (col=1, row=1)
        var inputCell = new CellAddress(sheet1.Id, 1, 1);
        // Formula cell B1 (col=2, row=1)
        var formulaCell = new CellAddress(sheet1.Id, 1, 2);
        sheet1.SetCell(inputCell, new NumberValue(5));

        // Formula references BOTH the local A1 (input cell) and the cross-sheet Sheet2!A1
        sheet1.SetFormula(formulaCell, "A1+Sheet2!A1");

        // Table occupies A1:B3 (Column orientation).
        //   - trial values: A2=1, A3=2
        //   - body cells: B2, B3
        // For row 2, trialInputAddress = A2 → formula becomes "A2+Sheet2!A1"
        sheet1.SetCell(new CellAddress(sheet1.Id, 2, 1), new NumberValue(1));
        sheet1.SetCell(new CellAddress(sheet1.Id, 3, 1), new NumberValue(2));

        var command = new OneVariableDataTableCommand(
            new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 3, 2)),
            formulaCell,
            inputCell);

        command.Apply(ctx).Success.Should().BeTrue();

        // Body cell B2 should have the local A1 replaced by A2, Sheet2!A1 unchanged
        var rewritten = sheet1.GetCell(2, 2)!.FormulaText!;
        rewritten.Should().Contain("Sheet2!A1",
            because: "the cross-sheet reference must never be rewritten by the table rewriter");
        rewritten.Should().NotContain("Sheet2!A2",
            because: "the cross-sheet ref row number must not change");
        rewritten.Should().Contain("A2",
            because: "the local input cell A1 must be substituted with the trial input A2");
    }
}
