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

    /// <summary>
    /// Number of times the effect plays, including its first pass. Null means no finite count
    /// was authored; use <see cref="RepeatIndefinitely"/> for the explicit indefinite token.
    /// </summary>
    public int? RepeatCount { get; set; }

    /// <summary>Whether the effect repeats indefinitely in the authored timing.</summary>
    public bool RepeatIndefinitely { get; set; }

    /// <summary>Whether each repeat reverses direction before the next pass.</summary>
    public bool AutoReverse { get; set; }

    /// <summary>Authored OOXML acceleration timing (p:cTn/@accel, 0..100000).</summary>
    public int? Acceleration { get; set; }

    /// <summary>Authored OOXML deceleration timing (p:cTn/@decel, 0..100000).</summary>
    public int? Deceleration { get; set; }

    /// <summary>Optional direction modifier (e.g. FlyIn from left vs. right).</summary>
    public AnimationDirection? Direction { get; set; }

    /// <summary>Optional PowerPoint Wheel spoke count metadata for <see cref="AnimationPreset.Wheel"/>.</summary>
    public int? WheelSpokeCount { get; set; }

    /// <summary>
    /// Optional authored effect-option subtype for modeled presets whose options are not
    /// directional (for example, PowerPoint Spin's quarter/half/full/two-turn choices).
    /// For Grow/Shrink this is opaque legacy metadata only; the amount authority is
    /// <see cref="ScaleBehavior"/>, not presetSubtype.
    /// </summary>
    public string? EffectSubtype { get; set; }

    /// <summary>The authored p:animScale behavior for Grow/Shrink, when present.</summary>
    public AnimationScaleBehavior? ScaleBehavior { get; set; }

    /// <summary>
    /// Preserves the authored <c>p:animClr</c> behavior for color emphasis effects.
    /// The current playback model exposes the effect kind but not every color-transition
    /// option, so the native payload remains authoritative for package round-trip.
    /// </summary>
    public string? PreservedColorBehaviorXml { get; set; }

    /// <summary>
    /// Preserves an authored numeric <c>p:anim</c> behavior whose target is not
    /// represented by the current renderer-neutral model (for example PowerPoint's
    /// <c>style.fontSize</c> Change Font Size emphasis effect).
    /// </summary>
    public string? PreservedNumericBehaviorXml { get; set; }

    /// <summary>
    /// Preserves a native color-target behavior group whose auxiliary setters
    /// are not represented by the renderer-neutral model (for example
    /// PowerPoint's <c>fill.type</c> and <c>fill.on</c> setters).
    /// </summary>
    public string? PreservedFillBehaviorXml { get; set; }

    /// <summary>
    /// Preserves PowerPoint's native line-color behavior group, including the
    /// <c>stroke.on</c> setter that accompanies <c>stroke.color</c>.
    /// </summary>
    public string? PreservedLineBehaviorXml { get; set; }

    /// <summary>
    /// Preserves an animation preset that is not represented by the current
    /// <see cref="AnimationPreset"/> enum. Playback still uses the mapped
    /// fallback, but package save can re-emit the authored PowerPoint token.
    /// </summary>
    public string? RawPresetClass { get; set; }

    /// <summary>The authored OOXML presetID when <see cref="RawPresetClass"/> is set.</summary>
    public int? RawPresetId { get; set; }

    /// <summary>The authored OOXML presetSubtype when it is not modeled.</summary>
    public string? RawPresetSubtype { get; set; }

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
    ChangeFillColor,
    ChangeLineColor,
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

    /// <summary>
    /// Creates a copy whose drawable segments run in the opposite direction.
    /// The returned path uses the same native OOXML segment vocabulary; cubic
    /// control points are swapped so the reversed curve has the same geometry.
    /// </summary>
    public static MotionPath ReversedClone(MotionPath source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var reversed = new MotionPath
        {
            Origin = source.Origin,
            PtsTypes = source.PtsTypes,
        };

        var subpath = new List<MotionPathSegment>();
        foreach (var segment in source.Segments)
        {
            if (segment.Kind == MotionPathSegmentKind.Move && subpath.Count > 0)
            {
                AppendReversedSubpath(reversed.Segments, subpath);
                subpath.Clear();
            }

            subpath.Add(segment);
        }

        if (subpath.Count > 0)
            AppendReversedSubpath(reversed.Segments, subpath);

        return reversed;
    }

    private static void AppendReversedSubpath(
        List<MotionPathSegment> destination,
        IReadOnlyList<MotionPathSegment> source)
    {
        var move = source.FirstOrDefault(segment => segment.Kind == MotionPathSegmentKind.Move);
        if (move is null)
            return;

        var drawable = source
            .Where(segment => segment.Kind is MotionPathSegmentKind.Line or MotionPathSegmentKind.Cubic)
            .ToList();
        var endX = drawable.Count == 0 ? move.X : drawable[^1].X;
        var endY = drawable.Count == 0 ? move.Y : drawable[^1].Y;
        destination.Add(MotionPathSegment.MoveTo(endX, endY));

        var points = new List<(double X, double Y)> { (move.X, move.Y) };
        foreach (var segment in drawable)
        {
            points.Add((segment.X, segment.Y));
        }

        for (var index = drawable.Count - 1; index >= 0; index--)
        {
            var segment = drawable[index];
            var previous = points[index];
            if (segment.Kind == MotionPathSegmentKind.Line)
            {
                destination.Add(MotionPathSegment.LineTo(previous.X, previous.Y));
            }
            else
            {
                destination.Add(MotionPathSegment.CubicTo(
                    segment.X2,
                    segment.Y2,
                    segment.X1,
                    segment.Y1,
                    previous.X,
                    previous.Y));
            }
        }

        if (source[^1].Kind == MotionPathSegmentKind.Close)
            destination.Add(MotionPathSegment.Close());
    }
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
    private const int CubicLengthSamples = 64;

    private sealed class SegmentMeasure
    {
        public required MotionPathSegment Segment { get; init; }
        public required double StartX { get; init; }
        public required double StartY { get; init; }
        public required double Length { get; init; }
        public double[]? ArcLengths { get; init; }
    }

    /// <summary>
    /// Samples the path at fraction <paramref name="t"/> (0=start, 1=end).
    /// Returns the (dx, dy) displacement from the path's origin (first Move point).
    /// </summary>
    public static (double dx, double dy) Sample(MotionPath path, double t)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Segments.Count == 0) return (0, 0);

        var measures = MeasureSegments(path, out var originX, out var originY);
        if (measures.Count == 0) return (0, 0);

        var totalLength = measures.Sum(measure => measure.Length);
        if (totalLength <= double.Epsilon)
            return (0, 0);

        var targetDistance = Math.Clamp(t, 0, 1) * totalLength;
        var distanceBefore = 0.0;
        foreach (var measure in measures)
        {
            var distanceInto = targetDistance - distanceBefore;
            if (distanceInto <= measure.Length || ReferenceEquals(measure, measures[^1]))
            {
                var localT = ResolveSegmentParameter(measure, Math.Clamp(distanceInto, 0, measure.Length));
                var (x, y) = measure.Segment.Evaluate(localT, measure.StartX, measure.StartY);
                return (x - originX, y - originY);
            }

            distanceBefore += measure.Length;
        }

        var last = measures[^1];
        return (last.Segment.X - originX, last.Segment.Y - originY);
    }

    private static List<SegmentMeasure> MeasureSegments(
        MotionPath path,
        out double originX,
        out double originY)
    {
        var measures = new List<SegmentMeasure>();
        originX = 0;
        originY = 0;
        var currentX = 0.0;
        var currentY = 0.0;
        var subpathStartX = 0.0;
        var subpathStartY = 0.0;
        var started = false;

        foreach (var segment in path.Segments)
        {
            if (segment.Kind == MotionPathSegmentKind.Move)
            {
                if (!started)
                {
                    originX = segment.X;
                    originY = segment.Y;
                    started = true;
                }

                currentX = subpathStartX = segment.X;
                currentY = subpathStartY = segment.Y;
                continue;
            }

            if (!started)
                continue;

            if (segment.Kind == MotionPathSegmentKind.Close)
            {
                currentX = subpathStartX;
                currentY = subpathStartY;
                continue;
            }

            var measure = CreateMeasure(segment, currentX, currentY);
            if (measure.Length > double.Epsilon)
                measures.Add(measure);

            currentX = segment.X;
            currentY = segment.Y;
        }

        return measures;
    }

    private static SegmentMeasure CreateMeasure(
        MotionPathSegment segment,
        double startX,
        double startY)
    {
        if (segment.Kind == MotionPathSegmentKind.Line)
        {
            var length = Distance(segment.X - startX, segment.Y - startY);
            return new SegmentMeasure
            {
                Segment = segment,
                StartX = startX,
                StartY = startY,
                Length = length
            };
        }

        var arcLengths = new double[CubicLengthSamples + 1];
        var previousX = startX;
        var previousY = startY;
        for (var index = 1; index <= CubicLengthSamples; index++)
        {
            var sampleT = index / (double)CubicLengthSamples;
            var (x, y) = segment.Evaluate(sampleT, startX, startY);
            arcLengths[index] = arcLengths[index - 1] + Distance(x - previousX, y - previousY);
            previousX = x;
            previousY = y;
        }

        return new SegmentMeasure
        {
            Segment = segment,
            StartX = startX,
            StartY = startY,
            Length = arcLengths[^1],
            ArcLengths = arcLengths
        };
    }

    private static double ResolveSegmentParameter(SegmentMeasure measure, double distance)
    {
        if (measure.Length <= double.Epsilon)
            return 0;

        if (measure.ArcLengths is not { } arcLengths)
            return distance / measure.Length;

        var upper = Array.BinarySearch(arcLengths, distance);
        if (upper >= 0)
            return upper / (double)CubicLengthSamples;

        upper = ~upper;
        if (upper <= 0)
            return 0;
        if (upper >= arcLengths.Length)
            return 1;

        var lower = upper - 1;
        var span = arcLengths[upper] - arcLengths[lower];
        var fraction = span <= double.Epsilon
            ? 0
            : (distance - arcLengths[lower]) / span;
        return (lower + fraction) / CubicLengthSamples;
    }

    private static double Distance(double x, double y) => Math.Sqrt(x * x + y * y);
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
    /// <summary>Split effect: panels move from the outside edges toward the center.</summary>
    HorizontalIn,
    /// <summary>Split effect: panels move from the center toward the outside edges.</summary>
    HorizontalOut,
    /// <summary>Split effect: panels move from the outside edges toward the center.</summary>
    VerticalIn,
    /// <summary>Split effect: panels move from the center toward the outside edges.</summary>
    VerticalOut,
}
