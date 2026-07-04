using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public abstract record TextRunEffectPass
{
    private TextRunEffectPass() { }

    public sealed record Shadow(
        double OffsetX,
        double OffsetY,
        SrgbColor Color,
        byte Alpha,
        double BlurDip,
        double SpreadDip,
        bool IsBlurPass) : TextRunEffectPass;

    public sealed record Reflection(
        double OffsetX,
        double OffsetY,
        double ScaleY,
        byte Alpha,
        double BlurDip,
        ResolvedFill FillBrush) : TextRunEffectPass;

    public sealed record Glow(
        double StrokeWidthDip,
        SrgbColor Color,
        byte Alpha,
        double RadiusDip) : TextRunEffectPass;

    public sealed record SoftEdge(
        ResolvedFill FillBrush,
        double OffsetX,
        double OffsetY,
        byte Alpha,
        double RadiusDip,
        bool IsBlurPass) : TextRunEffectPass;

    public sealed record Fill(ResolvedFill FillBrush) : TextRunEffectPass;

    public sealed record Outline(ResolvedOutline OutlinePen) : TextRunEffectPass;
}

public sealed record TextRunEffectRenderPlan(
    LayoutRect GlyphBoundsDip,
    double WarpYOffsetDip,
    WordArtWarpTransform? WarpTransform,
    IReadOnlyList<TextRunEffectPass> Passes)
{
    public bool HasWarp =>
        WarpTransform is { } warp &&
        (warp.HasOffset || warp.HasAffineTransform);
}

public static class TextRunEffectRenderPlanner
{
    public static TextRunEffectRenderPlan Plan(
        ResolvedRun run,
        LayoutRect runBoundsDip,
        double horizontalProgress,
        LayoutRect shapeBoundsDip,
        ResolvedTextLayout textLayout)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(textLayout);

        double progress = Math.Clamp(horizontalProgress, 0.0, 1.0);
        var warpTransform = WordArtWarpPlanner.Plan(
            textLayout.WarpPreset,
            runBoundsDip,
            shapeBoundsDip,
            textLayout.WarpAdjusts);
        double warpYOffset = warpTransform?.OffsetYDip
            ?? ComputeWarpYOffset(textLayout, progress, shapeBoundsDip)
            ?? 0.0;
        var glyphBounds = runBoundsDip with { Y = runBoundsDip.Y + warpYOffset };

        var passes = new List<TextRunEffectPass>();
        if (run.TextShadow is { } shadow)
            AddShadowPasses(passes, shadow);

        var fillBrush = run.TextFill ?? new ResolvedFill.Solid(run.Color);
        if (run.TextGlow is { } glow)
            AddGlowPasses(passes, glow);

        if (run.TextReflection is { } reflection)
            AddReflectionPass(passes, reflection, fillBrush);

        if (run.TextSoftEdge is { } softEdge)
            AddSoftEdgePasses(passes, softEdge, fillBrush);

        passes.Add(new TextRunEffectPass.Fill(fillBrush));

        if (run.TextOutline is not null)
            passes.Add(new TextRunEffectPass.Outline(run.TextOutline));

        return new TextRunEffectRenderPlan(glyphBounds, warpYOffset, warpTransform, passes);
    }

    public static double? ComputeWarpYOffset(
        ResolvedTextLayout textLayout,
        double horizontalProgress,
        LayoutRect shapeBoundsDip)
    {
        ArgumentNullException.ThrowIfNull(textLayout);

        double? baseOffset = WordArtWarpPlanner.ComputeYOffset(
            textLayout.WarpPreset,
            Math.Clamp(horizontalProgress, 0.0, 1.0),
            shapeBoundsDip);
        if (!baseOffset.HasValue)
            return null;

        return baseOffset.Value * WordArtWarpPlanner.GetAdjustAmplitudeScale(textLayout.WarpAdjusts);
    }

    private static void AddShadowPasses(List<TextRunEffectPass> passes, ResolvedRunShadow shadow)
    {
        double rad = shadow.DirDeg * Math.PI / 180.0;
        double dx = Math.Cos(rad) * shadow.DistDip;
        double dy = Math.Sin(rad) * shadow.DistDip;

        if (shadow.BlurDip > 0.5)
        {
            int blurPasses = Math.Min(3, (int)Math.Ceiling(shadow.BlurDip / 1.5));
            for (int pi = 1; pi <= blurPasses; pi++)
            {
                double spread = shadow.BlurDip * pi / blurPasses;
                byte passAlpha = (byte)(shadow.Alpha / (blurPasses + 1));
                for (int ox = -1; ox <= 1; ox++)
                for (int oy = -1; oy <= 1; oy++)
                {
                    if (ox == 0 && oy == 0)
                        continue;

                    passes.Add(new TextRunEffectPass.Shadow(
                        dx + ox * spread,
                        dy + oy * spread,
                        shadow.Color,
                        passAlpha,
                        shadow.BlurDip,
                        spread,
                        IsBlurPass: true));
                }
            }
        }

        passes.Add(new TextRunEffectPass.Shadow(
            dx,
            dy,
            shadow.Color,
            shadow.Alpha,
            shadow.BlurDip,
            SpreadDip: 0,
            IsBlurPass: false));
    }

    private static void AddReflectionPass(
        List<TextRunEffectPass> passes,
        ResolvedRunReflection reflection,
        ResolvedFill fillBrush)
    {
        double rad = reflection.DirDeg * Math.PI / 180.0;
        double dx = Math.Cos(rad) * reflection.DistDip;
        double dy = Math.Sin(rad) * reflection.DistDip;
        double scaleY = Math.Abs(reflection.ScaleY) < 0.001 ? -1.0 : reflection.ScaleY;

        passes.Add(new TextRunEffectPass.Reflection(
            dx,
            dy,
            scaleY,
            reflection.Alpha,
            reflection.BlurDip,
            fillBrush));
    }

    private static void AddGlowPasses(List<TextRunEffectPass> passes, ResolvedRunGlow glow)
    {
        int glowPasses = Math.Min(5, (int)Math.Ceiling(glow.RadiusDip / 2));
        if (glowPasses <= 0)
            return;

        byte passAlpha = (byte)(glow.Alpha / (glowPasses + 1));
        for (int i = glowPasses; i >= 1; i--)
        {
            double radius = glow.RadiusDip * i / glowPasses;
            passes.Add(new TextRunEffectPass.Glow(
                radius * 2,
                glow.Color,
                passAlpha,
                radius));
        }
    }

    private static void AddSoftEdgePasses(
        List<TextRunEffectPass> passes,
        ResolvedRunSoftEdge softEdge,
        ResolvedFill fillBrush)
    {
        int blurPasses = Math.Min(3, (int)Math.Ceiling(softEdge.RadiusDip / 2));
        if (blurPasses <= 0)
            return;

        byte passAlpha = (byte)Math.Max(8, 64 / (blurPasses + 1));
        for (int pi = blurPasses; pi >= 1; pi--)
        {
            double spread = softEdge.RadiusDip * pi / blurPasses;
            for (int ox = -1; ox <= 1; ox++)
            for (int oy = -1; oy <= 1; oy++)
            {
                if (ox == 0 && oy == 0)
                    continue;

                passes.Add(new TextRunEffectPass.SoftEdge(
                    fillBrush,
                    ox * spread,
                    oy * spread,
                    passAlpha,
                    spread,
                    IsBlurPass: true));
            }
        }
    }

}
