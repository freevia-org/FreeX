using FluentAssertions;

using FreeX.App.Avalonia.Dialogs;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.Dialogs;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression tests for the Avalonia Manage Conditional Formatting Rules dialog's buffered-edit
/// contract (review finding H32) and the rule editor's edit-preserves-StopIfTrue contract (H35).
///
/// H32: every New/Edit/Delete/Duplicate/Move/AppliesTo action in the dialog must mutate an in-memory
/// working copy only — nothing may reach the live sheet until a single commit. <see cref="ConditionalFormatManageModel.CloneAll"/>
/// seeds that working copy with independent clones, and the working-copy mutators
/// (<see cref="ConditionalFormatManageModel.AddToWorkingCopy"/>, <see cref="ConditionalFormatManageModel.DeleteFromWorkingCopy"/>,
/// <see cref="ConditionalFormatManageModel.ReplaceInWorkingCopy"/>, <see cref="ConditionalFormatManageModel.DuplicateInWorkingCopy"/>,
/// <see cref="ConditionalFormatManageModel.MoveInWorkingCopy"/>, <see cref="ConditionalFormatManageModel.ApplyRangeInWorkingCopy"/>)
/// never touch the source list or the rules within it, so Cancel (simply discarding the working copy)
/// can never leave a partial edit applied to the workbook.
///
/// H35: <see cref="ConditionalFormatRuleBuilder.TryBuildApplyCommand"/> must forward the rule being
/// edited so an edit clones (not recreates) the existing rule, preserving fields the editor doesn't
/// surface — most importantly <see cref="ConditionalFormat.StopIfTrue"/>.
/// </summary>
public sealed class ConditionalFormatManageWorkingCopyTests
{
    // ── H32: CloneAll seeds an independent working copy ───────────────────────

    [Fact]
    public void CloneAll_ProducesIndependentClones_MutatingWorkingCopyDoesNotAffectSource()
    {
        var sheet = SheetId();
        var source = new List<ConditionalFormat> { RuleAt(sheet, 0, 0, 1, 1) };

        var workingCopy = ConditionalFormatManageModel.CloneAll(source);
        workingCopy[0].StopIfTrue = true;
        workingCopy[0].Value1 = "changed";

        source[0].StopIfTrue.Should().BeFalse("the working copy must be a deep clone, not the same instances");
        source[0].Value1.Should().BeNull();
    }

    [Fact]
    public void CloneAll_PreservesPriorityOrder()
    {
        var sheet = SheetId();
        var second = RuleAt(sheet, 5, 5, 6, 6);
        second.Priority = 2;
        var first = RuleAt(sheet, 0, 0, 1, 1);
        first.Priority = 1;
        var source = new List<ConditionalFormat> { second, first };

        var workingCopy = ConditionalFormatManageModel.CloneAll(source);

        workingCopy.Select(r => r.Priority).Should().Equal(1, 2);
    }

    // ── H32: working-copy mutators never touch the source list's rules ────────

    [Fact]
    public void DeleteFromWorkingCopy_DoesNotMutateSourceListOrItsRules()
    {
        var sheet = SheetId();
        var rule = RuleAt(sheet, 0, 0, 1, 1);
        var source = new List<ConditionalFormat> { rule };

        var result = ConditionalFormatManageModel.DeleteFromWorkingCopy(source, rule.Id);

        result.Should().NotBeNull().And.BeEmpty();
        source.Should().ContainSingle().Which.Should().BeSameAs(rule, "the source list itself must be untouched");
    }

    [Fact]
    public void ReplaceInWorkingCopy_DoesNotMutateSourceListOrItsRules()
    {
        var sheet = SheetId();
        var rule = RuleAt(sheet, 0, 0, 1, 1);
        rule.Value1 = "original";
        var source = new List<ConditionalFormat> { rule };
        var edited = rule.Clone();
        edited.Value1 = "edited";

        var result = ConditionalFormatManageModel.ReplaceInWorkingCopy(source, edited);

        result.Should().NotBeNull();
        result!.Single().Value1.Should().Be("edited");
        source.Single().Value1.Should().Be("original", "editing the working copy must not reach the source list");
    }

    [Fact]
    public void DuplicateInWorkingCopy_DoesNotChangeSourceListCount()
    {
        var sheet = SheetId();
        var rule = RuleAt(sheet, 0, 0, 1, 1);
        var source = new List<ConditionalFormat> { rule };

        var result = ConditionalFormatManageModel.DuplicateInWorkingCopy(source, rule.Id, Guid.NewGuid());

        result.Should().NotBeNull().And.HaveCount(2);
        source.Should().ContainSingle("duplicating in the working copy must not touch the source list");
    }

    [Fact]
    public void MoveInWorkingCopy_DoesNotChangeSourceListOrder()
    {
        var sheet = SheetId();
        var first = RuleAt(sheet, 0, 0, 1, 1);
        first.Priority = 1;
        var second = RuleAt(sheet, 5, 5, 6, 6);
        second.Priority = 2;
        var source = new List<ConditionalFormat> { first, second };

        var result = ConditionalFormatManageModel.MoveInWorkingCopy(source, scope: null, second.Id, ConditionalFormatRuleMoveDirection.Up);

        result.Should().NotBeNull();
        result![0].Id.Should().Be(second.Id);
        source[0].Id.Should().Be(first.Id, "moving in the working copy must not reorder the source list");
        source[1].Id.Should().Be(second.Id);
    }

    // ── R120: Move Up/Down must reorder against the FILTERED/displayed list, not the full
    //          unfiltered priority order, or a Move click silently reprioritizes a hidden rule the
    //          user never saw instead of the visible neighbour the user meant to swap with. ──────

    /// <summary>
    /// Reproduces the reported scenario exactly: RuleA (A1:A10, priority 1) and RuleC (A1:A10,
    /// StopIfTrue, priority 3) both show for a selection of A1; RuleB (A5:A5 only, priority 2) does
    /// not, since its range doesn't overlap the single-cell selection. The user selects RuleC (the
    /// bottom of the two-item VISIBLE list) and clicks Move Up, expecting it to swap with RuleA — the
    /// only rule they can see above it. Through the real command path (BuildMoveCommand ->
    /// ReplaceAllConditionalFormatsCommand -> session), RuleC must end up above RuleA, and RuleB —
    /// never shown to the user — must keep its unrelated priority slot rather than being silently
    /// swapped in.
    /// </summary>
    [Fact]
    public void R120_MoveInWorkingCopy_FilteredScope_SwapsWithVisibleNeighbour_NotHiddenFullListNeighbour()
    {
        var session = CreateSession();
        var sheetId = session.ActiveSheet.Id;
        var wholeColumn = RangeAt(sheetId, 0, 0, 9, 0); // A1:A10
        var a5Only = RangeAt(sheetId, 4, 0, 4, 0); // A5:A5 only

        var ruleA = ConditionalFormatRuleBuilder.Build(new CfRuleInput { RuleType = CfRuleType.DataBar }, wholeColumn);
        var ruleB = ConditionalFormatRuleBuilder.Build(new CfRuleInput { RuleType = CfRuleType.IconSet }, a5Only);
        var ruleC = ConditionalFormatRuleBuilder.Build(new CfRuleInput { RuleType = CfRuleType.ColorScale }, wholeColumn);
        ruleC.StopIfTrue = true;
        session.ExecuteReviewCommand(ConditionalFormatRuleBuilder.ToApplyCommand(sheetId, ruleA));
        session.ExecuteReviewCommand(ConditionalFormatRuleBuilder.ToApplyCommand(sheetId, ruleB));
        session.ExecuteReviewCommand(ConditionalFormatRuleBuilder.ToApplyCommand(sheetId, ruleC));

        // Sanity: seeded in the reported priority order (A=1, B=2, C=3) before any move.
        session.ActiveSheet.ConditionalFormats.Single(r => r.Id == ruleA.Id).Priority.Should().Be(1);
        session.ActiveSheet.ConditionalFormats.Single(r => r.Id == ruleB.Id).Priority.Should().Be(2);
        session.ActiveSheet.ConditionalFormats.Single(r => r.Id == ruleC.Id).Priority.Should().Be(3);

        // The dialog's default "Current Selection" scope, with the user's selection collapsed to A1.
        var selection = RangeAt(sheetId, 0, 0, 0, 0);

        var moveCommand = ConditionalFormatManageModel.BuildMoveCommand(
            sheetId, session.ActiveSheet.ConditionalFormats, selection, ruleC.Id, ConditionalFormatRuleMoveDirection.Up);
        moveCommand.Should().NotBeNull("RuleC has a visible neighbour (RuleA) above it in the A1-filtered list");
        session.ExecuteReviewCommand(moveCommand!).Success.Should().BeTrue();

        session.ActiveSheet.ConditionalFormats.Single(r => r.Id == ruleC.Id).Priority.Should().Be(
            1, "RuleC must move above RuleA, the rule the user actually sees adjacent to it in the filtered list");
        session.ActiveSheet.ConditionalFormats.Single(r => r.Id == ruleA.Id).Priority.Should().Be(
            3, "RuleA (the swapped visible neighbour) takes RuleC's old slot");
        session.ActiveSheet.ConditionalFormats.Single(r => r.Id == ruleB.Id).Priority.Should().Be(
            2, "RuleB was never shown to the user for this scope, so a Move click must not touch its priority at all");
    }

    /// <summary>
    /// No-regression sibling: with no scope filter (e.g. "This Worksheet"), every rule is visible, so
    /// the filtered-neighbour computation must degenerate back to the original full-list adjacent
    /// swap — Move Up on RuleC in an unfiltered 3-rule list still swaps it with its immediate
    /// full-list neighbour RuleB, exactly as before this fix.
    /// </summary>
    [Fact]
    public void R120_MoveInWorkingCopy_NoScope_StillSwapsWithFullListAdjacentNeighbour()
    {
        var session = CreateSession();
        var sheetId = session.ActiveSheet.Id;
        var wholeColumn = RangeAt(sheetId, 0, 0, 9, 0);
        var a5Only = RangeAt(sheetId, 4, 0, 4, 0);

        var ruleA = ConditionalFormatRuleBuilder.Build(new CfRuleInput { RuleType = CfRuleType.DataBar }, wholeColumn);
        var ruleB = ConditionalFormatRuleBuilder.Build(new CfRuleInput { RuleType = CfRuleType.IconSet }, a5Only);
        var ruleC = ConditionalFormatRuleBuilder.Build(new CfRuleInput { RuleType = CfRuleType.ColorScale }, wholeColumn);
        session.ExecuteReviewCommand(ConditionalFormatRuleBuilder.ToApplyCommand(sheetId, ruleA));
        session.ExecuteReviewCommand(ConditionalFormatRuleBuilder.ToApplyCommand(sheetId, ruleB));
        session.ExecuteReviewCommand(ConditionalFormatRuleBuilder.ToApplyCommand(sheetId, ruleC));

        var moveCommand = ConditionalFormatManageModel.BuildMoveCommand(
            sheetId, session.ActiveSheet.ConditionalFormats, scope: null, ruleC.Id, ConditionalFormatRuleMoveDirection.Up);
        moveCommand.Should().NotBeNull();
        session.ExecuteReviewCommand(moveCommand!).Success.Should().BeTrue();

        session.ActiveSheet.ConditionalFormats.Single(r => r.Id == ruleC.Id).Priority.Should().Be(2);
        session.ActiveSheet.ConditionalFormats.Single(r => r.Id == ruleB.Id).Priority.Should().Be(3);
        session.ActiveSheet.ConditionalFormats.Single(r => r.Id == ruleA.Id).Priority.Should().Be(
            1, "RuleA is untouched since it was never adjacent to the moved rule");
    }

    [Fact]
    public void ApplyRangeInWorkingCopy_DoesNotMutateSourceRuleRange()
    {
        var sheet = SheetId();
        var rule = RuleAt(sheet, 0, 0, 1, 1);
        var originalRange = rule.AppliesTo;
        var source = new List<ConditionalFormat> { rule };
        var newRange = RangeAt(sheet, 9, 9, 12, 12);

        var result = ConditionalFormatManageModel.ApplyRangeInWorkingCopy(source, rule.Id, newRange);

        result.Should().NotBeNull();
        result!.Single().AppliesTo.Should().Be(newRange);
        source.Single().AppliesTo.Should().Be(originalRange, "changing the applies-to range in the working copy must not touch the source rule");
    }

    // ── R82-app-dialog-parity-5-2: ApplyRangeInWorkingCopy must clear stale AdditionalRanges ──

    [Fact]
    public void ApplyRangeInWorkingCopy_ClearsStaleAdditionalRanges_WhenReplacingWithASingleNewRange()
    {
        // Rule's "Applies to" originally spanned two non-contiguous areas: A1:A5 (AppliesTo) and
        // C1:C5 (AdditionalRanges). The Avalonia Applies-To editor only ever supplies ONE resolved
        // range (it has no multi-area UI and the box doesn't even display AdditionalRanges), so
        // retyping a single new range must replace the WHOLE applies-to, not just its primary area —
        // otherwise C1:C5 keeps silently getting the conditional format even though the user never
        // re-selected it and can't see it in the edit box.
        var sheet = SheetId();
        var rule = RuleAt(sheet, 0, 0, 4, 0); // A1:A5
        rule.AdditionalRanges = [RangeAt(sheet, 0, 2, 4, 2)]; // C1:C5
        var source = new List<ConditionalFormat> { rule };
        var newRange = RangeAt(sheet, 0, 1, 4, 1); // B1:B5

        var result = ConditionalFormatManageModel.ApplyRangeInWorkingCopy(source, rule.Id, newRange);

        result.Should().NotBeNull();
        var updated = result!.Single();
        updated.AppliesTo.Should().Be(newRange);
        updated.AdditionalRanges.Should().BeNull(
            "a retyped single-range Applies-To must drop any stale multi-area remainder, matching " +
            "Excel and the WPF host's Applies-To LostFocus handler");
    }

    [Fact]
    public void ApplyRangeInWorkingCopy_WithNoAdditionalRanges_LeavesAdditionalRangesNull()
    {
        // No-regression sibling: the ordinary single-area case (no AdditionalRanges to begin with)
        // must keep working exactly as before — AdditionalRanges simply stays null.
        var sheet = SheetId();
        var rule = RuleAt(sheet, 0, 0, 4, 0);
        var source = new List<ConditionalFormat> { rule };
        var newRange = RangeAt(sheet, 0, 1, 4, 1);

        var result = ConditionalFormatManageModel.ApplyRangeInWorkingCopy(source, rule.Id, newRange);

        result.Should().NotBeNull();
        var updated = result!.Single();
        updated.AppliesTo.Should().Be(newRange);
        updated.AdditionalRanges.Should().BeNull();
    }

    [Fact]
    public void AddToWorkingCopy_DoesNotMutateSourceList()
    {
        var sheet = SheetId();
        var source = new List<ConditionalFormat> { RuleAt(sheet, 0, 0, 1, 1) };
        var added = RuleAt(sheet, 2, 2, 3, 3);

        var result = ConditionalFormatManageModel.AddToWorkingCopy(source, added);

        result.Should().HaveCount(2);
        source.Should().ContainSingle("appending to the working copy must not touch the source list");
    }

    // ── H32: a Cancel-shaped scenario never builds/executes a command ─────────

    [Fact]
    public void CancelScenario_WorkingCopyEditsNeverProduceALiveCommand_SheetStaysUntouched()
    {
        var session = CreateSession();
        var sheetId = session.ActiveSheet.Id;
        var range = SelectionRange(session);
        var rule = ConditionalFormatRuleBuilder.Build(new CfRuleInput { RuleType = CfRuleType.DataBar }, range);
        session.ExecuteReviewCommand(ConditionalFormatRuleBuilder.ToApplyCommand(sheetId, rule));
        var another = ConditionalFormatRuleBuilder.Build(new CfRuleInput { RuleType = CfRuleType.IconSet }, range);
        session.ExecuteReviewCommand(ConditionalFormatRuleBuilder.ToApplyCommand(sheetId, another));
        var third = ConditionalFormatRuleBuilder.Build(new CfRuleInput { RuleType = CfRuleType.ColorScale }, range);
        session.ExecuteReviewCommand(ConditionalFormatRuleBuilder.ToApplyCommand(sheetId, third));

        // Simulate opening Manage Rules, deleting one rule and moving another — exactly what the
        // dialog's working copy would do — then simulate Cancel: never build/execute a replace-all
        // command from the mutated working copy. (Three rules seeded so the post-delete move is a
        // REAL reorder: MoveInWorkingCopy returns null for an impossible boundary move, e.g.
        // moving the only remaining rule.)
        var workingRules = ConditionalFormatManageModel.CloneAll(session.ActiveSheet.ConditionalFormats);
        var afterDelete = ConditionalFormatManageModel.DeleteFromWorkingCopy(workingRules, rule.Id);
        afterDelete.Should().NotBeNull();
        var afterMove = ConditionalFormatManageModel.MoveInWorkingCopy(afterDelete!, scope: null, another.Id, ConditionalFormatRuleMoveDirection.Down);
        afterMove.Should().NotBeNull();

        // Cancel: the working copy (afterMove) is simply discarded — no command built, no command executed.

        session.ActiveSheet.ConditionalFormats.Should().HaveCount(3, "Cancel must leave the live sheet exactly as it was before the dialog opened");
        session.ActiveSheet.ConditionalFormats.Should().Contain(r => r.Id == rule.Id);
        session.ActiveSheet.ConditionalFormats.Should().Contain(r => r.Id == another.Id);
        session.ActiveSheet.ConditionalFormats.Should().Contain(r => r.Id == third.Id);
    }

    [Fact]
    public void CommitScenario_ReplaceAllConditionalFormatsCommand_AppliesWorkingCopyInOneUndoStep()
    {
        var session = CreateSession();
        var sheetId = session.ActiveSheet.Id;
        var range = SelectionRange(session);
        var keep = ConditionalFormatRuleBuilder.Build(new CfRuleInput { RuleType = CfRuleType.DataBar }, range);
        var drop = ConditionalFormatRuleBuilder.Build(new CfRuleInput { RuleType = CfRuleType.IconSet }, range);
        session.ExecuteReviewCommand(ConditionalFormatRuleBuilder.ToApplyCommand(sheetId, keep));
        session.ExecuteReviewCommand(ConditionalFormatRuleBuilder.ToApplyCommand(sheetId, drop));

        var workingRules = ConditionalFormatManageModel.CloneAll(session.ActiveSheet.ConditionalFormats);
        var afterDelete = ConditionalFormatManageModel.DeleteFromWorkingCopy(workingRules, drop.Id);
        afterDelete.Should().NotBeNull();

        // Commit: exactly one ReplaceAllConditionalFormatsCommand for every buffered edit.
        var outcome = session.ExecuteReviewCommand(new ReplaceAllConditionalFormatsCommand(sheetId, afterDelete!));

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        session.ActiveSheet.ConditionalFormats.Should().ContainSingle().Which.Id.Should().Be(keep.Id);

        // A single undo reverts the whole dialog session at once.
        var undo = session.UndoLastEdit();
        undo.Success.Should().BeTrue(undo.ErrorMessage);
        session.ActiveSheet.ConditionalFormats.Should().HaveCount(2, "one undo must restore both rules from the single atomic commit");
    }

    // ── H35: editing a rule through TryBuildApplyCommand preserves StopIfTrue ─

    [Fact]
    public void TryBuildApplyCommand_EditingExistingRule_PreservesStopIfTrue()
    {
        var range = Range();
        var existingRule = ConditionalFormatRuleBuilder.Build(
            new CfRuleInput { RuleType = CfRuleType.CellValue, Operator = CfOperator.GreaterThan, Value1 = "5" },
            range);
        existingRule.StopIfTrue = true;

        // The rule editor's OK handler re-submits the same fields (as if the user made no change to
        // Stop If True, which the shared CfRuleInput schema does not surface) but must now forward
        // existingRule so Build clones it instead of starting a fresh ConditionalFormat.
        var input = new CfRuleInput { RuleType = CfRuleType.CellValue, Operator = CfOperator.GreaterThan, Value1 = "9" };
        var result = ConditionalFormatRuleBuilder.TryBuildApplyCommand(
            input, SheetId(), range, existingRule: existingRule);

        result.IsValid.Should().BeTrue();
        result.Rule!.StopIfTrue.Should().BeTrue("editing a rule must not silently reset StopIfTrue to false");
        result.Rule.Value1.Should().Be("9", "the edited field must still take effect");
        result.Rule.Id.Should().Be(existingRule.Id, "editing must keep the rule's identity");
    }

    [Fact]
    public void TryBuildApplyCommand_WithoutExistingRule_DefaultsStopIfTrueToFalse()
    {
        var input = new CfRuleInput { RuleType = CfRuleType.CellValue, Operator = CfOperator.GreaterThan, Value1 = "5" };

        var result = ConditionalFormatRuleBuilder.TryBuildApplyCommand(input, SheetId(), Range());

        result.IsValid.Should().BeTrue();
        result.Rule!.StopIfTrue.Should().BeFalse();
    }

    [Fact]
    public void TryBuildApplyCommand_EditingExistingRule_ThroughSession_PersistsStopIfTrue()
    {
        var session = CreateSession();
        var sheetId = session.ActiveSheet.Id;
        var range = SelectionRange(session);
        var existingRule = ConditionalFormatRuleBuilder.Build(
            new CfRuleInput { RuleType = CfRuleType.CellValue, Operator = CfOperator.GreaterThan, Value1 = "5" },
            range);
        existingRule.StopIfTrue = true;
        session.ExecuteReviewCommand(ConditionalFormatRuleBuilder.ToApplyCommand(sheetId, existingRule));

        var input = new CfRuleInput { RuleType = CfRuleType.CellValue, Operator = CfOperator.GreaterThan, Value1 = "9" };
        var build = ConditionalFormatRuleBuilder.TryBuildApplyCommand(
            input, sheetId, range, existingRule: existingRule);
        build.IsValid.Should().BeTrue();
        var outcome = session.ExecuteReviewCommand(build.Command!);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        session.ActiveSheet.ConditionalFormats.Should().ContainSingle()
            .Which.StopIfTrue.Should().BeTrue("the edit committed through the session must retain StopIfTrue");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static SheetId SheetId() => new(Guid.NewGuid());

    private static GridRange Range() => RangeAt(SheetId(), 0, 0, 4, 0);

    private static GridRange RangeAt(SheetId sheet, uint r1, uint c1, uint r2, uint c2) =>
        new(new CellAddress(sheet, r1, c1), new CellAddress(sheet, r2, c2));

    private static ConditionalFormat RuleAt(SheetId sheet, uint r1, uint c1, uint r2, uint c2) =>
        new() { AppliesTo = RangeAt(sheet, r1, c1, r2, c2), RuleType = CfRuleType.DataBar };

    private static WorkbookSession CreateSession() =>
        new WorkbookSessionFactory().CreateNew(viewportHeight: 240, viewportWidth: 320);

    private static GridRange SelectionRange(WorkbookSession session)
    {
        var sheet = session.ActiveSheet.Id;
        return new GridRange(new CellAddress(sheet, 0, 0), new CellAddress(sheet, 4, 0));
    }
}
