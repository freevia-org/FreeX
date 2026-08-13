namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Renderer-neutral Reviewing Pane state derived after a revision-list refresh.
/// </summary>
public readonly record struct ReviewingPaneRefreshState(
    int SelectedIndex,
    string StatusText);

/// <summary>
/// Owns the selection and status-text rules shared by the WPF and Avalonia Reviewing Panes.
/// </summary>
public static class ReviewingPaneStatePlanner
{
    public static ReviewingPaneRefreshState BuildRefreshState(int revisionCount, int previousIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(revisionCount);

        var selectedIndex = revisionCount == 0
            ? -1
            : Math.Clamp(previousIndex < 0 ? 0 : previousIndex, 0, revisionCount - 1);

        return new ReviewingPaneRefreshState(
            selectedIndex,
            revisionCount switch
            {
                0 => "No tracked changes",
                1 => "1 change",
                _ => $"{revisionCount} changes",
            });
    }

    /// <summary>
    /// Resolves Previous/Next navigation with Word-style wrapping. A negative direction means Previous;
    /// a positive direction means Next. Returns -1 when there are no revisions.
    /// </summary>
    public static int ResolveStep(int revisionCount, int currentIndex, int direction)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(revisionCount);
        if (direction == 0)
            throw new ArgumentOutOfRangeException(nameof(direction));
        if (revisionCount == 0)
            return -1;

        if (currentIndex < 0)
            return direction < 0 ? revisionCount - 1 : 0;
        if (currentIndex >= revisionCount)
            throw new ArgumentOutOfRangeException(nameof(currentIndex));

        return (currentIndex + Math.Sign(direction) + revisionCount) % revisionCount;
    }
}
