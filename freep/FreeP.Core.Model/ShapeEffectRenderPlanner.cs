using Free.Shared.Drawing;

namespace FreeP.Core.Model;

public readonly record struct ShapeShadowPass(
    double OffsetX,
    double OffsetY,
    SrgbColor Color,
    byte Alpha);

public readonly record struct ShapeGlowPass(
    double StrokeWidthDip,
    SrgbColor Color,
    byte Alpha);

public sealed record ShapeEffectRenderPlan(
    IReadOnlyList<ShapeShadowPass> ShadowPasses,
    IReadOnlyList<ShapeGlowPass> GlowPasses)
{
    public static ShapeEffectRenderPlan Empty { get; } = new(
        Array.Empty<ShapeShadowPass>(),
        Array.Empty<ShapeGlowPass>());
}

public readonly record struct ShapeEffectValues(
    bool HasOuterShadow,
    SrgbColor OuterShadowColor,
    byte OuterShadowAlpha,
    double OuterShadowBlurDip,
    double OuterShadowDistDip,
    double OuterShadowDirDeg,
    bool HasGlow,
    SrgbColor GlowColor,
    byte GlowAlpha,
    double GlowRadiusDip);

public static class ShapeEffectRenderPlanner
{
    public static ShapeEffectRenderPlan PlanOuterEffects(ShapeEffects? effects)
    {
        if (effects is null)
            return ShapeEffectRenderPlan.Empty;

        return PlanOuterEffects(new ShapeEffectValues(
            effects.HasOuterShadow,
            effects.OuterShadowColor,
            effects.OuterShadowAlpha,
            DrawingMlCoordinateUnits.EmuToPixels(effects.OuterShadowBlurRadEmu),
            DrawingMlCoordinateUnits.EmuToPixels(effects.OuterShadowDistEmu),
            effects.OuterShadowDirDeg,
            effects.HasGlow,
            effects.GlowColor,
            effects.GlowAlpha,
            DrawingMlCoordinateUnits.EmuToPixels(effects.GlowRadiusEmu)));
    }

    public static ShapeEffectRenderPlan PlanOuterEffects(ShapeEffectValues effects)
    {
        var shadows = PlanShadowPasses(effects);
        var glows = PlanGlowPasses(effects);
        return shadows.Count == 0 && glows.Count == 0
            ? ShapeEffectRenderPlan.Empty
            : new ShapeEffectRenderPlan(shadows, glows);
    }

    private static IReadOnlyList<ShapeShadowPass> PlanShadowPasses(ShapeEffectValues effects)
    {
        if (!effects.HasOuterShadow)
            return Array.Empty<ShapeShadowPass>();

        double rad = effects.OuterShadowDirDeg * Math.PI / 180.0;
        double dx = Math.Cos(rad) * effects.OuterShadowDistDip;
        double dy = Math.Sin(rad) * effects.OuterShadowDistDip;
        byte alpha = effects.OuterShadowAlpha;
        var passes = new List<ShapeShadowPass>();

        if (effects.OuterShadowBlurDip > 1.0)
        {
            int blurPasses = Math.Min(4, (int)Math.Ceiling(effects.OuterShadowBlurDip / 2));
            for (int i = blurPasses; i >= 1; i--)
            {
                double spread = effects.OuterShadowBlurDip * i / blurPasses;
                byte passAlpha = (byte)(alpha / (blurPasses + 1));
                for (int ox = -1; ox <= 1; ox++)
                for (int oy = -1; oy <= 1; oy++)
                {
                    if (ox == 0 && oy == 0)
                        continue;

                    passes.Add(new ShapeShadowPass(
                        dx + ox * spread,
                        dy + oy * spread,
                        effects.OuterShadowColor,
                        passAlpha));
                }
            }
        }

        passes.Add(new ShapeShadowPass(dx, dy, effects.OuterShadowColor, alpha));
        return passes;
    }

    private static IReadOnlyList<ShapeGlowPass> PlanGlowPasses(ShapeEffectValues effects)
    {
        if (!effects.HasGlow)
            return Array.Empty<ShapeGlowPass>();

        int glowPasses = Math.Min(5, (int)Math.Ceiling(effects.GlowRadiusDip / 2));
        if (glowPasses <= 0)
            return Array.Empty<ShapeGlowPass>();

        byte passAlpha = (byte)(effects.GlowAlpha / (glowPasses + 1));
        var passes = new List<ShapeGlowPass>(glowPasses);
        for (int i = glowPasses; i >= 1; i--)
        {
            double radius = effects.GlowRadiusDip * i / glowPasses;
            passes.Add(new ShapeGlowPass(
                radius * 2,
                effects.GlowColor,
                passAlpha));
        }

        return passes;
    }
}
