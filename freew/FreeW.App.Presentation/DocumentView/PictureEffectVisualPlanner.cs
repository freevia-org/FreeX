using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

public sealed record PictureShadowVisualPlan(
    double BlurPoints,
    double DistancePoints,
    double DirectionDegrees,
    double OffsetXPoints,
    double OffsetYPoints,
    double Opacity,
    string ColorHex);

public sealed record PictureReflectionVisualPlan(double Opacity, double DistanceDip);

public static class PictureEffectVisualPlanner
{
    public const double PresetGlowOpacity = 0.60;

    public static PictureReflectionVisualPlan? BuildReflectionPlan(InlineImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (image.ImportedEffects is { HasReflection: true } imported)
        {
            return new PictureReflectionVisualPlan(
                Math.Clamp(imported.ReflectionStartAlpha / 100000d, 0, 1),
                Math.Max(0, imported.ReflectionDist / 12700d) * 96d / 72d);
        }

        return image.ReflectionPreset switch
        {
            1 => new PictureReflectionVisualPlan(0.5, 0),
            2 => new PictureReflectionVisualPlan(0.5, 4 * 96d / 72d),
            3 => new PictureReflectionVisualPlan(0.5, 8 * 96d / 72d),
            4 => new PictureReflectionVisualPlan(1, 0),
            5 => new PictureReflectionVisualPlan(1, 4 * 96d / 72d),
            _ => null,
        };
    }

    public static PictureShadowVisualPlan BuildShadowPlan(InlineImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var preset = image.ShadowPreset switch
        {
            1 => (Blur: 4.0, Distance: 3.0, Opacity: 0.50),
            2 => (Blur: 6.0, Distance: 5.0, Opacity: 0.55),
            3 => (Blur: 8.0, Distance: 7.0, Opacity: 0.60),
            4 => (Blur: 4.0, Distance: 4.0, Opacity: 0.50),
            _ => (Blur: 10.0, Distance: 10.0, Opacity: 0.65),
        };

        if (image.ImportedEffects is not { HasShadow: true } imported)
        {
            return new PictureShadowVisualPlan(
                preset.Blur,
                preset.Distance,
                315,
                preset.Distance,
                preset.Distance,
                preset.Opacity,
                "000000");
        }

        var distance = Math.Max(0, imported.ShadowDist / 12700d);
        var direction = ((imported.ShadowDir / 60000d) % 360 + 360) % 360;
        var radians = direction * Math.PI / 180d;
        return new PictureShadowVisualPlan(
            Math.Max(0, imported.ShadowBlurRad / 12700d),
            distance,
            direction,
            Math.Cos(radians) * distance,
            -Math.Sin(radians) * distance,
            Math.Clamp(imported.ShadowAlpha / 100000d, 0, 1),
            ResolveShadowColorHex(image));
    }

    public static string ResolveShadowColorHex(InlineImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        return image.ImportedEffects is { HasShadow: true } imported &&
               !string.IsNullOrWhiteSpace(imported.ShadowColorHex)
            ? imported.ShadowColorHex
            : "000000";
    }

    public static double ResolveShadowOpacity(InlineImage image, double presetOpacity)
    {
        ArgumentNullException.ThrowIfNull(image);

        return image.ImportedEffects is { HasShadow: true } imported
            ? Math.Clamp(imported.ShadowAlpha / 100000d, 0, 1)
            : Math.Clamp(presetOpacity, 0, 1);
    }

    public static double ResolveGlowOpacity(InlineImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        return image.ImportedEffects is { HasGlow: true } imported
            ? Math.Clamp(imported.GlowAlpha / 100000d, 0, 1)
            : PresetGlowOpacity;
    }
}
