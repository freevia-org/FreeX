using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record SlideShowMediaShapePlan(
    uint ShapeId,
    bool IsVideo,
    LayoutRect Bounds,
    bool HasSource,
    string SourceKind,
    string PlaybackCapabilityNote);

public sealed record SlideShowMediaClickPlan(
    bool IsHandled,
    bool ShouldTogglePlayback,
    SlideShowMediaShapePlan? Media)
{
    public static SlideShowMediaClickPlan NotMedia { get; } = new(false, false, null);
}

/// <summary>
/// Shared slideshow media hit-testing and source policy. WPF and Avalonia keep
/// native playback optional, but they must agree on hit rectangles and consume
/// media clicks before the normal slideshow advance route.
/// </summary>
public static class SlideShowMediaInteractionPlanner
{
    public const string PlaybackBackendCapabilityNote =
        "LibVLC cross-platform audio/video playback is available when the native runtime is present; poster rendering and media click routing remain available as fallback.";

    public static IReadOnlyList<SlideShowMediaShapePlan> BuildSlidePlan(
        Slide slide,
        double slideDipW,
        double slideDipH,
        double canvasW,
        double canvasH)
    {
        ArgumentNullException.ThrowIfNull(slide);

        return EnumerateShapes(slide.Shapes)
            .Where(shape => shape.Kind == SlideShapeKind.Media && shape.Media is not null)
            .Select(shape => BuildShapePlan(shape, slideDipW, slideDipH, canvasW, canvasH))
            .ToArray();
    }

    public static SlideShowMediaClickPlan PlanClick(
        Slide slide,
        double slideDipW,
        double slideDipH,
        double canvasW,
        double canvasH,
        double canvasX,
        double canvasY)
    {
        foreach (var media in BuildSlidePlan(slide, slideDipW, slideDipH, canvasW, canvasH).Reverse())
        {
            if (canvasX >= media.Bounds.Left && canvasX <= media.Bounds.Right &&
                canvasY >= media.Bounds.Top && canvasY <= media.Bounds.Bottom)
                return new SlideShowMediaClickPlan(true, true, media);
        }

        return SlideShowMediaClickPlan.NotMedia;
    }

    public static LayoutRect ComputeMediaBounds(
        SlideShape shape,
        double slideDipW,
        double slideDipH,
        double canvasW,
        double canvasH)
    {
        ArgumentNullException.ThrowIfNull(shape);

        var scale = canvasW > 0 && canvasH > 0 && slideDipW > 0 && slideDipH > 0
            ? Math.Min(canvasW / slideDipW, canvasH / slideDipH)
            : 1.0;
        var offsetX = (canvasW - slideDipW * scale) / 2;
        var offsetY = (canvasH - slideDipH * scale) / 2;

        var shapeX = shape.OffsetXEmu / 9525.0;
        var shapeY = shape.OffsetYEmu / 9525.0;
        var shapeW = shape.ExtentCxEmu / 9525.0;
        var shapeH = shape.ExtentCyEmu / 9525.0;

        return new LayoutRect(
            offsetX + shapeX * scale,
            offsetY + shapeY * scale,
            shapeW * scale,
            shapeH * scale);
    }

    /// <summary>
    /// Keeps the host-independent media volume contract in the shared 0-100 range.
    /// Native WPF and LibVLC adapters consume different representations of this value.
    /// </summary>
    public static int NormalizeVolumePercent(int volume) => Math.Clamp(volume, 0, 100);

    private static SlideShowMediaShapePlan BuildShapePlan(
        SlideShape shape,
        double slideDipW,
        double slideDipH,
        double canvasW,
        double canvasH)
    {
        var media = shape.Media!;
        var hasEmbeddedSource = media.Bytes is { Length: > 0 };
        var hasLinkedSource = Uri.TryCreate(media.LinkUrl, UriKind.Absolute, out var link) &&
            link.Scheme is "http" or "https";

        return new SlideShowMediaShapePlan(
            shape.Id,
            media.IsVideo,
            ComputeMediaBounds(shape, slideDipW, slideDipH, canvasW, canvasH),
            hasEmbeddedSource || hasLinkedSource,
            hasEmbeddedSource ? "embedded" : hasLinkedSource ? "http-link" : "missing",
            PlaybackBackendCapabilityNote);
    }

    private static IEnumerable<SlideShape> EnumerateShapes(IEnumerable<SlideShape> shapes)
    {
        foreach (var shape in shapes)
        {
            yield return shape;
            foreach (var child in EnumerateShapes(shape.Children))
                yield return child;
        }
    }
}
