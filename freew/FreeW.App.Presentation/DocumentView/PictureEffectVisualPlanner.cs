using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

public static class PictureEffectVisualPlanner
{
    public const double PresetGlowOpacity = 0.60;

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
