using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests;

/// <summary>
/// Twin regression tests for the portable <see cref="ConditionalFormatEvaluator"/> (the
/// framework-free port of <c>ViewportConditionalFormatEvaluator</c> used by PDF/print rendering),
/// mirroring the grid-engine fixes for R68-render-conditional-format-6-1 (data-bar Axis Position
/// "Midpoint" ignored for all-positive data) and R68-render-conditional-format-6-2 (3-color scale
/// MidColor dropped entirely when the resolved midpoint is degenerate).
/// </summary>
public sealed class R68_ConditionalFormatEvaluatorTwinTests
{
    [Fact]
    public void EvaluateDataBar_AxisMidpoint_AllPositiveRange_DrawsAxisAtCenterWithHalfWidthBars()
    {
        var rule = new ConditionalFormat
        {
            RuleType = CfRuleType.DataBar,
            DataBarColor = new RgbColor(99, 142, 198),
            DataBarAxisPosition = "middle",
            // Min/Max threshold types default to CfThresholdType.Min/Max (automatic).
        };
        var stats = ConditionalFormatStatistics.FromValues([10, 20, 30]);

        var maxBar = ConditionalFormatEvaluator.EvaluateDataBar(rule, 30, stats);
        maxBar.Should().NotBeNull();
        maxBar!.Value.IsNegative.Should().BeFalse();
        maxBar.Value.AxisFraction.Should().BeApproximately(0.5, 0.0001, "Midpoint pins the axis at cell-center even for all-positive data");
        maxBar.Value.StartFraction.Should().BeApproximately(0.5, 0.0001);
        maxBar.Value.EndFraction.Should().BeApproximately(1.0, 0.0001, "the max value fills the whole right half");

        var minBar = ConditionalFormatEvaluator.EvaluateDataBar(rule, 10, stats);
        minBar.Should().NotBeNull();
        minBar!.Value.AxisFraction.Should().BeApproximately(0.5, 0.0001);
        minBar.Value.StartFraction.Should().BeApproximately(0.5, 0.0001);
        minBar.Value.EndFraction.Should().BeApproximately(0.5 + (10d / 30d) * 0.5, 0.0001,
            "bar length is scaled against half the cell width (1 - axisFraction), not the full width");
    }

    [Fact]
    public void EvaluateDataBar_AxisAutomatic_AllPositiveRange_StillLeftAnchored_NoRegression()
    {
        var rule = new ConditionalFormat
        {
            RuleType = CfRuleType.DataBar,
            DataBarColor = new RgbColor(99, 142, 198),
            // DataBarAxisPosition left null -> Automatic.
        };
        var stats = ConditionalFormatStatistics.FromValues([10, 20, 30]);

        var maxBar = ConditionalFormatEvaluator.EvaluateDataBar(rule, 30, stats);

        maxBar.Should().NotBeNull();
        maxBar!.Value.AxisFraction.Should().Be(0d, "Automatic axis on an all-positive range has no axis");
        maxBar.Value.StartFraction.Should().Be(0d, "left-anchored, unaffected by the Midpoint fix");
        maxBar.Value.EndFraction.Should().BeApproximately(1.0, 0.0001);
    }

    [Fact]
    public void EvaluateDataBar_AxisMidpoint_NegativeStraddlingRange_StillWorks_NoRegression()
    {
        var rule = new ConditionalFormat
        {
            RuleType = CfRuleType.DataBar,
            DataBarColor = new RgbColor(0, 112, 192),
            DataBarNegativeFillColor = new RgbColor(255, 0, 0),
            DataBarAxisPosition = "middle",
        };
        var stats = ConditionalFormatStatistics.FromValues([-50, 50]);

        var positiveBar = ConditionalFormatEvaluator.EvaluateDataBar(rule, 50, stats);

        positiveBar.Should().NotBeNull();
        positiveBar!.Value.IsNegative.Should().BeFalse();
        positiveBar.Value.AxisFraction.Should().BeApproximately(0.5, 0.001);
        positiveBar.Value.StartFraction.Should().BeApproximately(0.5, 0.001);
        positiveBar.Value.EndFraction.Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public void EvaluateColorScale_DegenerateMidpointEqualToMin_StillBlendsMidToMaxAboveIt()
    {
        var rule = new ConditionalFormat
        {
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = true,
            MinThresholdType = CfThresholdType.Min,
            MidThresholdType = CfThresholdType.Percentile,
            MidThresholdValue = "50",
            MaxThresholdType = CfThresholdType.Max,
            MinColor = new RgbColor(248, 105, 107),  // red
            MidColor = new RgbColor(255, 235, 132),  // yellow
            MaxColor = new RgbColor(99, 190, 123),   // green
        };
        var stats = ConditionalFormatStatistics.FromValues([1, 1, 1, 1, 10, 5]);

        var result = ConditionalFormatEvaluator.EvaluateColorScale(rule, 5, stats);

        result.Should().NotBeNull();
        // See the engine-side test for the full Min->Max vs Mid->Max arithmetic: the distinguishing
        // channel is G (~215 for the correct Mid->Max blend vs ~143 for the buggy Min->Max blend).
        result!.Value.Fill.G.Should().BeGreaterThan(180,
            "value 5 must blend from MidColor (yellow, G=235) toward MaxColor (green, G=190), not from MinColor (red, G=105)");
    }

    [Fact]
    public void EvaluateColorScale_DegenerateMidpointEqualToMin_ValueAtDegeneratePoint_UsesMidColor()
    {
        var rule = new ConditionalFormat
        {
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = true,
            MinThresholdType = CfThresholdType.Min,
            MidThresholdType = CfThresholdType.Number,
            MidThresholdValue = "1",
            MaxThresholdType = CfThresholdType.Max,
            MinColor = new RgbColor(248, 105, 107),
            MidColor = new RgbColor(255, 235, 132),
            MaxColor = new RgbColor(99, 190, 123),
        };
        var stats = ConditionalFormatStatistics.FromValues([1, 10]);

        var result = ConditionalFormatEvaluator.EvaluateColorScale(rule, 1, stats);

        result.Should().NotBeNull();
        result!.Value.Fill.Should().Be(new PresentationRgb(255, 235, 132), "the degenerate min==mid point renders as MidColor exactly");
    }

    [Fact]
    public void EvaluateColorScale_NormalNonDegenerateThreeStop_Unchanged_NoRegression()
    {
        var rule = new ConditionalFormat
        {
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = true,
            MinThresholdType = CfThresholdType.Min,
            MidThresholdType = CfThresholdType.Number,
            MidThresholdValue = "50",
            MaxThresholdType = CfThresholdType.Max,
            MinColor = new RgbColor(0, 0, 255),
            MidColor = new RgbColor(255, 255, 255),
            MaxColor = new RgbColor(255, 0, 0),
        };
        var stats = ConditionalFormatStatistics.FromValues([0, 50, 100]);

        var result = ConditionalFormatEvaluator.EvaluateColorScale(rule, 50, stats);

        result.Should().NotBeNull();
        result!.Value.Fill.Should().Be(new PresentationRgb(255, 255, 255));
    }
}
