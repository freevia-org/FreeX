namespace FreeP.Core.Model;

/// <summary>
/// A single animation build step targeting one shape on a slide.
/// Covers preset entrance/emphasis/exit effects, motion-path animations, and
/// trigger (interactive on-click) animations.
///
/// Main-sequence animations (TriggerShapeId == null) live inside the main <c>p:seq</c>.
/// Trigger animations (TriggerShapeId != null) live inside a separate <c>p:seq</c> with
/// an onClick condition whose target is the trigger shape.
/// </summary>
public sealed class ShapeAnimation
{
    /// <summary>The shape this animation targets (matches <see cref="SlideShape.Id"/>).</summary>
    public uint ShapeId { get; set; }

    /// <summary>Entrance, Emphasis, Exit, or Motion.</summary>
    public AnimationKind Kind { get; set; } = AnimationKind.Entrance;

    /// <summary>The specific visual preset effect. Ignored when <see cref="Kind"/> is Motion.</summary>
    public AnimationPreset Preset { get; set; } = AnimationPreset.Appear;

    /// <summary>When this animation step fires relative to the previous step.</summary>
    public AnimationTrigger Trigger { get; set; } = AnimationTrigger.OnClick;

    /// <summary>
    /// Delay before the animation starts, in milliseconds.
    /// For <see cref="AnimationTrigger.WithPrevious"/> or <see cref="AnimationTrigger.AfterPrevious"/>,
    /// this is an offset from the trigger event. For OnClick this is typically 0.
    /// </summary>
    public int DelayMs { get; set; } = 0;

    /// <summary>Duration of the animation effect in milliseconds. Typical: 500 (fast), 1000 (medium), 2000 (slow).</summary>
    public int DurationMs { get; set; } = 500;

    /// <summary>Optional direction modifier (e.g. FlyIn from left vs. right).</summary>
    public AnimationDirection? Direction { get; set; }

    /// <summary>Optional PowerPoint Wheel spoke count metadata for <see cref="AnimationPreset.Wheel"/>.</summary>
    public int? WheelSpokeCount { get; set; }

    /// <summary>
    /// Motion-path data. Non-null only when <see cref="Kind"/> is <see cref="AnimationKind.Motion"/>.
    /// Segments are expressed in slide-normalized coordinates (0..1) where the origin is the
    /// shape center at the start of the animation (as per the OOXML p:animMotion coordinate system).
    /// </summary>
    public MotionPath? Motion { get; set; }

    /// <summary>
    /// When non-null, this animation belongs to an interactive trigger sequence rather than the
    /// main click chain. Clicking the shape with this Id fires the trigger sequence.
    /// Maps to <c>p:seq/p:cTn/p:stCondLst/p:cond evt="onClick" tgtEl/p:spTgt spid="…"</c>.
    /// </summary>
    public uint? TriggerShapeId { get; set; }
}

/// <summary>The role of the animation in the build sequence.</summary>
public enum AnimationKind
{
    Entrance,
    Emphasis,
    Exit,
    /// <summary>A motion-path animation; the shape moves along a <see cref="MotionPath"/>.</summary>
    Motion,
}

/// <summary>
/// Preset animation effects. Maps to OOXML presetClass + presetID combinations.
/// See mapping table in PptxAnimationMap.
/// </summary>
public enum AnimationPreset
{
    // Entrance / Exit
    Appear,
    Fade,
    FlyIn,
    Wipe,
    Zoom,
    Split,
    Blinds,
    Box,
    Checkerboard,
    Circle,
    Crawl,
    Diamond,
    Dissolve,
    Flash,
    Peek,
    Plus,
    RandomBars,
    Spiral,
    Strips,
    Swivel,
    Wedge,
    Wheel,
    Bounce,
    Float,
    Swoop,
    Boomerang,

    // Emphasis
    Grow,
    Shrink,
    Spin,
    Pulse,
    ColorPulse,
    Teeter,
    Blink,
    Bold,
    Wave,
    Underline,
    GrowWithColor,
    ChangeColor,
    Shimmer,
}

/// <summary>When an animation step is triggered.</summary>
public enum AnimationTrigger
{
    /// <summary>Fires on next mouse click.</summary>
    OnClick,
    /// <summary>Fires simultaneously with the previous animation.</summary>
    WithPrevious,
    /// <summary>Fires after the previous animation completes.</summary>
    AfterPrevious,
}

// ── Motion path ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A motion path for a <see cref="ShapeAnimation"/> with <see cref="AnimationKind.Motion"/>.
/// Coordinates are in slide-normalized space (0..1), where the origin is the shape's centre at
/// the start of the animation.  Matches the OOXML <c>p:animMotion path</c> coordinate system.
/// </summary>
public sealed class MotionPath
{
    /// <summary>
    /// Ordered path segments (Move, Line, Cubic, Close).
    /// The first segment must always be a Move (M) anchoring the start point.
    /// </summary>
    public List<MotionPathSegment> Segments { get; } = new();

    /// <summary>
    /// Origin model for the path.  "parent" = relative to shape's natural position (default).
    /// Stored as the OOXML string value of <c>p:animMotion/@origin</c>.
    /// </summary>
    public string Origin { get; set; } = "parent";

    /// <summary>
    /// Path calculation mode.  "linear" = linear interpolation (default), "spline" = spline.
    /// Stored as the OOXML string value of <c>p:animMotion/@ptsTypes</c> when present.
    /// </summary>
    public string? PtsTypes { get; set; }
}

/// <summary>Kind of a single segment in a <see cref="MotionPath"/>.</summary>
public enum MotionPathSegmentKind
{
    /// <summary>Move (M) — lifts the pen to a new point without drawing.</summary>
    Move,
    /// <summary>Line (L) — draws a straight line to the point.</summary>
    Line,
    /// <summary>Cubic Bezier (C) — draws a cubic curve using two control points.</summary>
    Cubic,
    /// <summary>Close (Z) — closes the path back to the last Move point.</summary>
    Close,
}

/// <summary>
/// A single segment in a <see cref="MotionPath"/>.
/// For Move/Line: X,Y are the endpoint.
/// For Cubic: X1,Y1 are control-point 1; X2,Y2 are control-point 2; X,Y are the endpoint.
/// For Close: all coords are 0.
/// All values are in slide-normalized coordinates (0..1).
/// </summary>
public sealed class MotionPathSegment
{
    public MotionPathSegmentKind Kind { get; init; }
    /// <summary>End point X (slide-normalized).</summary>
    public double X  { get; init; }
    /// <summary>End point Y (slide-normalized).</summary>
    public double Y  { get; init; }
    /// <summary>Cubic control point 1 X.</summary>
    public double X1 { get; init; }
    /// <summary>Cubic control point 1 Y.</summary>
    public double Y1 { get; init; }
    /// <summary>Cubic control point 2 X.</summary>
    public double X2 { get; init; }
    /// <summary>Cubic control point 2 Y.</summary>
    public double Y2 { get; init; }

    public static MotionPathSegment MoveTo(double x, double y) =>
        new() { Kind = MotionPathSegmentKind.Move, X = x, Y = y };

    public static MotionPathSegment LineTo(double x, double y) =>
        new() { Kind = MotionPathSegmentKind.Line, X = x, Y = y };

    public static MotionPathSegment CubicTo(double x1, double y1, double x2, double y2, double x, double y) =>
        new() { Kind = MotionPathSegmentKind.Cubic, X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, X = x, Y = y };

    public static MotionPathSegment Close() =>
        new() { Kind = MotionPathSegmentKind.Close };

    /// <summary>
    /// Evaluates the position along this segment at parameter t ∈ [0,1], given the previous endpoint.
    /// </summary>
    public (double x, double y) Evaluate(double t, double prevX, double prevY)
    {
        return Kind switch
        {
            MotionPathSegmentKind.Move => (X, Y),
            MotionPathSegmentKind.Line =>
                (prevX + (X - prevX) * t, prevY + (Y - prevY) * t),
            MotionPathSegmentKind.Cubic =>
                EvalCubic(t, prevX, prevY, X1, Y1, X2, Y2, X, Y),
            _ => (prevX, prevY),
        };
    }

    private static (double x, double y) EvalCubic(
        double t, double p0x, double p0y,
        double p1x, double p1y, double p2x, double p2y,
        double p3x, double p3y)
    {
        double u  = 1 - t;
        double u2 = u * u;
        double u3 = u2 * u;
        double t2 = t * t;
        double t3 = t2 * t;
        double x  = u3 * p0x + 3 * u2 * t * p1x + 3 * u * t2 * p2x + t3 * p3x;
        double y  = u3 * p0y + 3 * u2 * t * p1y + 3 * u * t2 * p2y + t3 * p3y;
        return (x, y);
    }
}

/// <summary>
/// Helper: evaluates a complete <see cref="MotionPath"/> at t ∈ [0,1],
/// returning the (dx, dy) displacement from the start point in slide-normalized coords.
/// </summary>
public static class MotionPathEvaluator
{
    /// <summary>
    /// Samples the path at fraction <paramref name="t"/> (0=start, 1=end).
    /// Returns the (dx, dy) displacement from the path's origin (first Move point).
    /// </summary>
    public static (double dx, double dy) Sample(MotionPath path, double t)
    {
        if (path.Segments.Count == 0) return (0, 0);

        // Assign proportional arc-lengths to segments for t interpolation.
        // Simplified: treat each non-Close segment as equally weighted.
        var active = path.Segments
            .Where(s => s.Kind != MotionPathSegmentKind.Move || path.Segments.IndexOf(s) == 0)
            .ToList();

        // Build list of drawable segments (skip first Move for distance counting).
        var drawable = path.Segments
            .Where(s => s.Kind != MotionPathSegmentKind.Move && s.Kind != MotionPathSegmentKind.Close)
            .ToList();

        if (drawable.Count == 0) return (0, 0);

        double segT   = t * drawable.Count;
        int    segIdx = Math.Clamp((int)segT, 0, drawable.Count - 1);
        double localT = segT - segIdx;

        // Find previous endpoint for the selected segment.
        // Walk through the full path to track the current pen position.
        double px = 0, py = 0; // path-space pen
        double startX = 0, startY = 0;

        int drawableCount = 0;
        bool started = false;
        foreach (var seg in path.Segments)
        {
            if (!started && seg.Kind == MotionPathSegmentKind.Move)
            {
                startX = seg.X;
                startY = seg.Y;
                px = seg.X;
                py = seg.Y;
                started = true;
                continue;
            }
            if (seg.Kind == MotionPathSegmentKind.Close) { px = startX; py = startY; continue; }
            if (seg.Kind == MotionPathSegmentKind.Move)  { px = seg.X;  py = seg.Y;  continue; }

            // drawable segment
            if (drawableCount == segIdx)
            {
                var (ex, ey) = seg.Evaluate(localT, px, py);
                return (ex - startX, ey - startY);
            }
            px = seg.Kind == MotionPathSegmentKind.Cubic ? seg.X : seg.X;
            py = seg.Kind == MotionPathSegmentKind.Cubic ? seg.Y : seg.Y;
            drawableCount++;
        }

        // Fallback: endpoint of last segment minus start.
        var last = drawable[^1];
        return (last.X - startX, last.Y - startY);
    }
}

/// <summary>
/// Direction modifier for animations that support it (FlyIn, Wipe, Split, etc.).
/// </summary>
public enum AnimationDirection
{
    Left,
    Right,
    Up,
    Down,
    LeftUp,
    LeftDown,
    RightUp,
    RightDown,
    Horizontal,
    Vertical,
    In,
    Out,
    FromLeft,
    FromRight,
    FromTop,
    FromBottom,
    FromTopLeft,
    FromTopRight,
    FromBottomLeft,
    FromBottomRight,
}
