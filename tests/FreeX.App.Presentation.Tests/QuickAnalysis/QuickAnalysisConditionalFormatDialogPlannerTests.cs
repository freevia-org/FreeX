using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.QuickAnalysis;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.QuickAnalysis;

public sealed class QuickAnalysisConditionalFormatDialogPlannerTests
{
    [Theory]
    [InlineData(QuickAnalysisConditionalFormatCommand.GreaterThan, CfRuleType.CellValue, CfOperator.GreaterThan, true, false)]
    [InlineData(QuickAnalysisConditionalFormatCommand.LessThan, CfRuleType.CellValue, CfOperator.LessThan, true, false)]
    [InlineData(QuickAnalysisConditionalFormatCommand.Between, CfRuleType.CellValue, CfOperator.Between, true, false)]
    [InlineData(QuickAnalysisConditionalFormatCommand.EqualTo, CfRuleType.CellValue, CfOperator.Equal, true, false)]
    [InlineData(QuickAnalysisConditionalFormatCommand.Top10Items, CfRuleType.Top10, CfOperator.Equal, true, false)]
    [InlineData(QuickAnalysisConditionalFormatCommand.Top10Percent, CfRuleType.Top10, CfOperator.Equal, true, true)]
    [InlineData(QuickAnalysisConditionalFormatCommand.Bottom10Items, CfRuleType.Top10, CfOperator.Equal, false, false)]
    [InlineData(QuickAnalysisConditionalFormatCommand.Bottom10Percent, CfRuleType.Top10, CfOperator.Equal, false, true)]
    [InlineData(QuickAnalysisConditionalFormatCommand.AboveAverage, CfRuleType.AboveAverage, CfOperator.Equal, true, false)]
    [InlineData(QuickAnalysisConditionalFormatCommand.BelowAverage, CfRuleType.AboveAverage, CfOperator.Equal, false, false)]
    public void Plan_PreservesDistinctComparisonAndDirectionState(
        QuickAnalysisConditionalFormatCommand command,
        CfRuleType ruleType,
        CfOperator op,
        bool isTop,
        bool isPercent)
    {
        var seed = QuickAnalysisConditionalFormatDialogPlanner.Plan(command);

        seed.RuleType.Should().Be(ruleType);
        seed.Operator.Should().Be(op);
        seed.IsTop.Should().Be(isTop);
        seed.TopBottomPercent.Should().Be(isPercent);
        seed.TopBottomRank.Should().Be(10);
    }

    [Theory]
    [InlineData(QuickAnalysisConditionalFormatCommand.DataBar, CfRuleType.DataBar)]
    [InlineData(QuickAnalysisConditionalFormatCommand.ColorScale, CfRuleType.ColorScale)]
    [InlineData(QuickAnalysisConditionalFormatCommand.IconSet, CfRuleType.IconSet)]
    [InlineData(QuickAnalysisConditionalFormatCommand.DuplicateValues, CfRuleType.DuplicateValues)]
    public void Plan_PreservesVisualAndDuplicateRuleFamilies(
        QuickAnalysisConditionalFormatCommand command,
        CfRuleType ruleType)
    {
        QuickAnalysisConditionalFormatDialogPlanner.Plan(command).RuleType.Should().Be(ruleType);
    }

    [Fact]
    public void Plan_PreservesTextAndDateDialogState()
    {
        var text = QuickAnalysisConditionalFormatDialogPlanner.Plan(QuickAnalysisConditionalFormatCommand.TextContains);
        var date = QuickAnalysisConditionalFormatDialogPlanner.Plan(QuickAnalysisConditionalFormatCommand.DateOccurring);

        text.RuleType.Should().Be(CfRuleType.ContainsText);
        text.Text.Should().BeEmpty();
        date.RuleType.Should().Be(CfRuleType.DateOccurring);
        date.DateOccurringPeriod.Should().Be("Today");
    }
}
