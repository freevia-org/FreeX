using System;
using Free.Shared.AppServices;

namespace FreeW.App.Host;

/// <summary>
/// FreeW's local diagnostics: usage/crash events written to FreeW's own diagnostics folder
/// (<c>%LOCALAPPDATA%\FreeW\Diagnostics</c>) through the shared <see cref="AppDiagnosticsFileStore"/>.
/// Mirrors FreeX's local-diagnostics wiring <em>minus Sentry</em> — FreeW does no remote telemetry.
///
/// <para>
/// Everything here is best-effort and swallows its own failures (via the shared
/// <see cref="LocalAppDiagnostics"/> base), so diagnostics can never disrupt launch, save, or shutdown.
/// Disable for a run with <c>FREEW_DIAGNOSTICS=0</c> (the env var carried by FreeW's
/// <see cref="AppProduct"/> identity).
/// </para>
/// </summary>
public sealed class FreeWDiagnostics : LocalAppDiagnostics
{
    private FreeWDiagnostics(AppDiagnosticsFileStore fileStore, AppDiagnosticsMetadata metadata)
        : base(fileStore, metadata)
    {
    }

    /// <summary>
    /// Builds the default FreeW diagnostics service: a file store rooted at FreeW's diagnostics folder
    /// (honouring <c>FREEW_DIAGNOSTICS=0</c>) tagged with this build's version. Never throws.
    /// </summary>
    public static FreeWDiagnostics CreateDefault(string appVersion)
    {
        var options = AppDiagnosticsOptions.CreateDefault();
        var fileStore = new AppDiagnosticsFileStore(options);
        var metadata = AppDiagnosticsMetadata.Create(appVersion);
        return new FreeWDiagnostics(fileStore, metadata);
    }

    /// <summary>
    /// Subscribes the process-wide unhandled-exception hooks so crashes land in FreeW's diagnostics folder.
    /// Mirrors FreeX's handler set (dispatcher / appdomain / unobserved task) minus any remote reporting.
    /// Safe to call once at startup.
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
