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

    [Fact]
    public void CloneRule_PreservesDataBarOptions()
    {
        var sheetId = SheetId.New();
        var source = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 5, 1)),
            RuleType = CfRuleType.DataBar,
            DataBarColor = new RgbColor(10, 20, 30),
            DataBarShowValue = false,
            DataBarMinLength = 7,
            DataBarMaxLength = 88,
            DataBarGradient = false,
            DataBarBorder = true,
            DataBarAxisPosition = "middle",
            DataBarAxisColor = new RgbColor(1, 2, 3),
            DataBarNegativeFillColor = new RgbColor(4, 5, 6),
            DataBarNegativeBorderColor = new RgbColor(7, 8, 9),
            NativeChildXmls = ["<extLst />"],
            NativePayloadAttributes = new Dictionary<string, string> { ["futureAttr"] = "1" },
            NativePayloadChildXmls = ["<axisColor theme=\"1\" />"],
            NativeContainerAttributes = new Dictionary<string, string> { ["containerAttr"] = "x" },
            NativeContainerChildXmls = ["<extLst />"]
        };

        var clone = ConditionalFormatDialogPlanner.CloneRule(source);

        clone.DataBarShowValue.Should().BeFalse();
        clone.DataBarMinLength.Should().Be(7);
        clone.DataBarMaxLength.Should().Be(88);
        clone.DataBarGradient.Should().BeFalse();
        clone.DataBarBorder.Should().BeTrue();
        clone.DataBarAxisPosition.Should().Be("middle");
        clone.DataBarAxisColor.Should().Be(new RgbColor(1, 2, 3));
        clone.DataBarNegativeFillColor.Should().Be(new RgbColor(4, 5, 6));
        clone.DataBarNegativeBorderColor.Should().Be(new RgbColor(7, 8, 9));
        clone.NativeChildXmls.Should().BeEquivalentTo(source.NativeChildXmls);
        clone.NativePayloadAttributes.Should().BeEquivalentTo(source.NativePayloadAttributes);
        clone.NativePayloadChildXmls.Should().BeEquivalentTo(source.NativePayloadChildXmls);
        clone.NativeContainerAttributes.Should().BeEquivalentTo(source.NativeContainerAttributes);
        clone.NativeContainerChildXmls.Should().BeEquivalentTo(source.NativeContainerChildXmls);
    }
}
