using FluentAssertions;

namespace Freexcel.App.Host.Tests;

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
}
