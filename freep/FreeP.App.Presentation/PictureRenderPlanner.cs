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
    private readonly LayoutRect? _imageDestinationDip;

    /// <summary>
    /// The rectangle the image body itself is painted into. This is <see cref="DestinationDip"/>
    /// except when the picture carries negative <c>a:srcRect</c> insets, which pad (letterbox) the
    /// image inside its frame instead of cropping it. Frame-level decoration -- shadow, outline,
    /// frame clip, media glyph -- stays on <see cref="DestinationDip"/>.
    /// </summary>
    public LayoutRect ImageDestinationDip
    {
        get => _imageDestinationDip ?? DestinationDip;
        init => _imageDestinationDip = value;
    }

    /// <summary>True when the source rectangle is a strict sub-rectangle of the decoded bitmap.</summary>
    public bool HasSourceCrop { get; init; }

    /// <summary>True when the image body is padded inside its frame (negative insets).</summary>
    public bool HasDestinationInset => ImageDestinationDip != DestinationDip;

    /// <summary>True when the picture is cropped or padded in any way.</summary>
    public bool HasCrop => HasSourceCrop || HasDestinationInset;

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
        var crop = PlanCrop(picture, sourceWidth, sourceHeight);
        var sourceRect = new PictureSourceRectPixels(
            crop.SourceX,
            crop.SourceY,
            crop.SourceWidth,
            crop.SourceHeight);
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
            HasSourceCrop = crop.HasSourceCrop,
            ImageDestinationDip = ApplyDestinationInset(picture.DestDip, crop),
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

    /// <summary>
    /// Applies the destination inset a negative <c>a:srcRect</c> produces. Positive-only crops
    /// leave the frame untouched.
    /// </summary>
    private static LayoutRect ApplyDestinationInset(LayoutRect frame, SourceRectCropPlan crop)
    {
        if (!crop.HasDestinationInset)
            return frame;

        var width = frame.Width * (1.0 - crop.DestinationInsetLeft - crop.DestinationInsetRight);
        var height = frame.Height * (1.0 - crop.DestinationInsetTop - crop.DestinationInsetBottom);
        if (width <= 0 || height <= 0)
            return frame;

        return new LayoutRect(
            frame.X + crop.DestinationInsetLeft * frame.Width,
            frame.Y + crop.DestinationInsetTop * frame.Height,
            width,
            height);
    }

    private static SourceRectCropPlan PlanCrop(
        DrawOp.Picture picture,
        int pixelWidth,
        int pixelHeight)
    {
        if (picture.HasCrop)
            return SourceRectCropGeometry.Plan(
                pixelWidth,
                pixelHeight,
                picture.CropLeft,
                picture.CropTop,
                picture.CropRight,
                picture.CropBottom);

        if (!picture.IsCover)
            return NoCrop(pixelWidth, pixelHeight);

        var sourceAspect = pixelWidth / (double)pixelHeight;
        var destinationAspect = picture.DestDip.Width / picture.DestDip.Height;
        if (!double.IsFinite(destinationAspect) || destinationAspect <= 0)
            return NoCrop(pixelWidth, pixelHeight);

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

        return SourceRectCropGeometry.Plan(
            pixelWidth,
            pixelHeight,
            cropLeft,
            cropTop,
            cropRight,
            cropBottom);
    }

    private static SourceRectCropPlan NoCrop(int pixelWidth, int pixelHeight) =>
        new(0, 0, pixelWidth, pixelHeight, 0, 0, 0, 0);
}
