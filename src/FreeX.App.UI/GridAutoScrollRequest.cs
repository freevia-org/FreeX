namespace FreeX.App.UI;

public readonly record struct GridAutoScrollRequest(int HorizontalDirection, int VerticalDirection)
{
    public bool HasAnyDirection => HorizontalDirection != 0 || VerticalDirection != 0;
}
