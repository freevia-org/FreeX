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

    /// <summary>
    /// Runs out any work a previous test queued but never dispatched.
    ///
    /// <para>
    /// Every test in this assembly shares one STA dispatcher (see <see cref="StaDispatcher"/>) and
    /// parallelisation is disabled, so a test that returns while items are still queued leaves them
    /// to run inside whichever test starts next. That is not theoretical: it showed up as a stray
    /// keystroke landing in the next test's formula bar, an extra entry on its undo stack, and its
    /// selection moving on its own -- symptoms that vanish when the test is run alone, which is what
    /// made them look like ordering bugs.
    /// </para>
    ///
    /// <para>
    /// This deliberately does NOT close windows a test left open. Doing that trips WPF's default
    /// OnLastWindowClose shutdown and takes the whole assembly down with it; see
    /// docs/known-issues/FREEX-HOST-KEYTIP-TEST-INSTABILITY-2026-08-20.md. Draining the queue is
    /// enough, and is bounded so a test that queues work perpetually cannot hang the run.
    /// </para>
    /// </summary>
    private static void DrainPendingDispatcherWork()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var deadline = Environment.TickCount64 + 2000;

        while (Environment.TickCount64 < deadline)
        {
            var frame = new DispatcherFrame();
            var pending = true;
            dispatcher.BeginInvoke(
                DispatcherPriority.SystemIdle,
                new Action(() =>
                {
                    pending = false;
                    frame.Continue = false;
                }));
            Dispatcher.PushFrame(frame);

            // Reaching SystemIdle means nothing of higher priority is left waiting.
            if (!pending)
                return;
        }
    }

    public static void Run(Action action)
    {
        var dispatcher = StaDispatcher.Value;
        if (dispatcher.CheckAccess())
        {
            try
            {
                ReleaseModifierKeys();
                DrainPendingDispatcherWork();
                action();
            }
            finally
            {
                ReleaseModifierKeys();
                DrainPendingDispatcherWork();
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
                    DrainPendingDispatcherWork();
                    action();
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
                finally
                {
                    ReleaseModifierKeys();
                    DrainPendingDispatcherWork();
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

            RunOnDedicatedSta(() =>
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

    private static void RunOnDedicatedSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                ReleaseModifierKeys();
                DrainPendingDispatcherWork();
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            finally
            {
                ReleaseModifierKeys();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        if (exception is not null)
            ExceptionDispatchInfo.Capture(exception).Throw();
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
