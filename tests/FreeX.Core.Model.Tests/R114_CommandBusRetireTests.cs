using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R114-commands-workbook-retire-1: <see cref="CommandBus"/> keys its undo/redo stacks
/// (<c>_stacks</c>) and pending repeatable-command factories (<c>_repeatableCommandFactories</c>)
/// by <see cref="WorkbookId"/> forever, with no prior eviction API. <see cref="ICommandBus.Retire"/>
/// is the fix: a host that reuses one <see cref="CommandBus"/> instance across File &gt; Open /
/// File &gt; New (the WPF host does, see <c>MainWindow.AdoptWorkbookAsInitial</c> /
/// <c>MainWindow.OpenFileAsync</c>) must call it for the outgoing workbook right before dropping
/// its last reference, mirroring <c>RecalcEngine.RetireWorkbook</c>.
/// </summary>
public sealed class R114_CommandBusRetireTests
{
    private sealed class NoOpCommand : IWorkbookCommand
    {
        public string Label => "R114 no-op";
        public CommandOutcome Apply(ICommandContext ctx) => new(true);
        public void Revert(ICommandContext ctx) { }
    }

    [Fact]
    public void Retire_RemovesTheWorkbooksUndoRedoStack()
    {
        var workbook = new Workbook("test");
        var bus = new CommandBus(_ => new TestCommandContext(workbook));

        bus.Execute(workbook.Id, new NoOpCommand()).Success.Should().BeTrue();
        bus.CanUndo(workbook.Id).Should().BeTrue("sanity: the command must have landed on the undo stack");

        bus.Retire(workbook.Id);

        bus.CanUndo(workbook.Id).Should().BeFalse("Retire must drop the workbook's undo stack entirely");
        bus.CanRedo(workbook.Id).Should().BeFalse();
        bus.GetUndoStackDepth(workbook.Id).Should().Be(0);
        bus.GetUndoHistory(workbook.Id, 10).Should().BeEmpty();
    }

    [Fact]
    public void Retire_ClearsThePendingRepeatableCommandFactory()
    {
        var workbook = new Workbook("test");
        var bus = new CommandBus(_ => new TestCommandContext(workbook));

        bus.ExecuteRepeatable(workbook.Id, () => new NoOpCommand()).Success.Should().BeTrue();
        bus.CanRepeat(workbook.Id).Should().BeTrue("sanity: ExecuteRepeatable must register a factory");

        bus.Retire(workbook.Id);

        bus.CanRepeat(workbook.Id).Should().BeFalse("Retire must drop the pending repeatable-command factory too");
        bus.RepeatLast(workbook.Id).Should().Be(new CommandOutcome(false, "Nothing to repeat"));
    }

    [Fact]
    public void Retire_OnAWorkbookThatWasNeverExecutedAgainst_IsANoOp()
    {
        // A CommandBus that never saw the workbook (e.g. it was closed before any edit) has no
        // entry to remove -- Retire must not throw on the miss.
        var workbook = new Workbook("never touched");
        var bus = new CommandBus(_ => new TestCommandContext(workbook));

        var act = () => bus.Retire(workbook.Id);

        act.Should().NotThrow();
        bus.CanUndo(workbook.Id).Should().BeFalse();
    }

    [Fact]
    public void Retire_OnOneWorkbook_LeavesAnotherLiveWorkbooksStackOnTheSameBusUntouched()
    {
        // No-regression sibling coverage: CommandBus is shared across every open document in the
        // WPF host's per-window instance model is NOT the case (each window owns its bus), but the
        // dictionaries themselves are still keyed per-WorkbookId within one bus instance -- Retire
        // must only ever touch the one key it was asked to remove.
        var outgoingWorkbook = new Workbook("outgoing");
        var stillLiveWorkbook = new Workbook("still live");
        var bus = new CommandBus(id => new TestCommandContext(id == outgoingWorkbook.Id ? outgoingWorkbook : stillLiveWorkbook));

        bus.ExecuteRepeatable(outgoingWorkbook.Id, () => new NoOpCommand()).Success.Should().BeTrue();
        bus.ExecuteRepeatable(stillLiveWorkbook.Id, () => new NoOpCommand()).Success.Should().BeTrue();

        bus.Retire(outgoingWorkbook.Id);

        bus.CanUndo(outgoingWorkbook.Id).Should().BeFalse();
        bus.CanRepeat(outgoingWorkbook.Id).Should().BeFalse();

        bus.CanUndo(stillLiveWorkbook.Id).Should().BeTrue(
            "retiring one workbook's entry must not disturb a different workbook's stack in the same bus");
        bus.CanRepeat(stillLiveWorkbook.Id).Should().BeTrue();
    }
}
