using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

internal static class AvaloniaSlideShowAnimationTiming
{
    public static double Apply(double progress, int? acceleration, int? deceleration) =>
        SlideShowPlaybackPlanner.ApplyHostTimingEasing(progress, acceleration, deceleration);

    public static double RetimedKeyframeProgress(
        double progress,
        int? acceleration,
        int? deceleration) =>
        SlideShowPlaybackPlanner.InvertHostTimingEasing(progress, acceleration, deceleration);
}
