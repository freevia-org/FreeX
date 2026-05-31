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
}
