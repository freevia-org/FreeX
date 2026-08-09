using System.Windows;
using System.Windows.Media.Animation;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

/// <summary>
/// WPF adapter for the authored PowerPoint acceleration/deceleration envelope.
/// </summary>
internal sealed class PowerPointAnimationEasing : EasingFunctionBase
{
    public static readonly DependencyProperty AccelerationProperty =
        DependencyProperty.Register(
            nameof(Acceleration),
            typeof(int?),
            typeof(PowerPointAnimationEasing),
            new PropertyMetadata(null));

    public static readonly DependencyProperty DecelerationProperty =
        DependencyProperty.Register(
            nameof(Deceleration),
            typeof(int?),
            typeof(PowerPointAnimationEasing),
            new PropertyMetadata(null));

    private PowerPointAnimationEasing()
    {
    }

    public PowerPointAnimationEasing(int? acceleration, int? deceleration)
    {
        Acceleration = acceleration;
        Deceleration = deceleration;
    }

    public int? Acceleration
    {
        get => (int?)GetValue(AccelerationProperty);
        private set => SetValue(AccelerationProperty, value);
    }

    public int? Deceleration
    {
        get => (int?)GetValue(DecelerationProperty);
        private set => SetValue(DecelerationProperty, value);
    }

    protected override double EaseInCore(double normalizedTime) =>
        SlideShowPlaybackPlanner.ApplyHostTimingEasing(normalizedTime, Acceleration, Deceleration);

    protected override Freezable CreateInstanceCore() => new PowerPointAnimationEasing();
}
