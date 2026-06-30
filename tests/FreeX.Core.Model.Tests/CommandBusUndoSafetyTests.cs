using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Tests that CommandBus.Undo is safe when Revert throws:
/// the command must be restored to the undo stack and the redo stack must be untouched.
/// </summary>
public sealed class CommandBusUndoSafetyTests
{
    private static readonly WorkbookId WbId = WorkbookId.New();

    private static CommandBus MakeBus(out Workbook workbook)
    {
        workbook = new Workbook("safety-test");
        var wb = workbook;
        return new CommandBus(_ => new TestCommandContext(wb));
    }

    // ── helper stubs ──────────────────────────────────────────────────────────

    private sealed class NoOpCommand : IWorkbookCommand
    {
        public string Label => "NoOp";
        public CommandOutcome Apply(ICommandContext ctx) => new(true);
        public void Revert(ICommandContext ctx) { }
    }

    private sealed class ThrowingRevertCommand : IWorkbookCommand
    {
        public string Label => "ThrowingRevert";
        public CommandOutcome Apply(ICommandContext ctx) => new(true);
        public void Revert(ICommandContext ctx) => throw new InvalidOperationException("simulated revert failure");
    }

    private sealed class ThrowingApplyCommand : IWorkbookCommand
    {
        private readonly Exception _exception;

        public ThrowingApplyCommand(string message)
            : this(new InvalidOperationException(message))
        {
        }

        public ThrowingApplyCommand(Exception exception) => _exception = exception;

        public string Label => "ThrowingApply";
        public CommandOutcome Apply(ICommandContext ctx) => throw _exception;
        public void Revert(ICommandContext ctx) { }
    }

    private sealed class MissingSheetLookupCommand : IWorkbookCommand
    {
        public string Label => "MissingSheetLookup";
        public CommandOutcome Apply(ICommandContext ctx)
        {
            ctx.GetSheet(default);
            return new(true);
        }

        public void Revert(ICommandContext ctx) { }
    }

    private sealed class ObservingCommand(Action onApply, Action onRevert) : IWorkbookCommand
    {
        public string Label => "Observing";
        public CommandOutcome Apply(ICommandContext ctx)
        {
            onApply();
            return new(true);
        }

        public void Revert(ICommandContext ctx) => onRevert();
    }

    private sealed class OutcomeAffectedCellsCommand(
        IReadOnlyList<CellAddress> firstApplyAffectedCells,
        IReadOnlyList<CellAddress>? redoAffectedCells = null) : IWorkbookCommand
    {
        private int _applyCount;

        public string Label => "OutcomeAffectedCells";

        public CommandOutcome Apply(ICommandContext ctx)
        {
            _applyCount++;
            return new CommandOutcome(
                true,
                AffectedCells: _applyCount == 1
                    ? firstApplyAffectedCells
                    : redoAffectedCells ?? firstApplyAffectedCells);
        }

        public void Revert(ICommandContext ctx)
        {
        }
    }

    // ── happy path ────────────────────────────────────────────────────────────

    [Fact]
    public void Undo_WhenRevertSucceeds_ReturnsSuccessOutcome()
    {
        var bus = MakeBus(out _);
        bus.Execute(WbId, new NoOpCommand());

        var outcome = bus.Undo(WbId);

        outcome.Success.Should().BeTrue();
    }

    [Fact]
    public void ExecuteUndoRedo_RunBeforeMutationCallbackBeforeCommandMutation()
    {
        var workbook = new Workbook("hook-test");
        var callbackRan = false;
        var applySawCallback = false;
        var revertSawCallback = false;
        var redoSawCallback = false;
        var applyCount = 0;
        var bus = new CommandBus(
            _ => new TestCommandContext(workbook),
            (_, _) => callbackRan = true);
        var command = new ObservingCommand(
            () =>
            {
                applyCount++;
                if (applyCount == 1)
                    applySawCallback = callbackRan;
                else
                    redoSawCallback = callbackRan;
            },
            () => revertSawCallback = callbackRan);

        bus.Execute(WbId, command);
        callbackRan = false;
        bus.Undo(WbId);
        callbackRan = false;
        bus.Redo(WbId);

        applySawCallback.Should().BeTrue();
        revertSawCallback.Should().BeTrue();
        redoSawCallback.Should().BeTrue();
    }

    [Fact]
    public void Undo_ReturnsApplyAffectedCells_WhenCommandDoesNotImplementAffectedCellsCommand()
    {
        var bus = MakeBus(out _);
        var sheetId = SheetId.New();
        var affectedCells = new[]
        {
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 1, 2)
        };

        bus.Execute(WbId, new OutcomeAffectedCellsCommand(affectedCells));

        bus.Undo(WbId).AffectedCells.Should().Equal(affectedCells);
    }

    [Fact]
    public void Redo_RefreshesStoredAffectedCells_ForNextUndo()
    {
        var bus = MakeBus(out _);
        var sheetId = SheetId.New();
        var firstApplyAffectedCells = new[] { new CellAddress(sheetId, 1, 1) };
        var redoAffectedCells = new[] { new CellAddress(sheetId, 2, 1) };

        bus.Execute(WbId, new OutcomeAffectedCellsCommand(firstApplyAffectedCells, redoAffectedCells));
        bus.Undo(WbId).AffectedCells.Should().Equal(firstApplyAffectedCells);

        bus.Redo(WbId).AffectedCells.Should().Equal(redoAffectedCells);

        bus.Undo(WbId).AffectedCells.Should().Equal(redoAffectedCells);
    }

    [Fact]
    public void Undo_WhenRevertSucceeds_CommandRemovedFromUndoStack()
    {
        var bus = MakeBus(out _);
        bus.Execute(WbId, new NoOpCommand());

        bus.Undo(WbId);

        bus.CanUndo(WbId).Should().BeFalse();
    }

    // ── failure-path: Revert throws ───────────────────────────────────────────

    [Fact]
    public void Execute_WhenApplyThrowsMissingSheetId_NormalizesInternalSheetId()
    {
        var bus = MakeBus(out _);

        var outcome = bus.Execute(WbId, new ThrowingApplyCommand("Sheet 00000000 not found"));

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be("Sheet not found.");
    }

    [Fact]
    public void Execute_WhenApplyLooksUpMissingSheet_NormalizesInternalSheetId()
    {
        var bus = MakeBus(out _);

        var outcome = bus.Execute(WbId, new MissingSheetLookupCommand());

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be("Sheet not found.");
    }

    [Fact]
    public void Execute_WhenApplyThrowsWrappedMissingSheetId_NormalizesInternalSheetId()
    {
        var bus = MakeBus(out _);
        var exception = new InvalidOperationException(
            "outer command failure",
            new KeyNotFoundException("Sheet 00000000 not found"));

        var outcome = bus.Execute(WbId, new ThrowingApplyCommand(exception));

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be("Sheet not found.");
    }

    [Fact]
    public void Execute_WhenApplyThrowsOtherError_PreservesCommandFailureContext()
    {
        var bus = MakeBus(out _);

        var outcome = bus.Execute(WbId, new ThrowingApplyCommand("simulated apply failure"));

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be("Command failed: simulated apply failure");
    }

    [Fact]
    public void Undo_WhenRevertThrows_ReturnsFailureOutcome()
    {
        var bus = MakeBus(out _);
        bus.Execute(WbId, new ThrowingRevertCommand());

        var outcome = bus.Undo(WbId);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("simulated revert failure");
    }

    [Fact]
    public void Undo_WhenRevertThrows_CommandIsRestoredToUndoStack()
    {
        var bus = MakeBus(out _);
        bus.Execute(WbId, new ThrowingRevertCommand());

        bus.Undo(WbId); // should fail but restore

        bus.CanUndo(WbId).Should().BeTrue("the command must still be undoable after a failed Revert");
    }

    [Fact]
    public void Undo_WhenRevertThrows_RedoStackIsNotModified()
    {
        var bus = MakeBus(out _);
        bus.Execute(WbId, new NoOpCommand());
        bus.Undo(WbId); // succeeds — puts command on redo stack
        bus.Execute(WbId, new ThrowingRevertCommand()); // new command, clears redo stack

        // Now redo is empty; undo the throwing command — redo must stay empty
        bus.Undo(WbId);

        bus.CanRedo(WbId).Should().BeFalse(
            "a failed Undo must not push anything onto the redo stack");
    }

    [Fact]
    public void Undo_WhenRevertThrows_DoesNotThrow()
    {
        var bus = MakeBus(out _);
        bus.Execute(WbId, new ThrowingRevertCommand());

        var act = () => bus.Undo(WbId);

        act.Should().NotThrow("CommandBus.Undo must absorb exceptions from Revert");
    }
}
