using System.Windows.Threading;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;

namespace FreeX.App.Host.Tests;

internal static class StaTestRunner
{
    private const string RenderWithoutDisplayDevicesSwitch =
        "Switch.System.Windows.Media.ShouldRenderEvenWhenNoDisplayDevicesAreAvailable";
    private static readonly Lazy<Dispatcher> StaDispatcher = new(CreateDispatcher);
    private static readonly object RunLock = new();
    private const int KeyEventKeyUp = 0x0002;
    private const int KeyStatePressedMask = 0x8000;
    private static readonly byte[] ModifierVirtualKeys =
    [
        0x10, // Shift
        0xA0, // Left Shift
        0xA1, // Right Shift
        0x11, // Control
        0xA2, // Left Control
        0xA3, // Right Control
        0x12, // Alt
        0xA4, // Left Alt
        0xA5 // Right Alt
    ];

    public static void Run(Action action)
    {
        InitializeOffscreenRendering();
        var dispatcher = StaDispatcher.Value;
        if (dispatcher.CheckAccess())
        {
            try
            {
                ReleaseModifierKeys();
                UseSoftwareRendering();
                action();
            }
            finally
            {
                ReleaseModifierKeys();
            }

            return;
        }

        lock (RunLock)
        {
            Exception? exception = null;
            dispatcher.Invoke(() =>
            {
                try
                {
                    ReleaseModifierKeys();
                    UseSoftwareRendering();
                    action();
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
                finally
                {
                    ReleaseModifierKeys();
                }
            });

            if (exception is not null)
                ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }

    private static Dispatcher CreateDispatcher()
    {
        InitializeOffscreenRendering();
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            InitializeOffscreenRendering();
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        ready.Wait();

        return dispatcher ?? throw new InvalidOperationException("STA dispatcher was not created.");
    }

    private static void InitializeOffscreenRendering()
    {
        AppContext.SetSwitch(RenderWithoutDisplayDevicesSwitch, true);
        UseSoftwareRendering();
    }

    private static void UseSoftwareRendering() =>
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

    private static void ReleaseModifierKeys()
    {
        foreach (var virtualKey in ModifierVirtualKeys)
        {
            if ((GetKeyState(virtualKey) & KeyStatePressedMask) != 0)
                keybd_event(virtualKey, 0, KeyEventKeyUp, UIntPtr.Zero);
        }
    }

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, int dwFlags, UIntPtr dwExtraInfo);
}
