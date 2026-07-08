using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookSessionDataTableTests
{
    [Fact]
    public void ExecuteDataTablePlan_AppliesPlanThroughSessionMutationPath()
    {
        var (session, sheet, plan) = CreateOneVariableDataTableSession();

        var result = session.ExecuteDataTablePlan(plan);

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().Equal(
            Address(sheet, 3, 3),
            Address(sheet, 3, 4),
            Address(sheet, 4, 3),
            Address(sheet, 4, 4));
        sheet.GetCell(Address(sheet, 3, 3))!.FormulaText.Should().Be("B3*2");
        // Column D's header (D2) is entirely blank — it never carried a formula of its own — so
        // (matching Excel and R14-data-tables-whatif-3) that column repeats the constant 0 rather
        // than borrowing column C's formula.
        sheet.GetCell(Address(sheet, 3, 4))!.FormulaText.Should().BeNull();
        sheet.GetCell(Address(sheet, 3, 4))!.Value.Should().Be(new NumberValue(0));
        sheet.GetCell(Address(sheet, 4, 3))!.FormulaText.Should().Be("B4*2");
        sheet.GetCell(Address(sheet, 4, 4))!.FormulaText.Should().BeNull();
        sheet.GetCell(Address(sheet, 4, 4))!.Value.Should().Be(new NumberValue(0));
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.ActiveCell.Should().Be(plan.OutputRange.Start);
        session.SelectedRange.Should().Be(plan.OutputRange);
    }

    [Fact]
    public void ExecuteDataTablePlan_UndoRedoRestoresGeneratedOutput()
    {
        var (session, sheet, plan) = CreateOneVariableDataTableSession();

        var apply = session.ExecuteDataTablePlan(plan);
        var undo = session.UndoLastEdit();
        var redo = session.RedoLastEdit();

        apply.Success.Should().BeTrue();
        undo.Success.Should().BeTrue();
        redo.Success.Should().BeTrue();
        sheet.GetCell(Address(sheet, 3, 3))!.FormulaText.Should().Be("B3*2");
        // Column D's header (D2) is blank, so it holds the repeated constant 0, not a formula.
        sheet.GetCell(Address(sheet, 4, 4))!.FormulaText.Should().BeNull();
        sheet.GetCell(Address(sheet, 4, 4))!.Value.Should().Be(new NumberValue(0));

        session.UndoLastEdit().Success.Should().BeTrue();
        sheet.GetCell(Address(sheet, 3, 3)).Should().BeNull();
        sheet.GetCell(Address(sheet, 4, 4)).Should().BeNull();
        session.CanRedo.Should().BeTrue();
    }

    [Fact]
    public void ExecuteDataTablePlan_FailedCommandDoesNotDirtyOrMoveSelection()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        var selected = Address(sheet, 6, 6);
        var missingSheet = new SheetId(Guid.NewGuid());
        sheet.SetFormula(Address(sheet, 2, 3), "G1*2");
        var session = CreateSession(workbook);
        session.SelectCell(selected);
        var plan = new DataTablePlan(
            DataTablePlanMode.OneVariable,
            Range(sheet.Id, 2, 2, 4, 4),
            Address(sheet, 2, 3),
            DataTableInputOrientation.Column,
            RowInputCell: null,
            ColumnInputCell: new CellAddress(missingSheet, 1, 7),
            OutputRange: Range(sheet.Id, 3, 3, 4, 4));

        var result = session.ExecuteDataTablePlan(plan);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Data Table cells must be on one sheet.");
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        session.ActiveCell.Should().Be(selected);
        session.SelectedRange.Should().Be(new GridRange(selected, selected));
    }

    private static (WorkbookSession Session, Sheet Sheet, DataTablePlan Plan) CreateOneVariableDataTableSession()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        sheet.SetFormula(Address(sheet, 2, 3), "G1*2");
        var plan = DataTablePlanner.CreatePlan(
            workbook,
            sheet.Id,
            tableRangeText: "B2:D4",
            rowInputCellText: "",
            columnInputCellText: "G1").Plan!;

        return (CreateSession(workbook), sheet, plan);
    }

    private static WorkbookSession CreateSession(Workbook workbook) =>
        new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);

    private static CellAddress Address(Sheet sheet, uint row, uint col) =>
        new(sheet.Id, row, col);

    private static GridRange Range(SheetId sheetId, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheetId, startRow, startCol), new CellAddress(sheetId, endRow, endCol));
}
