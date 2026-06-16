using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.ConditionalFormatting;

public sealed class ConditionalFormatStatisticsTests
{
    [Fact]
    public void FromValues_ComputesMinMaxAverageAndSortedValues()
    {
        var stats = ConditionalFormatStatistics.FromValues([3, 1, 2, 4]);

        stats.Count.Should().Be(4);
        stats.Min.Should().Be(1);
        stats.Max.Should().Be(4);
        stats.Average.Should().Be(2.5);
        stats.SortedValues.Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public void FromValues_IgnoresNonFiniteValues()
    {
        var stats = ConditionalFormatStatistics.FromValues([1, double.NaN, double.PositiveInfinity, 5]);

        stats.Count.Should().Be(2);
        stats.Min.Should().Be(1);
        stats.Max.Should().Be(5);
    }

    [Fact]
    public void FromValues_EmptyRange_YieldsZeroes()
    {
        var stats = ConditionalFormatStatistics.FromValues([]);

        stats.Count.Should().Be(0);
        stats.Min.Should().Be(0);
        stats.Max.Should().Be(0);
        stats.Average.Should().Be(0);
        stats.SortedValues.Should().BeEmpty();
    }

    [Theory]
    [InlineData(CfThresholdType.Min, null, 1)]
    [InlineData(CfThresholdType.Max, null, 10)]
    [InlineData(CfThresholdType.Number, "7.5", 7.5)]
    [InlineData(CfThresholdType.Percent, "50", 5.5)] // min + (max-min)*0.5 = 1 + 9*0.5
    public void TryResolveThreshold_StaticTypes(CfThresholdType type, string? value, double expected)
    {
        var stats = ConditionalFormatStatistics.FromValues([1, 10]);

        stats.TryResolveThreshold(type, value, out var resolved).Should().BeTrue();
        resolved.Should().BeApproximately(expected, 1e-9);
    }

    [Fact]
    public void TryResolveThreshold_Formula_ReturnsFalse()
    {
        var stats = ConditionalFormatStatistics.FromValues([1, 10]);

        stats.TryResolveThreshold(CfThresholdType.Formula, "A1", out _).Should().BeFalse();
    }

    [Fact]
    public void TryResolveThreshold_NumberWithInvalidText_ReturnsFalse()
    {
        var stats = ConditionalFormatStatistics.FromValues([1, 10]);

        stats.TryResolveThreshold(CfThresholdType.Number, "not-a-number", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, 2)]   // midpoint of [0,1,2,3,4]
    [InlineData(100, 4)]
    [InlineData(25, 1)]
    public void Percentile_MatchesLinearInterpolation(double percentile, double expected)
    {
        var sorted = new double[] { 0, 1, 2, 3, 4 };

        ConditionalFormatStatistics.TryResolvePercentile(sorted, percentile, out var value).Should().BeTrue();
        value.Should().BeApproximately(expected, 1e-9);
    }

    [Fact]
    public void Percentile_InterpolatesBetweenValues()
    {
        // position = (4-1)*70/100 = 2.1 → between index 2 (30) and 3 (40): 30 + 10*0.1 = 31
        var sorted = new double[] { 10, 20, 30, 40 };

        ConditionalFormatStatistics.TryResolvePercentile(sorted, 70, out var value).Should().BeTrue();
        value.Should().BeApproximately(31, 1e-9);
    }

    [Fact]
    public void Percentile_SingleValue_ReturnsThatValue()
    {
        ConditionalFormatStatistics.TryResolvePercentile([42], 13, out var value).Should().BeTrue();
        value.Should().Be(42);
    }

    [Fact]
    public void Percentile_EmptyList_ReturnsFalse()
    {
        ConditionalFormatStatistics.TryResolvePercentile([], 50, out _).Should().BeFalse();
    }
}
