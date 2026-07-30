namespace FreeP.App.Compositor;

/// <summary>
/// Shared logical contract for a pointer drag in an in-canvas rich editor.
/// Hosts only translate framework pointer coordinates into logical offsets;
/// anchor direction and document bounds stay identical across WPF/Avalonia.
/// </summary>
public static class InCanvasRichTextPointerSelectionPlanner
{
    internal const double DefaultEdgeThreshold = 18;
    internal const double DefaultScrollStep = 18;

    public static InCanvasEditorTextSelection Plan(
        int anchor,
        int caret,
        int textLength)
    {
        if (textLength < 0)
            throw new ArgumentOutOfRangeException(nameof(textLength));

        return new(
            Math.Clamp(anchor, 0, textLength),
            Math.Clamp(caret, 0, textLength));
    }

    public static (int Start, int End) Normalize(
        int anchor,
        int caret,
        int textLength)
    {
        var selection = Plan(anchor, caret, textLength);
        return (
            Math.Min(selection.Start, selection.End),
            Math.Max(selection.Start, selection.End));
    }

    /// <summary>
    /// Returns -1 while the pointer is in the top edge band, 1 while it is in the
    /// bottom edge band, and 0 while it is away from either edge.  Coordinates may
    /// be outside the editor because native text controls continue a captured drag
    /// after the pointer leaves their bounds.
    /// </summary>
    public static int ResolveVerticalEdgeDirection(
        double pointerY,
        double viewportHeight,
        double edgeThreshold = DefaultEdgeThreshold)
    {
        if (!double.IsFinite(pointerY))
            throw new ArgumentOutOfRangeException(nameof(pointerY));
        if (!double.IsFinite(viewportHeight) || viewportHeight < 0)
            throw new ArgumentOutOfRangeException(nameof(viewportHeight));
        if (!double.IsFinite(edgeThreshold) || edgeThreshold < 0)
            throw new ArgumentOutOfRangeException(nameof(edgeThreshold));

        if (pointerY <= edgeThreshold)
            return -1;
        if (pointerY >= viewportHeight - edgeThreshold)
            return 1;
        return 0;
    }

    /// <summary>Advances a vertical editor scroll offset and clamps it to content bounds.</summary>
    public static double AdvanceVerticalScroll(
        double currentOffset,
        double contentExtent,
        double viewportExtent,
        int direction,
        double step = DefaultScrollStep)
    {
        if (!double.IsFinite(currentOffset) || currentOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(currentOffset));
        if (!double.IsFinite(contentExtent) || contentExtent < 0)
            throw new ArgumentOutOfRangeException(nameof(contentExtent));
        if (!double.IsFinite(viewportExtent) || viewportExtent < 0)
            throw new ArgumentOutOfRangeException(nameof(viewportExtent));
        if (!double.IsFinite(step) || step < 0)
            throw new ArgumentOutOfRangeException(nameof(step));
        if (direction is < -1 or > 1)
            throw new ArgumentOutOfRangeException(nameof(direction));

        double maximum = Math.Max(0, contentExtent - viewportExtent);
        if (direction == 0)
            return Math.Clamp(currentOffset, 0, maximum);

        return Math.Clamp(currentOffset + direction * step, 0, maximum);
    }
}
