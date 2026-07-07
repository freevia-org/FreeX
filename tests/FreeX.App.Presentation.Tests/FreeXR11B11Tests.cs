using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests;

/// <summary>
/// Regression test for FreeX round-11 finding R11-conditional-format-1 (twin fix): the portable
/// <see cref="ConditionalFormatEvaluator.EvaluateDataBar"/> used by the Avalonia shell shares the
/// same negative-axis condition as the engine's <c>ViewportConditionalFormatEvaluator</c> and had
/// the identical "max &gt; 0" (strict) bug -- an all-negative range whose resolved maximum lands at
/// exactly 0 must still take the negative-axis path (axis at the right edge, bars growing leftward
/// in the negative fill color) rather than falling through to the left-anchored positive-only path.
/// </summary>
public sealed class FreeXR11B11Tests
{
    [Fact]
    public void EvaluateDataBar_AllNegativeRange_MaxExactlyZero_UsesNegativeAxisPath()
    {
        var rule = new ConditionalFormat
        {
            RuleType = CfRuleType.DataBar,
            DataBarColor = new RgbColor(99, 142, 198),
            DataBarNegativeFillColor = new RgbColor(255, 0, 0),
            DataBarMinThresholdType = CfThresholdType.Number,
            DataBarMinThresholdValue = "-30",
            DataBarMaxThresholdType = CfThresholdType.Number,
            DataBarMaxThresholdValue = "0",
            // DataBarAxisPosition left null -> Automatic (not "none").
        };
        var stats = ConditionalFormatStatistics.FromValues([-10, -20, -30]);

        var mostNegative = ConditionalFormatEvaluator.EvaluateDataBar(rule, -30, stats);
        var leastNegative = ConditionalFormatEvaluator.EvaluateDataBar(rule, -10, stats);

        mostNegative.Should().NotBeNull();
        leastNegative.Should().NotBeNull();

        // Axis at the right edge: (0 - -30) / (0 - -30) = 1.0.
        mostNegative!.Value.AxisFraction.Should().BeApproximately(1.0, 1e-9);
        leastNegative!.Value.AxisFraction.Should().BeApproximately(1.0, 1e-9);

        mostNegative.Value.IsNegative.Should().BeTrue("an all-negative range must use the negative-axis path");
        leastNegative.Value.IsNegative.Should().BeTrue();

        mostNegative.Value.FillColor.Should().Be(new PresentationRgb(255, 0, 0), "negative fill color must be used");
        leastNegative.Value.FillColor.Should().Be(new PresentationRgb(255, 0, 0));

        // The most-negative value must have the longer bar (fills the whole width); the
        // least-negative value must have a strictly shorter bar.
        var mostLength = mostNegative.Value.EndFraction - mostNegative.Value.StartFraction;
        var leastLength = leastNegative.Value.EndFraction - leastNegative.Value.StartFraction;
        mostLength.Should().BeGreaterThan(leastLength, "the most-negative value must have the longer bar");
        mostNegative.Value.StartFraction.Should().BeApproximately(0.0, 1e-9, "the most-negative value fills the entire bar width");
    }
}
