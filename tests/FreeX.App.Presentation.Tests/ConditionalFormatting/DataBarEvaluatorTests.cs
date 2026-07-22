using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.ConditionalFormatting;

public sealed class DataBarEvaluatorTests
{
    private static ConditionalFormat DataBarRule() => new()
    {
        RuleType = CfRuleType.DataBar,
        DataBarColor = new RgbColor(10, 20, 30),
        DataBarMinThresholdType = CfThresholdType.AutoMin,
        DataBarMaxThresholdType = CfThresholdType.AutoMax,
    };

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(50, 0.5)]
    [InlineData(100, 1.0)]
    [InlineData(25, 0.25)]
    public void DataBar_AutoMinMax_AllPositive_FractionIsLinear(double value, double expectedEnd)
    {
        var rule = DataBarRule();
        var stats = ConditionalFormatStatistics.FromValues([0, 100]);

        var layout = ConditionalFormatEvaluator.EvaluateDataBar(rule, value, stats);

        // value 0 produces an empty (null) bar; otherwise start at axis 0 and end at the fraction.
        if (expectedEnd <= 0)
        {
            layout.Should().BeNull();
            return;
        }

        layout.Should().NotBeNull();
        layout!.Value.StartFraction.Should().Be(0);
        layout.Value.EndFraction.Should().BeApproximately(expectedEnd, 1e-9);
        layout.Value.IsNegative.Should().BeFalse();
        layout.Value.AxisFraction.Should().Be(0);
    }

    [Fact]
    public void DataBar_FillColor_MapsFromRuleColor()
    {
        var rule = DataBarRule();
        var stats = ConditionalFormatStatistics.FromValues([0, 100]);

        var layout = ConditionalFormatEvaluator.EvaluateDataBar(rule, 50, stats);

        layout!.Value.FillColor.Should().Be(new PresentationRgb(10, 20, 30));
    }

    [Fact]
    public void DataBar_MinMaxLength_ScalesFraction()
    {
        var rule = DataBarRule();
        rule.DataBarMinLength = 10;
        rule.DataBarMaxLength = 90;
        var stats = ConditionalFormatStatistics.FromValues([0, 100]);

        // fraction 0.5 → length = 0.1 + (0.9-0.1)*0.5 = 0.5
        var mid = ConditionalFormatEvaluator.EvaluateDataBar(rule, 50, stats);
        mid!.Value.EndFraction.Should().BeApproximately(0.5, 1e-9);

        // fraction 0 → length = 0.1 (min length, still a visible bar)
        var lo = ConditionalFormatEvaluator.EvaluateDataBar(rule, 0, stats);
        lo!.Value.EndFraction.Should().BeApproximately(0.1, 1e-9);
    }

    [Fact]
    public void DataBar_MaxNotGreaterThanMin_ReturnsNull()
    {
        var rule = DataBarRule();
        rule.DataBarMinThresholdType = CfThresholdType.Number;
        rule.DataBarMinThresholdValue = "100";
        rule.DataBarMaxThresholdType = CfThresholdType.Number;
        rule.DataBarMaxThresholdValue = "100";
        var stats = ConditionalFormatStatistics.FromValues([0, 100]);

        ConditionalFormatEvaluator.EvaluateDataBar(rule, 50, stats).Should().BeNull();
    }

    [Fact]
    public void DataBar_NonFiniteValue_ReturnsNull()
    {
        var rule = DataBarRule();
        var stats = ConditionalFormatStatistics.FromValues([0, 100]);

        ConditionalFormatEvaluator.EvaluateDataBar(rule, double.NaN, stats).Should().BeNull();
    }

    [Fact]
    public void DataBar_NegativeRange_PlacesAxisProportionally()
    {
        var rule = DataBarRule();
        // range -50..50 → axis at (0 - -50)/(50 - -50) = 0.5
        var stats = ConditionalFormatStatistics.FromValues([-50, 50]);

        var positive = ConditionalFormatEvaluator.EvaluateDataBar(rule, 50, stats);
        positive.Should().NotBeNull();
        positive!.Value.AxisFraction.Should().BeApproximately(0.5, 1e-9);
        positive.Value.IsNegative.Should().BeFalse();
        positive.Value.StartFraction.Should().BeApproximately(0.5, 1e-9);
        positive.Value.EndFraction.Should().BeApproximately(1.0, 1e-9);

        var negative = ConditionalFormatEvaluator.EvaluateDataBar(rule, -50, stats);
        negative.Should().NotBeNull();
        negative!.Value.IsNegative.Should().BeTrue();
        negative.Value.AxisFraction.Should().BeApproximately(0.5, 1e-9);
        negative.Value.StartFraction.Should().BeApproximately(0.0, 1e-9);
        negative.Value.EndFraction.Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void DataBar_NegativeRange_HalfNegativeValue_FillsHalfOfNegativeSide()
    {
        var rule = DataBarRule();
        var stats = ConditionalFormatStatistics.FromValues([-100, 100]); // axis at 0.5

        var layout = ConditionalFormatEvaluator.EvaluateDataBar(rule, -50, stats);

        layout!.Value.IsNegative.Should().BeTrue();
        // negative magnitude 50 of 100 → half the negative side (0.5 wide) → start 0.25, end 0.5
        layout.Value.StartFraction.Should().BeApproximately(0.25, 1e-9);
        layout.Value.EndFraction.Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void DataBar_NegativeRange_UsesNegativeFillColorWhenSet()
    {
        var rule = DataBarRule();
        rule.DataBarNegativeFillColor = new RgbColor(200, 0, 0);
        var stats = ConditionalFormatStatistics.FromValues([-50, 50]);

        var negative = ConditionalFormatEvaluator.EvaluateDataBar(rule, -25, stats);

        negative!.Value.FillColor.Should().Be(new PresentationRgb(200, 0, 0));
    }

    [Fact]
    public void DataBar_NegativeRange_DefaultsToExcelAutomaticRed_WhenNoNegativeColorSet()
    {
        // R61-io-cf-databar-x14-6-1: with no explicit DataBarNegativeFillColor, Excel's "automatic"
        // negative data-bar fill is solid red (0xFF,0x00,0x00), never the positive fill color -- this
        // must match the on-screen grid engine (ViewportConditionalFormatEvaluator).
        var rule = DataBarRule(); // DataBarColor = (10, 20, 30), DataBarNegativeFillColor left null.
        var stats = ConditionalFormatStatistics.FromValues([-50, 50]);

        var negative = ConditionalFormatEvaluator.EvaluateDataBar(rule, -25, stats);

        negative!.Value.FillColor.Should().Be(new PresentationRgb(0xFF, 0x00, 0x00));
        negative.Value.FillColor.Should().NotBe(new PresentationRgb(10, 20, 30));
    }

    [Fact]
    public void DataBar_AxisNone_UsesLeftAnchoredEvenWithNegativeRange()
    {
        var rule = DataBarRule();
        rule.DataBarAxisPosition = "none";
        var stats = ConditionalFormatStatistics.FromValues([-50, 50]);

        var layout = ConditionalFormatEvaluator.EvaluateDataBar(rule, 0, stats);

        // left-anchored: fraction = (0 - -50)/(50 - -50) = 0.5, start at 0
        layout!.Value.StartFraction.Should().Be(0);
        layout.Value.EndFraction.Should().BeApproximately(0.5, 1e-9);
        layout.Value.IsNegative.Should().BeFalse();
    }
}
