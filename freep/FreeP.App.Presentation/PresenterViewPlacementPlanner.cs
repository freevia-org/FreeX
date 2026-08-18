namespace FreeP.App.Compositor;

/// <summary>
/// The bounds of one display, in whatever single consistent unit the caller enumerates
/// screens with (device pixels for both WPF's <c>System.Windows.Forms.Screen</c> and
/// Avalonia's <c>Screen.WorkingArea</c>). <see cref="IsPrimary"/> mirrors the OS's notion
/// of "primary display" (the one carrying the taskbar/menu bar on Windows/macOS).
/// </summary>
public readonly record struct SlideShowScreenBounds(double Left, double Top, double Width, double Height, bool IsPrimary);

/// <summary>
/// Framework-neutral decision for where the Presenter View dashboard should appear relative
/// to the audience-facing slideshow window, mirroring PowerPoint's dual-monitor behavior:
/// the presenter's private current/next-slide/notes/timer dashboard must never land on the
/// same screen the audience is looking at when a second display exists to put it on instead.
/// </summary>
public static class PresenterViewPlacementPlanner
{
    /// <summary>
    /// Chooses the screen Presenter View should be placed on, or <c>null</c> when it should
    /// stay on whichever screen the caller's own single-monitor fallback already uses (i.e.
    /// centered over the slideshow window) because no other display exists to move it to.
    ///
    /// With two or more displays, a screen other than <paramref name="slideShowScreen"/> is
    /// always preferred; among those, the primary display is preferred (it is typically the
    /// presenter's own laptop panel, with the slideshow projected to a secondary display) and
    /// otherwise the first non-matching screen in enumeration order is used.
    ///
    /// This is a pure placement decision recomputed by the caller every time Presenter View is
    /// opened (see <c>SlideShowNativePresenterWindowHost&lt;TWindow&gt;.Open</c>), so a monitor
    /// plugged in or removed between shows is picked up the next time Presenter View opens. An
    /// already-open Presenter View window is deliberately NOT relocated live while the monitor
    /// arrangement changes mid-show -- reacting to a live display-change notification would
    /// need a native OS hook (WM_DISPLAYCHANGE on Windows, an X11/Wayland/NSScreen equivalent
    /// elsewhere) that neither shell wires up today, so a monitor unplugged mid-show simply
    /// leaves the already-positioned window where it was (typically still visible, since
    /// Windows/most desktops reflow orphaned windows back onto a surviving display) until the
    /// presenter closes and reopens Presenter View, at which point this method runs again
    /// against the current arrangement.
    /// </summary>
    public static SlideShowScreenBounds? SelectPresenterScreen(
        SlideShowScreenBounds slideShowScreen,
        IReadOnlyList<SlideShowScreenBounds> allScreens)
    {
        ArgumentNullException.ThrowIfNull(allScreens);
        if (allScreens.Count < 2)
            return null;

        SlideShowScreenBounds? firstOther = null;
        foreach (var screen in allScreens)
        {
            if (IsSameScreen(screen, slideShowScreen))
                continue;
            if (screen.IsPrimary)
                return screen;
            firstOther ??= screen;
        }

        return firstOther;
    }

    private static bool IsSameScreen(SlideShowScreenBounds a, SlideShowScreenBounds b) =>
        NearlyEqual(a.Left, b.Left) && NearlyEqual(a.Top, b.Top) &&
        NearlyEqual(a.Width, b.Width) && NearlyEqual(a.Height, b.Height);

    private static bool NearlyEqual(double a, double b) => Math.Abs(a - b) < 0.5;
}
