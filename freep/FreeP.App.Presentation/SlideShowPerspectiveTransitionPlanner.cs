using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum SlideShowPerspectiveTransitionKind
{
    Flip,
    Cube,
    Rotate
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
    public const double CubeRotationDegrees = 90;
    public const double RotateRotationDegrees = 90;
    public const double CubeTravelFactor = 0.12;
    public const double RotateTravelFactor = 0.04;

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
