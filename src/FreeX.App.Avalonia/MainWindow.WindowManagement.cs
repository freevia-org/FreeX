using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;

namespace FreeX.App.Avalonia;

// Windows-parity "Window" group commands for the View tab:
//   view.newWindow  -> NewWindow()
//   view.arrangeAll -> ArrangeAllWindows()
//   view.hide       -> HideActiveWindow()
//
// Avalonia's classic-desktop lifetime fully supports multiple top-level Windows
// (Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime
// exposes a live Windows collection), so these are genuinely feasible rather than
// emulated.
//
// IMPORTANT HONESTY NOTES:
//  * NEW WINDOW: Excel's "New Window" opens a second *view of the same workbook*.
//    The current MainWindow constructor (MainWindow.cs:573 / :581) always creates
//    its own WorkbookSession internally (_session = _sessionFactory.Create(...));
//    there is no constructor overload that accepts a shared session. Because this
//    file is forbidden from editing existing files, New Window here opens a new
//    independent top-level window with its own fresh document. It is therefore a
//    NEW WINDOW, not a synchronized second view of the active workbook. A separate
//    deliverable proposes the ctor change required to make it a true shared-session
//    second view; until that lands centrally, the behaviour is documented honestly
//    to the user via the status message.
//  * HIDE: view.unhide is already wired (MainWindow.cs:732) but it maps to
//    UnhideSheetAsync() -- that restores a hidden *worksheet*, NOT a hidden window.
//    So there is no existing window-restore path to stay consistent with. To avoid
//    stranding the user with an unrecoverable hidden window, Hide records hidden
//    windows in a static registry and ArrangeAllWindows() re-shows every hidden
//    window before tiling. Thus "Arrange All" is the reliable way back from "Hide",
//    and Hide refuses to hide the last remaining visible window.
public sealed partial class MainWindow : Window
{
    // Tracks windows hidden via HideActiveWindow so ArrangeAllWindows can restore them.
    // Static so any visible window's "Arrange All" can recover windows hidden elsewhere.
    private static readonly List<Window> HiddenWindows = new();

    private IClassicDesktopStyleApplicationLifetime? DesktopLifetime =>
        Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;

    private IReadOnlyList<Window> AllTopLevelWindows
    {
        get
        {
            var windows = DesktopLifetime?.Windows;
            return windows is null ? Array.Empty<Window>() : windows.ToArray();
        }
    }

    // view.newWindow
    private void NewWindow()
    {
        // Opens a new independent top-level window (own fresh workbook). See the
        // file header: this is a new window, not a synchronized second view of the
        // active workbook, because the MainWindow ctor does not accept a shared
        // WorkbookSession.
        var window = new MainWindow(App.StartupArguments);
        window.Show();
        window.Activate();
        RefreshShell("Opened a new window (independent workbook).");
    }

    // view.arrangeAll
    private void ArrangeAllWindows()
    {
        // First, restore anything previously hidden so "Arrange All" is the reliable
        // way back from "Hide".
        if (HiddenWindows.Count > 0)
        {
            foreach (var hidden in HiddenWindows.ToArray())
            {
                if (!hidden.IsVisible)
                    hidden.Show();
            }

            HiddenWindows.Clear();
        }

        var windows = AllTopLevelWindows
            .Where(static w => w.IsVisible)
            .ToList();

        if (windows.Count == 0)
        {
            RefreshShell("No windows to arrange.");
            return;
        }

        var workArea = GetPrimaryWorkArea();

        // Simple grid tile: as square as possible.
        var columns = (int)Math.Ceiling(Math.Sqrt(windows.Count));
        var rows = (int)Math.Ceiling(windows.Count / (double)columns);

        var tileWidth = workArea.Width / columns;
        var tileHeight = workArea.Height / rows;

        for (var index = 0; index < windows.Count; index++)
        {
            var window = windows[index];
            var column = index % columns;
            var row = index / columns;

            // A maximized/full-screen window cannot be positioned; normalize first.
            window.WindowState = WindowState.Normal;

            window.Position = new PixelPoint(
                workArea.X + (column * tileWidth),
                workArea.Y + (row * tileHeight));
            window.Width = Math.Max(window.MinWidth, tileWidth);
            window.Height = Math.Max(window.MinHeight, tileHeight);
        }

        RefreshShell($"Arranged {windows.Count} window(s).");
    }

    // view.hide
    private void HideActiveWindow()
    {
        var visibleCount = AllTopLevelWindows.Count(static w => w.IsVisible);

        // Hiding the last visible window with no on-screen way back would strand the
        // user, since view.unhide restores worksheets, not windows.
        if (visibleCount <= 1)
        {
            RefreshShell("Cannot hide the last visible window. Open a new window first.");
            return;
        }

        if (!HiddenWindows.Contains(this))
            HiddenWindows.Add(this);

        Hide();
        // The hidden window's own status bar is now off-screen; remaining visible
        // windows can recover it via View ▸ Arrange All.
    }

    private PixelRect GetPrimaryWorkArea()
    {
        var screens = Screens;
        var screen = screens?.ScreenFromWindow(this) ?? screens?.Primary;

        if (screen is not null)
            return screen.WorkingArea;

        // Fallback when no screen metrics are available (e.g. headless).
        return new PixelRect(0, 0, 1280, 800);
    }
}
