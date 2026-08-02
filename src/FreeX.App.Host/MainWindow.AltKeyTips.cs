using System.Windows.Interop;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_ENTERSIZEMOVE = 0x0231;
    private const int WM_EXITSIZEMOVE = 0x0232;

    private HwndSource? _keyTipHwndSource;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _keyTipHwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _keyTipHwndSource?.AddHook(MainWindow_WndProc);

        // Pin the taskbar/title-bar icon so it cannot fall back to the generic default.
        EnsureNativeWindowIcons();
    }

    protected override void OnClosed(EventArgs e)
    {
        _keyTipHwndSource?.RemoveHook(MainWindow_WndProc);
        _keyTipHwndSource = null;
        ReleaseNativeWindowIcons();

        base.OnClosed(e);
    }

    private IntPtr MainWindow_WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_ENTERSIZEMOVE)
        {
            _isInWindowResizeMoveLoop = true;
            _ribbonResizeCompactionPendingOnExit = false;
            _resizeViewportRefreshTimer?.Stop();
        }
        else if (msg == WM_EXITSIZEMOVE && _isInWindowResizeMoveLoop)
        {
            _isInWindowResizeMoveLoop = false;
            CompleteRibbonResizeCompaction();
            if (_resizeViewportRefreshPending)
                CompleteViewportResizeRefresh();
            else
                SheetGrid.IsLiveResizing = false;
        }

        else if (msg == WM_DPICHANGED)
        {
            // A DPI/monitor change is the most common moment the shell drops back to the generic
            // taskbar icon; re-pin the correctly-sized icons after the window has moved.
            Dispatcher.BeginInvoke(new Action(EnsureNativeWindowIcons), System.Windows.Threading.DispatcherPriority.Background);

            // A visible chartsheet's raster bitmap is baked at the OLD monitor's DPI scale
            // (RenderActiveChartsheet reads VisualTreeHelper.GetDpi once and never revisits it).
            // Dragging the window to a differently-scaled monitor without resizing never fires
            // ChartsheetView_SizeChanged, so the bitmap would otherwise stay stale/blurry until a
            // manual resize or a sheet-switch away and back. Defer to Background so this runs after
            // WPF's own per-monitor-DPI layout pass has updated GetDpi(ChartsheetView) for the new
            // monitor.
            Dispatcher.BeginInvoke(new Action(RefreshChartsheetForDpiChange), System.Windows.Threading.DispatcherPriority.Background);
        }

        if (msg is WM_KEYDOWN or WM_SYSKEYDOWN &&
            !StandaloneAltKeyTipTracker.IsAltVirtualKey(wParam.ToInt32()))
        {
            _standaloneAltKeyTipTracker.CancelStandaloneAltCandidate();
        }

        return IntPtr.Zero;
    }
}
