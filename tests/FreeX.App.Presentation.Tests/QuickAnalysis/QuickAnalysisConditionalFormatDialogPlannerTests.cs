using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.QuickAnalysis;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.QuickAnalysis;

public sealed class QuickAnalysisConditionalFormatDialogPlannerTests
{
    [Fact]
    public void PlanDialog_CarriesSharedCommandTitleAndEditorSeed()
    {
        var plan = QuickAnalysisConditionalFormatDialogPlanner.PlanDialog(
            QuickAnalysisConditionalFormatCommand.Between);

        plan.Command.Should().Be(QuickAnalysisConditionalFormatCommand.Between);
        plan.Title.Should().Be("Between");
        plan.Seed.RuleType.Should().Be(CfRuleType.CellValue);
        plan.Seed.Operator.Should().Be(CfOperator.Between);
    }

    [Fact]
    public void SharedCatalog_CoversEveryConditionalFormatCommand()
    {
        foreach (var command in Enum.GetValues<QuickAnalysisConditionalFormatCommand>())
        {
            var dialogPlan = QuickAnalysisConditionalFormatDialogPlanner.PlanDialog(command);

            dialogPlan.Command.Should().Be(command);
            dialogPlan.Title.Should().NotBeNullOrWhiteSpace();
            QuickAnalysisConditionalFormatPresetPlanner.TryResolve(command, out _).Should().BeTrue();
        }
    }

    [Fact]
    public void SharedCatalog_RejectsUnknownConditionalFormatCommand()
    {
        var unknown = (QuickAnalysisConditionalFormatCommand)int.MaxValue;

        var plan = () => QuickAnalysisConditionalFormatDialogPlanner.PlanDialog(unknown);

        plan.Should().Throw<ArgumentOutOfRangeException>();
        QuickAnalysisConditionalFormatPresetPlanner.TryResolve(unknown, out _).Should().BeFalse();
    }

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
        var seed = QuickAnalysisConditionalFormatDialogPlanner.PlanDialog(command).Seed;

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
        QuickAnalysisConditionalFormatDialogPlanner.PlanDialog(command).Seed.RuleType.Should().Be(ruleType);
    }

    [Fact]
    public void Plan_PreservesTextAndDateDialogState()
    {
        var text = QuickAnalysisConditionalFormatDialogPlanner
            .PlanDialog(QuickAnalysisConditionalFormatCommand.TextContains)
            .Seed;
        var date = QuickAnalysisConditionalFormatDialogPlanner
            .PlanDialog(QuickAnalysisConditionalFormatCommand.DateOccurring)
            .Seed;

        text.RuleType.Should().Be(CfRuleType.ContainsText);
        text.Text.Should().BeEmpty();
        date.RuleType.Should().Be(CfRuleType.DateOccurring);
        date.DateOccurringPeriod.Should().Be("Today");
    }

}
