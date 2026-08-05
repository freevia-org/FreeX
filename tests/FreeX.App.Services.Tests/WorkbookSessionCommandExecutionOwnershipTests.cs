using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookSessionCommandExecutionOwnershipTests
{
    [Fact]
    public void ExecuteCommandPreservingSelection_OwnsRecalcDirtyStateAndSelectionPolicy()
    {
        using var session = new WorkbookSessionFactory().CreateNew(120, 160);
        var sheet = session.ActiveSheet;
        var precedent = new CellAddress(sheet.Id, 1, 1);
        var formula = new CellAddress(sheet.Id, 1, 2);
        var selected = new CellAddress(sheet.Id, 4, 3);
        var selectedRange = new GridRange(selected, selected);

        session.SynchronizeSelectionState(sheet.Id, new GridRange(formula, formula), [new GridRange(formula, formula)], formula);
        session.CommitCellText("=A1*2").Success.Should().BeTrue();
        session.MarkSavedFromHost();
        session.SynchronizeSelectionState(sheet.Id, selectedRange, [selectedRange], selected);

        var result = session.ExecuteCommandPreservingSelection(
            EditCellsCommand.ForValue(sheet.Id, precedent, new NumberValue(4)));

        result.Success.Should().BeTrue();
        result.RecalcReport.Should().NotBeNull();
        sheet.GetCell(formula)!.Value.Should().Be(new NumberValue(8));
        session.IsDirty.Should().BeTrue();
        session.ActiveCell.Should().Be(selected);
        session.SelectedRange.Should().Be(selectedRange);
        session.CanUndo.Should().BeTrue();
    }

    [Fact]
    public void ExecuteRepeatableCommandPreservingSelection_RebuildsFactoryForRepeatLast()
    {
        using var session = new WorkbookSessionFactory().CreateNew(120, 160);
        var sheet = session.ActiveSheet;
        var first = new CellAddress(sheet.Id, 2, 2);
        var second = new CellAddress(sheet.Id, 5, 4);

        Select(session, first);
        session.ExecuteRepeatableCommandPreservingSelection(
                () => EditCellsCommand.ForValue(sheet.Id, session.ActiveCell, new NumberValue(7)))
            .Success.Should().BeTrue();

        Select(session, second);
        session.RepeatLastAction().Success.Should().BeTrue();

        sheet.GetCell(first)!.Value.Should().Be(new NumberValue(7));
        sheet.GetCell(second)!.Value.Should().Be(new NumberValue(7));
        session.ActiveCell.Should().Be(second);
    }

    [Fact]
    public void ExecuteCommandPreservingSelection_PropagatesNoOpWithoutDirtyingOrHistory()
    {
        using var session = new WorkbookSessionFactory().CreateNew(120, 160);

        var result = session.ExecuteCommandPreservingSelection(
            new SetFreezePanesCommand(session.ActiveSheet.Id, frozenRows: 0, frozenCols: 0));

        result.Success.Should().BeTrue();
        result.IsNoOp.Should().BeTrue();
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void RecalculateDirtyCells_PreservesSelectionAndDocumentDirtyState()
    {
        using var session = new WorkbookSessionFactory().CreateNew(120, 160);
        var sheet = session.ActiveSheet;
        var precedent = new CellAddress(sheet.Id, 1, 1);
        var formula = new CellAddress(sheet.Id, 1, 2);
        var selected = new CellAddress(sheet.Id, 4, 3);

        session.ExecuteCommandPreservingSelection(
                EditCellsCommand.ForValue(sheet.Id, precedent, new NumberValue(5)))
            .Success.Should().BeTrue();
        Select(session, formula);
        session.CommitCellText("=A1*2").Success.Should().BeTrue();
        session.MarkSavedFromHost();
        Select(session, selected);

        session.RecalculateDirtyCells();

        sheet.GetCell(formula)!.Value.Should().Be(new NumberValue(10));
        session.ActiveCell.Should().Be(selected);
        session.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void FindGoalSeekSolution_ComputesProposalWithoutApplyingIt()
    {
        using var session = new WorkbookSessionFactory().CreateNew(120, 160);
        var sheet = session.ActiveSheet;
        var changingCell = new CellAddress(sheet.Id, 1, 1);
        var setCell = new CellAddress(sheet.Id, 1, 2);

        session.ExecuteCommandPreservingSelection(
                EditCellsCommand.ForValue(sheet.Id, changingCell, new NumberValue(2)))
            .Success.Should().BeTrue();
        Select(session, setCell);
        session.CommitCellText("=A1*2").Success.Should().BeTrue();

        var result = session.FindGoalSeekSolution(new GoalSeekRequest(setCell, 10, changingCell));

        result.Converged.Should().BeTrue();
        result.FoundValue.Should().BeApproximately(5, 0.000001);
        sheet.GetCell(changingCell)!.Value.Should().Be(new NumberValue(2));
    }

    private static void Select(WorkbookSession session, CellAddress address)
    {
        var range = new GridRange(address, address);
        session.SynchronizeSelectionState(session.ActiveSheet.Id, range, [range], address);
    }
}
