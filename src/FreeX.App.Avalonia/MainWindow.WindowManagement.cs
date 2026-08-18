using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.Shell;
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
//  * HIDE / UNHIDE: hidden workbook windows remain separate from worksheet visibility and from
//    Arrange All. The Window-group Unhide dialog restores one explicitly selected window, matching
//    WPF; Arrange All tiles visible workbook windows only and never changes hidden state.
public sealed partial class MainWindow : Window
{
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

    internal string WindowMenuDisplayName => WorkbookWindowSelectionPlanner.FormatDisplayName(
        _session.Workbook.Name,
        _windowTitleSuffix);

    internal void RefreshWindowVisibilityCommandStates() => _refreshRibbonToggleStates?.Invoke();

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
        _session.SortAdjacentDataPromptResolver = ResolveSortAdjacentDataPrompt;
        _session.WorkbookChanged += Session_WorkbookChanged;
        // R126-avalonia-watch-window-stale-after-open: matches CloseFindReplaceDialogIfOpen above --
        // the modeless Watch Window's RefreshList closure reads _session.Workbook at call time (see
        // ShowWatchWindowDialogAsync), so it silently kept showing the just-discarded workbook's
        // watched cells (a genuinely different, per-Workbook WatchedCells collection) after every
        // File > New/Open/recovery-load until the user manually clicked Add/Refresh/Delete. _session
        // is already reassigned above, so this repopulates the dialog from the new workbook's own
        // (normally empty) watch list -- exactly matching Excel, which drops a workbook's watches the
        // moment that workbook is gone.
        if (_watchWindowDialog is { IsVisible: true })
            _refreshWatchWindow?.Invoke();
        ResetSlicerTimelinePaneState();
        previousSession.Dispose();
        WindowRegistry.RefreshWindowNumbering();
    }

    // view.newWindow
    private void NewWindow()
    {
        var window = new MainWindow(
            App.StartupArguments,
            _session.CreateSiblingView(InitialViewportHeight, InitialViewportWidth),
            _optionsRuntimeSession);
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
    // undo/redo, parity with the WPF host) and then applies the shared registry core's visible-window
    // target/geometry plan. Hidden workbook windows remain untouched until explicitly unhidden.
    private void ArrangeAllWindows(WorkbookWindowArrangement arrangement)
    {
        var (workArea, scaling) = GetPrimaryWorkAreaMetrics();
        var targets = WindowRegistry.PlanVisibleArrangement(
            arrangement,
            AvaloniaWindowBoundsTranslator.PixelsToDips(workArea.Width, scaling),
            AvaloniaWindowBoundsTranslator.PixelsToDips(workArea.Height, scaling));
        if (targets.Count == 0)
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

        ReconcileSideBySideAfterWindowArrangement(targets.Select(target => target.Window));

        var tiles = AvaloniaWindowBoundsTranslator.Translate(
            workArea,
            scaling,
            targets.Select(target => target.Bounds).ToArray());
        for (var index = 0; index < targets.Count; index++)
        {
            var window = targets[index].Window;
            var tile = tiles[index];

            // A maximized/full-screen window cannot be positioned; normalize first.
            window.WindowState = WindowState.Normal;

            window.Position = tile.Position;
            window.Width = Math.Max(window.MinWidth, tile.Width);
            window.Height = Math.Max(window.MinHeight, tile.Height);
        }

        RefreshShell(UiText.Format("WTA_ArrangeAll_Arranged", targets.Count, ArrangementDisplayName(arrangement)));
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
        // Hiding the last visible window would strand the user because the Window-group Unhide
        // command itself is available only from a visible workbook window.
        if (!WindowRegistry.Hide(this))
        {
            RefreshShell(UiText.Get("ShellLoc_CannotHideLastWindow"));
            return;
        }

        SideBySideCoordinator.DisableFor(this);
        WindowRegistry.NotifyVisibilityChanged(this);
        // The hidden window's own status bar is now off-screen; remaining visible
        // windows can recover it via View ▸ Arrange All.
    }

    protected override void OnClosed(EventArgs e)
    {
        // A closing window must drop out of the shared registry so it and its WorkbookSession
        // document graph are no longer retained.
        _session.WorkbookChanged -= Session_WorkbookChanged;
        _fileOperationCancellationSession.Dispose();
        _session.Dispose();
        WindowRegistry.Unregister(this);
        // If this window was part of a side-by-side pair, clear the pair so the partner window
        // is not left broadcasting to a closed window.
        CleanUpSideBySideOnClose();
        base.OnClosed(e);
    }

    private (PixelRect WorkingArea, double Scaling) GetPrimaryWorkAreaMetrics()
    {
        var screens = Screens;
        var screen = screens?.ScreenFromWindow(this) ?? screens?.Primary;

        if (screen is not null)
            return (screen.WorkingArea, screen.Scaling);

        // Fallback when no screen metrics are available (e.g. headless).
        return (new PixelRect(0, 0, 1280, 800), 1);
    }
}
