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
}

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
        var diagnostics = runtime.CreateDiagnostics(runtime.ResolveVersion());
        return SisterAvaloniaApplicationStartupRunner.Run(
            preparation.StartupArguments,
            new SisterAvaloniaApplicationStartupSpec(
                spec.StartApplication,
                diagnostics.RegisterCrashHandlers,
                diagnostics.RecordCrash)
        {
            RegisterRibbonCommandFaultHandler = runtime.RegisterRibbonCommandFaultHandler,
            StartupCrashSource = spec.CrashSource
        });
    }
}

internal sealed class SisterAvaloniaProgramRuntime
{
    public static SisterAvaloniaProgramRuntime Default { get; } = new();

    public Func<string> ResolveVersion { get; init; } = EntryAssemblyVersion.Resolve;

    public Func<string, ISisterAvaloniaProgramDiagnostics> CreateDiagnostics { get; init; } =
        version => new LocalSisterAvaloniaProgramDiagnostics(LocalAppDiagnostics.CreateDefault(version));

    public Action<Action<Exception, string>> RegisterRibbonCommandFaultHandler { get; init; } =
        handler => RibbonCommandFaultReporter.Handler = handler;
}

internal interface ISisterAvaloniaProgramDiagnostics
{
    void RegisterCrashHandlers();

    void RecordCrash(Exception exception, string source);
}

internal sealed class LocalSisterAvaloniaProgramDiagnostics(LocalAppDiagnostics diagnostics)
    : ISisterAvaloniaProgramDiagnostics
{
    public void RegisterCrashHandlers() => diagnostics.RegisterCrashHandlers();

    public void RecordCrash(Exception exception, string source) => diagnostics.RecordCrash(exception, source);
}
