using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R115-data-table-master-formula-refresh: OneVariableDataTableCommand/TwoVariableDataTableCommand
/// only read the master/result formula's text ONCE, at table-creation time, then baked a literal
/// text substitution into every body cell -- so editing the master formula cell afterward left the
/// whole table computing against the stale, pre-edit formula forever, unlike real Excel's
/// {=TABLE(...)} body, which re-reads the master formula on every recalc. These tests exercise the
/// fix through the real product entry point for retyping a cell's formula -- EditCellsCommand (and
/// its grouped-sheets sibling GroupedEditCellsCommand) -- rather than asserting on the rewriter
/// helper directly.
/// </summary>
public sealed class R115_DataTableMasterFormulaRefreshTests
{
    [Fact]
    public void EditCellsCommand_EditingMasterFormulaCell_RefreshesOneVariableRowOrientedBody()
    {
        // Row-oriented one-variable table, mirroring
        // OneVariableDataTableCommand_RowInputUsesTopRowTrialValues: formulaCell A2 hosts "=B1*2",
        // trial values across row 1 (B1=1, C1=2), body row 2 (B2, C2).
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var inputCell = new CellAddress(sheet.Id, 1, 2);
        var formulaCell = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(inputCell, new NumberValue(10));
        sheet.SetFormula(formulaCell, "B1*2");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(2));

        var creation = new OneVariableDataTableCommand(
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 3)),
            formulaCell,
            inputCell,
            DataTableInputOrientation.Row);
        creation.Apply(ctx).Success.Should().BeTrue();

        sheet.GetCell(2, 2)!.FormulaText.Should().Be("B1*2");
        sheet.GetCell(2, 3)!.FormulaText.Should().Be("C1*2");

        // Retype the master formula cell through the real cell-edit command -- exactly how a user
        // edits an existing formula in the grid.
        var edit = EditCellsCommand.ForFormula(sheet.Id, formulaCell, "B1*3");
        var outcome = edit.Apply(ctx);
        outcome.Success.Should().BeTrue();

        // The Data Table body must re-derive from the NEW master formula text, matching Excel's
        // live {=TABLE(...)} recompute -- not keep repeating the stale "*2" it was created with.
        sheet.GetCell(2, 2)!.FormulaText.Should().Be("B1*3",
            because: "editing the master formula cell must refresh the data table body, not leave it stale");
        sheet.GetCell(2, 3)!.FormulaText.Should().Be("C1*3",
            because: "every body cell in the row must re-derive from the edited master formula");

        // The refresh must also be reported so the recalc engine actually re-evaluates the body.
        outcome.AffectedCells.Should().Contain(new CellAddress(sheet.Id, 2, 2));
        outcome.AffectedCells.Should().Contain(new CellAddress(sheet.Id, 2, 3));

        // Undo must restore both the master formula's own edit AND the refreshed body, in the same
        // transaction -- otherwise Ctrl+Z would leave the body on the "*3" text while the master
        // formula reverted back to "*2".
        edit.Revert(ctx);
        sheet.GetCell(formulaCell)!.FormulaText.Should().Be("B1*2");
        sheet.GetCell(2, 2)!.FormulaText.Should().Be("B1*2");
        sheet.GetCell(2, 3)!.FormulaText.Should().Be("C1*2");
    }

    [Fact]
    public void EditCellsCommand_EditingMasterFormulaCell_RefreshesTwoVariableBody()
    {
        // Mirrors TwoVariableDataTableCommand_FillsGridFormulasAndUndoRestores.
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

        var creation = new TwoVariableDataTableCommand(
            new GridRange(new CellAddress(sheet.Id, 1, 4), new CellAddress(sheet.Id, 3, 6)),
            formulaCell,
            rowInputCell,
            columnInputCell);
        creation.Apply(ctx).Success.Should().BeTrue();

        sheet.GetCell(2, 5)!.FormulaText.Should().Be("E1+D2");

        var edit = EditCellsCommand.ForFormula(sheet.Id, formulaCell, "B1-C1");
        edit.Apply(ctx).Success.Should().BeTrue();

        sheet.GetCell(2, 5)!.FormulaText.Should().Be("E1-D2");
        sheet.GetCell(2, 6)!.FormulaText.Should().Be("F1-D2");
        sheet.GetCell(3, 5)!.FormulaText.Should().Be("E1-D3");
        sheet.GetCell(3, 6)!.FormulaText.Should().Be("F1-D3");
    }

    [Fact]
    public void EditCellsCommand_EditingUnrelatedCell_DoesNotTouchDataTableBody()
    {
        // No-regression sibling: an ordinary edit to some cell that is neither the master formula
        // cell nor a header driver cell must never perturb an existing Data Table's body.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var inputCell = new CellAddress(sheet.Id, 1, 2);
        var formulaCell = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(inputCell, new NumberValue(10));
        sheet.SetFormula(formulaCell, "B1*2");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("Result"));

        var creation = new OneVariableDataTableCommand(
            new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 3, 4)),
            formulaCell,
            inputCell);
        creation.Apply(ctx).Success.Should().BeTrue();

        var bodyBefore = sheet.GetCell(2, 4)!.Value;

        // Unrelated cell, well outside the table entirely.
        var edit = EditCellsCommand.ForValue(sheet.Id, new CellAddress(sheet.Id, 20, 20), new NumberValue(999));
        var outcome = edit.Apply(ctx);
        outcome.Success.Should().BeTrue();

        sheet.GetCell(2, 4)!.Value.Should().Be(bodyBefore,
            because: "an edit to an address that is neither the master formula cell nor a header driver cell must not refresh any Data Table");
        outcome.AffectedCells.Should().NotContain(new CellAddress(sheet.Id, 2, 4));
        outcome.AffectedCells.Should().NotContain(new CellAddress(sheet.Id, 3, 4));
    }

    [Fact]
    public void GroupedEditCellsCommand_EditingMasterFormulaCellOnGroupedSheet_RefreshesThatSheetsDataTableBody()
    {
        // Sibling family member: GroupedEditCellsCommand applies the same edit to N grouped sheets;
        // a Data Table living on one of those sheets must refresh exactly like the single-sheet
        // EditCellsCommand path above. Same row-oriented geometry as the first test in this file
        // (formulaCell A2 hosts the master formula; body row 2 is B2/C2), applied via the grouped
        // command instead.
        var workbook = new Workbook("test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var ctx = new TestCommandContext(workbook);

        var inputCell = new CellAddress(sheet1.Id, 1, 2);
        var formulaCell = new CellAddress(sheet1.Id, 2, 1);
        sheet1.SetCell(inputCell, new NumberValue(10));
        sheet1.SetFormula(formulaCell, "B1*2");
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 2), new NumberValue(1));
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 3), new NumberValue(2));

        var creation = new OneVariableDataTableCommand(
            new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 2, 3)),
            formulaCell,
            inputCell,
            DataTableInputOrientation.Row);
        creation.Apply(ctx).Success.Should().BeTrue();
        sheet1.GetCell(2, 2)!.FormulaText.Should().Be("B1*2");

        var groupedEdit = new GroupedEditCellsCommand(
            [sheet1.Id, sheet2.Id],
            sheet1.Id,
            [(formulaCell, Cell.FromFormula("B1*5"))]);
        var outcome = groupedEdit.Apply(ctx);
        outcome.Success.Should().BeTrue();

        sheet1.GetCell(2, 2)!.FormulaText.Should().Be("B1*5",
            because: "the grouped edit landed on Sheet1's Data Table master formula cell, so its body must refresh");
        sheet1.GetCell(2, 3)!.FormulaText.Should().Be("C1*5");

        groupedEdit.Revert(ctx);
        sheet1.GetCell(formulaCell)!.FormulaText.Should().Be("B1*2");
        sheet1.GetCell(2, 2)!.FormulaText.Should().Be("B1*2");
        sheet1.GetCell(2, 3)!.FormulaText.Should().Be("C1*2");
    }
}
