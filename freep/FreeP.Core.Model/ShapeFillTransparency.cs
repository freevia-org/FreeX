namespace FreeP.Core.Model;

/// <summary>
/// Applies PowerPoint fill transparency to the color-bearing fill types while preserving
/// theme references and the rest of the authored fill definition.
/// </summary>
public static class ShapeFillTransparency
{
    public static bool TryCreate(ShapeFill? fill, byte alpha, out ShapeFill? result)
    {
        result = null;
        switch (fill)
        {
            case ShapeFill.Solid solid:
                if (solid.Color.Alpha == alpha)
                    return false;

                result = new ShapeFill.Solid(WithAlpha(solid.Color, alpha));
                return true;

            case ShapeFill.Gradient gradient:
                if (gradient.Stops.Count == 0 || gradient.Stops.All(stop => stop.Color.Alpha == alpha))
                    return false;

                result = new ShapeFill.Gradient(
                    gradient.Stops
                        .Select(stop => new GradientStop(stop.Position, WithAlpha(stop.Color, alpha)))
                        .ToArray(),
                    gradient.Kind,
                    gradient.AngleDegrees);
                return true;

            case ShapeFill.Pattern pattern:
                if (pattern.ForegroundColor.Alpha == alpha && pattern.BackgroundColor.Alpha == alpha)
                    return false;

                result = new ShapeFill.Pattern(
                    pattern.Preset,
                    WithAlpha(pattern.ForegroundColor, alpha),
                    WithAlpha(pattern.BackgroundColor, alpha));
                return true;

            default:
                // Picture and None fills do not expose a color alpha in the current model.
                return false;
        }
    }

    private static ThemeAwareColor WithAlpha(ThemeAwareColor color, byte alpha) =>
        color.SchemeColor is { } scheme
            ? new ThemeAwareColor(color.Resolved, scheme, alpha)
            : new ThemeAwareColor(color.Resolved, alpha);
}
