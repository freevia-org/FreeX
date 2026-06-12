using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class ManageConditionalFormatsPlannerTests
{
    [Fact]
    public void DuplicateRule_InsertsDeepCopyBelowSelectedRuleWithNewIdentity()
    {
        var sheetId = SheetId.New();
        var first = CreateRule(sheetId, 1, 1, 1);
        var selected = CreateRule(sheetId, 2, 1, 2);
        selected.RuleType = CfRuleType.IconSet;
        selected.IconSetStyle = "5Arrows";
        selected.IconSetThresholds.Add(new CfThresholdModel(CfThresholdType.Percent, "25"));
        selected.IconOverrides.Add(new CfIconOverride("3TrafficLights1", 0));
        selected.FormatIfTrue = new CellStyle { Bold = true, FillColor = new CellColor(1, 2, 3) };
        selected.NativeChildXmls =
        [
            """<extLst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><ext uri="{B025F937-6E4E-48BE-B07C-B91C50BE2FA4}"><x14:id xmlns:x14="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main">{11111111-2222-3333-4444-555555555555}</x14:id></ext><ext uri="{FUTURE}" /></extLst>"""
        ];
        var duplicateId = Guid.NewGuid();

        var result = ManageConditionalFormatsPlanner.DuplicateRule([first, selected], selected.Id, duplicateId);

        result.Select(rule => rule.Id).Should().Equal(first.Id, selected.Id, duplicateId);
        result.Select(rule => rule.Priority).Should().Equal(1, 2, 3);

        var duplicate = result[2];
        duplicate.AppliesTo.Should().Be(selected.AppliesTo);
        duplicate.RuleType.Should().Be(CfRuleType.IconSet);
        duplicate.IconSetThresholds.Should().Equal(selected.IconSetThresholds);
        duplicate.IconOverrides.Should().Equal(selected.IconOverrides);
        duplicate.FormatIfTrue.Should().NotBeSameAs(selected.FormatIfTrue);
        duplicate.FormatIfTrue.Should().Be(selected.FormatIfTrue);
        duplicate.NativeChildXmls.Should().ContainSingle(xml => xml.Contains("{FUTURE}", StringComparison.Ordinal));
        duplicate.NativeChildXmls.Should().NotContain(xml => xml.Contains("11111111-2222-3333-4444-555555555555", StringComparison.Ordinal));
    }

    [Fact]
    public void ReplaceRule_PreservesRuleSlotAndReprioritizesEditedRule()
    {
        var sheetId = SheetId.New();
        var first = CreateRule(sheetId, 1, 1, 1);
        var second = CreateRule(sheetId, 2, 1, 2);
        var edited = CreateRule(sheetId, 5, 3, 99, second.Id);
        edited.StopIfTrue = true;

        var result = ManageConditionalFormatsPlanner.ReplaceRule([first, second], edited);

        result.Select(rule => rule.Id).Should().Equal(first.Id, second.Id);
        result.Select(rule => rule.Priority).Should().Equal(1, 2);
        result[1].AppliesTo.Should().Be(edited.AppliesTo);
        result[1].StopIfTrue.Should().BeTrue();
    }

    [Fact]
    public void DeleteRule_RemovesOnlyMatchingRuleAndReassignsPriorities()
    {
        var sheetId = SheetId.New();
        var first = CreateRule(sheetId, 1, 1, 5);
        var second = CreateRule(sheetId, 2, 1, 8);
        var third = CreateRule(sheetId, 3, 1, 13);

        var result = ManageConditionalFormatsPlanner.DeleteRule([first, second, third], second.Id);

        result.Select(rule => rule.Id).Should().Equal(first.Id, third.Id);
        result.Select(rule => rule.Priority).Should().Equal(1, 2);
    }

    [Fact]
    public void MoveRule_ReordersOneStepAndReassignsPriorities()
    {
        var sheetId = SheetId.New();
        var first = CreateRule(sheetId, 1, 1, 1);
        var second = CreateRule(sheetId, 2, 1, 2);
        var third = CreateRule(sheetId, 3, 1, 3);

        var movedDown = ManageConditionalFormatsPlanner.MoveRule(
            [first, second, third],
            first.Id,
            ConditionalFormatRuleMoveDirection.Down);
        var movedBackUp = ManageConditionalFormatsPlanner.MoveRule(
            movedDown,
            first.Id,
            ConditionalFormatRuleMoveDirection.Up);

        movedDown.Select(rule => rule.Id).Should().Equal(second.Id, first.Id, third.Id);
        movedDown.Select(rule => rule.Priority).Should().Equal(1, 2, 3);
        movedBackUp.Select(rule => rule.Id).Should().Equal(first.Id, second.Id, third.Id);
    }

    [Fact]
    public void ApplyRuleRange_UpdatesOnlyTargetRuleRange()
    {
        var sheetId = SheetId.New();
        var first = CreateRule(sheetId, 1, 1, 1);
        var second = CreateRule(sheetId, 2, 1, 2);
        var newRange = new GridRange(new CellAddress(sheetId, 4, 4), new CellAddress(sheetId, 8, 6));

        var result = ManageConditionalFormatsPlanner.ApplyRuleRange([first, second], second.Id, newRange);

        result.Select(rule => rule.Id).Should().Equal(first.Id, second.Id);
        result.Select(rule => rule.Priority).Should().Equal(1, 2);
        result[0].AppliesTo.Should().Be(first.AppliesTo);
        result[1].AppliesTo.Should().Be(newRange);
    }

    [Fact]
    public void BuildResultRules_FilteredScopeKeepsEditedRulesInOriginalVisibleSlots()
    {
        var sheetId = SheetId.New();
        var firstVisible = CreateRule(sheetId, 2, 1, 1);
        var hidden = CreateRule(sheetId, 8, 1, 2);
        var secondVisible = CreateRule(sheetId, 3, 1, 3);
        var editedSecond = CreateRule(sheetId, 20, 4, 7, secondVisible.Id);
        var selection = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 1));

        var result = ManageConditionalFormatsPlanner.BuildResultRules(
            [firstVisible, hidden, secondVisible],
            selection,
            filterToSelection: true,
            [editedSecond, firstVisible]);

        result.Select(rule => rule.Id).Should().Equal(secondVisible.Id, hidden.Id, firstVisible.Id);
        result.Select(rule => rule.Priority).Should().Equal(1, 2, 3);
        result[0].AppliesTo.Should().Be(editedSecond.AppliesTo);
    }

    private static ConditionalFormat CreateRule(
        SheetId sheetId,
        uint row,
        uint col,
        int priority,
        Guid? id = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            AppliesTo = new GridRange(new CellAddress(sheetId, row, col), new CellAddress(sheetId, row, col)),
            Priority = priority,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "1",
            FormatIfTrue = new CellStyle { Italic = true }
        };
}
