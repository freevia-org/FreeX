using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class ConditionalFormatDialogPlannerTests
{
    [Theory]
    [InlineData(CfRuleType.Formula, null, true, false, "Formula")]
    [InlineData(CfRuleType.DataBar, null, true, false, "Data Bar")]
    [InlineData(CfRuleType.ColorScale, null, true, false, "Color Scale")]
    [InlineData(CfRuleType.IconSet, null, true, false, "Icon Set")]
    [InlineData(CfRuleType.AboveAverage, null, true, false, "Above Average")]
    [InlineData(CfRuleType.AboveAverage, null, false, false, "Below Average")]
    [InlineData(CfRuleType.Top10, null, true, false, "Top 10 Items")]
    [InlineData(CfRuleType.Top10, null, false, false, "Bottom 10 Items")]
    [InlineData(CfRuleType.Top10, null, true, true, "Top 10%")]
    [InlineData(CfRuleType.CellValue, CfOperator.Between, true, false, "Between")]
    [InlineData(CfRuleType.ContainsText, null, true, false, "Text Contains")]
    [InlineData(CfRuleType.DateOccurring, null, true, false, "Date Occurring")]
    [InlineData(CfRuleType.DuplicateValues, null, true, false, "Duplicate Values")]
    [InlineData(CfRuleType.UniqueValues, null, true, false, "Duplicate Values")]
    public void RuleTypeLabel_MapsConditionalFormatToDialogLabel(
        CfRuleType ruleType,
        CfOperator? op,
        bool aboveAverage,
        bool topBottomPercent,
        string expected)
    {
        var format = new ConditionalFormat
        {
            RuleType = ruleType,
            Operator = op ?? CfOperator.GreaterThan,
            AboveAverage = aboveAverage,
            TopBottomPercent = topBottomPercent
        };

        ConditionalFormatDialogPlanner.RuleTypeLabel(format).Should().Be(expected);
    }

    [Fact]
    public void CloneRule_CopiesFieldsAndClonesStyle()
    {
        var sheetId = SheetId.New();
        var source = new ConditionalFormat
        {
            Id = Guid.NewGuid(),
            AppliesTo = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3)),
            Priority = 2,
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "3ArrowsGray",
            IconSetShowValue = false,
            IconSetReverse = true,
            TopBottomRank = 5,
            StopIfTrue = true,
            FormatIfTrue = new CellStyle { Bold = true }
        };

        var clone = ConditionalFormatDialogPlanner.CloneRule(source);

        clone.Should().BeEquivalentTo(source, options => options.Excluding(format => format.FormatIfTrue));
        clone.FormatIfTrue.Should().NotBeSameAs(source.FormatIfTrue);
        clone.FormatIfTrue!.Bold.Should().BeTrue();
    }
}
