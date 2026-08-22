using System.Windows;
using Free.Shared.Shell.Wpf;

namespace FreeW.App.Host;

/// <summary>
/// Best-effort emergency-snapshot fan-out wired into the shared WPF runner's crash handler (see
/// <see cref="Free.Shared.Shell.WpfApplicationStartupSpec{TOptions}.OnEmergencySnapshot"/>, set by
/// Program.cs). Split out of Program.cs (rather than inlined there) so Program.cs itself never
/// references <c>Application.Current</c> directly -- Program.cs's job is to stay a thin adapter
/// that only composes shared-runner spec objects; the shared runner and its diagnostics own all
/// direct WPF <c>Application</c>/dispatcher access (see
/// <c>SharedWpfStartupRunnerTests.SisterAppPrograms_UseSharedWpfStartupRunner</c>, which pins that
/// contract).
/// </summary>
internal static class EmergencySnapshotCrashHandler
{
    /// <summary>
    /// Best-effort emergency snapshot of every open dirty document window. Must never throw.
    ///
    /// <para>
    /// AppDomain.UnhandledException fires on the faulting thread, which may not be the dispatcher
    /// thread. <c>Application.Current.Windows</c> and the autosave coordinator are UI-thread-affine
    /// and will throw from any other thread. We therefore marshal the work via
    /// <c>Dispatcher.Invoke</c> with a short bounded timeout -- mirrors FreeX's App.xaml.cs
    /// TryEmergencySaveAllWindows. If the UI thread is itself wedged the marshal times out and the
    /// save silently does not happen -- that is the correct best-effort outcome rather than
    /// crashing the crash handler.
    /// </para>
    /// </summary>
    public static void TryEmergencySnapshotAllWindows()
    {
        WpfEmergencySnapshotFanOut.TrySnapshotAllWindows(window =>
        {
            if (window is MainWindow mainWindow)
                mainWindow.AutosaveCoordinatorForCrashHandler?.TryEmergencySnapshot();
        });
    }
}
