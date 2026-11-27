using Free.Shared.Shell;

namespace FreeP.App.Compositor;

/// <summary>One slide-sorter tile's top-left origin and size in the list's coordinate space.</summary>
public readonly record struct SlideSorterTileBounds(
    int SlideIndex,
    double Left,
    double Top,
    double Width,
    double Height);

/// <summary>
/// Toolkit-neutral input decisions of the slide pane shared by the WPF <c>SlidePane</c> and the
/// Avalonia main window: which keyboard intent a key chord means, and where a slide-sorter drop
/// lands. Hosts translate native events and read native tile bounds; the rules live here once.
/// </summary>
public static class SlidePaneInputPlanner
{
    /// <summary>
    /// Maps a slide-pane key chord to its intent. <paramref name="keyName"/> is the native key's
    /// enum name (identical in WPF and Avalonia for these keys); modifiers must match exactly.
    /// </summary>
    public static SlidePaneKeyboardIntentKind ResolveKeyboardIntent(
        string keyName,
        FreeKeyModifiers modifiers) =>
        (keyName, modifiers) switch
        {
            ("Insert", FreeKeyModifiers.None) => SlidePaneKeyboardIntentKind.InsertAfterCurrentSlide,
            ("Delete", FreeKeyModifiers.None) => SlidePaneKeyboardIntentKind.DeleteCurrentSlide,
            ("D", FreeKeyModifiers.Control) => SlidePaneKeyboardIntentKind.DuplicateCurrentSlide,
            ("Up", FreeKeyModifiers.Alt) => SlidePaneKeyboardIntentKind.MoveCurrentSlideEarlier,
            ("Down", FreeKeyModifiers.Alt) => SlidePaneKeyboardIntentKind.MoveCurrentSlideLater,
            _ => SlidePaneKeyboardIntentKind.None,
        };

    /// <summary>
    /// Slide-sorter insertion index for a pointer position: tiles are scanned in reading order
    /// (top, then left) and the drop lands before the first tile whose upper half contains the
    /// pointer or whose left half contains it on the same row; past every tile it appends.
    /// </summary>
    public static int ResolveSlideSorterInsertionIndex(
        IEnumerable<SlideSorterTileBounds> tiles,
        double pointerX,
        double pointerY,
        int slideCount)
    {
        ArgumentNullException.ThrowIfNull(tiles);

        foreach (var tile in tiles.OrderBy(t => t.Top).ThenBy(t => t.Left))
        {
            if (pointerY < tile.Top + tile.Height / 2 ||
                (pointerY < tile.Top + tile.Height && pointerX < tile.Left + tile.Width / 2))
                return tile.SlideIndex;
        }

        return slideCount;
    }
}
