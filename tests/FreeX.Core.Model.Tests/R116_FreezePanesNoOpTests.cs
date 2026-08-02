using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R116: SetFreezePanesCommand must treat a value-identical re-apply (same FrozenRows/FrozenCols
/// as the sheet already has) as a no-op, mirroring MoveSheetCommand's `_fromIndex == _toIndex`
/// convention (SheetCommands.cs). Before this fix, Apply() unconditionally reassigned
/// FrozenRows/FrozenCols and returned `new CommandOutcome(true)` with no IsNoOp flag, so
/// CommandBus.Execute (which only skips the undo-stack push when outcome.IsNoOp is true) pushed a
/// second, indistinguishable "Freeze Panes" entry onto the undo stack every time the user re-issued
/// an already-applied freeze -- a very plausible click since neither shell's Freeze Panes menu item
/// shows an "already frozen" indicator the way Excel's combined Freeze/Unfreeze toggle does.
/// </summary>
public sealed class R116_FreezePanesNoOpTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    [Fact]
    public void Apply_SameFrozenRowsAndCols_ReturnsNoOpAndLeavesSheetUnchanged()
    {
        var (_, sheet, ctx) = Setup();
        sheet.FrozenRows = 1;
        sheet.FrozenCols = 0;

        var cmd = new SetFreezePanesCommand(sheet.Id, frozenRows: 1, frozenCols: 0);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.IsNoOp.Should().BeTrue("re-freezing at the position the sheet is already frozen at must be a no-op");
        sheet.FrozenRows.Should().Be(1);
        sheet.FrozenCols.Should().Be(0);
    }

    [Fact]
    public void Apply_ReapplyingUnfrozenState_ReturnsNoOp()
    {
        var (_, sheet, ctx) = Setup();
        // Sheet starts with FrozenRows/Cols == 0 by default (no freeze).
        sheet.FrozenRows.Should().Be(0);
        sheet.FrozenCols.Should().Be(0);

        var cmd = new SetFreezePanesCommand(sheet.Id, frozenRows: 0, frozenCols: 0);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.IsNoOp.Should().BeTrue("clicking Unfreeze Panes when nothing is frozen must be a no-op");
    }

    [Fact]
    public void Execute_ReissuingSameFreeze_DoesNotPushSecondUndoEntry()
    {
        // Real product entry point: CommandBus.Execute, exactly what both
        // MainWindow.ViewCommands.cs's SetFreezePanes() and WorkbookSession.cs's SetFreezePanes()
        // invoke.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var bus = new CommandBus(_ => new TestCommandContext(wb));

        var firstOutcome = bus.Execute(wb.Id, new SetFreezePanesCommand(sheet.Id, frozenRows: 1, frozenCols: 0));
        firstOutcome.Success.Should().BeTrue();
        firstOutcome.IsNoOp.Should().BeFalse("the first freeze is a genuine state change");
        sheet.FrozenRows.Should().Be(1);

        // Re-click "Freeze Top Row" again -- nothing visibly changes.
        var secondOutcome = bus.Execute(wb.Id, new SetFreezePanesCommand(sheet.Id, frozenRows: 1, frozenCols: 0));
        secondOutcome.Success.Should().BeTrue();
        secondOutcome.IsNoOp.Should().BeTrue();
        sheet.FrozenRows.Should().Be(1);

        // Exactly one undo entry should exist: a single Undo must return to the pre-freeze state,
        // and there must be nothing further to undo.
        bus.CanUndo(wb.Id).Should().BeTrue();
        bus.Undo(wb.Id);
        sheet.FrozenRows.Should().Be(0, "the single real freeze must have been undone");
        bus.CanUndo(wb.Id).Should().BeFalse("the redundant re-apply must not have pushed a second undo entry");
    }

    [Fact]
    public void Apply_SameCountsButClearingLingeringSplit_IsNotANoOp()
    {
        // Sibling/regression coverage: a freeze re-apply that would still clear a lingering
        // Split (e.g. a workbook loaded with both fields set) is a REAL mutation and must not be
        // short-circuited as a no-op, even though FrozenRows/FrozenCols themselves are unchanged.
        var (_, sheet, ctx) = Setup();
        sheet.FrozenRows = 1;
        sheet.FrozenCols = 0;
        sheet.SplitRow = 5;
        sheet.SplitColumn = 3;

        var cmd = new SetFreezePanesCommand(sheet.Id, frozenRows: 1, frozenCols: 0);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.IsNoOp.Should().BeFalse("clearing a lingering split is a real mutation, not a no-op");
        sheet.SplitRow.Should().BeNull();
        sheet.SplitColumn.Should().BeNull();

        cmd.Revert(ctx);
        sheet.SplitRow.Should().Be(5);
        sheet.SplitColumn.Should().Be(3);
    }

    [Fact]
    public void Apply_DifferentFrozenCounts_StillAppliesAndIsNotNoOp()
    {
        // No-regression: the ordinary value-change path (already covered by
        // FreezePanesCommandTests) must remain a real, undoable mutation.
        var (_, sheet, ctx) = Setup();
        sheet.FrozenRows = 1;
        sheet.FrozenCols = 0;

        var cmd = new SetFreezePanesCommand(sheet.Id, frozenRows: 3, frozenCols: 2);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.IsNoOp.Should().BeFalse();
        sheet.FrozenRows.Should().Be(3);
        sheet.FrozenCols.Should().Be(2);

        cmd.Revert(ctx);
        sheet.FrozenRows.Should().Be(1);
        sheet.FrozenCols.Should().Be(0);
    }
}
