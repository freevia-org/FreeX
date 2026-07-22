using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.ConditionalFormatting;

public sealed class ColorScaleEvaluatorTests
{
    private static ConditionalFormat TwoColorRule() => new()
    {
        RuleType = CfRuleType.ColorScale,
        UseThreeColorScale = false,
        MinColor = new RgbColor(0, 0, 0),
        MaxColor = new RgbColor(100, 100, 100),
        MinThresholdType = CfThresholdType.Min,
        MaxThresholdType = CfThresholdType.Max,
    };

    private static ConditionalFormat ThreeColorRule() => new()
    {
        RuleType = CfRuleType.ColorScale,
        UseThreeColorScale = true,
        MinColor = new RgbColor(0, 0, 0),
        MidColor = new RgbColor(100, 100, 100),
        MaxColor = new RgbColor(200, 200, 200),
        MinThresholdType = CfThresholdType.Min,
        MidThresholdType = CfThresholdType.Percentile,
        MidThresholdValue = "50",
        MaxThresholdType = CfThresholdType.Max,
    };

    [Fact]
    public void TwoColor_AtMin_ReturnsMinColor()
    {
        var stats = ConditionalFormatStatistics.FromValues([0, 100]);
        var result = ConditionalFormatEvaluator.EvaluateColorScale(TwoColorRule(), 0, stats);
        result!.Value.Fill.Should().Be(new PresentationRgb(0, 0, 0));
    }

    [Fact]
    public void TwoColor_AtMax_ReturnsMaxColor()
    {
        var stats = ConditionalFormatStatistics.FromValues([0, 100]);
        var result = ConditionalFormatEvaluator.EvaluateColorScale(TwoColorRule(), 100, stats);
        result!.Value.Fill.Should().Be(new PresentationRgb(100, 100, 100));
    }

    [Fact]
    public void TwoColor_AtMidpoint_InterpolatesHalfway()
    {
        var stats = ConditionalFormatStatistics.FromValues([0, 100]);
        var result = ConditionalFormatEvaluator.EvaluateColorScale(TwoColorRule(), 50, stats);
        result!.Value.Fill.Should().Be(new PresentationRgb(50, 50, 50));
    }

    [Fact]
    public void TwoColor_MaxEqualsMin_ReturnsMinColor()
    {
        var rule = TwoColorRule();
        rule.MinThresholdType = CfThresholdType.Number;
        rule.MinThresholdValue = "5";
        rule.MaxThresholdType = CfThresholdType.Number;
        rule.MaxThresholdValue = "5";
        var stats = ConditionalFormatStatistics.FromValues([0, 100]);

        var result = ConditionalFormatEvaluator.EvaluateColorScale(rule, 5, stats);
        result!.Value.Fill.Should().Be(new PresentationRgb(0, 0, 0));
    }

    [Fact]
    public void TwoColor_NonNumericInputModeledAsNonFinite_ReturnsNull()
    {
        var stats = ConditionalFormatStatistics.FromValues([0, 100]);
        ConditionalFormatEvaluator.EvaluateColorScale(TwoColorRule(), double.NaN, stats).Should().BeNull();
    }

    [Fact]
    public void ThreeColor_AtMin_Mid_Max()
    {
        var stats = ConditionalFormatStatistics.FromValues([0, 50, 100]); // percentile 50 → 50

        var rule = ThreeColorRule();
        ConditionalFormatEvaluator.EvaluateColorScale(rule, 0, stats)!.Value.Fill.Should().Be(new PresentationRgb(0, 0, 0));
        ConditionalFormatEvaluator.EvaluateColorScale(rule, 50, stats)!.Value.Fill.Should().Be(new PresentationRgb(100, 100, 100));
        ConditionalFormatEvaluator.EvaluateColorScale(rule, 100, stats)!.Value.Fill.Should().Be(new PresentationRgb(200, 200, 200));
    }

    [Fact]
    public void ThreeColor_BelowMid_InterpolatesMinToMid()
    {
        var stats = ConditionalFormatStatistics.FromValues([0, 50, 100]); // mid = 50
        // value 25 → halfway between min(0,0,0) and mid(100,100,100)
        var result = ConditionalFormatEvaluator.EvaluateColorScale(ThreeColorRule(), 25, stats);
        result!.Value.Fill.Should().Be(new PresentationRgb(50, 50, 50));
    }

    [Fact]
    public void ThreeColor_AboveMid_InterpolatesMidToMax()
    {
        var stats = ConditionalFormatStatistics.FromValues([0, 50, 100]); // mid = 50
        // value 75 → halfway between mid(100,100,100) and max(200,200,200)
        var result = ConditionalFormatEvaluator.EvaluateColorScale(ThreeColorRule(), 75, stats);
        result!.Value.Fill.Should().Be(new PresentationRgb(150, 150, 150));
    }

    [Fact]
    public void ThreeColor_MidOutsideRange_ClampsToBoundaryAndKeepsThreeStopPath()
    {
        // R68-render-conditional-format-6-2: a resolved midpoint outside [min,max] (here 999 vs a
        // 0..100 range) used to null out `mid` entirely, silently falling back to a two-color
        // Min->Max lerp for every value and erasing MidColor everywhere. It is now clamped to the
        // nearest boundary (max, here) and the 3-stop path is kept, so a value below the clamped
        // midpoint still blends Min->Mid (not Min->Max) using the resolvedMid==max degenerate case.
        var rule = ThreeColorRule();
        rule.MidThresholdType = CfThresholdType.Number;
        rule.MidThresholdValue = "999"; // clamps to max (100)
        var stats = ConditionalFormatStatistics.FromValues([0, 100]);

        // value 50 interpolates min(0,0,0) -> mid(100,100,100) at t=(50-0)/(100-0)=0.5, NOT
        // min(0,0,0) -> max(200,200,200) at the same t (which would give (100,100,100)).
        var result = ConditionalFormatEvaluator.EvaluateColorScale(rule, 50, stats);
        result!.Value.Fill.Should().Be(new PresentationRgb(50, 50, 50));
    }

    [Fact]
    public void ThreeColor_MidOutsideRange_AtClampedBoundary_UsesMidColorExactly()
    {
        // Sibling: the clamped-max degenerate point itself (cellValue == max == clamped mid) must
        // render as MidColor exactly, mirroring the resolvedMid==min degenerate case.
        var rule = ThreeColorRule();
        rule.MidThresholdType = CfThresholdType.Number;
        rule.MidThresholdValue = "999";
        var stats = ConditionalFormatStatistics.FromValues([0, 100]);

        var result = ConditionalFormatEvaluator.EvaluateColorScale(rule, 100, stats);
        result!.Value.Fill.Should().Be(new PresentationRgb(100, 100, 100), "the clamped mid==max boundary renders as MidColor");
    }
}
