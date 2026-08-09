using System.Windows;
using System.Windows.Media.Animation;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

/// <summary>WPF adapter for the shared OOXML animation timing envelope.</summary>
internal sealed class PowerPointTimingEasingFunction : EasingFunctionBase
{
    public PowerPointTimingEasingFunction(int? acceleration, int? deceleration)
    {
        Acceleration = acceleration;
        Deceleration = deceleration;
        EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn;
    }

    public int? Acceleration { get; }
    public int? Deceleration { get; }

    protected override double EaseInCore(double normalizedTime) =>
        SlideShowPlaybackPlanner.ApplyHostTimingEasing(normalizedTime, Acceleration, Deceleration);

    protected override Freezable CreateInstanceCore() =>
        new PowerPointTimingEasingFunction(Acceleration, Deceleration);
}
