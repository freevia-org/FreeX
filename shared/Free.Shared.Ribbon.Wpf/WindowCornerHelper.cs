using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Free.Shared.Ribbon.Wpf;

/// <summary>
/// Applies the Windows 11 rounded-corner DWM preference to a window. A borderless shell
/// (<c>WindowStyle="None"</c> + <see cref="System.Windows.Shell.WindowChrome"/>) does not automatically
/// inherit the OS rounded frame the way a standard window does, so a code-built app shell looks squared
/// off next to native windows. Calling <see cref="ApplyRoundedCorners"/> once the HWND exists asks DWM to
/// round the frame, matching the FreeX look. No-op on Windows 10 / Server (the attribute is unknown there).
/// </summary>
public static class WindowCornerHelper
{
    // DWMWA_WINDOW_CORNER_PREFERENCE (Windows 11 build 22000+). Value DWMWCP_ROUND rounds the frame.
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>
    /// Requests Win11 rounded corners for <paramref name="window"/>. Safe to call from
    /// <see cref="Window.SourceInitialized"/> onward (the HWND must exist). Silently does nothing if the
    /// handle is not yet created or the OS predates the corner-preference attribute.
    /// </summary>
    public static void ApplyRoundedCorners(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        int preference = DWMWCP_ROUND;
        try
        {
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
        }
        catch (DllNotFoundException)
        {
            // dwmapi.dll missing (non-desktop SKU) — leave the frame as-is.
        }
        catch (EntryPointNotFoundException)
        {
            // Older OS without the attribute — leave the frame as-is.
        }
    }
}
