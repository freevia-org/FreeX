using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class RunBaselinePositionPlannerTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, -4)]
    [InlineData(-2.25, 3)]
    public void ResolveOffsetDip_MapsWordBaselineDirection(double positionPt, double expectedDip)
    {
        var formatting = new RunFormatting { PositionPt = positionPt };

        RunBaselinePositionPlanner.ResolveOffsetDip(formatting).Should().BeApproximately(expectedDip, 0.001);
    }

    [Fact]
    public void ResolveOffsetDip_UsesCallerScale()
    {
        var formatting = new RunFormatting { PositionPt = 2.5 };

        RunBaselinePositionPlanner.ResolveOffsetDip(formatting, 2).Should().Be(-5);
    }
}
