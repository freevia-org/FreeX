using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ManageConditionalFormatsDialogTests
{
    [Fact]
    public void BuildResultRules_ForCurrentSelectionPreservesRulesOutsideSelection()
    {
        var sheetId = SheetId.New();
        var outsideBefore = CreateRule(sheetId, 1, 1, 1);
        var selected = CreateRule(sheetId, 2, 2, 2);
        var outsideAfter = CreateRule(sheetId, 4, 4, 3);
        var editedSelected = CreateRule(sheetId, 2, 2, 9, selected.Id, stopIfTrue: true);

        var result = ManageConditionalFormatsDialog.BuildResultRules(
            [outsideBefore, selected, outsideAfter],
            new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 2, 2)),
            filterToSelection: true,
            [editedSelected]);

        result.Should().HaveCount(3);
        result.Select(rule => rule.Id).Should().Equal(outsideBefore.Id, selected.Id, outsideAfter.Id);
        result.Select(rule => rule.Priority).Should().Equal(1, 2, 3);
        result[1].StopIfTrue.Should().BeTrue();
    }

    [Fact]
    public void BuildResultRules_ForCurrentSelectionReordersVisibleRulesWithoutMovingHiddenWorksheetRules()
    {
        var sheetId = SheetId.New();
        var firstVisible = CreateRule(sheetId, 2, 2, 1);
        var hiddenWorksheetRule = CreateRule(sheetId, 7, 7, 2);
        var secondVisible = CreateRule(sheetId, 4, 2, 3);

        var result = ManageConditionalFormatsDialog.BuildResultRules(
            [firstVisible, hiddenWorksheetRule, secondVisible],
            new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 4, 2)),
            filterToSelection: true,
            [secondVisible, firstVisible]);

        result.Select(rule => rule.Id).Should().Equal(secondVisible.Id, hiddenWorksheetRule.Id, firstVisible.Id);
        result.Select(rule => rule.Priority).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void BuildResultRules_ForCurrentSelectionAddsNewRulesAfterLastVisibleRuleSlot()
    {
        var sheetId = SheetId.New();
        var firstVisible = CreateRule(sheetId, 2, 2, 1);
        var hiddenBetween = CreateRule(sheetId, 7, 7, 2);
        var secondVisible = CreateRule(sheetId, 4, 2, 3);
        var hiddenAfter = CreateRule(sheetId, 9, 9, 4);
        var addedVisible = CreateRule(sheetId, 3, 2, 99);

        var result = ManageConditionalFormatsDialog.BuildResultRules(
            [firstVisible, hiddenBetween, secondVisible, hiddenAfter],
            new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 4, 2)),
            filterToSelection: true,
            [firstVisible, secondVisible, addedVisible]);

        result.Select(rule => rule.Id).Should().Equal(
            firstVisible.Id,
            hiddenBetween.Id,
            secondVisible.Id,
            addedVisible.Id,
            hiddenAfter.Id);
        result.Select(rule => rule.Priority).Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public void BuildResultRules_ForCurrentSelectionCanDeleteSelectedRulesOnly()
    {
        var sheetId = SheetId.New();
        var outsideBefore = CreateRule(sheetId, 1, 1, 1);
        var selected = CreateRule(sheetId, 2, 2, 2);
        var outsideAfter = CreateRule(sheetId, 4, 4, 3);

        var result = ManageConditionalFormatsDialog.BuildResultRules(
            [outsideBefore, selected, outsideAfter],
            new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 2, 2)),
            filterToSelection: true,
            []);

        result.Select(rule => rule.Id).Should().Equal(outsideBefore.Id, outsideAfter.Id);
        result.Select(rule => rule.Priority).Should().Equal(1, 2);
    }

    [Fact]
    public void BuildResultRules_ForTableScopePreservesRulesOutsideTable()
    {
        var sheetId = SheetId.New();
        var tableRange = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 6, 4));
        var tableRule = CreateRule(sheetId, 3, 3, 1);
        var outsideRule = CreateRule(sheetId, 10, 10, 2);
        var editedTableRule = CreateRule(sheetId, 4, 4, 99, stopIfTrue: true);

        var result = ManageConditionalFormatsDialog.BuildResultRules(
            [tableRule, outsideRule],
            tableRange,
            filterToSelection: true,
            [editedTableRule]);

        result.Should().HaveCount(2);
        result[0].StopIfTrue.Should().BeTrue();
        result[0].Priority.Should().Be(1);
        result[1].AppliesTo.Should().Be(outsideRule.AppliesTo);
        result[1].Priority.Should().Be(2);
    }
}
