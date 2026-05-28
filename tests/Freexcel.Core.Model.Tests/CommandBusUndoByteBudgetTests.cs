using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using Xunit;

namespace Freexcel.Core.Model.Tests;

/// <summary>
/// Tests for the byte-budget eviction policy on the CommandBus undo stack.
/// The stack caps by both command count (MaxUndoDepth = 100) and
/// estimated snapshot size (MaxUndoByteBudget = 50 MB).
/// </summary>
public sealed class CommandBusUndoByteBudgetTests
{
    private static readonly WorkbookId WbId = WorkbookId.New();

    // ── helper stubs ──────────────────────────────────────────────────────────

    /// <summary>A no-op command with a configurable byte estimate.</summary>
    private sealed class SizedCommand(int bytes) : IWorkbookCommand, IEstimatesMemory
    {
        public string Label => $"Sized({bytes})";
        public int EstimatedBytes => bytes;
        public CommandOutcome Apply(ICommandContext ctx) => new(true);
        public void Revert(ICommandContext ctx) { }
    }

    /// <summary>A no-op command with no IEstimatesMemory — falls back to default 200 bytes.</summary>
    private sealed class DefaultSizedCommand : IWorkbookCommand
    {
        public string Label => "DefaultSized";
        public CommandOutcome Apply(ICommandContext ctx) => new(true);
        public void Revert(ICommandContext ctx) { }
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }

    private static CommandBus MakeBus()
    {
        var wb = new Workbook("budget-test");
        return new CommandBus(_ => new SimpleCtx(wb));
    }

    // ── byte-budget eviction ──────────────────────────────────────────────────

    [Fact]
    public void CommandStack_EvictsOldCommandsWhenByteBudgetExceeded()
    {
        // MaxUndoByteBudget = 50 MB = 52_428_800 bytes.
        // Push one command that is 30 MB and another that is 30 MB.
        // After the second push the total (60 MB) exceeds the budget,
        // so the first command must be evicted.
        const int thirtyMb = 31_457_280; // 30 MB
        var bus = MakeBus();

        bus.Execute(WbId, new SizedCommand(thirtyMb)); // command A — oldest
        bus.Execute(WbId, new SizedCommand(thirtyMb)); // command B

        // The combined 60 MB exceeds the 50 MB limit; A must have been evicted.
        // After eviction only B remains, so exactly one undo is possible.
        bus.CanUndo(WbId).Should().BeTrue("the second command should still be on the stack");

        // Pop command B via undo.
        bus.Undo(WbId).Success.Should().BeTrue();

        // Now the undo stack must be empty — A was evicted.
        bus.CanUndo(WbId).Should().BeFalse("the first command should have been evicted to satisfy the byte budget");
    }

    [Fact]
    public void CommandStack_TracksBytesOnPushAndPop()
    {
        // Push a command with a known byte size, then undo it.
        // After a successful undo the bytes subtracted from the stack total must
        // bring CanUndo back to false (no remaining commands means zero bytes).
        const int oneMb = 1_048_576;
        var bus = MakeBus();

        bus.Execute(WbId, new SizedCommand(oneMb));
        bus.CanUndo(WbId).Should().BeTrue();

        bus.Undo(WbId).Success.Should().BeTrue();

        // The undo stack is empty — byte tracking was correct throughout.
        bus.CanUndo(WbId).Should().BeFalse("all bytes should be subtracted after a successful undo");
    }

    [Fact]
    public void CommandStack_CountLimitStillApplied()
    {
        // MaxUndoDepth = 100. Push 101 tiny commands and verify only 100 remain.
        // (Each tiny command reports 1 byte so the byte budget is nowhere near exhausted.)
        var bus = MakeBus();

        for (var i = 0; i < 101; i++)
            bus.Execute(WbId, new SizedCommand(1));

        // Pop all 100 commands.
        var undoCount = 0;
        while (bus.CanUndo(WbId))
        {
            bus.Undo(WbId).Success.Should().BeTrue();
            undoCount++;
        }

        undoCount.Should().Be(100, "the count cap should evict the oldest command when 101 are pushed");
    }

    [Fact]
    public void CommandStack_DefaultByteEstimateAppliedWhenIEstimatesMemoryNotImplemented()
    {
        // A command that does not implement IEstimatesMemory should not cause any
        // error and should be pushed / popped correctly.
        var bus = MakeBus();

        bus.Execute(WbId, new DefaultSizedCommand());
        bus.CanUndo(WbId).Should().BeTrue();

        bus.Undo(WbId).Success.Should().BeTrue();
        bus.CanUndo(WbId).Should().BeFalse();
    }

    [Fact]
    public void CommandStack_ByteBudgetRespectedAfterMultipleEvictions()
    {
        // Push 4 commands of 15 MB each (total would be 60 MB).
        // With a 50 MB budget and 15 MB each, at most 3 commands fit (45 MB <= 50 MB).
        // After pushing the 4th the 1st is evicted; after pushing a 5th the 2nd is
        // evicted, and so on.  Verify that at most 3 undos are ever available.
        const int fifteenMb = 15_728_640; // 15 MB
        var bus = MakeBus();

        for (var i = 0; i < 5; i++)
            bus.Execute(WbId, new SizedCommand(fifteenMb));

        var undoCount = 0;
        while (bus.CanUndo(WbId))
        {
            bus.Undo(WbId).Success.Should().BeTrue();
            undoCount++;
        }

        // 5 * 15 MB = 75 MB total; budget is 50 MB; at 15 MB per command the stack
        // holds floor(50 / 15) = 3 commands before the next push evicts the oldest.
        undoCount.Should().Be(3,
            "the byte-budget should limit the stack to at most 3 commands of 15 MB each");
    }
}
