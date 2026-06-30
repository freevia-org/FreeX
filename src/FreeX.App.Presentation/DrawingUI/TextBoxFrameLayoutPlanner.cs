using Free.Shared.Drawing;

namespace FreeX.App.Presentation.DrawingUI;

public readonly record struct TextBoxFrameLayout(
    LayoutRect Bounds,
    LayoutRect TextBounds);

/// <summary>
/// Shared text-box frame geometry for renderers that need a border rectangle plus an inset text area.
/// </summary>
public static class TextBoxFrameLayoutPlanner
{
    public const double MinimumWidth = TextFrameLayoutPlanner.DefaultMinimumWidth;
    public const double MinimumHeight = TextFrameLayoutPlanner.DefaultMinimumHeight;
    public const double TextInset = TextFrameLayoutPlanner.DefaultTextInset;

    public static TextBoxFrameLayout Create(
        LayoutRect bounds,
        double textInset = TextInset) =>
        ToTextBoxLayout(TextFrameLayoutPlanner.Create(bounds, textInset));

    public static TextBoxFrameLayout CreateNormalized(
        LayoutRect bounds,
        double minimumWidth = MinimumWidth,
        double minimumHeight = MinimumHeight,
        double textInset = TextInset) =>
        ToTextBoxLayout(TextFrameLayoutPlanner.CreateNormalized(
            bounds,
            minimumWidth,
            minimumHeight,
            textInset));

    public static TextBoxFrameLayout CreateScaled(
        LayoutRect bounds,
        double scale,
        double textInset = TextInset) =>
        ToTextBoxLayout(TextFrameLayoutPlanner.CreateScaled(bounds, scale, textInset));

    public static LayoutRect NormalizeBounds(
        LayoutRect bounds,
        double minimumWidth = MinimumWidth,
        double minimumHeight = MinimumHeight) =>
        TextFrameLayoutPlanner.NormalizeBounds(bounds, minimumWidth, minimumHeight);

    public static LayoutRect ScaleBounds(LayoutRect bounds, double scale) =>
        TextFrameLayoutPlanner.ScaleBounds(bounds, scale);

    public static LayoutRect CreateTextBounds(
        LayoutRect bounds,
        double textInset = TextInset) =>
        TextFrameLayoutPlanner.CreateTextBounds(bounds, textInset);

    private static TextBoxFrameLayout ToTextBoxLayout(TextFrameLayout layout) =>
        new(layout.Bounds, layout.TextBounds);
}
