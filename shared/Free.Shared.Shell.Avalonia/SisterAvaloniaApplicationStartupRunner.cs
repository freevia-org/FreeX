using Free.Shared.Ribbon;

namespace Free.Shared.Shell.Avalonia;

/// <summary>
/// Product-owned inputs for the common Avalonia application-lifetime boundary. Argument parsing,
/// product identity, application configuration, and window construction remain with each host.
/// </summary>
public sealed record SisterAvaloniaApplicationStartupSpec(
    Func<string[], int> StartApplication,
    Action RegisterUnhandledExceptionHandlers,
    Action<Exception, string> RecordCrash)
{
    public Action<Action<Exception, string>> RegisterRibbonCommandFaultHandler { get; init; } =
        handler => RibbonCommandFaultReporter.Handler = handler;

    public Action? BeforeRun { get; init; }

    public Action<int>? AfterRun { get; init; }

    public int? CompletedExitCode { get; init; }

    public string StartupCrashSource { get; init; } = SisterAvaloniaApplicationStartupRunner.StartupCrashSource;

    /// <summary>
    /// Optional pre-start display gate (macOS: wait for a display the Avalonia.Native render timer can
    /// attach to). Null skips the gate; <see cref="SisterAvaloniaDisplayGateOutcome.Exit"/> returns
    /// <see cref="SisterAvaloniaDisplayReadinessGate.DisplayUnavailableExitCode"/> without starting.
    /// </summary>
    public Func<SisterAvaloniaDisplayGateOutcome>? WaitForDisplay { get; init; }

    /// <summary>
    /// Optional recovery for a fault that escaped the running UI loop (macOS Avalonia.Native callback
    /// faults bypass the dispatcher's unhandled-exception event). Receives the fault and the crash
    /// recorder; returns the final lifetime exit code after resuming, or null to record and rethrow.
    /// </summary>
    public Func<Exception, Action<Exception, string>, int?>? RecoverFromUiLoopFault { get; init; }

    /// <summary>Best-effort sink for startup notices that are not crashes.</summary>
    public Action<string> LogStartupNotice { get; init; } = SisterAvaloniaDisplayReadinessGate.Log;
}

/// <summary>
/// Owns the crash-handler, command-fault, and application-run ceremony shared by the Avalonia
/// product entry points after each product has completed its own launch preparation.
/// </summary>
public static class SisterAvaloniaApplicationStartupRunner
{
    public const string StartupCrashSource = "avalonia_startup";

    public const string RibbonCommandCrashSourcePrefix = "ribbon_command:";

    public static int Run(string[] startupArguments, SisterAvaloniaApplicationStartupSpec spec)
    {
        ArgumentNullException.ThrowIfNull(startupArguments);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(spec.StartApplication);
        ArgumentNullException.ThrowIfNull(spec.RegisterUnhandledExceptionHandlers);
        ArgumentNullException.ThrowIfNull(spec.RecordCrash);
        ArgumentNullException.ThrowIfNull(spec.RegisterRibbonCommandFaultHandler);
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.StartupCrashSource);

        spec.RegisterUnhandledExceptionHandlers();
        spec.RegisterRibbonCommandFaultHandler((exception, commandId) =>
            spec.RecordCrash(exception, RibbonCommandCrashSourcePrefix + commandId));
        spec.BeforeRun?.Invoke();

        if (spec.WaitForDisplay?.Invoke() == SisterAvaloniaDisplayGateOutcome.Exit)
            return SisterAvaloniaDisplayReadinessGate.DisplayUnavailableExitCode;

        try
        {
            var lifetimeExitCode = spec.StartApplication(startupArguments);
            spec.AfterRun?.Invoke(lifetimeExitCode);
            return spec.CompletedExitCode ?? lifetimeExitCode;
        }
        catch (Exception ex)
        {
            // Resume in the catch body (never an exception filter: the UI loop must not run before the
            // faulting frames have unwound). A later unrecoverable fault propagates from here, recorded.
            if (spec.RecoverFromUiLoopFault?.Invoke(ex, spec.RecordCrash) is { } resumedExitCode)
            {
                spec.AfterRun?.Invoke(resumedExitCode);
                return spec.CompletedExitCode ?? resumedExitCode;
            }

            spec.RecordCrash(ex, spec.StartupCrashSource);
            if (SisterAvaloniaDisplayReadinessGate.IsDisplayUnavailableStartupFailure(ex))
            {
                // The display slept between the gate's probe and Avalonia's own render-timer setup.
                // Avalonia cannot be re-initialised in-process, so exit cleanly instead of aborting.
                try
                {
                    spec.LogStartupNotice(
                        "The UI could not start because no display was available; exiting. Relaunch once the display is awake.");
                }
                catch
                {
                    // Best effort only.
                }

                return SisterAvaloniaDisplayReadinessGate.DisplayUnavailableExitCode;
            }

            throw;
        }
    }
}
