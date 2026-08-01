using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>Host-neutral editable representation of one motion-path segment.</summary>
public sealed record MotionPathSegmentEdit(
    MotionPathSegmentKind Kind,
    double X,
    double Y,
    double X1,
    double Y1,
    double X2,
    double Y2)
{
    public static MotionPathSegmentEdit FromSegment(MotionPathSegment segment) =>
        new(segment.Kind, segment.X, segment.Y, segment.X1, segment.Y1, segment.X2, segment.Y2);

    public MotionPathSegment ToSegment() => Kind switch
    {
        MotionPathSegmentKind.Move => MotionPathSegment.MoveTo(X, Y),
        MotionPathSegmentKind.Line => MotionPathSegment.LineTo(X, Y),
        MotionPathSegmentKind.Cubic => MotionPathSegment.CubicTo(X1, Y1, X2, Y2, X, Y),
        MotionPathSegmentKind.Close => MotionPathSegment.Close(),
        _ => throw new ArgumentOutOfRangeException(),
    };
}

/// <summary>Snapshot used by WPF and Avalonia motion-path editors.</summary>
public sealed record MotionPathEditorPlan(
    bool CanEdit,
    int AnimationIndex,
    string Message,
    string Origin,
    string? PtsTypes,
    IReadOnlyList<MotionPathSegmentEdit> Segments);

/// <summary>
/// Shared validation and mutation for motion-path geometry editing. A successful
/// apply replaces the animation once, so the complete edit is one undo step.
/// </summary>
public static class MotionPathEditingPlanner
{
    public static MotionPathEditorPlan BuildPlan(
        IReadOnlyList<ShapeAnimation> animations,
        int animationIndex)
    {
        if (animationIndex < 0 || animationIndex >= animations.Count)
            return Unavailable(animationIndex, "The selected animation no longer exists.");

        var animation = animations[animationIndex];
        if (animation.Kind != AnimationKind.Motion || animation.Motion is null)
            return Unavailable(animationIndex, "Only motion-path animations have editable geometry.");

        return new MotionPathEditorPlan(
            true,
            animationIndex,
            string.Empty,
            string.IsNullOrWhiteSpace(animation.Motion.Origin) ? "parent" : animation.Motion.Origin,
            animation.Motion.PtsTypes,
            animation.Motion.Segments.Select(MotionPathSegmentEdit.FromSegment).ToArray());
    }

    public static bool TryApply(
        EditingSession editor,
        int animationIndex,
        IEnumerable<MotionPathSegmentEdit> edits,
        string? origin,
        string? ptsTypes,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(edits);

        var segments = edits.ToArray();
        if (animationIndex < 0 || animationIndex >= editor.CurrentSlideAnimations.Count)
            return Fail("The selected animation no longer exists.", out error);

        var current = editor.CurrentSlideAnimations[animationIndex];
        if (current.Kind != AnimationKind.Motion || current.Motion is null)
            return Fail("Only motion-path animations can be edited.", out error);

        if (segments.Length < 2)
            return Fail("A motion path needs a start point and at least one drawable segment.", out error);
        if (segments[0].Kind != MotionPathSegmentKind.Move)
            return Fail("The first path segment must be Move.", out error);
        if (!segments.Any(segment => segment.Kind is MotionPathSegmentKind.Line or MotionPathSegmentKind.Cubic))
            return Fail("The path must contain a line or curve.", out error);
        if (segments.Any(segment => !HasFiniteCoordinates(segment)))
            return Fail("All path coordinates must be finite numbers.", out error);

        var motion = new MotionPath
        {
            Origin = string.IsNullOrWhiteSpace(origin) ? "parent" : origin.Trim(),
            PtsTypes = string.IsNullOrWhiteSpace(ptsTypes) ? null : ptsTypes.Trim(),
        };
        foreach (var segment in segments)
            motion.Segments.Add(segment.ToSegment());

        var updated = PresentationAnimationCommandPlanner.CloneAnimation(current);
        updated.Motion = motion;
        editor.SetAnimation(animationIndex, updated);
        error = string.Empty;
        return true;
    }

    public static MotionPathSegmentEdit CreateLineAfter(IReadOnlyList<MotionPathSegmentEdit> segments)
    {
        var point = LastPoint(segments);
        return new MotionPathSegmentEdit(MotionPathSegmentKind.Line, point.X + 0.1, point.Y, 0, 0, 0, 0);
    }

    public static MotionPathSegmentEdit CreateCubicAfter(IReadOnlyList<MotionPathSegmentEdit> segments)
    {
        var point = LastPoint(segments);
        return new MotionPathSegmentEdit(
            MotionPathSegmentKind.Cubic,
            point.X + 0.1,
            point.Y,
            point.X + 0.03,
            point.Y - 0.08,
            point.X + 0.07,
            point.Y + 0.08);
    }

    private static (double X, double Y) LastPoint(IReadOnlyList<MotionPathSegmentEdit> segments)
    {
        for (var index = segments.Count - 1; index >= 0; index--)
        {
            var segment = segments[index];
            if (segment.Kind is MotionPathSegmentKind.Move or MotionPathSegmentKind.Line or MotionPathSegmentKind.Cubic)
                return (segment.X, segment.Y);
        }

        return (0, 0);
    }

    private static bool HasFiniteCoordinates(MotionPathSegmentEdit segment) =>
        double.IsFinite(segment.X)
        && double.IsFinite(segment.Y)
        && double.IsFinite(segment.X1)
        && double.IsFinite(segment.Y1)
        && double.IsFinite(segment.X2)
        && double.IsFinite(segment.Y2);

    private static MotionPathEditorPlan Unavailable(int animationIndex, string message) =>
        new(false, animationIndex, message, "parent", null, Array.Empty<MotionPathSegmentEdit>());

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }
}
