namespace FreeP.Core.Model;

/// <summary>
/// Applies PowerPoint outline transparency to color-bearing strokes while preserving
/// the authored stroke geometry and theme references.
/// </summary>
public static class ShapeOutlineTransparency
{
    public static bool TryCreate(ShapeOutline? outline, byte alpha, out ShapeOutline? result)
    {
        result = null;
        switch (outline)
        {
            case ShapeOutline.Visible visible:
                if (visible.Color.Alpha == alpha)
                    return false;

                result = new ShapeOutline.Visible(
                    WithAlpha(visible.Color, alpha),
                    visible.WidthPt,
                    visible.Dash,
                    visible.BeginLineEnd,
                    visible.EndLineEnd);
                return true;

            case ShapeOutline.GradientVisible gradient:
                if (gradient.Gradient.Stops.Count == 0 ||
                    gradient.Gradient.Stops.All(stop => stop.Color.Alpha == alpha))
                {
                    return false;
                }

                result = new ShapeOutline.GradientVisible(
                    new ShapeFill.Gradient(
                        gradient.Gradient.Stops
                            .Select(stop => new GradientStop(stop.Position, WithAlpha(stop.Color, alpha)))
                            .ToArray(),
                        gradient.Gradient.Kind,
                        gradient.Gradient.AngleDegrees),
                    gradient.WidthPt,
                    gradient.Dash,
                    gradient.BeginLineEnd,
                    gradient.EndLineEnd);
                return true;

            default:
                // None outlines do not expose a color alpha in the current model.
                return false;
        }
    }

    private static ThemeAwareColor WithAlpha(ThemeAwareColor color, byte alpha) =>
        color.SchemeColor is { } scheme
            ? new ThemeAwareColor(color.Resolved, scheme, alpha)
            : new ThemeAwareColor(color.Resolved, alpha);
}
