using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum ShapeTransparencyTarget
{
    Fill,
    Outline,
}

public sealed record ShapeTransparencyOption(double Percent, string Label, string CommandId);

/// <summary>Shared shape transparency authoring and ribbon option definitions.</summary>
public static class ShapeTransparencyPlanner
{
    public const string FillCommandId = "freep.shape.fill-transparency";
    public const string OutlineCommandId = "freep.shape.outline-transparency";

    public static IReadOnlyList<ShapeTransparencyOption> Options { get; } =
    [
        Option(0),
        Option(25),
        Option(50),
        Option(75),
        Option(100),
    ];

    public static string OptionCommandId(ShapeTransparencyTarget target, double percent) =>
        $"{(target == ShapeTransparencyTarget.Fill ? FillCommandId : OutlineCommandId)}.{ToPercent(percent)}";

    public static ShapeFill? ApplyFill(ShapeFill? fill, double transparencyPercent)
    {
        var alpha = ToAlpha(transparencyPercent);
        return fill switch
        {
            ShapeFill.Solid solid => new ShapeFill.Solid(WithAlpha(solid.Color, alpha)),
            ShapeFill.Gradient gradient => new ShapeFill.Gradient(
                gradient.Stops.Select(stop => new GradientStop(stop.Position, WithAlpha(stop.Color, alpha))).ToArray(),
                gradient.Kind,
                gradient.AngleDegrees),
            ShapeFill.Pattern pattern => new ShapeFill.Pattern(
                pattern.Preset,
                WithAlpha(pattern.ForegroundColor, alpha),
                WithAlpha(pattern.BackgroundColor, alpha)),
            _ => fill,
        };
    }

    public static ShapeOutline? ApplyOutline(ShapeOutline? outline, double transparencyPercent)
    {
        var alpha = ToAlpha(transparencyPercent);
        return outline switch
        {
            ShapeOutline.Visible visible => new ShapeOutline.Visible(
                WithAlpha(visible.Color, alpha), visible.WidthPt, visible.Dash,
                visible.BeginLineEnd, visible.EndLineEnd),
            ShapeOutline.GradientVisible gradient => new ShapeOutline.GradientVisible(
                ApplyGradient(gradient.Gradient, alpha), gradient.WidthPt, gradient.Dash,
                gradient.BeginLineEnd, gradient.EndLineEnd),
            _ => outline,
        };
    }

    public static byte ToAlpha(double transparencyPercent)
    {
        if (!double.IsFinite(transparencyPercent) || transparencyPercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(transparencyPercent), "Transparency must be between 0 and 100 percent.");
        return (byte)Math.Clamp(
            Math.Round(255 * (1 - transparencyPercent / 100), MidpointRounding.AwayFromZero), 0, 255);
    }

    private static ShapeFill.Gradient ApplyGradient(ShapeFill.Gradient gradient, byte alpha) =>
        new(gradient.Stops.Select(stop => new GradientStop(stop.Position, WithAlpha(stop.Color, alpha))).ToArray(),
            gradient.Kind, gradient.AngleDegrees);

    private static ThemeAwareColor WithAlpha(ThemeAwareColor color, byte alpha) =>
        color.SchemeColor is { } scheme
            ? new ThemeAwareColor(color.Resolved, scheme, alpha)
            : new ThemeAwareColor(color.Resolved, alpha);

    private static ShapeTransparencyOption Option(double percent) =>
        new(percent, $"{ToPercent(percent)}%", OptionCommandId(ShapeTransparencyTarget.Fill, percent));

    private static string ToPercent(double percent) =>
        percent.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
}
