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
        return new CommandBus(_ => new SimpleCtx(wb));
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

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
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
    public void Undo_WhenRevertSucceeds_CommandRemovedFromUndoStack()
    {
        var bus = MakeBus(out _);
        bus.Execute(WbId, new NoOpCommand());

        bus.Undo(WbId);

        bus.CanUndo(WbId).Should().BeFalse();
    }

    // ── failure-path: Revert throws ───────────────────────────────────────────

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
