using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.Dialogs;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.ConditionalFormatting;

public sealed class ManageConditionalFormatsSessionTests
{
    [Fact]
    public void FullSheetWorkingCopy_ClonesSourceAndNormalizesPriorityOrder()
    {
        var sheetId = SheetId.New();
        var second = RuleAt(sheetId, 5, 0, 2);
        var first = RuleAt(sheetId, 1, 0, 1);

        var session = FullSheet([second, first]);
        session.SetStopIfTrue(first.Id, true).Should().BeTrue();

        session.WorkingRules.Select(rule => rule.Id).Should().Equal(first.Id, second.Id);
        session.WorkingRules.Select(rule => rule.Priority).Should().Equal(1, 2);
        first.StopIfTrue.Should().BeFalse("the live sheet rule must not be mutated before apply");
        session.WorkingRules[0].Should().NotBeSameAs(first);
    }

    [Fact]
    public void BuildProjection_FiltersEveryAppliesToAreaAndUsesPlannerDescriptions()
    {
        var sheetId = SheetId.New();
        var primaryOutside = RuleAt(sheetId, 10, 0, 1);
        primaryOutside.AdditionalRanges = [RangeAt(sheetId, 2, 0, 2, 0)];
        var hidden = RuleAt(sheetId, 20, 0, 2);
        var session = FullSheet(
            [primaryOutside, hidden],
            RangeAt(sheetId, 2, 0, 2, 0));

        var projection = session.BuildProjection();

        projection.Should().ContainSingle().Which.Id.Should().Be(primaryOutside.Id);
        projection[0].Description.ResourceKey.Should().Be("ManageConditionalFormats_RuleDataBar");
    }

    [Fact]
    public void Mutations_UpdateOnlyThePrivateWorkingCopy()
    {
        var sheetId = SheetId.New();
        var keep = RuleAt(sheetId, 1, 0, 1);
        var edit = RuleAt(sheetId, 2, 0, 2);
        var source = new List<ConditionalFormat> { keep, edit };
        var session = FullSheet(source);
        var edited = edit.Clone();
        edited.Value1 = "changed";
        var added = RuleAt(sheetId, 3, 0, 99);

        session.Replace(edited).Should().BeTrue();
        session.Delete(keep.Id).Should().BeTrue();
        session.Add(added);

        source.Select(rule => rule.Id).Should().Equal(keep.Id, edit.Id);
        edit.Value1.Should().BeNull();
        session.WorkingRules.Select(rule => rule.Id).Should().Equal(edit.Id, added.Id);
        session.WorkingRules.Select(rule => rule.Priority).Should().Equal(1, 2);
        session.WorkingRules[0].Value1.Should().Be("changed");
    }

    [Fact]
    public void Duplicate_InsertsIndependentCopyBelowSelectedRule()
    {
        var sheetId = SheetId.New();
        var selected = RuleAt(sheetId, 1, 0, 1);
        selected.FormatIfTrue = new CellStyle { Bold = true };
        var following = RuleAt(sheetId, 2, 0, 2);
        var duplicateId = Guid.NewGuid();
        var session = FullSheet([selected, following]);

        session.Duplicate(selected.Id, duplicateId).Should().BeTrue();

        session.WorkingRules.Select(rule => rule.Id)
            .Should().Equal(selected.Id, duplicateId, following.Id);
        session.WorkingRules.Select(rule => rule.Priority).Should().Equal(1, 2, 3);
        session.WorkingRules[1].FormatIfTrue.Should().Be(selected.FormatIfTrue);
        session.WorkingRules[1].FormatIfTrue.Should().NotBeSameAs(selected.FormatIfTrue);
    }

    [Fact]
    public void Move_FilteredFullSheetWorkingCopy_SwapsVisibleNeighboursAroundHiddenRule()
    {
        var sheetId = SheetId.New();
        var visibleFirst = RuleAt(sheetId, 1, 0, 1);
        var hidden = RuleAt(sheetId, 5, 0, 2);
        var visibleLast = RuleAt(sheetId, 2, 0, 3);
        var session = FullSheet(
            [visibleFirst, hidden, visibleLast],
            RangeAt(sheetId, 1, 0, 2, 0));

        session.Move(visibleLast.Id, ConditionalFormatRuleMoveDirection.Up).Should().BeTrue();

        session.WorkingRules.Select(rule => rule.Id)
            .Should().Equal(visibleLast.Id, hidden.Id, visibleFirst.Id);
        session.WorkingRules.Single(rule => rule.Id == hidden.Id).Priority.Should().Be(2);
    }

    [Fact]
    public void Move_UnfilteredWorkingCopy_UsesAdjacentRuleAndRejectsBoundaries()
    {
        var sheetId = SheetId.New();
        var first = RuleAt(sheetId, 1, 0, 1);
        var second = RuleAt(sheetId, 2, 0, 2);
        var session = FullSheet([first, second]);

        session.Move(first.Id, ConditionalFormatRuleMoveDirection.Up).Should().BeFalse();
        session.Move(second.Id, ConditionalFormatRuleMoveDirection.Up).Should().BeTrue();

        session.WorkingRules.Select(rule => rule.Id).Should().Equal(second.Id, first.Id);
    }

    [Fact]
    public void ApplyRange_ClonesTargetAndDropsStaleAdditionalAreas()
    {
        var sheetId = SheetId.New();
        var sourceRule = RuleAt(sheetId, 1, 0, 1);
        sourceRule.AdditionalRanges = [RangeAt(sheetId, 1, 2, 1, 2)];
        var replacement = RangeAt(sheetId, 3, 1, 4, 1);
        var session = FullSheet([sourceRule]);

        session.ApplyRange(sourceRule.Id, replacement).Should().BeTrue();

        session.WorkingRules[0].AppliesTo.Should().Be(replacement);
        session.WorkingRules[0].AdditionalRanges.Should().BeNull();
        sourceRule.AppliesTo.Should().NotBe(replacement);
        sourceRule.AdditionalRanges.Should().ContainSingle();
    }

    [Fact]
    public void CurrentScopeWorkingCopy_BuildResultMergesEditedRulesWithHiddenSheetRules()
    {
        var sheetId = SheetId.New();
        var visibleFirst = RuleAt(sheetId, 1, 0, 1);
        var hidden = RuleAt(sheetId, 8, 0, 2);
        var visibleSecond = RuleAt(sheetId, 2, 0, 3);
        var scope = RangeAt(sheetId, 1, 0, 2, 0);
        var session = CurrentScope([visibleFirst, hidden, visibleSecond], scope);

        session.Move(visibleSecond.Id, ConditionalFormatRuleMoveDirection.Up).Should().BeTrue();
        var result = session.BuildResultRules();

        result.Select(rule => rule.Id).Should().Equal(visibleSecond.Id, hidden.Id, visibleFirst.Id);
        result.Select(rule => rule.Priority).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void CurrentScopeWorkingCopy_SetScopeAppliesNewScopeFilterAfterMergingPendingEdits()
    {
        var sheetId = SheetId.New();
        var first = RuleAt(sheetId, 1, 0, 1);
        var second = RuleAt(sheetId, 8, 0, 2);
        var source = new List<ConditionalFormat> { first, second };
        var session = CurrentScope(source, RangeAt(sheetId, 1, 0, 1, 0));
        session.Delete(first.Id).Should().BeTrue();

        session.SetScope(RangeAt(sheetId, 8, 0, 8, 0), source);

        // The deletion made under the old scope is merged into the snapshot before the new
        // scope's filter is applied, so it does not resurface -- and the untouched live
        // `source` list passed in for reconciliation is never mutated.
        session.WorkingRules.Should().ContainSingle().Which.Id.Should().Be(second.Id);
        session.BuildResultRules().Select(rule => rule.Id).Should().Equal(second.Id);
        source.Should().HaveCount(2);
    }

    [Fact]
    public void CurrentScopeWorkingCopy_SetScopeKeepsUnappliedEditWhenScopeWidensToStillShowTheRule()
    {
        // Regression test for the Manage Rules "scope switch discards unsaved edits" bug: Excel's
        // Rules Manager never discards edits made earlier in the same dialog session when the
        // "Show formatting rules for" scope changes -- only Cancel does. Before the fix, SetScope
        // reloaded the current-scope working copy straight from the live (unedited) sheet rules,
        // silently reverting any Edit-Rule change the user had not yet clicked OK/Apply for.
        var sheetId = SheetId.New();
        var rule = RuleAt(sheetId, 3, 0, 1);
        var source = new List<ConditionalFormat> { rule };
        var selectionScope = RangeAt(sheetId, 3, 0, 3, 0);
        var session = CurrentScope(source, selectionScope);

        var edited = rule.Clone();
        edited.StopIfTrue = true;
        session.Replace(edited).Should().BeTrue();

        var worksheetScope = RangeAt(sheetId, 0, 0, 50, 0);
        session.SetScope(worksheetScope, source);

        session.WorkingRules.Should().ContainSingle().Which.StopIfTrue.Should()
            .BeTrue("the un-applied edit made under the old scope must survive a scope switch");
        source.Single().StopIfTrue.Should()
            .BeFalse("the live sheet rule must not be mutated before Apply/OK");
    }

    [Fact]
    public void CurrentScopeWorkingCopy_SetScopeKeepsEditAfterRoundTripThroughUnrelatedScope()
    {
        // Sibling of the fix above: proves ordinary scope filtering (hiding/showing rules as the
        // scope changes) still behaves correctly, and that an unrelated rule is left untouched
        // while the edited rule's change survives being hidden and shown again.
        var sheetId = SheetId.New();
        var edited = RuleAt(sheetId, 1, 0, 1);
        var untouched = RuleAt(sheetId, 8, 0, 2);
        var source = new List<ConditionalFormat> { edited, untouched };
        var selectionScope = RangeAt(sheetId, 1, 0, 1, 0);
        var session = CurrentScope(source, selectionScope);

        var editedClone = edited.Clone();
        editedClone.Value1 = "changed";
        session.Replace(editedClone).Should().BeTrue();

        // Switch to a scope that hides the edited rule entirely.
        session.SetScope(RangeAt(sheetId, 8, 0, 8, 0), source);
        session.WorkingRules.Should().ContainSingle().Which.Id.Should().Be(untouched.Id);

        // Switch back to the scope that shows the edited rule.
        session.SetScope(selectionScope, source);

        session.WorkingRules.Should().ContainSingle().Which.Value1.Should().Be("changed");
        untouched.Value1.Should().BeNull("the unrelated rule must be untouched throughout");
        source.Select(rule => rule.Id).Should().Equal(edited.Id, untouched.Id);
    }

    [Fact]
    public void FullSheetWorkingCopy_SetScopeRetainsBufferedEdits()
    {
        var sheetId = SheetId.New();
        var first = RuleAt(sheetId, 1, 0, 1);
        var second = RuleAt(sheetId, 8, 0, 2);
        var session = FullSheet([first, second], RangeAt(sheetId, 1, 0, 1, 0));
        session.Delete(first.Id).Should().BeTrue();

        session.SetScope(RangeAt(sheetId, 8, 0, 8, 0));

        session.WorkingRules.Should().ContainSingle().Which.Id.Should().Be(second.Id);
        session.VisibleRules.Should().ContainSingle().Which.Id.Should().Be(second.Id);
    }

    [Fact]
    public void UnknownRuleOperations_AreNoOps()
    {
        var sheetId = SheetId.New();
        var session = FullSheet([RuleAt(sheetId, 1, 0, 1)]);
        var missing = Guid.NewGuid();

        session.Delete(missing).Should().BeFalse();
        session.Replace(RuleAt(sheetId, 2, 0, 1, missing)).Should().BeFalse();
        session.Duplicate(missing, Guid.NewGuid()).Should().BeFalse();
        session.Move(missing, ConditionalFormatRuleMoveDirection.Down).Should().BeFalse();
        session.ApplyRange(missing, RangeAt(sheetId, 3, 0, 3, 0)).Should().BeFalse();
        session.SetStopIfTrue(missing, true).Should().BeFalse();
    }

    [Fact]
    public void CreateApplyCommand_ProducesSingleAtomicReplaceCommand()
    {
        var sheetId = SheetId.New();
        var session = FullSheet([RuleAt(sheetId, 1, 0, 1)]);

        session.CreateApplyCommand(sheetId).Should().BeOfType<ReplaceAllConditionalFormatsCommand>();
    }

    [Fact]
    public void Replace_WithRuleEditorResult_PreservesFieldsTheEditorDoesNotSurface()
    {
        var sheetId = SheetId.New();
        var range = RangeAt(sheetId, 1, 0, 3, 0);
        var existing = new ConditionalFormat
        {
            AppliesTo = range,
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "5",
            StopIfTrue = true
        };
        var edit = ConditionalFormatRuleBuilder.TryBuildApplyCommand(
            new CfRuleInput
            {
                RuleType = CfRuleType.CellValue,
                Operator = CfOperator.GreaterThan,
                Value1 = "9"
            },
            sheetId,
            range,
            existingRule: existing);
        var session = FullSheet([existing]);

        edit.IsValid.Should().BeTrue();
        session.Replace(edit.Rule!).Should().BeTrue();

        session.WorkingRules.Should().ContainSingle().Which.StopIfTrue.Should().BeTrue();
        session.WorkingRules[0].Value1.Should().Be("9");
        session.WorkingRules[0].Id.Should().Be(existing.Id);
    }

    private static ManageConditionalFormatsSession FullSheet(
        IReadOnlyList<ConditionalFormat> rules,
        GridRange? scope = null) =>
        new(rules, scope, ManageConditionalFormatsWorkingCopyPolicy.FullSheet);

    private static ManageConditionalFormatsSession CurrentScope(
        IReadOnlyList<ConditionalFormat> rules,
        GridRange scope) =>
        new(rules, scope, ManageConditionalFormatsWorkingCopyPolicy.CurrentScope);

    private static ConditionalFormat RuleAt(
        SheetId sheetId,
        uint row,
        uint col,
        int priority,
        Guid? id = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            AppliesTo = RangeAt(sheetId, row, col, row, col),
            Priority = priority,
            RuleType = CfRuleType.DataBar
        };

    private static GridRange RangeAt(
        SheetId sheetId,
        uint startRow,
        uint startCol,
        uint endRow,
        uint endCol) =>
        new(
            new CellAddress(sheetId, startRow, startCol),
            new CellAddress(sheetId, endRow, endCol));
}
