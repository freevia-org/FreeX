using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum PictureRenderPhase
{
    OuterEffects,
    PixelColorEffects,
    AlphaOpacity,
    ImageBody
}

public readonly record struct PictureSourceRectPixels(int X, int Y, int Width, int Height);

public sealed record PictureRenderPlan(
    LayoutRect DestinationDip,
    PictureSourceRectPixels SourceRectPixels,
    PictureColorEffectPlan ColorEffects,
    double AlphaOpacity,
    ShapeEffectRenderPlan OuterEffects,
    IReadOnlyList<PictureRenderPhase> PhaseOrder)
{
    public bool HasCrop { get; init; }

    public bool HasPixelEffects => ColorEffects.HasPixelEffects;

    public bool HasAlphaOpacity => AlphaOpacity < 1.0;

    public bool HasOuterEffects =>
        OuterEffects.ShadowPasses.Count > 0 ||
        OuterEffects.GlowPasses.Count > 0 ||
        HasReflection;

    public bool HasReflection { get; init; }
    public byte ReflectionAlpha { get; init; }
    public double ReflectionDistDip { get; init; }
    public double ReflectionScaleY { get; init; } = -1;
    public double ReflectionEndPos { get; init; } = 1;

    public bool AlphaAppliesToImageBody { get; init; } = true;
}

public static class PictureRenderPlanner
{
    private static readonly PictureRenderPhase[] DefaultPhaseOrder =
    [
        PictureRenderPhase.OuterEffects,
        PictureRenderPhase.PixelColorEffects,
        PictureRenderPhase.AlphaOpacity,
        PictureRenderPhase.ImageBody
    ];

    public static PictureRenderPlan Plan(DrawOp.Picture picture, int pixelWidth, int pixelHeight)
    {
        int sourceWidth = Math.Max(1, pixelWidth);
        int sourceHeight = Math.Max(1, pixelHeight);
        var sourceRect = PlanSourceRect(picture, sourceWidth, sourceHeight);

        return new PictureRenderPlan(
            picture.DestDip,
            sourceRect,
            PictureColorEffectPlanner.Plan(picture),
            Math.Clamp(picture.AlphaModPct ?? 1.0, 0.0, 1.0),
            ResolvedShapeEffectRenderPlanner.PlanOuterEffects(picture.Effects),
            DefaultPhaseOrder)
        {
            HasCrop = sourceRect.X != 0 ||
                sourceRect.Y != 0 ||
                sourceRect.Width != sourceWidth ||
                sourceRect.Height != sourceHeight,
            HasReflection = picture.Effects?.HasReflection == true,
            ReflectionAlpha = picture.Effects?.ReflectionAlpha ?? 0,
            ReflectionDistDip = picture.Effects?.ReflectionDistDip ?? 0,
            ReflectionScaleY = picture.Effects?.ReflectionScaleY ?? -1,
            ReflectionEndPos = picture.Effects?.ReflectionEndPos ?? 1
        };
    }

    private static PictureSourceRectPixels PlanSourceRect(
        DrawOp.Picture picture,
        int pixelWidth,
        int pixelHeight)
    {
        if (!picture.HasCrop)
            return new PictureSourceRectPixels(0, 0, pixelWidth, pixelHeight);

        int x = (int)Math.Round(picture.CropLeft * pixelWidth);
        int y = (int)Math.Round(picture.CropTop * pixelHeight);
        int width = (int)Math.Round((1.0 - picture.CropLeft - picture.CropRight) * pixelWidth);
        int height = (int)Math.Round((1.0 - picture.CropTop - picture.CropBottom) * pixelHeight);

        x = Math.Max(0, Math.Min(x, pixelWidth - 1));
        y = Math.Max(0, Math.Min(y, pixelHeight - 1));
        width = Math.Max(1, Math.Min(width, pixelWidth - x));
        height = Math.Max(1, Math.Min(height, pixelHeight - y));

        return new PictureSourceRectPixels(x, y, width, height);
    }
}
