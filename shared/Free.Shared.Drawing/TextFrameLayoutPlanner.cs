namespace Free.Shared.Drawing;

public readonly record struct TextFrameInsets(
    double Left,
    double Top,
    double Right,
    double Bottom)
{
    public static TextFrameInsets Uniform(double inset) => new(inset, inset, inset, inset);
}

public readonly record struct TextFrameLayout(
    LayoutRect Bounds,
    LayoutRect TextBounds,
    TextFrameInsets Insets);

/// <summary>
/// Framework-neutral geometry for text-bearing drawing frames.
/// </summary>
public static class TextFrameLayoutPlanner
{
    public const double DefaultMinimumWidth = 24.0;
    public const double DefaultMinimumHeight = 18.0;
    public const double DefaultTextInset = 4.0;

    public static TextFrameLayout Create(
        LayoutRect bounds,
        double textInset = DefaultTextInset) =>
        Create(bounds, TextFrameInsets.Uniform(NormalizeUniformInset(textInset)));

    public static TextFrameLayout Create(
        LayoutRect bounds,
        TextFrameInsets insets) =>
        new(bounds, CreateTextBounds(bounds, insets), insets);

    public static TextFrameLayout CreateNormalized(
        LayoutRect bounds,
        double minimumWidth = DefaultMinimumWidth,
        double minimumHeight = DefaultMinimumHeight,
        double textInset = DefaultTextInset) =>
        Create(NormalizeBounds(bounds, minimumWidth, minimumHeight), textInset);

    public static TextFrameLayout CreateScaled(
        LayoutRect bounds,
        double scale,
        double textInset = DefaultTextInset) =>
        Create(ScaleBounds(bounds, scale), textInset);

    public static LayoutRect NormalizeBounds(
        LayoutRect bounds,
        double minimumWidth = DefaultMinimumWidth,
        double minimumHeight = DefaultMinimumHeight) =>
        new(
            bounds.Left,
            bounds.Top,
            Math.Max(NormalizeMinimum(minimumWidth, DefaultMinimumWidth), bounds.Width),
            Math.Max(NormalizeMinimum(minimumHeight, DefaultMinimumHeight), bounds.Height));

    public static LayoutRect ScaleBounds(LayoutRect bounds, double scale) =>
        new(
            bounds.Left * scale,
            bounds.Top * scale,
            bounds.Width * scale,
            bounds.Height * scale);

    public static TextFrameInsets FromOptionalInsets(
        double? left,
        double? top,
        double? right,
        double? bottom,
        double defaultHorizontal,
        double defaultVertical) =>
        new(
            ResolveInset(left, defaultHorizontal),
            ResolveInset(top, defaultVertical),
            ResolveInset(right, defaultHorizontal),
            ResolveInset(bottom, defaultVertical));

    public static LayoutRect CreateTextBounds(
        LayoutRect bounds,
        double textInset = DefaultTextInset) =>
        CreateTextBounds(bounds, TextFrameInsets.Uniform(NormalizeUniformInset(textInset)));

    public static LayoutRect CreateTextBounds(
        LayoutRect bounds,
        TextFrameInsets insets) =>
        new(
            bounds.Left + insets.Left,
            bounds.Top + insets.Top,
            Math.Max(1, bounds.Width - insets.Left - insets.Right),
            Math.Max(1, bounds.Height - insets.Top - insets.Bottom));

    private static double NormalizeUniformInset(double value) =>
        double.IsFinite(value) && value >= 0 ? value : DefaultTextInset;

    private static double NormalizeMinimum(double value, double fallback) =>
        double.IsFinite(value) && value > 0 ? value : fallback;

    private static double ResolveInset(double? value, double fallback) =>
        value is { } resolved && double.IsFinite(resolved) ? resolved : fallback;
}
