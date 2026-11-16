using System.IO;

namespace FreeP.App.Avalonia.Tests;

public sealed class SlideShowAnimationTimingParityTests
{
    [Fact]
    public void Avalonia_timing_adapter_uses_authored_host_curve_and_preserves_endpoints()
    {
        AvaloniaSlideShowAnimationTiming.Apply(0, 25000, 35000).Should().Be(0);
        AvaloniaSlideShowAnimationTiming.Apply(1, 25000, 35000).Should().Be(1);
        AvaloniaSlideShowAnimationTiming.Apply(0.0625, 25000, 35000)
            .Should().BeApproximately(0.0390625, 0.000001);

        var retimed = AvaloniaSlideShowAnimationTiming.RetimedKeyframeProgress(0.05, 25000, 35000);
        AvaloniaSlideShowAnimationTiming.Apply(retimed, 25000, 35000)
            .Should().BeApproximately(0.05, 0.000001);
    }

    [Fact]
    public void Avalonia_shape_animation_timers_consume_the_authored_timing_plan()
    {
        var source = File.ReadAllText(RepoFile(
            "freep",
            "FreeP.App.Avalonia",
            "SlideShowWindow.cs"));

        source.Should().Contain("ResolveAnimationProgress(t, animationPlan)");
        source.Should().Contain("AvaloniaSlideShowAnimationTiming.Apply(");
        source.Should().Contain("animationPlan.Acceleration,");
        source.Should().Contain("animationPlan.Deceleration)");
        source.Should().Contain("animationPlan: plan");
        source.Should().Contain("AvaloniaSlideShowAnimationTiming.RetimedKeyframeProgress(");
    }

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.ResolveFromDirectoryContainingFile(
            "FreeX.slnx", parts);
}
