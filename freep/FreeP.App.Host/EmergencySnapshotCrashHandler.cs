using System.Windows;
using System.Windows.Threading;

namespace FreeP.App.Host;

/// <summary>
/// Best-effort emergency-snapshot fan-out wired into the shared WPF runner's crash handler (see
/// <see cref="Free.Shared.Shell.WpfApplicationStartupSpec{TOptions}.OnEmergencySnapshot"/>, set by
/// Program.cs). Split out of Program.cs (rather than inlined there) so Program.cs itself never
/// references <c>Application.Current</c> directly -- Program.cs's job is to stay a thin adapter
/// that only composes shared-runner spec objects; the shared runner and its diagnostics own all
/// direct WPF <c>Application</c>/dispatcher access. Mirrors FreeW's file of the same name.
/// </summary>
internal static class EmergencySnapshotCrashHandler
{
    /// <summary>
    /// Bound on the dispatcher marshal below. A wedged UI thread must degrade to "no snapshot",
    /// never to "the crash handler never returns" -- see the class remarks on FreeW's Avalonia
    /// sibling for why a crash handler that hangs is strictly worse than the data loss it exists
    /// to avoid.
    /// </summary>
    private static readonly TimeSpan DispatcherTimeout = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Best-effort emergency snapshot of every open dirty presentation window. Must never throw.
    ///
    /// <para>
    /// AppDomain.UnhandledException fires on the faulting thread, which may not be the dispatcher
    /// thread. <c>Application.Current.Windows</c> and the autosave coordinator are UI-thread-affine
    /// and will throw from any other thread. We therefore marshal the work via
    /// <c>Dispatcher.Invoke</c> with a short bounded timeout -- and skip the marshal entirely when
    /// already on the dispatcher thread, which is the common reentrant case and would otherwise
    /// deadlock.
    /// </para>
    /// </summary>
    public static void TryEmergencySnapshotAllWindows()
    {
        try
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null)
                return;

            if (dispatcher.CheckAccess())
            {
                TryEmergencySnapshotAllWindowsOnDispatcher();
            }
            else
            {
                dispatcher.Invoke(
                    TryEmergencySnapshotAllWindowsOnDispatcher,
                    DispatcherPriority.Send,
                    System.Threading.CancellationToken.None,
                    DispatcherTimeout);
            }
        }
        catch
        {
            // Outer guard — crash handlers must never throw.
        }
    }

    private static void TryEmergencySnapshotAllWindowsOnDispatcher()
    {
        try
        {
            foreach (Window window in Application.Current.Windows)
            {
                if (window is not MainWindow mainWindow)
                    continue;

                try
                {
                    mainWindow.AutosaveCoordinatorForCrashHandler?.TryEmergencySnapshot();
                }
                catch
                {
                    // A crash handler must never throw.
                }
            }
        }
        catch
        {
            // Outer guard — crash handlers must never throw.
        }
    }
}
