using System.Threading.Tasks;

namespace Free.Shared.AppServices;

/// <summary>
/// Shared process-wide unhandled-exception wiring for the desktop hosts. Subscribes the three
/// crash sources every host cares about — the UI dispatcher, the <see cref="AppDomain"/>, and
/// unobserved <see cref="Task"/>s — routing each through a host-supplied <c>recordCrash</c> callback
/// tagged with a source string (<c>"dispatcher"</c> / <c>"appdomain"</c> / <c>"task"</c>).
///
/// <para>
/// The dispatcher source is UI-toolkit-specific (WPF on the current hosts), so the host owns that
/// subscription and forwards it via <paramref name="subscribeDispatcher"/>; the neutral
/// <see cref="AppDomain"/> and <see cref="TaskScheduler"/> hooks are subscribed here. An optional
/// <c>onAfterFault</c> step runs after a dispatcher- or appdomain-sourced crash is recorded (FreeX
/// uses it for an emergency snapshot of open windows); it deliberately does <em>not</em> run for the
/// unobserved-task source, matching the existing hosts' behaviour.
/// </para>
/// </summary>
public static class AppCrashHandlers
{
    /// <summary>
    /// Runs the dispatcher diagnostics/emergency-snapshot callback and returns whether the UI
    /// framework should mark the exception handled. Ordinary event/continuation faults are
    /// recoverable at this final boundary; process-wide memory exhaustion is not.
    /// </summary>
    public static bool HandleDispatcherException(
        Exception exception,
        Action<Exception> recordAndSnapshot)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(recordAndSnapshot);

        try
        {
            recordAndSnapshot(exception);
        }
        catch
        {
            // Crash diagnostics and emergency snapshots are best-effort. A secondary failure in
            // either must not replace an otherwise recoverable UI exception or defeat this safety
            // net. OutOfMemoryException remains fatal regardless.
        }

        return exception is not OutOfMemoryException;
    }

    /// <summary>
    /// Wires the dispatcher / appdomain / unobserved-task crash hooks. Safe to call once at startup.
    /// </summary>
    /// <param name="recordCrash">Records a crash for the given (exception, source). Must not throw.</param>
    /// <param name="subscribeDispatcher">
    /// Host hook that subscribes the UI dispatcher's unhandled-exception event, invoking the supplied
    /// handler with the faulting exception. Pass <see langword="null"/> for hosts with no UI dispatcher
    /// available at registration time.
    /// </param>
    /// <param name="onAfterFault">
    /// Optional best-effort step run after a dispatcher- or appdomain-sourced crash is recorded
    /// (e.g. an emergency save). Not invoked for the unobserved-task source. Must not throw.
    /// </param>
    public static void Register(
        Action<Exception, string> recordCrash,
        Action<Action<Exception>>? subscribeDispatcher,
        Action? onAfterFault = null)
    {
        ArgumentNullException.ThrowIfNull(recordCrash);

        subscribeDispatcher?.Invoke(exception =>
        {
            recordCrash(exception, "dispatcher");
            onAfterFault?.Invoke();
        });

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
                recordCrash(exception, "appdomain");
            onAfterFault?.Invoke();
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
            recordCrash(args.Exception, "task");
    }
}
