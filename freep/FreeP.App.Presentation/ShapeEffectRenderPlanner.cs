using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public static class ResolvedShapeEffectRenderPlanner
{
    public static ShapeEffectRenderPlan PlanOuterEffects(ResolvedShapeEffects? effects)
    {
        if (effects is null)
            return ShapeEffectRenderPlan.Empty;

        return ShapeEffectRenderPlanner.PlanOuterEffects(new ShapeEffectValues(
            effects.HasOuterShadow,
            effects.OuterShadowColor,
            effects.OuterShadowAlpha,
            effects.OuterShadowBlurDip,
            effects.OuterShadowDistDip,
            effects.OuterShadowDirDeg,
            effects.HasGlow,
            effects.GlowColor,
            effects.GlowAlpha,
            effects.GlowRadiusDip,
            effects.HasSoftEdge,
            effects.SoftEdgeRadiusDip));
    }
}
