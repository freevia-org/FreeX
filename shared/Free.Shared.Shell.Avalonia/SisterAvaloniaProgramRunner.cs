using Free.Shared.AppServices;
using Free.Shared.Ribbon;

namespace Free.Shared.Shell.Avalonia;

/// <summary>
/// Result of app-specific command-line preparation before the Avalonia desktop lifetime starts.
/// </summary>
public sealed record SisterAvaloniaLaunchPreparation(string[] StartupArguments, int? ExitCode)
{
    public static SisterAvaloniaLaunchPreparation Continue(string[] startupArguments)
    {
        ArgumentNullException.ThrowIfNull(startupArguments);
        return new SisterAvaloniaLaunchPreparation(startupArguments, ExitCode: null);
    }

    public static SisterAvaloniaLaunchPreparation Exit(int exitCode) => new([], exitCode);
}

/// <summary>
/// Product-specific inputs for the shared Avalonia program lifecycle. Apps retain ownership of
/// headless commands, argument parsing, static app state, and AppBuilder construction.
/// </summary>
public sealed record SisterAvaloniaProgramSpec(
    AppProductIdentity ProductIdentity,
    Func<string[], SisterAvaloniaLaunchPreparation> PrepareLaunch,
    Func<string[], int> StartApplication)
{
    public string CrashSource { get; init; } = "avalonia_startup";

    public Func<SisterAvaloniaProgramDiagnostics>? CreateDiagnostics { get; init; }

    public Action? BeforeRun { get; init; }

    public Action<int>? AfterRun { get; init; }

    public int? CompletedExitCode { get; init; }

    /// <summary>
    /// Optional best-effort hook run immediately after a crash is recorded by the neutral
    /// AppDomain/unobserved-task hooks this runner installs (R138: FreeW/FreeP's Avalonia shell had
    /// no way to plug in an emergency document snapshot here at all, unlike FreeX's hand-rolled
    /// Avalonia host -- a crash lost every edit since the last periodic autosave tick, the exact
    /// scenario autosave exists to prevent). Ignored when the caller supplies a custom
    /// <see cref="CreateDiagnostics"/> override (as FreeX's Program.cs does) -- those hosts own their
    /// own diagnostics wiring and register this kind of hook themselves. Must never throw.
    /// </summary>
    public Action? OnEmergencySnapshot { get; init; }
}

/// <summary>
/// Product-supplied diagnostics callbacks for apps that need a custom diagnostics directory or
/// version source while retaining the shared Avalonia lifecycle.
/// </summary>
public sealed record SisterAvaloniaProgramDiagnostics(
    Action RegisterCrashHandlers,
    Action<Exception, string> RecordCrash);

/// <summary>
/// Runs the common Avalonia program lifecycle used by sister apps: installs product identity before
/// command processing, honors headless/validation short circuits, registers local crash handlers,
/// and records startup failures without taking ownership of product-specific argument parsing.
/// </summary>
public static class SisterAvaloniaProgramRunner
{
    public static int Run(string[] arguments, SisterAvaloniaProgramSpec spec) =>
        Run(arguments, spec, SisterAvaloniaProgramRuntime.Default);

    internal static int Run(
        string[] arguments,
        SisterAvaloniaProgramSpec spec,
        SisterAvaloniaProgramRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(spec.ProductIdentity);
        ArgumentNullException.ThrowIfNull(spec.PrepareLaunch);
        ArgumentNullException.ThrowIfNull(spec.StartApplication);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.CrashSource);

        AppProduct.Current = spec.ProductIdentity;

        var preparation = spec.PrepareLaunch(arguments)
            ?? throw new InvalidOperationException("Avalonia launch preparation returned no result.");
        if (preparation.ExitCode is { } exitCode)
            return exitCode;

        ArgumentNullException.ThrowIfNull(preparation.StartupArguments);
        var diagnostics = spec.CreateDiagnostics?.Invoke()
            ?? runtime.CreateDiagnostics(runtime.ResolveVersion(), spec.OnEmergencySnapshot);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(diagnostics.RegisterCrashHandlers);
        ArgumentNullException.ThrowIfNull(diagnostics.RecordCrash);
        return SisterAvaloniaApplicationStartupRunner.Run(
            preparation.StartupArguments,
            new SisterAvaloniaApplicationStartupSpec(
                spec.StartApplication,
                diagnostics.RegisterCrashHandlers,
                diagnostics.RecordCrash)
        {
            RegisterRibbonCommandFaultHandler = runtime.RegisterRibbonCommandFaultHandler,
            BeforeRun = spec.BeforeRun,
            AfterRun = spec.AfterRun,
            CompletedExitCode = spec.CompletedExitCode,
            StartupCrashSource = spec.CrashSource
        });
    }
}

internal sealed class SisterAvaloniaProgramRuntime
{
    public static SisterAvaloniaProgramRuntime Default { get; } = new();

    public Func<string> ResolveVersion { get; init; } = EntryAssemblyVersion.Resolve;

    public Func<string, Action?, SisterAvaloniaProgramDiagnostics> CreateDiagnostics { get; init; } =
        (version, onEmergencySnapshot) =>
        {
            var diagnostics = LocalAppDiagnostics.CreateDefault(version);
            return new SisterAvaloniaProgramDiagnostics(
                () => diagnostics.RegisterCrashHandlers(subscribeDispatcher: null, onAfterFault: onEmergencySnapshot),
                (exception, source) => diagnostics.RecordCrash(exception, source));
        };

    public Action<Action<Exception, string>> RegisterRibbonCommandFaultHandler { get; init; } =
        handler => RibbonCommandFaultReporter.Handler = handler;
}
