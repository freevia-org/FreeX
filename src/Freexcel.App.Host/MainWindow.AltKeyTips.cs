using System.Windows.Interop;

namespace Freexcel.App.Host;

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
    }

    protected override void OnClosed(EventArgs e)
    {
        _keyTipHwndSource?.RemoveHook(MainWindow_WndProc);
        _keyTipHwndSource = null;

        base.OnClosed(e);
    }

    private IntPtr MainWindow_WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_ENTERSIZEMOVE)
        {
            _isInWindowResizeMoveLoop = true;
            _resizeViewportRefreshPending = true;
            _resizeViewportRefreshTimer?.Stop();
            SheetGrid.IsLiveResizing = true;
        }
        else if (msg == WM_EXITSIZEMOVE && _isInWindowResizeMoveLoop)
        {
            _isInWindowResizeMoveLoop = false;
            CompleteViewportResizeRefresh();
        }

        if (msg is WM_KEYDOWN or WM_SYSKEYDOWN &&
            !StandaloneAltKeyTipTracker.IsAltVirtualKey(wParam.ToInt32()))
        {
            _standaloneAltKeyTipTracker.CancelStandaloneAltCandidate();
        }

        return IntPtr.Zero;
    }
}
