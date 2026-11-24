namespace FreeW.App.Presentation.Dialogs;

/// <summary>
/// The roving-focus state of the Insert Icon tile grid: which tile is the single tab stop, how a
/// directional key moves it, and which tile Enter/Space activates. Both icon pickers keep one of
/// these and only apply the result to their own tiles (tab stops, focus outline, native focus), so
/// the focus rules live once next to <see cref="IconGridNavigation"/>.
/// </summary>
public sealed class IconGridFocusSession
{
    /// <summary>The tile that currently owns the roving tab stop.</summary>
    public int FocusedIndex { get; private set; }

    /// <summary>Resets the tab stop to the first tile (after a filter or search change).</summary>
    public void Reset() => FocusedIndex = 0;

    /// <summary>
    /// The tile a directional/Home/End key press should move the tab stop to over
    /// <paramref name="tileCount"/> tiles laid out <paramref name="columns"/> to a row. Returns
    /// <c>null</c> when the grid is empty (the key is not handled). The caller applies the result
    /// with <see cref="MoveTo"/> once the native tile has taken focus.
    /// </summary>
    public int? PlanNavigation(IconGridNavigationKey key, int tileCount, int columns) =>
        tileCount <= 0
            ? null
            : IconGridNavigation.Move(FocusedIndex, key, tileCount, columns);

    /// <summary>
    /// Moves the tab stop to <paramref name="index"/> without changing the selection; false (and no
    /// change) when the index is outside the grid.
    /// </summary>
    public bool MoveTo(int index, int tileCount)
    {
        if (index < 0 || index >= tileCount)
            return false;

        FocusedIndex = index;
        return true;
    }

    /// <summary>
    /// The tile Enter/Space should activate: the focused tile, clamped into the grid. Returns
    /// <c>null</c> when the grid is empty.
    /// </summary>
    public int? ActivationIndex(int tileCount) =>
        tileCount <= 0 ? null : Math.Clamp(FocusedIndex, 0, tileCount - 1);
}
