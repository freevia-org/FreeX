using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    // ── Multi-window registry wiring (Excel "New Window" / "Switch Windows") ──
    //
    // All windows share the one workbook + command bus + recalc engine (see App.xaml.cs DI).
    // This partial keeps the WPF glue thin: registration, activation, and refresh; every ordering
    // decision lives in the pure WorkbookWindowOrdering / WorkbookWindowRegistry.

    /// <summary>
    /// True when this window was opened as a secondary view of an already-open workbook and must
    /// adopt the shared workbook instead of creating a fresh one in <c>MainWindow_Loaded</c>.
    /// </summary>
    private bool ShouldAdoptSharedWorkbookOnLoad => _adoptSharedWorkbookOnLoad;

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
        _windowRegistry?.Unregister(this);
    }

    /// <summary>
    /// Adopts the shared workbook for a secondary window without recreating it (the injected
    /// workbook/ref are already the shared singletons; we only need to bind the UI to them).
    /// </summary>
    private void AdoptSharedWorkbook()
    {
        _workbook = _workbookRef.Current;
        _currentSheetId = _workbook.Sheets[0].Id;
        InvalidateNavigationCaches();
        UpdateTitleBar();
        SetActiveCell(new CellAddress(_currentSheetId, 1, 1));
        RefreshSheetTabs();
        RefreshToolbar();
        UpdateViewport();
        RefreshStatusBar();
        MarkWorkbookSaved();
    }

    // ── IWorkbookWindow (driven by WorkbookWindowRegistry) ────────────────────

    /// <summary>Applies the Excel-style window-number suffix and refreshes the title bar.</summary>
    public void ApplyWindowTitleSuffix(string suffix)
    {
        _windowTitleSuffix = suffix ?? string.Empty;
        UpdateTitleBar();
    }

    /// <summary>Re-reads the shared workbook into this window's viewport/status after an edit elsewhere.</summary>
    public void RefreshFromSharedWorkbook()
    {
        _workbook = _workbookRef.Current;
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

    /// <summary>Restores this window to Normal state and positions it at the given work-area bounds (View Side by Side).</summary>
    /// <remarks>
    /// <see cref="SideBySideLayoutPlanner"/> returns bounds relative to the work-area origin, so we
    /// offset by <see cref="SystemParameters.WorkArea"/> here (matching Reset Window Position). This
    /// keeps the tiling correct when the work area does not start at the screen origin, e.g. a
    /// top/left taskbar or a secondary monitor with non-zero coordinates.
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

    /// <summary>Broadcasts a shared-workbook change to every other live window.</summary>
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

        // Resolve a brand-new MainWindow from DI. Because the registry already has windows, the new
        // window's ctor flags itself secondary and adopts the shared workbook instead of replacing it.
        var newWindow = App.Services.GetRequiredService<MainWindow>();
        newWindow.Show();
        newWindow.Activate();
        RefreshViewWindowCommandState();
    }

    // ── Ribbon: View ▸ Window ▸ Switch Windows ────────────────────────────────

    private void ViewSwitchWindowsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_windowRegistry is null)
        {
            RefreshViewWindowCommandState();
            FocusSheetGridIfNeeded();
            return;
        }

        _windowRegistry.SwitchToNextWindow(this);
        RefreshViewWindowCommandState();
    }

    // ── Ribbon: View ▸ Window ▸ Hide / Unhide ─────────────────────────────────

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
                "At least one workbook window must stay visible.",
                "Hide Window");

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
                "There are no hidden workbook windows to unhide.",
                "Unhide Window");
            RefreshViewWindowCommandState();
            return;
        }

        _windowRegistry.Unhide(hidden[0]);
        RefreshViewWindowCommandState();
    }

    // ── Ribbon: View ▸ Window ▸ Reset Window Position ─────────────────────────

    private void ViewResetWindowPositionBtn_Click(object sender, RoutedEventArgs e)
    {
        var workArea = SystemParameters.WorkArea;
        var index = _windowRegistry?.IndexOf(this) ?? 0;
        var bounds = WindowResetPositionPlanner.Compute(workArea.Width, workArea.Height, index);

        WindowState = WindowState.Normal;
        Left = workArea.Left + bounds.Left;
        Top = workArea.Top + bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
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
                    "View Side by Side needs a second visible workbook window.",
                    "View Side by Side");
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
