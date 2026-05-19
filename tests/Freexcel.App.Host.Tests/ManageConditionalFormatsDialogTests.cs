using System.Reflection;
using System.Windows.Media;
using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class ManageConditionalFormatsDialogTests
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
    public void CloneWithPriority_PreservesAdvancedConditionalFormatFields()
    {
        var source = new ConditionalFormat
        {
            Id = Guid.NewGuid(),
            AppliesTo = new GridRange(new CellAddress(SheetId.New(), 2, 2), new CellAddress(SheetId.New(), 5, 4)),
            Priority = 7,
            RuleType = CfRuleType.IconSet,
            Operator = CfOperator.Between,
            Value1 = "1",
            Value2 = "10",
            FormatIfTrue = new CellStyle { Bold = true, FillColor = new CellColor(1, 2, 3) },
            MinColor = new RgbColor(10, 20, 30),
            MidColor = new RgbColor(40, 50, 60),
            MaxColor = new RgbColor(70, 80, 90),
            UseThreeColorScale = true,
            MinThresholdType = CfThresholdType.Number,
            MinThresholdValue = "5",
            MidThresholdType = CfThresholdType.Percent,
            MidThresholdValue = "50",
            MaxThresholdType = CfThresholdType.Formula,
            MaxThresholdValue = "A1",
            DataBarColor = new RgbColor(9, 8, 7),
            DataBarMinThresholdType = CfThresholdType.Percentile,
            DataBarMinThresholdValue = "10",
            DataBarMaxThresholdType = CfThresholdType.Number,
            DataBarMaxThresholdValue = "99",
            DataBarShowValue = false,
            DataBarMinLength = 5,
            DataBarMaxLength = 95,
            AboveAverage = false,
            FormulaText = "A1>0",
            IconSetStyle = "5Arrows",
            IconSetShowValue = false,
            IconSetReverse = true,
            TopBottomRank = 3,
            TopBottomPercent = true,
            TextRuleText = "urgent",
            DateOccurringPeriod = "last7Days",
            StopIfTrue = true
        };

        var clone = CloneWithPriority(source, 2);

        clone.Priority.Should().Be(2);
        clone.Id.Should().Be(source.Id);
        clone.Should().BeEquivalentTo(source, options => options
            .Excluding(rule => rule.Priority)
            .Excluding(rule => rule.FormatIfTrue));
        clone.FormatIfTrue.Should().NotBeSameAs(source.FormatIfTrue);
        clone.FormatIfTrue.Should().Be(source.FormatIfTrue);
    }

    private static ConditionalFormat CloneWithPriority(ConditionalFormat source, int priority)
    {
        var method = typeof(ManageConditionalFormatsDialog)
            .GetMethod("CloneWithPriority", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return method!.Invoke(null, [source, priority]).Should().BeOfType<ConditionalFormat>().Subject;
    }
}
