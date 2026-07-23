using System.Windows.Threading;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;

namespace FreeX.App.Host.Tests;

internal static class StaTestRunner
{
    private static readonly Lazy<Dispatcher> StaDispatcher = new(CreateDispatcher);
    private static readonly object RunLock = new();
    private static readonly Mutex ClipboardRunMutex = new(
        initiallyOwned: false,
        "Local\\FreeX.App.Host.Tests.WindowsClipboard");
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
        var dispatcher = StaDispatcher.Value;
        if (dispatcher.CheckAccess())
        {
            try
            {
                ReleaseModifierKeys();
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

    public static void RunClipboardIsolated(Action action)
    {
        var ownsMutex = false;
        try
        {
            try
            {
                ownsMutex = ClipboardRunMutex.WaitOne(TimeSpan.FromMinutes(2));
            }
            catch (AbandonedMutexException)
            {
                ownsMutex = true;
            }

            if (!ownsMutex)
                throw new TimeoutException("Timed out waiting for the shared Windows clipboard test lock.");

            Run(() =>
            {
                ResetClipboard();
                try
                {
                    action();
                }
                finally
                {
                    ResetClipboard();
                }
            });
        }
        finally
        {
            if (ownsMutex)
                ClipboardRunMutex.ReleaseMutex();
        }
    }

    private static Dispatcher CreateDispatcher()
    {
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
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

    private static void ReleaseModifierKeys()
    {
        foreach (var virtualKey in ModifierVirtualKeys)
        {
            if ((GetKeyState(virtualKey) & KeyStatePressedMask) != 0)
                keybd_event(virtualKey, 0, KeyEventKeyUp, UIntPtr.Zero);
        }
    }

    private static void ResetClipboard()
    {
        const int attempts = 10;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Clipboard.Clear();
                Clipboard.Flush();
                return;
            }
            catch (ExternalException) when (attempt < attempts)
            {
                Thread.Sleep(10);
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, int dwFlags, UIntPtr dwExtraInfo);
}
