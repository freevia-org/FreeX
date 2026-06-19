using Free.Shared.AppServices;

namespace FreeP.App.Host;

/// <summary>
/// FreeP's local diagnostics: usage/crash events written to FreeP's own diagnostics folder
/// (<c>%LOCALAPPDATA%\FreeP\Diagnostics</c>) through the shared <see cref="AppDiagnosticsFileStore"/>.
/// Mirrors FreeW's local-diagnostics wiring (and FreeX's <em>minus Sentry</em>) — FreeP does no remote
/// telemetry. Everything here is best-effort and swallows its own failures (via the shared
/// <see cref="LocalAppDiagnostics"/> base), so diagnostics can never disrupt launch, save, or shutdown.
/// Disable for a run with <c>FREEP_DIAGNOSTICS=0</c>.
/// </summary>
public sealed class FreePDiagnostics : LocalAppDiagnostics
{
    private FreePDiagnostics(AppDiagnosticsFileStore fileStore, AppDiagnosticsMetadata metadata)
        : base(fileStore, metadata)
    {
    }

    /// <summary>Builds the default FreeP diagnostics service. Never throws.</summary>
    public static FreePDiagnostics CreateDefault(string appVersion)
    {
        var options = AppDiagnosticsOptions.CreateDefault();
        var fileStore = new AppDiagnosticsFileStore(options);
        var metadata = AppDiagnosticsMetadata.Create(appVersion);
        return new FreePDiagnostics(fileStore, metadata);
    }

    /// <summary>
    /// Subscribes the process-wide unhandled-exception hooks so crashes land in FreeP's diagnostics folder.
    /// Mirrors FreeW's handler set (dispatcher / appdomain / unobserved task). Safe to call once at startup.
    /// </summary>
    public void RegisterCrashHandlers()
    {
        AppCrashHandlers.Register(
            recordCrash: (exception, source) => RecordCrash(exception, source),
            subscribeDispatcher: System.Windows.Application.Current is { } app
                ? handler => app.DispatcherUnhandledException += (_, args) => handler(args.Exception)
                : null);
    }
}
