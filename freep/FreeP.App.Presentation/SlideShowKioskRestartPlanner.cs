using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>Resolves the native kiosk restart duration for slideshow hosts.</summary>
public static class SlideShowKioskRestartPlanner
{
    public static bool TryGetInterval(
        Presentation presentation,
        out TimeSpan interval)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        if (presentation.ShowType != PresentationShowType.BrowsedAtKiosk
            || presentation.KioskRestartAfterMilliseconds is not > 0)
        {
            interval = default;
            return false;
        }

        interval = TimeSpan.FromMilliseconds(
            presentation.KioskRestartAfterMilliseconds.Value);
        return true;
    }
}
