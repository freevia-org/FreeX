using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Free.Shared.Shell;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

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
//  * NEW WINDOW: Excel's "New Window" opens a second view of the same workbook. Avalonia uses
//    per-view WorkbookSessions over shared document state plus a local window registry, so model
//    mutations and document state are visible to every sibling while selection/viewport/prompt
//    state remains local and opening/replacing a document detaches one view.
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

    public WorkbookId DocumentId => _session.Workbook.Id;

    internal void ApplyWindowTitleSuffix(string suffix)
    {
        _windowTitleSuffix = suffix ?? string.Empty;
        Title = FormatWindowWorkbookTitle();
    }

    internal void RefreshFromSharedWorkbook()
    {
        if (!IsVisible)
            return;

        if (!_session.IsDirty)
            _autosaveCoordinator?.NotifyAutosaveSaved();
        RefreshShell(_statusText.Text ?? UiText.Get("MainLoc_Ready"));
    }

    internal void ActivateWorkbookWindow()
    {
        if (!IsVisible)
            Show();

        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Activate();
        Focus();
        _sheetGridHost.Focus();
        // Avalonia may restore focus through its X11 focus proxy while Focus() settles.
        // Queue the native top-level activation last so the desktop active window agrees
        // with the managed workbook target after the focus handoff completes.
        X11WindowActivator.Activate(this);
    }

    private void Session_WorkbookChanged(object? sender, EventArgs e)
    {
        WindowRegistry.NotifyWorkbookChanged(this);
    }

    internal void ReplaceSession(WorkbookSession replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (ReferenceEquals(_session, replacement))
            return;

        // R119-avalonia-findreplace-stale-scope: the modeless Find & Replace dialog freezes its
        // selection scope at open time and never re-reads it, so it must not survive a workbook
        // swap -- otherwise it silently reports zero matches forever against the new document's
        // (always-fresh) SheetIds. See CloseFindReplaceDialogIfOpen for the full rationale.
        CloseFindReplaceDialogIfOpen();

        _autosaveCoordinator?.NotifyAutosaveSaved();
        var previousSession = _session;
        previousSession.WorkbookChanged -= Session_WorkbookChanged;
        _session = replacement;
        // Every session replacement (New, Open, recovery-snapshot load) starts a fresh document
        // identity -- any external-modification write-time snapshot captured for the PREVIOUS
        // document must not leak into this one (R116-avalonia-external-modification-detection).
        // OpenWorkbookFromTargetAsync re-populates it immediately afterward for a real file open.
        _currentFileSourceLastWriteTimeUtc = null;
        _session.DataValidationPromptResolver = ResolveDataValidationPrompt;
        _session.WorkbookChanged += Session_WorkbookChanged;
        ResetSlicerTimelinePaneState();
        previousSession.Dispose();
        WindowRegistry.RefreshWindowNumbering();
    }

    internal MainWindow CreateSharedViewForTest() =>
        new(App.StartupArguments, _session.CreateSiblingView(InitialViewportHeight, InitialViewportWidth));

    // view.newWindow
    private void NewWindow()
    {
        var window = new MainWindow(
            App.StartupArguments,
            _session.CreateSiblingView(InitialViewportHeight, InitialViewportWidth));
        var snapshotStore = AutosaveSnapshotStore.CreateDefault(
            PlatformApplicationDataPathProvider.LocalInstance);
        var autosaveCoordinator = new AvaloniaAutosaveCoordinator(window, snapshotStore);
        window.AttachAutosaveCoordinator(autosaveCoordinator);
        window.Closed += (_, _) => autosaveCoordinator.OnWindowClosed();
        autosaveCoordinator.Start();
        window.Show();
        window.Activate();
        RefreshShell(UiText.Get("ShellLoc_OpenedNewWindow"));
    }

    // view.arrangeAll button face — keeps the Excel default (Tiled).
    private void ArrangeAllWindows() => ArrangeAllWindows(WorkbookWindowArrangement.Tiled);

    // View ▸ Window ▸ Arrange All ▸ Tiled / Horizontal / Vertical / Cascade.
    // Each submenu item runs SetWorkbookWindowArrangementCommand (persists the choice with
    // undo/redo, parity with the WPF host) and then positions every visible top-level window
    // using the shared, WPF-free ArrangeAllLayoutPlanner in Free.Shared.Shell. The planner is the
    // single source of arrangement geometry for both the WPF and the cross-platform shells.
    private void ArrangeAllWindows(WorkbookWindowArrangement arrangement)
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
            RefreshShell(UiText.Get("WTA_ArrangeAll_NoWindows"));
            return;
        }

        // Persist the arrangement choice (undo/redo) so it round-trips like the WPF host.
        var stored = _session.ExecuteReviewCommand(new SetWorkbookWindowArrangementCommand(arrangement));
        if (!stored.Success)
        {
            RefreshShell(stored.ErrorMessage ?? UiText.Get("WTA_ArrangeAll_Failed"));
            return;
        }

        var workArea = GetPrimaryWorkArea();
        var bounds = ArrangeAllLayoutPlanner.Arrange(
            (ShellWindowArrangement)arrangement,
            workArea.Width,
            workArea.Height,
            windows.Count);

        if (bounds.Count != windows.Count)
        {
            RefreshShell(UiText.Get("WTA_ArrangeAll_Failed"));
            return;
        }

        for (var index = 0; index < windows.Count; index++)
        {
            var window = windows[index];
            var rect = bounds[index];

            // A maximized/full-screen window cannot be positioned; normalize first.
            window.WindowState = WindowState.Normal;

            window.Position = new PixelPoint(
                workArea.X + (int)rect.X,
                workArea.Y + (int)rect.Y);
            window.Width = Math.Max(window.MinWidth, rect.Width);
            window.Height = Math.Max(window.MinHeight, rect.Height);
        }

        RefreshShell(UiText.Format("WTA_ArrangeAll_Arranged", windows.Count, ArrangementDisplayName(arrangement)));
    }

    private static string ArrangementDisplayName(WorkbookWindowArrangement arrangement) => arrangement switch
    {
        WorkbookWindowArrangement.Horizontal => UiText.Get("WTA_ArrangeAll_Horizontal"),
        WorkbookWindowArrangement.Vertical => UiText.Get("WTA_ArrangeAll_Vertical"),
        WorkbookWindowArrangement.Cascade => UiText.Get("WTA_ArrangeAll_Cascade"),
        _ => UiText.Get("WTA_ArrangeAll_Tiled"),
    };

    // view.hide
    private void HideActiveWindow()
    {
        var visibleCount = AllTopLevelWindows.Count(static w => w.IsVisible);

        // Hiding the last visible window with no on-screen way back would strand the
        // user, since view.unhide restores worksheets, not windows.
        if (visibleCount <= 1)
        {
            RefreshShell(UiText.Get("ShellLoc_CannotHideLastWindow"));
            return;
        }

        if (!HiddenWindows.Contains(this))
            HiddenWindows.Add(this);

        Hide();
        // The hidden window's own status bar is now off-screen; remaining visible
        // windows can recover it via View ▸ Arrange All.
    }

    protected override void OnClosed(EventArgs e)
    {
        // A window hidden via View ▸ Hide must drop out of the static registry when it closes;
        // otherwise the closed window (and its whole WorkbookSession/document graph) leaks for the
        // rest of the session.
        HiddenWindows.Remove(this);
        _session.WorkbookChanged -= Session_WorkbookChanged;
        _session.Dispose();
        WindowRegistry.Unregister(this);
        // If this window was part of a side-by-side pair, clear the pair so the partner window
        // is not left broadcasting to a closed window.
        CleanUpSideBySideOnClose();
        base.OnClosed(e);
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
