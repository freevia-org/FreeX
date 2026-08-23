using System.Windows;
using System.Windows.Threading;

namespace Free.Shared.Shell.Wpf;

/// <summary>
/// Runs a bounded, best-effort emergency action for every open WPF window.
/// </summary>
public static class WpfEmergencySnapshotFanOut
{
    internal static readonly TimeSpan DispatcherTimeout = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Invokes <paramref name="snapshotWindow"/> on the application dispatcher for every open
    /// window. This method is safe to call from a crash handler and never throws.
    /// </summary>
    public static void TrySnapshotAllWindows(Action<Window> snapshotWindow)
    {
        try
        {
            var application = Application.Current;
            if (application is null)
                return;

            TrySnapshotAllWindows(
                new WpfApplicationEmergencySnapshotRuntime(application),
                window => snapshotWindow((Window)window),
                DispatcherTimeout);
        }
        catch
        {
            // Emergency snapshot orchestration must never replace the original crash.
        }
    }

    internal static void TrySnapshotAllWindows(
        IWpfEmergencySnapshotRuntime runtime,
        Action<object> snapshotWindow,
        TimeSpan dispatcherTimeout)
    {
        try
        {
            void FanOut()
            {
                try
                {
                    foreach (var window in runtime.GetWindows())
                    {
                        try
                        {
                            snapshotWindow(window);
                        }
                        catch
                        {
                            // One failing window must not prevent the remaining snapshots.
                        }
                    }
                }
                catch
                {
                    // Window enumeration is best-effort during process failure.
                }
            }

            if (runtime.CheckAccess())
            {
                FanOut();
            }
            else
            {
                runtime.Invoke(FanOut, dispatcherTimeout);
            }
        }
        catch
        {
            // Crash handlers must never throw, including dispatcher failures and timeouts.
        }
    }
}

internal interface IWpfEmergencySnapshotRuntime
{
    bool CheckAccess();

    IEnumerable<object> GetWindows();

    void Invoke(Action action, TimeSpan timeout);
}

internal sealed class WpfApplicationEmergencySnapshotRuntime(Application application)
    : IWpfEmergencySnapshotRuntime
{
    public bool CheckAccess() => application.Dispatcher.CheckAccess();

    public IEnumerable<object> GetWindows()
    {
        foreach (Window window in application.Windows)
            yield return window;
    }

    public void Invoke(Action action, TimeSpan timeout) =>
        application.Dispatcher.Invoke(
            action,
            DispatcherPriority.Send,
            System.Threading.CancellationToken.None,
            timeout);
}
