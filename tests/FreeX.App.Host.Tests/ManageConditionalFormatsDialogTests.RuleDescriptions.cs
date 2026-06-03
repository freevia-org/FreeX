using System.Windows.Media;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ManageConditionalFormatsDialogTests
{
    [Fact]
    public void DescribeRule_IconSetIncludesStyleAndFlags()
    {
        var rule = new ConditionalFormat
        {
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "3TrafficLights1",
            IconSetShowValue = false,
            IconSetReverse = true
        };

        ManageConditionalFormatsDialog.DescribeRule(rule)
            .Should().Be("Icon Set: 3TrafficLights1 (reverse, icons only)");
    }

    [Fact]
    public void DescribeRule_IconSetIncludesCustomIconOverrides()
    {
        var rule = new ConditionalFormat
        {
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "5Arrows"
        };
        rule.IconOverrides.Add(new CfIconOverride("3TrafficLights1", 0));

        ManageConditionalFormatsDialog.DescribeRule(rule)
            .Should().Be("Icon Set: 5Arrows (custom icons)");
    }

    [Theory]
    [InlineData(CfRuleType.ContainsText, "Text contains \"urgent\"")]
    [InlineData(CfRuleType.DateOccurring, "Date occurring: Last 7 Days")]
    [InlineData(CfRuleType.DuplicateValues, "Duplicate Values")]
    [InlineData(CfRuleType.UniqueValues, "Unique Values")]
    public void DescribeRule_LongTailHighlightRulesUseExcelLabels(CfRuleType ruleType, string expected)
    {
        var rule = new ConditionalFormat
        {
            RuleType = ruleType,
            TextRuleText = "urgent",
            DateOccurringPeriod = "last7Days"
        };

        ManageConditionalFormatsDialog.DescribeRule(rule).Should().Be(expected);
    }

    [Fact]
    public void PreviewBrush_IconSetUsesNeutralBrush()
    {
        var rule = new ConditionalFormat
        {
            RuleType = CfRuleType.IconSet,
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) }
        };

        ManageConditionalFormatsDialog.PreviewBrush(rule).Should().BeSameAs(Brushes.LightGray);
    }

    [Fact]
    public void PreviewBrush_DataBarUsesRuleColor()
    {
        var rule = new ConditionalFormat
        {
            RuleType = CfRuleType.DataBar,
            DataBarColor = new RgbColor(91, 155, 213)
        };

        var brush = ManageConditionalFormatsDialog.PreviewBrush(rule).Should().BeOfType<SolidColorBrush>().Subject;
        brush.Color.Should().Be(Color.FromRgb(91, 155, 213));
    }

    [Fact]
    public void PreviewBrush_ColorScaleUsesGradientPreview()
    {
        var rule = new ConditionalFormat
        {
            RuleType = CfRuleType.ColorScale,
            MinColor = new RgbColor(99, 190, 123),
            MaxColor = new RgbColor(248, 105, 107)
        };

        var brush = ManageConditionalFormatsDialog.PreviewBrush(rule).Should().BeOfType<LinearGradientBrush>().Subject;
        brush.GradientStops.Should().ContainSingle(stop => stop.Color == Color.FromRgb(99, 190, 123));
        brush.GradientStops.Should().ContainSingle(stop => stop.Color == Color.FromRgb(248, 105, 107));
    }
}
