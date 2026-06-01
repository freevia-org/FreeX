using System.Collections;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class RibbonResizeThresholdGateTests
{
    [Theory]
    [InlineData(761, 760)]
    [InlineData(760, 761)]
    [InlineData(921, 920)]
    [InlineData(920, 921)]
    public void CrossedAnyThreshold_TreatsThresholdEqualityAsAStateBoundary(
        double previousWidth,
        double currentWidth)
    {
        RibbonResizeThresholdGate
            .CrossedAnyThreshold(previousWidth, currentWidth, [760, 920])
            .Should()
            .BeTrue();
    }

    [Theory]
    [InlineData(761, 762)]
    [InlineData(760, 759)]
    [InlineData(920, 919)]
    [InlineData(919, 918)]
    public void CrossedAnyThreshold_IgnoresMovesInsideTheSameBreakpointBand(
        double previousWidth,
        double currentWidth)
    {
        RibbonResizeThresholdGate
            .CrossedAnyThreshold(previousWidth, currentWidth, [760, 920])
            .Should()
            .BeFalse();
    }

    [Theory]
    [InlineData(1500, 650)]
    [InlineData(650, 1500)]
    public void CrossedAnyThreshold_DetectsResizeJumpsAcrossBreakpointBands(
        double previousWidth,
        double currentWidth)
    {
        RibbonResizeThresholdGate
            .CrossedAnyThreshold(previousWidth, currentWidth, [760, 920, 1120, 1320])
            .Should()
            .BeTrue();
    }

    [Fact]
    public void CrossedAnyThreshold_UsesBandLookupForSameBandResizeNoise()
    {
        var thresholds = new CountingThresholdList(
            Enumerable
                .Range(0, 256)
                .Select(index => 640d + index * 3.5)
                .ToArray());

        RibbonResizeThresholdGate
            .CrossedAnyThreshold(1_800.1, 1_800.4, thresholds)
            .Should()
            .BeFalse();

        Console.WriteLine(
            "PERF RIBBON_RESIZE_THRESHOLD_GATE_SAME_BAND " +
            $"thresholds={thresholds.Count} item_accesses={thresholds.ItemAccessCount} " +
            $"linear_baseline_accesses={thresholds.Count}");
        thresholds.ItemAccessCount.Should().BeLessThan(32);
    }

    private sealed class CountingThresholdList(IReadOnlyList<double> thresholds) : IReadOnlyList<double>
    {
        public int ItemAccessCount { get; private set; }

        public double this[int index]
        {
            get
            {
                ItemAccessCount++;
                return thresholds[index];
            }
        }

        public int Count => thresholds.Count;

        public IEnumerator<double> GetEnumerator()
        {
            for (var index = 0; index < Count; index++)
                yield return this[index];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
