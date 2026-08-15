using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookSessionGoalSeekTests
{
    [Fact]
    public void ExecuteGoalSeek_AppliesConvergedResultThroughSessionMutationPath()
    {
        var (session, sheet, changingCell, setCell, _) = CreateLinearGoalSeekSession();

        var result = session.ExecuteGoalSeek(new GoalSeekRequest(setCell, 12, changingCell));

        result.Success.Should().BeTrue();
        result.Status.Should().Be(WorkbookGoalSeekStatus.Applied);
        result.Converged.Should().BeTrue();
        result.Applied.Should().BeTrue();
        result.SeekResult.Should().NotBeNull();
        result.SeekResult!.FoundValue.Should().BeApproximately(4, 1e-4);
        result.SeekResult.ActualResult.Should().BeApproximately(12, 1e-4);
        result.EditResult.Should().NotBeNull();
        result.EditResult!.AffectedCells.Should().Equal(changingCell);
        GetNumber(sheet, changingCell).Should().BeApproximately(4, 1e-4);
        GetNumber(sheet, setCell).Should().BeApproximately(12, 1e-4);
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.ActiveCell.Should().Be(changingCell);
        session.SelectedRange.Should().Be(new GridRange(changingCell, changingCell));
    }

    [Fact]
    public void ExecuteGoalSeek_NonConvergedResultDoesNotMutateOrDirtySession()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        var changingCell = new CellAddress(sheet.Id, 1, 1);
        var setCell = new CellAddress(sheet.Id, 1, 2);
        var selectionCell = new CellAddress(sheet.Id, 3, 3);
        sheet.SetCell(changingCell, new NumberValue(1));
        // Set cell's formula references the changing cell (so it recalculates along with it) but
        // its value is constant with respect to that input, so the solver's derivative guard fires
        // and it never converges -- distinct from an outright non-formula Set cell, which is
        // rejected up front (see ExecuteGoalSeek_RejectsNonFormulaSetCellWithoutMutating).
        sheet.SetFormula(setCell, "5+A1*0");
        var session = CreateSession(workbook);
        session.SelectCell(selectionCell);
        var originalSelection = session.SelectedRange;

        var result = session.ExecuteGoalSeek(new GoalSeekRequest(setCell, 10, changingCell));

        result.Success.Should().BeFalse();
        result.Status.Should().Be(WorkbookGoalSeekStatus.NotConverged);
        result.Converged.Should().BeFalse();
        result.Applied.Should().BeFalse();
        result.SeekResult.Should().NotBeNull();
        result.EditResult.Should().BeNull();
        GetNumber(sheet, changingCell).Should().Be(1);
        GetNumber(sheet, setCell).Should().Be(5);
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        session.ActiveCell.Should().Be(selectionCell);
        session.SelectedRange.Should().Be(originalSelection);
    }

    [Fact]
    public void ExecuteGoalSeek_RejectsNonFormulaSetCellWithoutMutating()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        var changingCell = new CellAddress(sheet.Id, 1, 1);
        var setCell = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(changingCell, new NumberValue(1));
        sheet.SetCell(setCell, new NumberValue(5));
        var session = CreateSession(workbook);

        var result = session.ExecuteGoalSeek(new GoalSeekRequest(setCell, 10, changingCell));

        result.Success.Should().BeFalse();
        result.Status.Should().Be(WorkbookGoalSeekStatus.InvalidRequest);
        result.ErrorMessage.Should().Be("Goal Seek set cell must contain a formula.");
        result.SeekResult.Should().BeNull();
        result.EditResult.Should().BeNull();
        GetNumber(sheet, changingCell).Should().Be(1);
        GetNumber(sheet, setCell).Should().Be(5);
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void ExecuteGoalSeek_FormulaSetCellProceedsPastValidation()
    {
        var (session, sheet, changingCell, setCell, _) = CreateLinearGoalSeekSession();

        var result = session.ExecuteGoalSeek(new GoalSeekRequest(setCell, 12, changingCell));

        result.Status.Should().NotBe(WorkbookGoalSeekStatus.InvalidRequest);
        result.Success.Should().BeTrue();
        GetNumber(sheet, changingCell).Should().BeApproximately(4, 1e-4);
    }

    [Fact]
    public void ExecuteGoalSeek_UndoRedoRestoresAndReappliesChangingCell()
    {
        var (session, sheet, changingCell, setCell, _) = CreateLinearGoalSeekSession();

        var result = session.ExecuteGoalSeek(new GoalSeekRequest(setCell, 12, changingCell));
        var undo = session.UndoLastEdit();
        var redo = session.RedoLastEdit();

        result.Success.Should().BeTrue();
        undo.Success.Should().BeTrue();
        redo.Success.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.CanRedo.Should().BeFalse();
        GetNumber(sheet, changingCell).Should().BeApproximately(4, 1e-4);
        GetNumber(sheet, setCell).Should().BeApproximately(12, 1e-4);

        session.UndoLastEdit().Success.Should().BeTrue();
        GetNumber(sheet, changingCell).Should().Be(1);
        GetNumber(sheet, setCell).Should().Be(3);
        session.CanRedo.Should().BeTrue();
    }

    [Fact]
    public void ExecuteGoalSeek_RejectsProtectedChangingCellWithoutMutating()
    {
        var (session, sheet, changingCell, setCell, _) = CreateLinearGoalSeekSession();
        sheet.IsProtected = true;

        var result = session.ExecuteGoalSeek(new GoalSeekRequest(setCell, 12, changingCell));

        result.Success.Should().BeFalse();
        result.Status.Should().Be(WorkbookGoalSeekStatus.InvalidRequest);
        result.ErrorMessage.Should().Be("The sheet is protected.");
        result.SeekResult.Should().BeNull();
        result.EditResult.Should().BeNull();
        GetNumber(sheet, changingCell).Should().Be(1);
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void ApplyGoalSeekProposal_AcceptsClosestNonConvergedValueThroughSessionMutationPath()
    {
        var (session, sheet, changingCell, setCell, _) = CreateLinearGoalSeekSession();
        var request = new GoalSeekRequest(setCell, 20, changingCell);
        var proposal = WorkbookGoalSeekProposal.Ready(
            request,
            new FreeX.Core.Calc.GoalSeekResult(false, 5, 15, 1000));

        var result = session.ApplyGoalSeekProposal(proposal);

        result.Success.Should().BeTrue();
        result.Status.Should().Be(WorkbookGoalSeekStatus.Applied);
        result.Converged.Should().BeFalse();
        result.Applied.Should().BeTrue();
        GetNumber(sheet, changingCell).Should().Be(5);
        GetNumber(sheet, setCell).Should().Be(15);
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.ActiveCell.Should().Be(changingCell);

        session.UndoLastEdit().Success.Should().BeTrue();
        GetNumber(sheet, changingCell).Should().Be(1);
        GetNumber(sheet, setCell).Should().Be(3);
    }

    [Fact]
    public void ApplyGoalSeekProposal_RevalidatesWorkbookStateAtConfirmationTime()
    {
        var (session, sheet, changingCell, setCell, _) = CreateLinearGoalSeekSession();
        var proposal = session.FindGoalSeekProposal(new GoalSeekRequest(setCell, 12, changingCell));
        proposal.Success.Should().BeTrue();
        sheet.IsProtected = true;

        var result = session.ApplyGoalSeekProposal(proposal);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(WorkbookGoalSeekStatus.InvalidRequest);
        result.ErrorMessage.Should().Be("The sheet is protected.");
        GetNumber(sheet, changingCell).Should().Be(1);
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
    }

    private static (WorkbookSession Session, Sheet Sheet, CellAddress ChangingCell, CellAddress SetCell, CellAddress SelectionCell)
        CreateLinearGoalSeekSession()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        var changingCell = new CellAddress(sheet.Id, 1, 1);
        var setCell = new CellAddress(sheet.Id, 1, 2);
        var selectionCell = new CellAddress(sheet.Id, 3, 3);
        sheet.SetCell(changingCell, new NumberValue(1));
        sheet.SetFormula(setCell, "A1*3");
        return (CreateSession(workbook), sheet, changingCell, setCell, selectionCell);
    }

    private static WorkbookSession CreateSession(Workbook workbook) =>
        new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);

    private static double GetNumber(Sheet sheet, CellAddress address) =>
        sheet.GetValue(address).Should().BeOfType<NumberValue>().Subject.Value;
}
