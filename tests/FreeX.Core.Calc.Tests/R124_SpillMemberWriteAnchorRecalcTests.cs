using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// R124-calc-spill-member-write-anchor-recalc: R123 legalized typing/pasting/clearing directly
/// into a non-anchor member of a live dynamic-array spill (see
/// CommandGuards.RejectIfSplitsArray's allowDynamicSpillMemberWrite branch), on the claim that
/// "the owning anchor's next recalculation naturally detects the now-occupied cell". Nothing
/// actually scheduled that recalculation: every content-write command's CommandOutcome.AffectedCells
/// reports only the written member address, and a typical spilling formula (e.g. "=SEQUENCE(3,1)")
/// has zero cell references, so there is no dependency-graph edge from the member back to the
/// anchor. The anchor kept showing its stale pre-write value until an unrelated edit happened to
/// dirty its real precedents, or F9/Shift+F9 was pressed -- unlike real Excel, where the anchor
/// collapses to #SPILL! in the very same keystroke.
///
/// Fixed at the single choke point every edit (forward Execute AND Undo/Redo) funnels through --
/// RecalcEngine.Recalculate's private overload -- via ExpandChangedCellsWithSpillMemberAnchors,
/// so every content-write command family member (EditCellsCommand, ClearContentsCommand, the
/// paste family, the fill family) is covered without touching any of their call sites.
///
/// These tests replay the exact steps WorkbookCellEditService.ApplyHistoryOutcome performs after a
/// real IWorkbookCommand.Apply (UpdateFormulaDependencies-equivalent ClearFormulaDependencies pass,
/// then RecalcEngine.Recalculate fed exactly outcome.AffectedCells) -- the real production path for
/// everything downstream of the command layer, which is as close to the real product entry point as
/// this project (FreeX.Core.Calc, paired with FreeX.Core.Commands) can reach headless.
/// </summary>
public sealed class R124SpillMemberWriteAnchorRecalcTests
{
    private static (RecalcEngine Engine, Workbook Workbook, Sheet Sheet, CellAddress Anchor, ICommandContext Ctx) MakeLiveDynamicSpillSetup()
    {
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 1, 1); // A1
        sheet.SetFormula(anchor, "SEQUENCE(3,1)");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [anchor]);

        // Spilled successfully: A1:A3 = 1,2,3.
        sheet.GetValue(1, 1).Should().Be(new NumberValue(1));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(2));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(3));

        return (engine, wb, sheet, anchor, new TestCommandContext(wb));
    }

    /// <summary>
    /// Mirrors WorkbookCellEditService.ApplyHistoryOutcome's post-Apply steps exactly:
    /// ClearFormulaDependencies (RegisterFormulaDependencies is a no-op here since the member holds
    /// no formula) followed by Recalculate fed precisely outcome.AffectedCells -- nothing more.
    /// </summary>
    private static RecalcReport ReplayApplyHistoryOutcome(RecalcEngine engine, Workbook workbook, CommandOutcome outcome)
    {
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        var affectedCells = outcome.AffectedCells ?? [];
        foreach (var affected in affectedCells)
        {
            var cell = workbook.GetSheet(affected.Sheet)?.GetCell(affected);
            if (cell?.FormulaText is null)
                engine.ClearFormulaDependencies(affected);
        }
        return engine.Recalculate(workbook, affectedCells);
    }

    [Fact]
    public void EditCellsCommand_WriteIntoLiveSpillMember_AnchorShowsSpillImmediately()
    {
        var (engine, wb, sheet, anchor, ctx) = MakeLiveDynamicSpillSetup();
        var member = new CellAddress(sheet.Id, 2, 1); // A2 - covered, non-anchor

        // Real product entry point: EditCellsCommand.Apply, exactly as CommitCellText constructs
        // and executes it, with the R123 allowDynamicSpillMemberWrite guard letting the write
        // through instead of rejecting it.
        var outcome = EditCellsCommand.ForValue(sheet.Id, member, new NumberValue(999)).Apply(ctx);
        ReplayApplyHistoryOutcome(engine, wb, outcome);

        // The member write itself always took effect (R123, unaffected by this fix).
        sheet.GetValue(member).Should().Be(new NumberValue(999));

        // R124: the anchor must collapse to #SPILL! in this SAME recalc pass, matching Excel --
        // not stay stuck at its stale pre-write value of 1.
        sheet.GetValue(anchor).Should().Be(ErrorValue.Spill);
    }

    [Fact]
    public void PasteCellsCommand_WriteIntoLiveSpillMember_AnchorShowsSpillImmediately_SiblingFamilyMember()
    {
        // Sibling coverage: the fix lives in RecalcEngine (the single choke point every command's
        // CommandOutcome.AffectedCells funnels through), not in any individual command, so
        // PasteCellsCommand -- a different member of the same allowDynamicSpillMemberWrite family
        // named in CommandGuards.cs (EditCellsCommand, the paste family, ClearContentsCommand, the
        // fill family) -- must be covered too, with zero code change of its own. This is the direct
        // payoff of fixing the choke point instead of touching N call sites.
        var (engine, wb, sheet, anchor, ctx) = MakeLiveDynamicSpillSetup();
        var member = new CellAddress(sheet.Id, 3, 1); // A3 - covered, non-anchor

        var command = new PasteCellsCommand(sheet.Id, [(member, Cell.FromValue(new NumberValue(777)))]);
        var outcome = command.Apply(ctx);
        ReplayApplyHistoryOutcome(engine, wb, outcome);

        sheet.GetValue(member).Should().Be(new NumberValue(777));
        sheet.GetValue(anchor).Should().Be(ErrorValue.Spill);
    }

    [Fact]
    public void UnrelatedCellEdit_OnSheetWithLiveSpill_DoesNotDisturbSpill_NoRegression()
    {
        // No-regression check: ExpandChangedCellsWithSpillMemberAnchors must only add an anchor when
        // a CHANGED address actually falls inside its live spill extent. An ordinary edit to some
        // unrelated cell on the very same sheet (which does have a live spill elsewhere) must not
        // needlessly touch the spill anchor or its values.
        var (engine, wb, sheet, anchor, ctx) = MakeLiveDynamicSpillSetup();
        var unrelated = new CellAddress(sheet.Id, 10, 10); // far outside the A1:A3 spill extent

        var outcome = EditCellsCommand.ForValue(sheet.Id, unrelated, new NumberValue(42)).Apply(ctx);
        var report = ReplayApplyHistoryOutcome(engine, wb, outcome);

        sheet.GetValue(unrelated).Should().Be(new NumberValue(42));
        report.RecalculatedCells.Should().NotContain(anchor);
        sheet.GetValue(anchor).Should().Be(new NumberValue(1));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(2));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void EditCellsCommand_OnAnchorItselfDirectly_StillWorks_NoRegression()
    {
        // Sibling/no-regression check for the R112-array-anchor-edit carve-out: retyping the anchor
        // cell directly (not a member) is a normal formula edit. Sheet.SetCell already tears down
        // the old spill registration as part of that same write (see Sheet.SetCell), so by the time
        // RecalcEngine's new expansion runs, TryGetArrayExtent(anchor) must no longer find a stale
        // registration to (redundantly, but harmlessly) re-add -- confirm the anchor edit still
        // produces the freshly-typed value with no error or leftover stale state.
        var (engine, wb, sheet, anchor, ctx) = MakeLiveDynamicSpillSetup();

        var outcome = EditCellsCommand.ForValue(sheet.Id, anchor, new NumberValue(7)).Apply(ctx);
        ReplayApplyHistoryOutcome(engine, wb, outcome);

        sheet.GetValue(anchor).Should().Be(new NumberValue(7));
        sheet.TryGetSpillExtent(anchor, out _, out _).Should().BeFalse();
    }
}
