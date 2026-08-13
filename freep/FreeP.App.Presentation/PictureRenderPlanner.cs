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

public sealed record PictureMediaPlayGlyphPlan(
    LayoutPoint CenterDip,
    double RadiusDip,
    IReadOnlyList<LayoutPoint> TriangleDip);

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
    public double ReflectionBlurDip { get; init; }
    public double ReflectionScaleY { get; init; } = -1;
    public double ReflectionEndPos { get; init; } = 1;
    public double ReflectionPivotY { get; init; }
    public bool ReflectionNeedsTerminalTransparentStop { get; init; }
    public IReadOnlyList<PictureReflectionBlurPass> ReflectionBlurPasses { get; init; } =
        [new PictureReflectionBlurPass(0, 0, 1)];

    public bool AlphaAppliesToImageBody { get; init; } = true;

    public double FrameCornerRadiusDip { get; init; }

    public PictureMediaPlayGlyphPlan? MediaPlayGlyph { get; init; }
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
        double reflectionDistance = picture.Effects?.ReflectionDistDip ?? 0;
        double reflectionScale = picture.Effects?.ReflectionScaleY ?? -1;
        double reflectionEndPosition = picture.Effects?.ReflectionEndPos ?? 1;

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
            ReflectionDistDip = reflectionDistance,
            ReflectionBlurDip = picture.Effects?.ReflectionBlurDip ?? 0,
            ReflectionScaleY = Math.Abs(reflectionScale) < 0.001 ? -1 : reflectionScale,
            ReflectionEndPos = Math.Clamp(reflectionEndPosition, 0.001, 1.0),
            ReflectionPivotY = picture.DestDip.Y + picture.DestDip.Height + reflectionDistance / 2.0,
            ReflectionNeedsTerminalTransparentStop = reflectionEndPosition < 0.999,
            ReflectionBlurPasses = PictureReflectionRenderPlanner.PlanBlurPasses(
                picture.Effects?.ReflectionBlurDip ?? 0),
            FrameCornerRadiusDip = PlanFrameCornerRadius(picture.DestDip),
            MediaPlayGlyph = picture.IsMedia ? PlanMediaPlayGlyph(picture.DestDip) : null,
        };
    }

    private static double PlanFrameCornerRadius(LayoutRect destination) =>
        Math.Min(destination.Width, destination.Height) * 0.18;

    private static PictureMediaPlayGlyphPlan PlanMediaPlayGlyph(LayoutRect destination)
    {
        var center = new LayoutPoint(
            destination.X + destination.Width / 2,
            destination.Y + destination.Height / 2);
        var radius = Math.Max(4, Math.Min(destination.Width, destination.Height) / 6);
        var triangleX = center.X - radius * 0.3;
        return new PictureMediaPlayGlyphPlan(
            center,
            radius,
            [
                new LayoutPoint(triangleX, center.Y - radius * 0.45),
                new LayoutPoint(triangleX + radius * 0.8, center.Y),
                new LayoutPoint(triangleX, center.Y + radius * 0.45),
            ]);
    }

    private static PictureSourceRectPixels PlanSourceRect(
        DrawOp.Picture picture,
        int pixelWidth,
        int pixelHeight)
    {
        if (picture.HasCrop)
            return PlanSourceRect(
                pixelWidth,
                pixelHeight,
                picture.CropLeft,
                picture.CropTop,
                picture.CropRight,
                picture.CropBottom);

        if (!picture.IsCover)
            return new PictureSourceRectPixels(0, 0, pixelWidth, pixelHeight);

        var sourceAspect = pixelWidth / (double)pixelHeight;
        var destinationAspect = picture.DestDip.Width / picture.DestDip.Height;
        if (!double.IsFinite(destinationAspect) || destinationAspect <= 0)
            return new PictureSourceRectPixels(0, 0, pixelWidth, pixelHeight);

        var cropLeft = 0d;
        var cropTop = 0d;
        var cropRight = 0d;
        var cropBottom = 0d;
        if (sourceAspect > destinationAspect)
        {
            var horizontalCrop = Math.Clamp(1 - destinationAspect / sourceAspect, 0, 1);
            cropLeft = cropRight = horizontalCrop / 2;
        }
        else if (sourceAspect < destinationAspect)
        {
            var verticalCrop = Math.Clamp(1 - sourceAspect / destinationAspect, 0, 1);
            cropTop = cropBottom = verticalCrop / 2;
        }

        return PlanSourceRect(
            pixelWidth,
            pixelHeight,
            cropLeft,
            cropTop,
            cropRight,
            cropBottom);
    }

    private static PictureSourceRectPixels PlanSourceRect(
        int pixelWidth,
        int pixelHeight,
        double cropLeft,
        double cropTop,
        double cropRight,
        double cropBottom)
    {
        int x = (int)Math.Round(cropLeft * pixelWidth);
        int y = (int)Math.Round(cropTop * pixelHeight);
        int width = (int)Math.Round((1.0 - cropLeft - cropRight) * pixelWidth);
        int height = (int)Math.Round((1.0 - cropTop - cropBottom) * pixelHeight);

        x = Math.Max(0, Math.Min(x, pixelWidth - 1));
        y = Math.Max(0, Math.Min(y, pixelHeight - 1));
        width = Math.Max(1, Math.Min(width, pixelWidth - x));
        height = Math.Max(1, Math.Min(height, pixelHeight - y));

        return new PictureSourceRectPixels(x, y, width, height);
    }
}
