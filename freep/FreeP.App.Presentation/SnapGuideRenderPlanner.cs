using Free.Shared.Drawing;

namespace FreeP.App.Compositor;

/// <summary>One screen-space guide line to stroke on the adorner layer.</summary>
public readonly record struct SnapGuideSegment(LayoutPoint Start, LayoutPoint End);

/// <summary>
/// Renderer-neutral snap-guide geometry shared by both adorner layers. A guide spans the adorner's own
/// extent (0..width for a horizontal guide, 0..height for a vertical one), so it never extends past the
/// canvas the way an unbounded line would.
/// </summary>
public static class SnapGuideRenderPlanner
{
    public static IReadOnlyList<SnapGuideSegment> Plan(
        IReadOnlyList<SnapGuideLine> guides,
        SlideTransformCore transform,
        double widthDip,
        double heightDip)
    {
        ArgumentNullException.ThrowIfNull(guides);
        ArgumentNullException.ThrowIfNull(transform);

        var segments = new List<SnapGuideSegment>(guides.Count);
        foreach (var guide in guides)
        {
            double position = SlideCanvasGeometryPlanner.SnapGuideToScreenPosition(guide, transform);
            segments.Add(guide.IsHorizontal
                ? new SnapGuideSegment(new LayoutPoint(0, position), new LayoutPoint(widthDip, position))
                : new SnapGuideSegment(new LayoutPoint(position, 0), new LayoutPoint(position, heightDip)));
        }

        return segments;
    }
}
