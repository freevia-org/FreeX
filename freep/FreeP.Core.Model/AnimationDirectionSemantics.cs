namespace FreeP.Core.Model;

/// <summary>
/// Renderer-neutral semantics for the four PowerPoint Split effect options.
/// The legacy axis-only values remain valid for older FreeP documents and are
/// interpreted as the historical center-out behavior.
/// </summary>
public static class AnimationDirectionSemantics
{
    public static AnimationDirection ResolveSplitDirection(ShapeAnimation animation)
    {
        ArgumentNullException.ThrowIfNull(animation);

        return animation.Direction switch
        {
            AnimationDirection.Horizontal => AnimationDirection.HorizontalOut,
            AnimationDirection.Vertical => AnimationDirection.VerticalOut,
            AnimationDirection.HorizontalIn
                or AnimationDirection.HorizontalOut
                or AnimationDirection.VerticalIn
                or AnimationDirection.VerticalOut => animation.Direction.Value,
            _ => AnimationDirection.HorizontalOut,
        };
    }

    public static bool IsSplitHorizontal(AnimationDirection direction) =>
        direction is AnimationDirection.HorizontalIn or AnimationDirection.HorizontalOut;

    public static bool IsSplitFromCenter(AnimationDirection direction) =>
        direction is AnimationDirection.HorizontalOut or AnimationDirection.VerticalOut;
}
