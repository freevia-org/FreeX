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

    public sealed record Fill(ResolvedFill FillBrush) : TextRunEffectPass;

    public sealed record Outline(ResolvedOutline OutlinePen) : TextRunEffectPass;
}

public sealed record TextRunEffectRenderPlan(
    LayoutRect GlyphBoundsDip,
    double WarpYOffsetDip,
    IReadOnlyList<TextRunEffectPass> Passes)
{
    public bool HasWarp => Math.Abs(WarpYOffsetDip) > 0.001;
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
        double warpYOffset = ComputeWarpYOffset(textLayout, progress, shapeBoundsDip) ?? 0.0;
        var glyphBounds = runBoundsDip with { Y = runBoundsDip.Y + warpYOffset };

        var passes = new List<TextRunEffectPass>();
        if (run.TextShadow is { } shadow)
            AddShadowPasses(passes, shadow);

        passes.Add(new TextRunEffectPass.Fill(
            run.TextFill ?? new ResolvedFill.Solid(run.Color)));

        if (run.TextOutline is not null)
            passes.Add(new TextRunEffectPass.Outline(run.TextOutline));

        return new TextRunEffectRenderPlan(glyphBounds, warpYOffset, passes);
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

        return baseOffset.Value * GetWarpAdjustAmplitudeScale(textLayout.WarpAdjusts);
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

    private static double GetWarpAdjustAmplitudeScale(
        IReadOnlyList<(string Name, string Formula)> warpAdjusts)
    {
        foreach (var adjust in warpAdjusts)
        {
            if (!adjust.Name.StartsWith("adj", StringComparison.OrdinalIgnoreCase))
                continue;

            if (TryReadGuideValue(adjust.Formula, out double guideValue))
                return Math.Clamp(guideValue / 50000.0, 0.1, 2.0);
        }

        return 1.0;
    }

    private static bool TryReadGuideValue(string formula, out double value)
    {
        value = 0;
        var parts = formula.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !parts[0].Equals("val", StringComparison.OrdinalIgnoreCase))
            return false;

        return double.TryParse(
            parts[1],
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out value);
    }
}
