using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace FreeX.App.Host;

public partial class MainWindow
{
    // WPF derives the title-bar and taskbar icons from Window.Icon. When the source .ico contains a
    // 256x256 frame, the shell occasionally fails to produce the large icon it needs and falls back
    // to the generic default ("every now and then" the taskbar icon turns blank). We defend against
    // this by loading explicit small/big HICONs from FreeX.ico at the exact sizes the shell asks for
    // (so the 256x256 frame is never used for the taskbar) and pinning them via WM_SETICON, then
    // re-asserting them after a DPI/monitor change where the regression is most likely to surface.

    private const int WM_SETICON = 0x0080;
    private const int WM_DPICHANGED = 0x02E0;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x0010;
    private const int SM_CXICON = 11;
    private const int SM_CYICON = 12;
    private const int SM_CXSMICON = 49;
    private const int SM_CYSMICON = 50;

    private IntPtr _smallWindowIcon;
    private IntPtr _bigWindowIcon;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>
    /// Pins the native window icons (title bar + taskbar) by loading explicit HICONs from the
    /// on-disk FreeX.ico at the system small/large icon sizes and applying them via WM_SETICON.
    /// Safe to call repeatedly; old handles are destroyed when replaced.
    /// </summary>
    private void EnsureNativeWindowIcons()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        var iconPath = Path.Combine(
            AppContext.BaseDirectory,
            "Resources",
            App.ActiveTheme.VisualAssets.WindowsIconFileName);
        if (!File.Exists(iconPath))
            return;

        ApplyWindowIcon(hwnd, iconPath, ICON_SMALL, GetSystemMetrics(SM_CXSMICON), GetSystemMetrics(SM_CYSMICON), ref _smallWindowIcon);
        ApplyWindowIcon(hwnd, iconPath, ICON_BIG, GetSystemMetrics(SM_CXICON), GetSystemMetrics(SM_CYICON), ref _bigWindowIcon);
    }

    private static void ApplyWindowIcon(IntPtr hwnd, string iconPath, int iconKind, int width, int height, ref IntPtr current)
    {
        if (width <= 0 || height <= 0)
            return;

        var icon = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, width, height, LR_LOADFROMFILE);
        if (icon == IntPtr.Zero)
            return;

        SendMessage(hwnd, WM_SETICON, (IntPtr)iconKind, icon);

        if (current != IntPtr.Zero)
            DestroyIcon(current);
        current = icon;
    }

    private void ReleaseNativeWindowIcons()
    {
        if (_smallWindowIcon != IntPtr.Zero)
        {
            DestroyIcon(_smallWindowIcon);
            _smallWindowIcon = IntPtr.Zero;
        }

        if (_bigWindowIcon != IntPtr.Zero)
        {
            DestroyIcon(_bigWindowIcon);
            _bigWindowIcon = IntPtr.Zero;
        }
    }
}
