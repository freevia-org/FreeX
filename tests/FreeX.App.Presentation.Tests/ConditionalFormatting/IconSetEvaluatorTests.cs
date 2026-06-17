using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.ConditionalFormatting;

public sealed class IconSetEvaluatorTests
{
    [Theory]
    [InlineData("3TrafficLights1", 3)]
    [InlineData("4Arrows", 4)]
    [InlineData("5Quarters", 5)]
    [InlineData("Arrows", 3)] // no leading digit → default 3
    [InlineData(null, 3)]
    [InlineData("9Foo", 5)]   // clamped to 5
    public void GetIconSetCount_ParsesLeadingDigit(string? style, int expected)
    {
        ConditionalFormatEvaluator.GetIconSetCount(style).Should().Be(expected);
    }

    private static ConditionalFormat ThreeIconRuleWithThresholds(double t1, double t2, bool gteDefault = true)
    {
        // 3-icon set with explicit thresholds; thresholds count == iconCount means a leading
        // (ignored) "min" entry, then the two real cut points.
        var rule = new ConditionalFormat
        {
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "3TrafficLights1",
        };
        rule.IconSetThresholds.Add(new CfThresholdModel(CfThresholdType.Percent, "0", gteDefault));
        rule.IconSetThresholds.Add(new CfThresholdModel(CfThresholdType.Number, t1.ToString(System.Globalization.CultureInfo.InvariantCulture), gteDefault));
        rule.IconSetThresholds.Add(new CfThresholdModel(CfThresholdType.Number, t2.ToString(System.Globalization.CultureInfo.InvariantCulture), gteDefault));
        return rule;
    }

    [Theory]
    [InlineData(0, 0)]    // below first threshold
    [InlineData(33, 1)]   // >= 33 (first cut) but < 66
    [InlineData(66, 2)]   // >= 66 (second cut)
    [InlineData(100, 2)]
    [InlineData(32.9, 0)]
    [InlineData(65.9, 1)]
    public void IconSet_ExplicitThresholds_SelectsBucket(double value, int expectedBucket)
    {
        var rule = ThreeIconRuleWithThresholds(33, 66);
        var stats = ConditionalFormatStatistics.FromValues([0, 100]);

        var result = ConditionalFormatEvaluator.EvaluateIconSet(rule, value, stats);

        result!.Value.BucketIndex.Should().Be(expectedBucket);
        result.Value.IconCount.Should().Be(3);
    }

    [Fact]
    public void IconSet_GreaterThanComparison_ExcludesExactThreshold()
    {
        var rule = ThreeIconRuleWithThresholds(33, 66, gteDefault: false);
        var stats = ConditionalFormatStatistics.FromValues([0, 100]);

        // value exactly 33 with strict greater-than → does NOT advance into bucket 1
        ConditionalFormatEvaluator.EvaluateIconSet(rule, 33, stats)!.Value.BucketIndex.Should().Be(0);
        ConditionalFormatEvaluator.EvaluateIconSet(rule, 33.1, stats)!.Value.BucketIndex.Should().Be(1);
    }

    [Fact]
    public void IconSet_Reverse_FlipsBucketIndex()
    {
        var rule = ThreeIconRuleWithThresholds(33, 66);
        rule.IconSetReverse = true;
        var stats = ConditionalFormatStatistics.FromValues([0, 100]);

        // value 0 → natural bucket 0 → reversed to 2
        ConditionalFormatEvaluator.EvaluateIconSet(rule, 0, stats)!.Value.BucketIndex.Should().Be(2);
        // value 100 → natural bucket 2 → reversed to 0
        ConditionalFormatEvaluator.EvaluateIconSet(rule, 100, stats)!.Value.BucketIndex.Should().Be(0);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(33, 0)]   // t = 0.33 → floor(0.33*3) = 0
    [InlineData(34, 1)]   // t = 0.34 → floor(1.02) = 1
    [InlineData(67, 2)]   // t = 0.67 → floor(2.01) = 2
    [InlineData(100, 2)]
    public void IconSet_NoThresholds_UsesEqualWidthInterpolation(double value, int expectedBucket)
    {
        // No thresholds → falls back to interpolation between min(0) and max(100).
        var rule = new ConditionalFormat { RuleType = CfRuleType.IconSet, IconSetStyle = "3TrafficLights1" };
        var stats = ConditionalFormatStatistics.FromValues([0, 100]);

        ConditionalFormatEvaluator.EvaluateIconSet(rule, value, stats)!.Value.BucketIndex.Should().Be(expectedBucket);
    }

    [Fact]
    public void IconSet_Interpolation_MaxEqualsMin_TopBucket()
    {
        var rule = new ConditionalFormat { RuleType = CfRuleType.IconSet, IconSetStyle = "3TrafficLights1" };
        var stats = ConditionalFormatStatistics.FromValues([5, 5]);

        ConditionalFormatEvaluator.EvaluateIconSet(rule, 5, stats)!.Value.BucketIndex.Should().Be(2);
    }

    [Fact]
    public void IconSet_DefaultStyleApplied_WhenStyleBlank()
    {
        var rule = new ConditionalFormat { RuleType = CfRuleType.IconSet, IconSetStyle = null };
        var stats = ConditionalFormatStatistics.FromValues([0, 100]);

        var result = ConditionalFormatEvaluator.EvaluateIconSet(rule, 50, stats);
        result!.Value.Style.Should().Be("3TrafficLights1");
        result.Value.IconCount.Should().Be(3);
    }

    [Fact]
    public void IconSet_NonFiniteValue_ReturnsNull()
    {
        var rule = new ConditionalFormat { RuleType = CfRuleType.IconSet, IconSetStyle = "3TrafficLights1" };
        var stats = ConditionalFormatStatistics.FromValues([0, 100]);

        ConditionalFormatEvaluator.EvaluateIconSet(rule, double.PositiveInfinity, stats).Should().BeNull();
    }

    [Fact]
    public void IconSet_FiveIcons_FourThresholds()
    {
        var rule = new ConditionalFormat { RuleType = CfRuleType.IconSet, IconSetStyle = "5Quarters" };
        // 5 thresholds (count == iconCount) → first ignored, then 4 cut points at 20/40/60/80
        rule.IconSetThresholds.Add(new CfThresholdModel(CfThresholdType.Percent, "0"));
        rule.IconSetThresholds.Add(new CfThresholdModel(CfThresholdType.Number, "20"));
        rule.IconSetThresholds.Add(new CfThresholdModel(CfThresholdType.Number, "40"));
        rule.IconSetThresholds.Add(new CfThresholdModel(CfThresholdType.Number, "60"));
        rule.IconSetThresholds.Add(new CfThresholdModel(CfThresholdType.Number, "80"));
        var stats = ConditionalFormatStatistics.FromValues([0, 100]);

        ConditionalFormatEvaluator.EvaluateIconSet(rule, 10, stats)!.Value.BucketIndex.Should().Be(0);
        ConditionalFormatEvaluator.EvaluateIconSet(rule, 50, stats)!.Value.BucketIndex.Should().Be(2);
        ConditionalFormatEvaluator.EvaluateIconSet(rule, 100, stats)!.Value.BucketIndex.Should().Be(4);
    }
}
