using System.Runtime.ExceptionServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace Free.Shared.Shell.Avalonia;

/// <summary>
/// macOS UI-loop fault recovery. Avalonia.Native delivers input (key, pointer, text, drag/drop),
/// window events and native-menu actions through MicroCom callbacks; a managed exception thrown
/// synchronously from such a callback (e.g. a non-async <c>KeyDown</c>/<c>PointerPressed</c>/
/// <c>Click</c>/<c>NativeMenuItem.Click</c> handler) bypasses <see cref="Dispatcher.UnhandledException"/>:
/// <c>NativeCallbackBase.RaiseException</c> parks it on the current run-loop frame, cancels the native
/// loop and rethrows it out of <c>Dispatcher.MainLoop</c>, i.e. out of
/// <c>StartWithClassicDesktopLifetime</c>, which used to terminate the process with every unsaved
/// edit. When the desktop is still alive (windows open, no shutdown in progress) this records the
/// fault and re-enters the UI loop instead, matching how dispatcher-delivered faults are handled.
/// </summary>
public static class SisterAvaloniaUiLoopRecovery
{
    public const string UiLoopFaultSource = "ui_loop";

    /// <summary>Upper bound on resumed faults per process so a fault on every frame cannot spin forever.</summary>
    public const int DefaultMaximumRecoveries = 25;

    /// <summary>
    /// Production hook for <see cref="SisterAvaloniaApplicationStartupSpec.RecoverFromUiLoopFault"/>:
    /// a no-op (null) off macOS or when the Avalonia desktop is not in a resumable state.
    /// </summary>
    public static int? TryResumeAfterFault(Exception fault, Action<Exception, string> recordCrash)
    {
        ArgumentNullException.ThrowIfNull(fault);
        ArgumentNullException.ThrowIfNull(recordCrash);

        if (!OperatingSystem.IsMacOS() ||
            Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime lifetime)
        {
            return null;
        }

        int? exitCode = null;
        using var exitRequested = new CancellationTokenSource();
        void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs args)
        {
            exitCode = args.ApplicationExitCode;
            exitRequested.Cancel();
        }

        lifetime.Exit += OnExit;
        try
        {
            return ResumeLoop(
                fault,
                canResume: () => CanResume(lifetime),
                runLoop: () =>
                {
                    Dispatcher.UIThread.MainLoop(exitRequested.Token);
                    return exitCode ?? Environment.ExitCode;
                },
                recordCrash,
                DefaultMaximumRecoveries);
        }
        finally
        {
            lifetime.Exit -= OnExit;
        }
    }

    /// <summary>
    /// Display-independent recovery policy. Returns null (caller records and rethrows the original
    /// fault) when <paramref name="firstFault"/> is not resumable; otherwise records each resumed fault
    /// under <see cref="UiLoopFaultSource"/> and re-enters <paramref name="runLoop"/>, returning its exit
    /// code once the loop ends normally. A later fault that cannot be resumed is recorded and rethrown.
    /// </summary>
    public static int? ResumeLoop(
        Exception firstFault,
        Func<bool> canResume,
        Func<int> runLoop,
        Action<Exception, string> recordCrash,
        int maximumRecoveries)
    {
        ArgumentNullException.ThrowIfNull(firstFault);
        ArgumentNullException.ThrowIfNull(canResume);
        ArgumentNullException.ThrowIfNull(runLoop);
        ArgumentNullException.ThrowIfNull(recordCrash);

        var fault = firstFault;
        var recoveries = 0;
        while (true)
        {
            if (!IsResumable(fault) || recoveries >= maximumRecoveries || !SafeCanResume(canResume))
            {
                if (ReferenceEquals(fault, firstFault))
                    return null;

                SafeRecord(recordCrash, fault, UiLoopFaultSource);
                ExceptionDispatchInfo.Throw(fault);
            }

            recoveries++;
            SafeRecord(recordCrash, fault, UiLoopFaultSource);
            try
            {
                return runLoop();
            }
            catch (Exception next)
            {
                fault = next;
            }
        }
    }

    public static bool IsResumable(Exception exception) =>
        exception is not (OutOfMemoryException or InsufficientExecutionStackException) &&
        !SisterAvaloniaDisplayReadinessGate.IsDisplayUnavailableStartupFailure(exception);

    private static bool CanResume(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        // A shutdown closes every window before the dispatcher stops, so "main window still open" also
        // rules out resuming a loop that was already being torn down.
        return Dispatcher.UIThread.CheckAccess() &&
            lifetime.MainWindow is not null &&
            lifetime.Windows.Count > 0;
    }

    private static bool SafeCanResume(Func<bool> canResume)
    {
        try
        {
            return canResume();
        }
        catch
        {
            return false;
        }
    }

    private static void SafeRecord(Action<Exception, string> recordCrash, Exception exception, string source)
    {
        try
        {
            recordCrash(exception, source);
        }
        catch
        {
            // Crash diagnostics are best-effort.
        }
    }
}
