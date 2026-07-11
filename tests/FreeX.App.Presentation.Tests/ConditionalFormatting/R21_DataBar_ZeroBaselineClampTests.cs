using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.ConditionalFormatting;

/// <summary>
/// R21-conditional-format-render-3: automatic (Min/Max) data-bar thresholds must keep a zero
/// baseline, matching Excel and the engine ConditionalFormatEvaluator mirrors
/// (ViewportConditionalFormatEvaluator.Thresholds.cs). Without the clamp, an all-positive range
/// resolves min = actual minimum (not 0), so the smallest value gets a zero-length bar instead of
/// Excel's proportional one.
/// </summary>
public sealed class R21_DataBar_ZeroBaselineClampTests
{
    private static ConditionalFormat DataBarRule() => new()
    {
        RuleType = CfRuleType.DataBar,
        DataBarColor = new RgbColor(10, 20, 30),
        DataBarMinThresholdType = CfThresholdType.Min,
        DataBarMaxThresholdType = CfThresholdType.Max,
    };

    [Fact]
    public void AutoMinMax_AllPositiveRange_ClampsMinToZero_SmallestValueGetsProportionalBar()
    {
        var rule = DataBarRule();
        var stats = ConditionalFormatStatistics.FromValues([10, 20, 30]);

        // Before the fix: min resolves to 10 (the actual minimum), so fraction = (10-10)/(30-10) = 0
        // and the bar is empty (null). Excel clamps the automatic minimum to min(0, 10) = 0, so the
        // smallest value should get a ~1/3-length bar, not an empty one.
        var layout = ConditionalFormatEvaluator.EvaluateDataBar(rule, 10, stats);

        layout.Should().NotBeNull();
        layout!.Value.IsNegative.Should().BeFalse();
        layout.Value.StartFraction.Should().Be(0);
        layout.Value.EndFraction.Should().BeApproximately(1d / 3d, 1e-9);
    }

    [Fact]
    public void AutoMinMax_AllNegativeRange_ClampsMaxToZero_LeastNegativeValueGetsProportionalBar()
    {
        var rule = DataBarRule();
        var stats = ConditionalFormatStatistics.FromValues([-30, -20, -10]);

        // Automatic maximum clamps to max(0, -10) = 0, pinning the axis at the right edge (1.0) and
        // growing negative bars leftward. The least-negative value (-10) should get a proportional
        // (non-empty) negative bar rather than resolving max=-10 and producing an empty/degenerate
        // range.
        var layout = ConditionalFormatEvaluator.EvaluateDataBar(rule, -10, stats);

        layout.Should().NotBeNull();
        layout!.Value.IsNegative.Should().BeTrue();
        layout.Value.AxisFraction.Should().BeApproximately(1d, 1e-9);
        layout.Value.EndFraction.Should().BeApproximately(1d, 1e-9);
        layout.Value.StartFraction.Should().BeApproximately(2d / 3d, 1e-9);
    }

    [Fact]
    public void ExplicitNumericThresholds_AreNotClamped()
    {
        var rule = DataBarRule();
        rule.DataBarMinThresholdType = CfThresholdType.Number;
        rule.DataBarMinThresholdValue = "10";
        rule.DataBarMaxThresholdType = CfThresholdType.Number;
        rule.DataBarMaxThresholdValue = "30";
        var stats = ConditionalFormatStatistics.FromValues([10, 20, 30]);

        // Explicit numeric thresholds must resolve exactly as specified (min=10, max=30), unaffected
        // by the automatic zero-baseline clamp, so the smallest value still yields an empty bar.
        ConditionalFormatEvaluator.EvaluateDataBar(rule, 10, stats).Should().BeNull();
    }
}
