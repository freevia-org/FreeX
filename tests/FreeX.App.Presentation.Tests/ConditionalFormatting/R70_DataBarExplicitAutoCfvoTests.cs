using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.ConditionalFormatting;

/// <summary>
/// Presentation-twin coverage for R70-io-cf-databar-cfvo-6-3 (deferred since round 61), mirroring
/// R70_DataBarExplicitAutoCfvoTests in FreeX.Core.Calc.Tests: an EXPLICIT Lowest/Highest Value
/// data-bar endpoint (<see cref="CfThresholdType.Min"/>/<see cref="CfThresholdType.Max"/>) must NOT
/// receive Excel's Automatic zero-baseline clamp that <see cref="CfThresholdType.AutoMin"/>/
/// <see cref="CfThresholdType.AutoMax"/> gets. Before this fix, CfThresholdType had no Auto* variants
/// and every data-bar Min/Max threshold was clamped identically, so this distinction did not exist.
/// </summary>
public sealed class R70_DataBarExplicitAutoCfvoTests
{
    private static ConditionalFormat DataBarRule(CfThresholdType minType, CfThresholdType maxType) => new()
    {
        RuleType = CfRuleType.DataBar,
        DataBarColor = new RgbColor(10, 20, 30),
        DataBarMinThresholdType = minType,
        DataBarMaxThresholdType = maxType,
    };

    [Fact]
    public void EvaluateDataBar_AutoMin_AllPositiveRange_ClampsToZero_SmallestValueGetsProportionalBar()
    {
        var rule = DataBarRule(CfThresholdType.AutoMin, CfThresholdType.AutoMax);
        var stats = ConditionalFormatStatistics.FromValues([10, 20, 30]);

        var layout = ConditionalFormatEvaluator.EvaluateDataBar(rule, 10, stats);

        layout.Should().NotBeNull("AutoMin clamps the automatic minimum to min(0, 10) = 0");
        layout!.Value.IsNegative.Should().BeFalse();
        layout.Value.StartFraction.Should().Be(0);
        layout.Value.EndFraction.Should().BeApproximately(1d / 3d, 1e-9);
    }

    [Fact]
    public void EvaluateDataBar_ExplicitMin_AllPositiveRange_NotClamped_SmallestValueGetsNoBar()
    {
        // Same rule shape and same all-positive data as the AutoMin test above, differing ONLY in
        // DataBarMinThresholdType (explicit Min == Excel's "Lowest Value" instead of "Automatic").
        // Before this fix there was no model distinction, so this rule behaved identically to the
        // AutoMin one above (min clamped to 0) and this test failed (a non-null 1/3-length bar).
        var rule = DataBarRule(CfThresholdType.Min, CfThresholdType.Max);
        var stats = ConditionalFormatStatistics.FromValues([10, 20, 30]);

        var layout = ConditionalFormatEvaluator.EvaluateDataBar(rule, 10, stats);

        layout.Should().BeNull(
            "an explicit Lowest Value endpoint resolves to the actual minimum (10) unclamped, so the " +
            "smallest cell's fraction is (10-10)/(30-10)=0 -- an authoritative empty bar, not Excel's " +
            "Automatic zero-baseline-clamped 1/3-length bar");
    }

    [Fact]
    public void EvaluateDataBar_AutoMax_AllNegativeRange_ClampsToZero_MostNegativeValueGetsFullBar()
    {
        var rule = DataBarRule(CfThresholdType.AutoMin, CfThresholdType.AutoMax);
        var stats = ConditionalFormatStatistics.FromValues([-30, -20, -10]);

        // min stays -30 (already <= 0, unaffected by the AutoMin clamp); max clamps from -10 up to 0,
        // so min < 0 <= max enters the negative-axis path with the axis pinned to the right edge
        // (axisFraction = 1) -- the most negative value (-30) fills the FULL negative side.
        var layout = ConditionalFormatEvaluator.EvaluateDataBar(rule, -30, stats);

        layout.Should().NotBeNull("AutoMax clamps the automatic maximum to max(0, -10) = 0");
        layout!.Value.IsNegative.Should().BeTrue();
        layout.Value.AxisFraction.Should().BeApproximately(1d, 1e-9);
        layout.Value.StartFraction.Should().BeApproximately(0d, 1e-9);
        layout.Value.EndFraction.Should().BeApproximately(1d, 1e-9);
    }

    [Fact]
    public void EvaluateDataBar_ExplicitMax_AllNegativeRange_NotClamped_MostNegativeValueGetsNoBar()
    {
        // Same rule shape and same all-negative data as the AutoMax test above, differing ONLY in
        // DataBarMaxThresholdType (explicit Max == Excel's "Highest Value" instead of "Automatic").
        // With max left at the actual -10 (unclamped, still negative), the negative-axis path's
        // "max >= 0" condition never engages, so this falls through to the plain left-anchored
        // fraction -- and the most negative value (-30), which equals the resolved minimum, resolves
        // to fraction 0: an authoritative empty bar. Before this fix there was no model distinction,
        // so this rule behaved identically to the AutoMax one above and this test failed.
        var rule = DataBarRule(CfThresholdType.Min, CfThresholdType.Max);
        var stats = ConditionalFormatStatistics.FromValues([-30, -20, -10]);

        var layout = ConditionalFormatEvaluator.EvaluateDataBar(rule, -30, stats);

        layout.Should().BeNull(
            "an explicit Highest Value endpoint must not receive Excel's Automatic zero-baseline clamp");
    }
}
