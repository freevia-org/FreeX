using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowScreenModePlannerTests
{
    [Fact]
    public void BlankModesAreOnlyBlackAndWhite()
    {
        SlideShowScreenModePlanner.IsBlank(SlideShowScreenMode.Normal).Should().BeFalse();
        SlideShowScreenModePlanner.IsBlank(SlideShowScreenMode.Black).Should().BeTrue();
        SlideShowScreenModePlanner.IsBlank(SlideShowScreenMode.White).Should().BeTrue();
    }

    [Theory]
    [InlineData("B", SlideShowScreenMode.Normal, SlideShowScreenMode.Black)]
    [InlineData("b", SlideShowScreenMode.Black, SlideShowScreenMode.Normal)]
    [InlineData("W", SlideShowScreenMode.Normal, SlideShowScreenMode.White)]
    [InlineData("w", SlideShowScreenMode.White, SlideShowScreenMode.Normal)]
    [InlineData("B", SlideShowScreenMode.White, SlideShowScreenMode.Black)]
    [InlineData("W", SlideShowScreenMode.Black, SlideShowScreenMode.White)]
    public void PresenterKeysSwitchAndRestoreBlankModes(
        string key,
        SlideShowScreenMode current,
        SlideShowScreenMode expected)
    {
        SlideShowScreenModePlanner.TryPlanKey(key, current, out var next).Should().BeTrue();
        next.Should().Be(expected);
    }

    [Fact]
    public void NavigationKeysAreNotConsumedByBlankScreenPlanner()
    {
        SlideShowScreenModePlanner.TryPlanKey("Right", SlideShowScreenMode.Black, out var next).Should().BeFalse();
        next.Should().Be(SlideShowScreenMode.Black);
    }
}
