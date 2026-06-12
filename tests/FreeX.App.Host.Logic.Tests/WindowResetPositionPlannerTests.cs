using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed class WindowResetPositionPlannerTests
{
    [Fact]
    public void StandardSize_IsAFractionOfTheWorkArea_ClampedToTheWorkArea()
    {
        var rect = WindowResetPositionPlanner.Compute(workAreaWidth: 1920, workAreaHeight: 1080, windowIndex: 0);

        rect.Width.Should().BeApproximately(1920 * WindowResetPositionPlanner.StandardSizeFraction, 0.001);
        rect.Height.Should().BeApproximately(1080 * WindowResetPositionPlanner.StandardSizeFraction, 0.001);
        rect.Width.Should().BeLessThanOrEqualTo(1920);
        rect.Height.Should().BeLessThanOrEqualTo(1080);
    }

    [Fact]
    public void FirstWindow_IsCenteredInTheWorkArea()
    {
        var rect = WindowResetPositionPlanner.Compute(workAreaWidth: 1920, workAreaHeight: 1080, windowIndex: 0);

        var expectedLeft = (1920 - rect.Width) / 2;
        var expectedTop = (1080 - rect.Height) / 2;
        rect.Left.Should().BeApproximately(expectedLeft, 0.001);
        rect.Top.Should().BeApproximately(expectedTop, 0.001);
    }

    [Fact]
    public void LaterWindows_CascadeDownAndRightFromTheCenteredOrigin()
    {
        var first = WindowResetPositionPlanner.Compute(1920, 1080, windowIndex: 0);
        var second = WindowResetPositionPlanner.Compute(1920, 1080, windowIndex: 1);

        second.Left.Should().BeApproximately(first.Left + WindowResetPositionPlanner.CascadeOffset, 0.001);
        second.Top.Should().BeApproximately(first.Top + WindowResetPositionPlanner.CascadeOffset, 0.001);
        second.Width.Should().Be(first.Width);
        second.Height.Should().Be(first.Height);
    }

    [Fact]
    public void Cascade_NeverPushesTheWindowOffTheRightOrBottomOfTheWorkArea()
    {
        for (var index = 0; index < 200; index++)
        {
            var rect = WindowResetPositionPlanner.Compute(1920, 1080, windowIndex: index);

            rect.Left.Should().BeGreaterThanOrEqualTo(0);
            rect.Top.Should().BeGreaterThanOrEqualTo(0);
            (rect.Left + rect.Width).Should().BeLessThanOrEqualTo(1920.001);
            (rect.Top + rect.Height).Should().BeLessThanOrEqualTo(1080.001);
        }
    }

    [Fact]
    public void NegativeWindowIndex_IsTreatedAsTheFirstWindow()
    {
        var first = WindowResetPositionPlanner.Compute(1920, 1080, windowIndex: 0);
        var negative = WindowResetPositionPlanner.Compute(1920, 1080, windowIndex: -5);

        negative.Should().Be(first);
    }

    [Fact]
    public void TinyWorkArea_StillProducesAPositiveSizeClampedToTheWorkArea()
    {
        var rect = WindowResetPositionPlanner.Compute(workAreaWidth: 200, workAreaHeight: 150, windowIndex: 0);

        rect.Width.Should().BeGreaterThan(0);
        rect.Height.Should().BeGreaterThan(0);
        rect.Width.Should().BeLessThanOrEqualTo(200);
        rect.Height.Should().BeLessThanOrEqualTo(150);
        rect.Left.Should().BeGreaterThanOrEqualTo(0);
        rect.Top.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void NonPositiveWorkArea_FallsBackToTheDefaultStandardSizeAtTheOrigin()
    {
        var rect = WindowResetPositionPlanner.Compute(workAreaWidth: 0, workAreaHeight: -10, windowIndex: 0);

        rect.Left.Should().Be(0);
        rect.Top.Should().Be(0);
        rect.Width.Should().Be(WindowResetPositionPlanner.FallbackWidth);
        rect.Height.Should().Be(WindowResetPositionPlanner.FallbackHeight);
    }
}
