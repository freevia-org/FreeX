namespace FreeW.App.Presentation.Dialogs;

/// <summary>
/// Directional keys the Insert Icon grid responds to. Kept shell-neutral so both the WPF
/// (<c>System.Windows.Input.Key</c>) and Avalonia (<c>Avalonia.Input.Key</c>) icon pickers can map
/// their own key enum onto this one and drive an identical navigation model.
/// </summary>
public enum IconGridNavigationKey
{
    Left,
    Right,
    Up,
    Down,
    Home,
    End,
}

/// <summary>
/// Pure, shell-agnostic keyboard navigation for the Insert Icon tile grid. Both icon pickers lay
/// tiles out left-to-right, wrapping every <c>columns</c> tiles into a new row (the surface spec's
/// <see cref="IconPickerSurfaceSpec.TilesPerRow"/>); this type computes which flat tile index a
/// directional/Home/End key press should move keyboard focus to, without depending on either
/// shell's real (measured) layout -- which keeps it usable from a plain unit test and keeps the two
/// shells' behaviour identical by construction.
/// </summary>
public static class IconGridNavigation
{
    /// <summary>
    /// Computes the next focused tile index for a directional key press. Movement is clamped to
    /// the grid: a key press that would leave the valid <c>[0, itemCount)</c> range (e.g. Left on
    /// the first tile, or Up on a tile in the first row) leaves the focused index unchanged rather
    /// than wrapping to the opposite edge or another row.
    /// </summary>
    public static int Move(int currentIndex, IconGridNavigationKey key, int itemCount, int columns)
    {
        if (itemCount <= 0)
            return 0;

        columns = Math.Max(1, columns);
        var current = Math.Clamp(currentIndex, 0, itemCount - 1);

        var next = key switch
        {
            IconGridNavigationKey.Left => current - 1,
            IconGridNavigationKey.Right => current + 1,
            IconGridNavigationKey.Up => current - columns,
            IconGridNavigationKey.Down => current + columns,
            IconGridNavigationKey.Home => 0,
            IconGridNavigationKey.End => itemCount - 1,
            _ => current,
        };

        return next < 0 || next >= itemCount ? current : next;
    }
}
