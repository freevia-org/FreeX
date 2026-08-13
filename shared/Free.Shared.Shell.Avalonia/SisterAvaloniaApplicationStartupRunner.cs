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

        try
        {
            var lifetimeExitCode = spec.StartApplication(startupArguments);
            spec.AfterRun?.Invoke(lifetimeExitCode);
            return spec.CompletedExitCode ?? lifetimeExitCode;
        }
        catch (Exception ex)
        {
            spec.RecordCrash(ex, spec.StartupCrashSource);
            throw;
        }
    }
}
