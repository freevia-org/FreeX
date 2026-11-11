namespace FreeP.App.Compositor;

/// <summary>
/// Toolkit-neutral press / threshold / drop-index state machine for dragging slides in the Custom Shows
/// dialog list. Hosts translate pointer events and supply the native drag threshold; the tracker owns the
/// press origin, the source index, the active flag and the insertion-index rule.
/// </summary>
public sealed class SlideShowCustomShowDragTracker
{
    private double _startX;
    private double _startY;
    private bool _hasStart;

    /// <summary>Index of the pressed slide, or -1 when no press is being tracked.</summary>
    public int SourceIndex { get; private set; } = -1;

    /// <summary>True once <see cref="MarkActive"/> was called for the current press.</summary>
    public bool IsActive { get; private set; }

    /// <summary>True while a press origin and a valid source index are recorded.</summary>
    public bool HasPress => _hasStart && SourceIndex >= 0;

    /// <summary>Records the press point and source slide index and clears any active drag.</summary>
    public void Press(double x, double y, int sourceIndex)
    {
        _startX = x;
        _startY = y;
        _hasStart = true;
        SourceIndex = sourceIndex;
        IsActive = false;
    }

    /// <summary>
    /// True when a press is tracked and the pointer travelled at least the host-supplied threshold on either
    /// axis since the press (matching the native minimum-drag-distance semantics).
    /// </summary>
    public bool ShouldBegin(double x, double y, double thresholdX, double thresholdY)
    {
        if (!HasPress)
            return false;

        return !(Math.Abs(x - _startX) < thresholdX && Math.Abs(y - _startY) < thresholdY);
    }

    /// <summary>Marks the tracked press as an active drag.</summary>
    public void MarkActive() => IsActive = true;

    /// <summary>Clears the press origin, source index and active flag.</summary>
    public void Reset()
    {
        _hasStart = false;
        _startX = 0;
        _startY = 0;
        SourceIndex = -1;
        IsActive = false;
    }

    /// <summary>
    /// Insertion index for a drop: over an item (itemIndex &gt;= 0) the lower half inserts after it and the
    /// upper half before it; off any item the slide is appended at <paramref name="count"/>.
    /// </summary>
    public static int ResolveDropIndex(int itemIndex, double yInItem, double itemHeight, int count)
    {
        if (itemIndex < 0)
            return count;

        return yInItem > itemHeight / 2 ? itemIndex + 1 : itemIndex;
    }
}
