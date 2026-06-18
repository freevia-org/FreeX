using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Free.Shared.Ribbon.Wpf;

/// <summary>
/// Makes a borderless <see cref="System.Windows.Shell.WindowChrome"/> shell (<c>WindowStyle="None"</c>)
/// maximize to the monitor's WORK AREA rather than the whole monitor. Without this a borderless window
/// covers the taskbar and spills a few pixels past every edge when maximized, which pushes the status bar
/// off-screen. Handling <c>WM_GETMINMAXINFO</c> clamps the maximized size/position to the work area so the
/// full shell (including the footer) stays visible. App-neutral; FreeW and FreeX shells both benefit.
/// </summary>
public static class MaximizedWindowFix
{
    private const int WM_GETMINMAXINFO = 0x0024;
    private const int MONITOR_DEFAULTTONEAREST = 0x00000002;

    /// <summary>Installs the work-area clamp. Call once the HWND exists (e.g. from SourceInitialized).</summary>
    public static void Install(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        HwndSource.FromHwnd(hwnd)?.AddHook(WindowProc);
    }

    private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            ClampToWorkArea(hwnd, lParam);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private static void ClampToWorkArea(IntPtr hwnd, IntPtr lParam)
    {
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
            return;

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info))
            return;

        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        var work = info.rcWork;
        var mon = info.rcMonitor;

        // Position/size the maximized window to the work area, expressed relative to the monitor origin.
        mmi.ptMaxPosition.x = work.left - mon.left;
        mmi.ptMaxPosition.y = work.top - mon.top;
        mmi.ptMaxSize.x = work.right - work.left;
        mmi.ptMaxSize.y = work.bottom - work.top;
        mmi.ptMaxTrackSize.x = work.right - work.left;
        mmi.ptMaxTrackSize.y = work.bottom - work.top;

        Marshal.StructureToPtr(mmi, lParam, fDeleteOld: false);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left; public int top; public int right; public int bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
}
