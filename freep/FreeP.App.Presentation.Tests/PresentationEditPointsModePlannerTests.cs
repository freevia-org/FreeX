using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationEditPointsModePlannerTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void BuildTogglePlan_ComputesTheNextModeFromLiveState(bool current, bool expectedNext)
    {
        var plan = PresentationEditPointsModePlanner.BuildTogglePlan(current);

        plan.CurrentIsEnabled.Should().Be(current);
        plan.NextIsEnabled.Should().Be(expectedNext);
    }
}
