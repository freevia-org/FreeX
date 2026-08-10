using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Free.Shared.AppServices;
using FreeX.App.Presentation.Shell;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    // ── Multi-window registry wiring (Excel "New Window" / "Switch Windows") ──
    //
    // Each window owns a renderer-neutral document context (see App.xaml.cs DI); the views of one
    // document created via View > New Window share that
    // context. This partial keeps the WPF glue thin: registration, activation, refresh, and the
    // share/detach transitions; every ordering decision lives in the pure
    // WorkbookWindowOrdering / WorkbookWindowRegistry.

    /// <summary>
    /// Identity of the document this window currently views; the registry scopes refreshes,
    /// title numbering, and dirty-state broadcasts to windows sharing this id.
    /// </summary>
    public WorkbookId DocumentId => _workbook.Id;

    /// <summary>
    /// True when this window was opened as a secondary view of an already-open workbook and must
    /// adopt the shared workbook instead of creating a fresh one in <c>MainWindow_Loaded</c>.
    /// </summary>
    private bool ShouldAdoptSharedWorkbookOnLoad => _adoptSharedWorkbookOnLoad;

    /// <summary>
    /// True when at least one other live window views this window's current document (Excel
    /// "New Window" siblings). Such siblings keep the document alive: replacing this window's
    /// document must detach into a fresh context, and closing this window must neither prompt
    /// to save nor tear the document down.
    /// </summary>
    private bool DocumentSharedWithOtherWindows() =>
        _windowRegistry?.HasOtherWindowsForDocument(this) == true;

    /// <summary>
    /// Splits this window off the document context it shares with "New Window" siblings, right
    /// before it hosts a different document (File &gt; Open / File &gt; New). The siblings keep
    /// the existing command infrastructure and document state — and with them the current
    /// workbook, its undo history, and its dirty flag — while this window continues with fresh
    /// instances, so the incoming document is fully independent (H39).
    /// </summary>
    private void DetachFromSharedDocumentContext()
    {
        _documentContext.CommandStackChanged -= CommandStackChangeNotifier_StackChanged;
        _documentContext = _documentContext.CreateDetached();
        _documentContext.CommandStackChanged += CommandStackChangeNotifier_StackChanged;
    }

    private void RegisterWithWindowRegistry()
    {
        if (_windowRegistry is null)
            return;

        _windowRegistry.Register(this);
        Closed += MainWindow_Closed_UnregisterFromRegistry;
        RefreshViewWindowCommandState();
    }

    private void MainWindow_Closed_UnregisterFromRegistry(object? sender, EventArgs e)
    {
        // Unregister is idempotent: if the window was already pre-unregistered inside
        // MainWindow_Closing (see PrepareActiveWorkbookForFinalClose), this is a no-op.
        _windowRegistry?.Unregister(this);
    }

    /// <summary>
    /// Adopts the shared workbook for a secondary window without recreating it (the injected
    /// document context and session are the originating window's — passed by ViewNewWindowBtn_Click —
    /// so we only need to bind the UI to them).
    /// </summary>
    private void AdoptSharedWorkbook()
    {
        _documentContext.SetCurrentWorkbook(_workbook);
        InvalidateToolbarVisualState();
        // Open on the same sheet as the originating window, if we can find it via
        // the registry.  Fall back to Sheets[0].
        _currentSheetId = ResolveAdoptedSheetId();
        InvalidateNavigationCaches();
        UpdateTitleBar();
        ApplyAdoptedWorksheetSelection();
        RefreshSheetTabs();
        UpdateViewport();
        // Do NOT call MarkWorkbookSaved here.  WorkbookDocumentState is now a shared
        // singleton, so calling MarkSaved in the new window would clear a dirty flag
        // set legitimately by the originating window.  The title bar already reflects
        // the correct shared dirty state via UpdateTitleBar() above.
    }

    /// <summary>
    /// The window that invoked View &gt; New Window to create this window, if any. Set by
    /// <see cref="ViewNewWindowBtn_Click"/> on the newly-constructed window before it is shown, so
    /// <see cref="ResolveAdoptedSheetId"/> can seed the new window from the sheet the user was
    /// actually looking at (Excel: New Window opens as a copy of the invoking window) instead of
    /// guessing from registry (registration) order — which picks the wrong sibling once 3+
    /// windows share a document and are independently navigated to different sheets.
    /// </summary>
    private MainWindow? _newWindowSourceHint;

    /// <summary>Records which window invoked View &gt; New Window to create this window (see <see cref="_newWindowSourceHint"/>).</summary>
    internal void SetNewWindowSourceHint(MainWindow sourceWindow) => _newWindowSourceHint = sourceWindow;

    /// <summary>
    /// Resolves the sheet id that a newly-adopted secondary window should open on. Prefers the
    /// invoking window's current sheet (<see cref="_newWindowSourceHint"/>, i.e. the window whose
    /// View &gt; New Window command actually created this one); falls back to the currently-visible
    /// sheet in any other already-registered window of the same document; falls back to <c>Sheets[0]</c>.
    /// </summary>
    private SheetId ResolveAdoptedSheetId()
    {
        if (_newWindowSourceHint is { } source && source.DocumentId == _workbook.Id)
        {
            var hintedId = source._currentSheetId;
            if (_workbook.GetSheet(hintedId) is not null)
                return hintedId;
        }

        if (_windowRegistry is not null)
        {
            foreach (var win in _windowRegistry.Windows)
            {
                if (win is MainWindow mw && !ReferenceEquals(mw, this) && mw.DocumentId == _workbook.Id)
                {
                    var candidateId = mw._currentSheetId;
                    if (_workbook.GetSheet(candidateId) is not null)
                        return candidateId;
                }
            }
        }

        return _workbook.Sheets.Count > 0 ? _workbook.Sheets[0].Id : _currentSheetId;
    }

    /// <summary>
    /// Seeds this newly-adopted secondary window's active cell/selection from the invoking
    /// window's current state (<see cref="_newWindowSourceHint"/>), matching Excel's View &gt; New
    /// Window, which opens the new window as a live duplicate of the invoking window -- same
    /// sheet, same active cell, same selection -- rather than resetting to A1 (R120-multiwindow
    /// -new-window-selection). Falls back to the shared Sheet's ActiveRow/ActiveCol -- the same
    /// fallback <see cref="ApplyOpenedWorksheetViewState"/> uses for File &gt; Open -- when no
    /// usable source hint is available (e.g. the invoking window has since closed, or moved to a
    /// different document/sheet before this window finished loading).
    /// </summary>
    private void ApplyAdoptedWorksheetSelection()
    {
        if (_newWindowSourceHint is { } source &&
            source.DocumentId == _workbook.Id &&
            source._currentSheetId == _currentSheetId &&
            source._selectionAnchor is { } sourceAnchor &&
            source._selectionCursor is { } sourceCursor &&
            source.SheetGrid.SelectedRange is { } sourceRange)
        {
            var snapshot = new WorksheetSelectionSnapshot(
                sourceAnchor, sourceCursor, sourceRange, source.SheetGrid.SelectedRanges);
            ApplyWorksheetSelectionSnapshot(snapshot);
            return;
        }

        var sheet = _workbook.GetSheet(_currentSheetId);
        var activeRow = sheet?.ActiveRow ?? 1;
        var activeCol = sheet?.ActiveCol ?? 1;
        SetActiveCell(new CellAddress(
            _currentSheetId,
            Math.Clamp(activeRow, 1u, CellAddress.MaxRow),
            Math.Clamp(activeCol, 1u, CellAddress.MaxCol)));
    }

    // ── IWorkbookWindow (driven by WorkbookWindowRegistry) ────────────────────

    /// <summary>Applies the Excel-style window-number suffix and refreshes the title bar.</summary>
    public void ApplyWindowTitleSuffix(string suffix)
    {
        _windowTitleSuffix = suffix ?? string.Empty;
        UpdateTitleBar();
    }

    /// <summary>
    /// Refreshes this window's title bar to reflect the current shared document state
    /// (dirty indicator, file name).  Called by the registry after a dirty/saved transition
    /// so all windows' title bars stay in sync without a full viewport refresh.
    /// </summary>
    public void RefreshTitleBar() => UpdateTitleBar();

    /// <summary>Re-reads the shared workbook into this window's viewport/status after an edit elsewhere.</summary>
    public void RefreshFromSharedWorkbook()
    {
        if (_documentContext.CurrentWorkbook.Id != _workbook.Id)
        {
            // The shared ref was repointed at a different workbook (defensive: the File > Open /
            // File > New paths now detach the opener into its own context instead, so siblings
            // should no longer observe a replacement — but a stale view must still recover).
            // Close our Find/Replace dialog so it cannot operate on a stale workbook.
            InvalidateToolbarVisualState();
            CloseFindReplaceDialogIfOpen();
        }
        _documentContext.SetCurrentWorkbook(_workbook);
        if (_workbook.GetSheet(_currentSheetId) is null && _workbook.Sheets.Count > 0)
            _currentSheetId = _workbook.Sheets[0].Id;

        InvalidateNavigationCaches();
        RefreshSheetTabs();
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
        UpdateTitleBar();
    }

    /// <summary>Brings this window to the foreground (Switch Windows / New Window activation).</summary>
    public void ActivateWindow()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Show();
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    /// <summary>Shows or hides this window (Hide Window / Unhide Window), driven by the registry.</summary>
    public void SetWindowVisible(bool visible)
    {
        if (visible)
            Show();
        else
            Hide();
    }

    /// <summary>The current worksheet scroll position, in scroll-bar units (Synchronous Scrolling).</summary>
    public WorkbookScrollOffset GetScrollOffset() =>
        new(VerticalScroll?.Value ?? 0, HorizontalScroll?.Value ?? 0);

    /// <summary>Applies a scroll position pushed from the paired side-by-side window, without re-broadcasting.</summary>
    public void SetScrollOffset(WorkbookScrollOffset offset)
    {
        if (VerticalScroll is null || HorizontalScroll is null)
            return;

        _suppressScrollBroadcast = true;
        try
        {
            VerticalScroll.Value = Math.Clamp(offset.Row, VerticalScroll.Minimum, VerticalScroll.Maximum);
            HorizontalScroll.Value = Math.Clamp(offset.Column, HorizontalScroll.Minimum, HorizontalScroll.Maximum);
        }
        finally
        {
            _suppressScrollBroadcast = false;
        }
    }

    /// <summary>Restores this window to Normal state and positions it at the given work-area bounds.</summary>
    /// <remarks>
    /// Window layout planners return bounds relative to the work-area origin, so we offset by
    /// <see cref="SystemParameters.WorkArea"/> here (matching Reset Window Position). This keeps the
    /// layout correct when the work area does not start at the screen origin, e.g. a top/left
    /// taskbar or a secondary monitor with non-zero coordinates.
    /// </remarks>
    public void TileToWorkArea(Rect bounds)
    {
        var workArea = SystemParameters.WorkArea;
        WindowState = WindowState.Normal;
        Left = workArea.Left + bounds.Left;
        Top = workArea.Top + bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
    }

    /// <summary>Mirrors this window's scroll position into its side-by-side partner when sync scrolling is on.</summary>
    private void BroadcastScrollOffsetToSideBySidePartner()
    {
        if (_suppressScrollBroadcast)
            return;

        _windowRegistry?.BroadcastScrollOffset(this, GetScrollOffset());
    }

    /// <summary>Broadcasts a workbook change to the other live views of this window's document.</summary>
    private void NotifyOtherWindowsOfWorkbookChange()
    {
        _windowRegistry?.NotifyWorkbookChanged(this);
    }

    // ── Ribbon: View ▸ Window ▸ New Window ────────────────────────────────────

    private void ViewNewWindowBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_windowRegistry is null || App.Services is null)
        {
            RefreshViewWindowCommandState();
            FocusSheetGridIfNeeded();
            return;
        }

        // Construct the new window over THIS window's document context so it becomes a second
        // view of the same document — Excel "New
        // Window". Resolving a plain DI MainWindow would instead create an independent document
        // (see the MainWindow factory in App.ConfigureServices). The ctor sees a registered
        // window (this one) with the same DocumentId and flags itself secondary, so it adopts
        // the shared workbook on load instead of creating a fresh one.
        var siblingSession = _session.CreateSiblingView(
            Math.Max(1, SheetGrid.ActualHeight),
            Math.Max(1, SheetGrid.ActualWidth));
        MainWindow newWindow;
        try
        {
            newWindow = ActivatorUtilities.CreateInstance<MainWindow>(
                App.Services,
                _documentContext,
                siblingSession);
        }
        catch
        {
            siblingSession.Dispose();
            throw;
        }

        // Record that THIS window invoked New Window, before the new window loads/adopts the
        // shared workbook, so ResolveAdoptedSheetId opens it on this window's current sheet —
        // matching Excel, which opens the new window as a copy of the invoking window — rather
        // than on whichever sibling happens to be first in the registry (R90-app-window-arrange
        // -freeze-ui-5-2).
        newWindow.SetNewWindowSourceHint(this);

        // Give the secondary window its own autosave timer + recovery snapshot (same wiring
        // App.xaml.cs performs for the primary window and for crash-recovery windows). Autosave
        // ownership is per-window, not per-workbook: as long as ANY window over the shared
        // workbook stays open, its own timer keeps snapshotting the (shared) dirty state, so
        // closing one window — even the one that opened first — can never leave the still-open
        // shared workbook with zero crash-recovery coverage (J25).
        var autosaveStore = AutosaveSnapshotStore.CreateDefault(
            App.Services.GetRequiredService<IApplicationDataPathProvider>());
        var autosaveService = new AutosaveService(autosaveStore);
        newWindow.AttachAutosaveService(autosaveService, autosaveStore);

        newWindow.Show();
        newWindow.Activate();
        RefreshViewWindowCommandState();
    }

    // ── Ribbon: View ▸ Window ▸ Switch Windows ────────────────────────────────

    private void ViewSwitchWindowsBtn_Click(object sender, RoutedEventArgs e)
    {
        SwitchWorkbookWindow(forward: true);
    }

    private void SwitchWorkbookWindow(bool forward)
    {
        if (_windowRegistry is null)
        {
            RefreshViewWindowCommandState();
            FocusSheetGridIfNeeded();
            return;
        }

        if (forward)
            _windowRegistry.SwitchToNextWindow(this);
        else
            _windowRegistry.SwitchToPreviousWindow(this);

        RefreshViewWindowCommandState();
    }

    // ── Ribbon: View ▸ Window ▸ Hide / Unhide ─────────────────────────────────

    private void SwitchWindowsContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
            return;

        menu.Items.Clear();
        if (_windowRegistry is null)
        {
            menu.Items.Add(new MenuItem
            {
                Header = UiText.Get("MainWindow_Content_SwitchWindows"),
                IsEnabled = false
            });
            return;
        }

        foreach (var target in WorkbookWindowSelectionPlanner.BuildSwitchWindowTargets(
                     BuildWorkbookWindowSelectionEntries(_windowRegistry, _windowRegistry.VisibleWindows),
                     this,
                     _workbook.Name,
                     _windowRegistry.Count))
        {
            var item = new MenuItem
            {
                Header = target.DisplayName,
                IsCheckable = true,
                IsChecked = target.IsCurrent,
                IsEnabled = !target.IsCurrent
            };
            item.Click += (_, _) =>
            {
                target.Window.ActivateWindow();
                RefreshViewWindowCommandState();
            };
            menu.Items.Add(item);
        }

        MenuKeyTipAssigner.AssignUniqueKeyTips(menu.Items.OfType<MenuItem>());
        FocusFirstWorksheetContextMenuItem(menu);
    }

    private void ViewHideWindowBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_windowRegistry is null)
        {
            RefreshViewWindowCommandState();
            FocusSheetGridIfNeeded();
            return;
        }

        if (!_windowRegistry.Hide(this))
            _messageService.ShowWarning(
                UiText.Get("MainWindowMessage_HideWindowMustStayVisible"),
                UiText.Get("MainWindowMessage_HideWindowTitle"));

        RefreshViewWindowCommandState();
    }

    private void ViewUnhideWindowBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_windowRegistry is null)
        {
            RefreshViewWindowCommandState();
            FocusSheetGridIfNeeded();
            return;
        }

        var hidden = _windowRegistry.HiddenWindows;
        if (hidden.Count == 0)
        {
            _messageService.ShowInfo(
                UiText.Get("MainWindowMessage_UnhideNoHiddenWindows"),
                UiText.Get("MainWindowMessage_UnhideWindowTitle"));
            RefreshViewWindowCommandState();
            return;
        }

        var targets = WorkbookWindowSelectionPlanner.BuildUnhideWindowTargets(
            BuildWorkbookWindowSelectionEntries(_windowRegistry, hidden),
            _workbook.Name,
            _windowRegistry.Count);
        var dialog = new UnhideWindowDialog(targets) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            RefreshViewWindowCommandState();
            return;
        }

        if (dialog.Result?.Window is not { } window ||
            !_windowRegistry.Unhide(window))
        {
            _messageService.ShowWarning(
                UiText.Get("MainWindowMessage_UnhideWindowNotHidden"),
                UiText.Get("MainWindowMessage_UnhideWindowTitle"));
        }

        RefreshViewWindowCommandState();
    }

    /// <summary>
    /// "{workbook name}{per-document window suffix}" — the entry text this window contributes to
    /// the Switch Windows / Unhide Window lists. Windows over different documents show their own
    /// workbook names, matching their title bars.
    /// </summary>
    internal string WindowMenuDisplayName =>
        WorkbookWindowSelectionPlanner.FormatDisplayName(_workbook.Name, _windowTitleSuffix);

    private static IReadOnlyList<WorkbookWindowSelectionEntry<IWorkbookWindow>> BuildWorkbookWindowSelectionEntries(
        WorkbookWindowRegistry registry,
        IEnumerable<IWorkbookWindow> windows) =>
        windows
            .Select(window => new WorkbookWindowSelectionEntry<IWorkbookWindow>(
                window,
                registry.IndexOf(window),
                (window as MainWindow)?.WindowMenuDisplayName))
            .ToList();

    // ── Ribbon: View ▸ Window ▸ Reset Window Position ─────────────────────────

    private void ViewResetWindowPositionBtn_Click(object sender, RoutedEventArgs e)
    {
        // Excel: Reset Window Position lives in the View Side by Side group and restores BOTH
        // windows of the active side-by-side pair back to their tiled top/bottom (or left/right)
        // halves, undoing any manual resize/drag made while comparing them -- it never touches an
        // unrelated window and is meaningless without an active pair (R90-app-window-arrange
        // -freeze-ui-5-3). Previously this cascaded/recentered ONLY the clicked window to an
        // unrelated 75%-of-work-area rectangle via WindowResetPositionPlanner, ignoring any
        // side-by-side pairing entirely.
        var workArea = SystemParameters.WorkArea;
        _windowRegistry?.ResetSideBySidePair(workArea.Width, workArea.Height);
        RefreshViewWindowCommandState();
    }

    // ── Ribbon: View ▸ Window ▸ View Side by Side ─────────────────────────────

    private void ViewSideBySideBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_windowRegistry is null)
        {
            SyncViewSideBySideToggleState();
            FocusSheetGridIfNeeded();
            return;
        }

        if (_windowRegistry.IsSideBySideActive)
        {
            _windowRegistry.DisableSideBySide();
        }
        else
        {
            var workArea = SystemParameters.WorkArea;
            if (!_windowRegistry.EnableSideBySide(this, workArea.Width, workArea.Height))
                _messageService.ShowWarning(
                    UiText.Get("MainWindowMessage_SideBySideNeedsSecondWindow"),
                    UiText.Get("MainWindowMessage_SideBySideTitle"));
        }

        RefreshViewWindowCommandState();
    }

    // ── Ribbon: View ▸ Window ▸ Synchronous Scrolling ─────────────────────────

    private void ViewSynchronousScrollingBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_windowRegistry is not null)
            _windowRegistry.SetSynchronousScroll(!_windowRegistry.IsSynchronousScrollActive);

        RefreshViewWindowCommandState();
    }
}
