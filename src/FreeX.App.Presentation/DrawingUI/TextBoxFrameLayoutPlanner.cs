namespace FreeX.App.Presentation.DrawingUI;

public readonly record struct TextBoxFrameLayout(
    LayoutRect Bounds,
    LayoutRect TextBounds);

/// <summary>
/// Shared text-box frame geometry for renderers that need a border rectangle plus an inset text area.
/// </summary>
public static class TextBoxFrameLayoutPlanner
{
    public const double MinimumWidth = 24.0;
    public const double MinimumHeight = 18.0;
    public const double TextInset = 4.0;

    public static TextBoxFrameLayout Create(
        LayoutRect bounds,
        double textInset = TextInset) =>
        new(bounds, CreateTextBounds(bounds, textInset));

    public static TextBoxFrameLayout CreateNormalized(
        LayoutRect bounds,
        double minimumWidth = MinimumWidth,
        double minimumHeight = MinimumHeight,
        double textInset = TextInset) =>
        Create(NormalizeBounds(bounds, minimumWidth, minimumHeight), textInset);

    public static TextBoxFrameLayout CreateScaled(
        LayoutRect bounds,
        double scale,
        double textInset = TextInset) =>
        Create(ScaleBounds(bounds, scale), textInset);

    public static LayoutRect NormalizeBounds(
        LayoutRect bounds,
        double minimumWidth = MinimumWidth,
        double minimumHeight = MinimumHeight) =>
        new(
            bounds.Left,
            bounds.Top,
            Math.Max(NormalizeMinimum(minimumWidth, MinimumWidth), bounds.Width),
            Math.Max(NormalizeMinimum(minimumHeight, MinimumHeight), bounds.Height));

    public static LayoutRect ScaleBounds(LayoutRect bounds, double scale) =>
        new(
            bounds.Left * scale,
            bounds.Top * scale,
            bounds.Width * scale,
            bounds.Height * scale);

    public static LayoutRect CreateTextBounds(
        LayoutRect bounds,
        double textInset = TextInset)
    {
        var inset = double.IsFinite(textInset) && textInset >= 0 ? textInset : TextInset;
        return new LayoutRect(
            bounds.Left + inset,
            bounds.Top + inset,
            Math.Max(1, bounds.Width - (inset * 2)),
            Math.Max(1, bounds.Height - (inset * 2)));
    }

    private static double NormalizeMinimum(double value, double fallback) =>
        double.IsFinite(value) && value > 0 ? value : fallback;
}
