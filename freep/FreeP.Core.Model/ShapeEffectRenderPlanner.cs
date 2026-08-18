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

public readonly record struct ShapeSoftEdgePass(
    double StrokeWidthDip,
    byte Alpha);

/// <summary>One renderer-neutral pass used to approximate a shape reflection's blur, mirroring
/// <c>PictureReflectionBlurPass</c> so both draw ops share the same visual ring-blur shape.</summary>
public readonly record struct ShapeReflectionPass(
    double OffsetXDip,
    double OffsetYDip,
    double Opacity);

public sealed record ShapeEffectRenderPlan(
    IReadOnlyList<ShapeShadowPass> ShadowPasses,
    IReadOnlyList<ShapeGlowPass> GlowPasses,
    IReadOnlyList<ShapeSoftEdgePass> SoftEdgePasses,
    IReadOnlyList<ShapeReflectionPass> ReflectionPasses,
    bool HasReflection = false,
    byte ReflectionAlpha = 0,
    double ReflectionScaleY = -1,
    double ReflectionEndPos = 1,
    double ReflectionPivotYDip = 0,
    bool ReflectionNeedsTerminalTransparentStop = false)
{
    public static ShapeEffectRenderPlan Empty { get; } = new(
        Array.Empty<ShapeShadowPass>(),
        Array.Empty<ShapeGlowPass>(),
        Array.Empty<ShapeSoftEdgePass>(),
        Array.Empty<ShapeReflectionPass>());
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
    double GlowRadiusDip,
    bool HasSoftEdge,
    double SoftEdgeRadiusDip,
    bool HasReflection = false,
    byte ReflectionAlpha = 0,
    double ReflectionBlurDip = 0,
    double ReflectionScaleY = -1,
    double ReflectionEndPos = 1);

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
            DrawingMlCoordinateUnits.EmuToPixels(effects.GlowRadiusEmu),
            effects.HasSoftEdge,
            DrawingMlCoordinateUnits.EmuToPixels(effects.SoftEdgeRadEmu)));
    }

    public static ShapeEffectRenderPlan PlanOuterEffects(ShapeEffectValues effects)
    {
        var shadows = PlanShadowPasses(effects);
        var glows = PlanGlowPasses(effects);
        var softEdges = PlanSoftEdgePasses(effects);
        var reflections = PlanReflectionPasses(effects);
        if (shadows.Count == 0 && glows.Count == 0 && softEdges.Count == 0 && !effects.HasReflection)
            return ShapeEffectRenderPlan.Empty;

        return new ShapeEffectRenderPlan(
            shadows,
            glows,
            softEdges,
            reflections,
            HasReflection: effects.HasReflection,
            ReflectionAlpha: effects.ReflectionAlpha,
            ReflectionScaleY: Math.Abs(effects.ReflectionScaleY) < 0.001 ? -1 : effects.ReflectionScaleY,
            ReflectionEndPos: Math.Clamp(effects.ReflectionEndPos, 0.001, 1.0),
            ReflectionNeedsTerminalTransparentStop: effects.ReflectionEndPos < 0.999);
    }

    /// <summary>Mirrors <c>PictureReflectionRenderPlanner.PlanBlurPasses</c> so shape and picture
    /// reflections share the same renderer-neutral ring-blur approximation.</summary>
    private static IReadOnlyList<ShapeReflectionPass> PlanReflectionPasses(ShapeEffectValues effects)
    {
        if (!effects.HasReflection)
            return Array.Empty<ShapeReflectionPass>();

        double blurDip = effects.ReflectionBlurDip;
        if (!double.IsFinite(blurDip) || blurDip <= 0.5)
            return [new ShapeReflectionPass(0, 0, 1)];

        int rings = Math.Min(3, (int)Math.Ceiling(blurDip / 2));
        double ringOpacity = 0.6 / (rings * 8);
        var passes = new List<ShapeReflectionPass>(rings * 8 + 1);
        for (int ring = rings; ring >= 1; ring--)
        {
            double radius = blurDip * ring / rings;
            double diagonal = radius * 0.7071067811865476;
            foreach (var (x, y) in new[]
            {
                (radius, 0d), (diagonal, diagonal), (0d, radius),
                (-diagonal, diagonal), (-radius, 0d), (-diagonal, -diagonal),
                (0d, -radius), (diagonal, -diagonal),
            })
                passes.Add(new ShapeReflectionPass(x, y, ringOpacity));
        }

        passes.Add(new ShapeReflectionPass(0, 0, 0.4));
        return passes;
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

        int glowPasses = Math.Min(8, (int)Math.Ceiling(effects.GlowRadiusDip / 2));
        if (glowPasses <= 0)
            return Array.Empty<ShapeGlowPass>();

        // Each ring is composited over the previous one. Choose the per-ring
        // alpha so the fully overlapped center reaches the authored opacity.
        double targetAlpha = effects.GlowAlpha / 255.0;
        double perPassAlpha = 1.0 - Math.Pow(1.0 - targetAlpha, 1.0 / glowPasses);
        byte passAlpha = (byte)Math.Clamp(Math.Round(perPassAlpha * 255.0), 1, 255);
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

    private static IReadOnlyList<ShapeSoftEdgePass> PlanSoftEdgePasses(ShapeEffectValues effects)
    {
        if (!effects.HasSoftEdge || effects.SoftEdgeRadiusDip <= 0)
            return Array.Empty<ShapeSoftEdgePass>();

        int passCount = Math.Min(6, (int)Math.Ceiling(effects.SoftEdgeRadiusDip / 2));
        var passes = new List<ShapeSoftEdgePass>(passCount);
        for (int index = passCount; index >= 1; index--)
        {
            double ratio = index / (double)passCount;
            double width = effects.SoftEdgeRadiusDip * 2 * ratio;
            byte alpha = (byte)Math.Round(16 + 16 * (1 - ratio));
            passes.Add(new ShapeSoftEdgePass(width, alpha));
        }

        return passes;
    }
}
