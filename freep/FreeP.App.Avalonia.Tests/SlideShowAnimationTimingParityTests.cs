using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia.Tests;

public sealed class SlideShowAnimationTimingParityTests
{
    private const int Acceleration = 25000;
    private const int Deceleration = 35000;

    [Fact]
    public void Flash_sampling_retimes_one_keyframe_timeline_without_restarting_easing()
    {
        var plan = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 1,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Flash,
                DurationMs = 1000,
                Acceleration = Acceleration,
                Deceleration = Deceleration
            },
            startDelayMs: 0);
        var firstKeyTime = SlideShowPlaybackPlanner.InvertHostTimingEasing(
            0.2,
            Acceleration,
            Deceleration);
        var secondKeyTime = SlideShowPlaybackPlanner.InvertHostTimingEasing(
            0.55,
            Acceleration,
            Deceleration);
        var timeline = AvaloniaSlideShowAnimationTiming.BuildFlashTimeline(plan);

        AvaloniaSlideShowAnimationTiming.SampleFlash(timeline, firstKeyTime)
            .Should().BeApproximately(0.7, 1e-9);
        AvaloniaSlideShowAnimationTiming.SampleFlash(timeline, secondKeyTime)
            .Should().BeApproximately(0.35, 1e-9);

        var halfwayToFirstKey = firstKeyTime / 2;
        var expectedSplineProgress = SampleKeySpline(0.2, 0, 0.4, 1, 0.5);
        AvaloniaSlideShowAnimationTiming.SampleFlash(timeline, halfwayToFirstKey)
            .Should().BeApproximately(0.7 * expectedSplineProgress, 1e-9);
        AvaloniaSlideShowAnimationTiming.SampleFlash(timeline, 0.2)
            .Should().NotBeApproximately(0.7, 1e-4,
                "the authored curve retimes the 20% key instead of restarting inside a fixed 20% phase");
    }

    [Fact]
    public void Trajectory_sampling_retimes_frame_keys_and_preserves_the_wpf_spline()
    {
        var playback = SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 2,
                Kind = AnimationKind.Entrance,
                Preset = AnimationPreset.Float,
                Direction = AnimationDirection.FromTop,
                DurationMs = 1000,
                Acceleration = Acceleration,
                Deceleration = Deceleration
            },
            startDelayMs: 0);
        var trajectory = SlideShowAnimationEffectFramePlanner.Build(
            playback.EffectKind,
            playback.Animation.Kind,
            playback.OffsetXFactor,
            playback.OffsetYFactor);
        var first = trajectory.Frames[0];
        var middle = trajectory.Frames[1];
        var middleKeyTime = SlideShowPlaybackPlanner.InvertHostTimingEasing(
            middle.Progress,
            Acceleration,
            Deceleration);
        var timeline = AvaloniaSlideShowAnimationTiming.BuildTrajectoryTimeline(
            trajectory,
            Acceleration,
            Deceleration);

        var atMiddleKey = AvaloniaSlideShowAnimationTiming.SampleTrajectory(
            timeline,
            middleKeyTime);
        atMiddleKey.NormalizedX.Should().BeApproximately(middle.NormalizedX, 1e-9);
        atMiddleKey.NormalizedY.Should().BeApproximately(middle.NormalizedY, 1e-9);

        var halfwayToMiddleKey = middleKeyTime / 2;
        var spline = middle.StoryboardSpline;
        spline.Should().NotBeNull();
        var expectedSplineProgress = SampleKeySpline(
            spline!.ControlPoint1X,
            spline.ControlPoint1Y,
            spline.ControlPoint2X,
            spline.ControlPoint2Y,
            0.5);
        var expected = (
            X: Lerp(first.NormalizedX, middle.NormalizedX, expectedSplineProgress),
            Y: Lerp(first.NormalizedY, middle.NormalizedY, expectedSplineProgress));
        var actual = AvaloniaSlideShowAnimationTiming.SampleTrajectory(
            timeline,
            halfwayToMiddleKey);

        actual.NormalizedX.Should().BeApproximately(expected.X, 1e-9);
        actual.NormalizedY.Should().BeApproximately(expected.Y, 1e-9);

        var oldGlobalProgressSample = SlideShowAnimationEffectFramePlanner.SampleSmooth(
            trajectory,
            SlideShowPlaybackPlanner.ApplyHostTimingEasing(
                halfwayToMiddleKey,
                Acceleration,
                Deceleration));
        Math.Abs(actual.NormalizedY - oldGlobalProgressSample.NormalizedY)
            .Should().BeGreaterThan(1e-3,
                "WPF retimes trajectory key times and preserves their interpolation instead of easing global elapsed progress");
    }

    private static double Lerp(double from, double to, double progress) =>
        from + (to - from) * progress;

    private static double SampleKeySpline(
        double controlPoint1X,
        double controlPoint1Y,
        double controlPoint2X,
        double controlPoint2Y,
        double progress)
    {
        var low = 0.0;
        var high = 1.0;
        for (var iteration = 0; iteration < 40; iteration++)
        {
            var parameter = (low + high) / 2;
            if (CubicBezier(parameter, controlPoint1X, controlPoint2X) < progress)
                low = parameter;
            else
                high = parameter;
        }

        return CubicBezier((low + high) / 2, controlPoint1Y, controlPoint2Y);
    }

    private static double CubicBezier(double parameter, double control1, double control2)
    {
        var inverse = 1 - parameter;
        return 3 * inverse * inverse * parameter * control1
            + 3 * inverse * parameter * parameter * control2
            + parameter * parameter * parameter;
    }
}
