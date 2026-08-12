using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class ConditionalFormatEvaluationMathTests
{
    [Fact]
    public void CalculateStatistics_IgnoresNonFiniteValuesAndUsesSampleStandardDeviation()
    {
        var statistics = ConditionalFormatEvaluationMath.CalculateStatistics(
            [1, 2, 3, double.NaN, double.PositiveInfinity]);

        statistics.Count.Should().Be(3);
        statistics.Min.Should().Be(1);
        statistics.Max.Should().Be(3);
        statistics.Average.Should().Be(2);
        statistics.StdDev.Should().Be(1);
        statistics.SortedValues.Should().Equal(1, 2, 3);
    }

    [Theory]
    [InlineData(CfThresholdType.Min, null, 10)]
    [InlineData(CfThresholdType.AutoMin, null, 10)]
    [InlineData(CfThresholdType.Max, null, 30)]
    [InlineData(CfThresholdType.AutoMax, null, 30)]
    [InlineData(CfThresholdType.Number, "12.5", 12.5)]
    [InlineData(CfThresholdType.Percent, "25", 15)]
    [InlineData(CfThresholdType.Percentile, "25", 15)]
    public void TryResolveStaticThreshold_HandlesEveryStaticThreshold(
        CfThresholdType type,
        string? text,
        double expected)
    {
        var statistics = ConditionalFormatEvaluationMath.CalculateStatistics([10, 20, 30]);

        ConditionalFormatEvaluationMath.TryResolveStaticThreshold(type, text, statistics, out var value)
            .Should().BeTrue();
        value.Should().Be(expected);
    }

    [Fact]
    public void TryResolveStaticThreshold_RejectsFormulaAndInvalidNumbers()
    {
        var statistics = ConditionalFormatEvaluationMath.CalculateStatistics([1, 2]);

        ConditionalFormatEvaluationMath.TryResolveStaticThreshold(CfThresholdType.Formula, "A1", statistics, out _)
            .Should().BeFalse();
        ConditionalFormatEvaluationMath.TryResolveStaticThreshold(CfThresholdType.Number, "not-a-number", statistics, out _)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(-10, 10)]
    [InlineData(0, 10)]
    [InlineData(50, 20)]
    [InlineData(100, 30)]
    [InlineData(110, 30)]
    public void TryResolvePercentile_ClampsAndInterpolates(double percentile, double expected)
    {
        ConditionalFormatEvaluationMath.TryResolvePercentile([10d, 20d, 30d], percentile, out var value)
            .Should().BeTrue();
        value.Should().Be(expected);
    }

    [Theory]
    [InlineData(3, true, false, null, true)]
    [InlineData(2, true, false, null, false)]
    [InlineData(2, true, true, null, true)]
    [InlineData(1, false, false, null, true)]
    [InlineData(3, true, true, 1, true)]
    [InlineData(2.9, true, true, 1, false)]
    public void MatchesAboveAverage_HonorsDirectionEqualityAndStandardDeviation(
        double value,
        bool above,
        bool equal,
        int? standardDeviations,
        bool expected)
    {
        var statistics = ConditionalFormatEvaluationMath.CalculateStatistics([1, 2, 3]);

        ConditionalFormatEvaluationMath.MatchesAboveAverage(
                value, statistics, above, equal, standardDeviations)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(null, 3)]
    [InlineData("2Arrows", 3)]
    [InlineData("3TrafficLights1", 3)]
    [InlineData("4Ratings", 4)]
    [InlineData("5Arrows", 5)]
    [InlineData("9Arrows", 5)]
    public void GetIconSetCount_UsesSupportedBounds(string? style, int expected) =>
        ConditionalFormatEvaluationMath.GetIconSetCount(style).Should().Be(expected);

    [Fact]
    public void IconBucketing_HonorsStrictThresholdsAndInterpolation()
    {
        ConditionalFormatEvaluationMath.GetIconSetThresholdStartIndex(3, 3).Should().Be(1);
        ConditionalFormatEvaluationMath.GetIconSetThresholdStartIndex(2, 3).Should().Be(0);
        ConditionalFormatEvaluationMath.ResolveIconBucket(20, [10, 20], [true, false], 3).Should().Be(1);
        ConditionalFormatEvaluationMath.ResolveIconBucket(20.1, [10, 20], [true, false], 3).Should().Be(2);
        ConditionalFormatEvaluationMath.ResolveInterpolatedIconBucket(20, 10, 40, 3).Should().Be(1);
        ConditionalFormatEvaluationMath.ResolveInterpolatedIconBucket(10, 10, 10, 3).Should().Be(2);
        ConditionalFormatEvaluationMath.ResolveInterpolatedIconBucket(double.NaN, 10, 40, 3).Should().Be(0);
    }
}
