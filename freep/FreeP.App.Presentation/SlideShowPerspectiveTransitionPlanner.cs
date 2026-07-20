using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum SlideShowPerspectiveTransitionKind
{
    Flip,
    Cube,
    Rotate,
    Switch,
    Orbit,
    Ferris
}

/// <summary>Shared geometry policy for the 2-D host projection of PowerPoint's exciting transitions.</summary>
public sealed record SlideShowPerspectiveTransitionPlan(
    SlideShowPerspectiveTransitionKind Kind,
    bool HorizontalAxis,
    double StartScale,
    double StartRotationDegrees,
    double TravelFactor)
{
    public bool IsAxisCollapsed => Kind is SlideShowPerspectiveTransitionKind.Flip
        or SlideShowPerspectiveTransitionKind.Cube;
}

public static class SlideShowPerspectiveTransitionPlanner
{
    public const double FlipStartScale = 0.02;
    public const double CubeStartScale = 0.08;
    public const double RotateStartScale = 0.82;
    public const double SwitchStartScale = 0.86;
    public const double OrbitStartScale = 0.64;
    public const double FerrisStartScale = 0.72;
    public const double CubeRotationDegrees = 90;
    public const double RotateRotationDegrees = 90;
    public const double SwitchRotationDegrees = 90;
    public const double OrbitRotationDegrees = 180;
    public const double FerrisRotationDegrees = 75;
    public const double CubeTravelFactor = 0.12;
    public const double RotateTravelFactor = 0.04;
    public const double SwitchTravelFactor = 0.18;
    public const double OrbitTravelFactor = 0.25;
    public const double FerrisTravelFactor = 0.18;

    public static SlideShowPerspectiveTransitionPlan Plan(SlideTransition transition)
    {
        ArgumentNullException.ThrowIfNull(transition);

        var horizontal = transition.Direction is not (
            TransitionDirection.Up or
            TransitionDirection.Down or
            TransitionDirection.Vertical);
        var sign = ResolveRotationSign(transition.Direction);

        return transition.Kind switch
        {
            TransitionKind.Flip => new(
                SlideShowPerspectiveTransitionKind.Flip,
                horizontal,
                FlipStartScale,
                0,
                0),
            TransitionKind.Cube => new(
                SlideShowPerspectiveTransitionKind.Cube,
                horizontal,
                CubeStartScale,
                sign * CubeRotationDegrees,
                CubeTravelFactor),
            TransitionKind.Rotate => new(
                SlideShowPerspectiveTransitionKind.Rotate,
                horizontal,
                RotateStartScale,
                sign * RotateRotationDegrees,
                RotateTravelFactor),
            TransitionKind.Switch => new(
                SlideShowPerspectiveTransitionKind.Switch,
                horizontal,
                SwitchStartScale,
                sign * SwitchRotationDegrees,
                SwitchTravelFactor),
            TransitionKind.Orbit => new(
                SlideShowPerspectiveTransitionKind.Orbit,
                horizontal,
                OrbitStartScale,
                sign * OrbitRotationDegrees,
                OrbitTravelFactor),
            TransitionKind.Ferris => new(
                SlideShowPerspectiveTransitionKind.Ferris,
                horizontal,
                FerrisStartScale,
                sign * FerrisRotationDegrees,
                FerrisTravelFactor),
            _ => throw new ArgumentException(
                $"Unsupported perspective transition kind: {transition.Kind}",
                nameof(transition))
        };
    }

    private static double ResolveRotationSign(TransitionDirection? direction) =>
        direction is TransitionDirection.Left or
            TransitionDirection.Up or
            TransitionDirection.LeftUp or
            TransitionDirection.LeftDown
            ? -1
            : 1;
}
