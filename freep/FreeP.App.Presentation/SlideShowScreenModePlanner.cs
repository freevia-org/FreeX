namespace FreeP.App.Compositor;

public enum SlideShowScreenMode
{
    Normal,
    Black,
    White,
}

public static class SlideShowScreenModePlanner
{
    public static bool IsBlank(SlideShowScreenMode mode) => mode != SlideShowScreenMode.Normal;

    public static bool TryPlanKey(
        string? keyName,
        SlideShowScreenMode current,
        out SlideShowScreenMode next)
    {
        switch (keyName?.Trim().ToUpperInvariant())
        {
            case "B":
                next = current == SlideShowScreenMode.Black
                    ? SlideShowScreenMode.Normal
                    : SlideShowScreenMode.Black;
                return true;
            case "W":
                next = current == SlideShowScreenMode.White
                    ? SlideShowScreenMode.Normal
                    : SlideShowScreenMode.White;
                return true;
            default:
                next = current;
                return false;
        }
    }
}
