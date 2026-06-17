namespace FreeX.App.Presentation.GridInteraction;

/// <summary>
/// An auto-scroll intent produced while a drag interaction hovers near the grid edge. Each axis is
/// -1 (scroll toward the start), 0 (no scroll), or +1 (scroll toward the end). The renderers convert
/// the intent into an actual scrollbar step.
/// </summary>
public readonly record struct GridAutoScrollRequest(int HorizontalDirection, int VerticalDirection)
{
    public bool HasAnyDirection => HorizontalDirection != 0 || VerticalDirection != 0;
}
