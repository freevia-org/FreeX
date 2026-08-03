using Free.Shared.Commands;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R120-undo-stack-self-eviction-1: <see cref="UndoRedoStack{TCommand,TPayload}.TrimUndoStack"/>'s
/// byte-budget branch did not exempt the sole remaining (just-pushed newest) entry -- with
/// <c>Count == 1</c>, `Count > 0 &amp;&amp; bytes > max` was still true, so if a single command's own
/// <see cref="IEstimatesMemory.EstimatedBytes"/> already exceeded <c>CommandBus.MaxUndoByteBudget</c>
/// (50 MB) the while-loop kept evicting from the oldest end until the stack was empty -- including the
/// one entry that was just pushed. Before r119 (which added SortCommand at 400 bytes/cell,
/// PasteCellsCommand at 300 bytes/cell, and RemoveSheetCommand at 200 bytes/occupied-cell) this was
/// practically unreachable, because every other command was billed a flat 200-byte default and could
/// not plausibly reach 50 MB alone. r119's SortCommand is the richest per-cell constant in the
/// codebase, so an entirely ordinary large sort (more than 131,072 cells) now trips the bug: the
/// user's own just-performed Sort becomes silently un-undoable the instant it completes.
/// </summary>
public sealed class R120_UndoRedoStackSelfEvictionTests
{
    private static readonly WorkbookId WbId = WorkbookId.New();

    private static CommandBus MakeBus(Workbook wb) => new(_ => new TestCommandContext(wb));

    private static void FillRange(Sheet sheet, uint rows, uint cols)
    {
        for (uint r = 1; r <= rows; r++)
            for (uint c = 1; c <= cols; c++)
                sheet.SetCell(new CellAddress(sheet.Id, r, c), Cell.FromValue(new NumberValue(r * 1000 + c)));
    }

    // ── Real product entry point: CommandBus.Execute → Push → TrimUndoStack ────────────────────

    [Fact]
    public void R120_CommandBus_SingleSortLargerThanBudgetAlone_RemainsUndoableImmediatelyAfterPush()
    {
        // 400 x 400 = 160,000 cells * 400 bytes/cell (SortCommand.BytesPerCell) = 64,000,000 bytes
        // (~61 MB), which by itself already exceeds MaxUndoByteBudget (52,428,800 / ~50 MB). This is
        // an entirely ordinary "sort a large data range" scenario (well within Excel's row/column
        // limits), not a contrived stress case.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        FillRange(sheet, 400, 400);
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 400, 400));
        var bus = MakeBus(wb);

        var result = bus.Execute(WbId, new SortCommand(sheet.Id, range, sortByColOffset: 0, ascending: true));
        result.Success.Should().BeTrue();

        bus.CanUndo(WbId).Should().BeTrue(
            "the user must be able to Ctrl+Z the sort they just performed, even though its own " +
            "estimated undo-snapshot size exceeds the 50 MB budget by itself -- the byte budget may " +
            "evict OLDER entries to make room, but must never evict the sole just-pushed entry");

        bus.Undo(WbId).Success.Should().BeTrue("the surviving entry must actually be revertible, not just reported as present");
        bus.CanUndo(WbId).Should().BeFalse("the single undo consumed the only entry on the stack");
    }

    [Fact]
    public void R120_CommandBus_TwoHugeSortsBackToBack_OldestStillEvicted_NewestSurvives_NoRegression()
    {
        // Sibling/no-regression coverage for the neighbouring behaviour: the byte budget must still
        // do its job of evicting an OLDER entry once a newer one exists alongside it. Two sorts that
        // each individually exceed the budget (~61 MB each, ~122 MB together) must still end up with
        // exactly the newest one undoable -- the fix must not turn off byte-budget eviction wholesale,
        // only stop it from touching the sole/newest entry.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        FillRange(sheet, 400, 400);
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 400, 400));
        var bus = MakeBus(wb);

        bus.Execute(WbId, new SortCommand(sheet.Id, range, sortByColOffset: 0, ascending: true)).Success.Should().BeTrue();
        bus.CanUndo(WbId).Should().BeTrue("the first huge sort alone must survive its own push (R120 fix)");

        bus.Execute(WbId, new SortCommand(sheet.Id, range, sortByColOffset: 0, ascending: false)).Success.Should().BeTrue();

        var undoCount = 0;
        while (bus.CanUndo(WbId))
        {
            bus.Undo(WbId).Success.Should().BeTrue();
            undoCount++;
        }

        undoCount.Should().Be(1,
            "with two entries on the stack and the combined size over budget, the OLDER entry must " +
            "still be evicted -- only the sole/newest entry is exempt from byte-budget eviction");
    }

    // ── Direct engine-level coverage of the fixed TrimUndoStack contract ───────────────────────

    private sealed record FakeCommand(string Name);

    [Fact]
    public void R120_UndoRedoStack_SinglePushExceedingByteBudgetAlone_IsNotEvicted()
    {
        var stack = new UndoRedoStack<FakeCommand, object?>(maxDepth: 100, maxBytes: 1_000);

        stack.Push(new FakeCommand("huge"), bytes: 5_000, payload: null, label: "huge");

        stack.CanUndo.Should().BeTrue("a lone entry must never be evicted purely because its own size exceeds the byte budget");
        stack.UndoDepth.Should().Be(1);
    }

    [Fact]
    public void R120_UndoRedoStack_DepthCapStillEvictsSoleEntry_WhenDepthCapIsZero()
    {
        // The depth cap is a distinct, unrelated limit from the byte budget and must remain free to
        // trim down to (and including) the newest entry -- this test pins that the R120 fix only
        // narrows the BYTE-BUDGET branch, not the depth-cap branch.
        var stack = new UndoRedoStack<FakeCommand, object?>(maxDepth: 0, maxBytes: 1_000_000);

        stack.Push(new FakeCommand("a"), bytes: 10, payload: null, label: "a");

        stack.CanUndo.Should().BeFalse("a maxDepth of 0 is a hard cap that legitimately evicts everything, including the newest entry");
    }

    [Fact]
    public void R120_UndoRedoStack_SecondPushOverBudget_EvictsOldestOnly_NewestSurvives()
    {
        var stack = new UndoRedoStack<FakeCommand, object?>(maxDepth: 100, maxBytes: 1_000);

        stack.Push(new FakeCommand("first"), bytes: 800, payload: null, label: "first");
        stack.Push(new FakeCommand("second"), bytes: 800, payload: null, label: "second");

        stack.UndoDepth.Should().Be(1, "the combined 1,600 bytes exceeds the 1,000 byte budget, so the oldest entry is evicted");
        stack.TryPeekUndo(out var entry).Should().BeTrue();
        entry.Label.Should().Be("second", "the newest entry must be the one that survives");
    }
}
