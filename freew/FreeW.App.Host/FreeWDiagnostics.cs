using System;
using System.Collections.Generic;
using Free.Shared.AppServices;

namespace FreeW.App.Host;

/// <summary>
/// FreeW's local diagnostics: usage/crash events written to FreeW's own diagnostics folder
/// (<c>%LOCALAPPDATA%\FreeW\Diagnostics</c>) through the shared <see cref="AppDiagnosticsFileStore"/>.
/// Mirrors FreeX's local-diagnostics wiring <em>minus Sentry</em> — FreeW does no remote telemetry.
///
/// <para>
/// Everything here is best-effort and swallows its own failures, so diagnostics can never disrupt
/// launch, save, or shutdown. Disable for a run with <c>FREEW_DIAGNOSTICS=0</c> (the env var carried by
/// FreeW's <see cref="AppProduct"/> identity).
/// </para>
/// </summary>
public sealed class FreeWDiagnostics
{
    private readonly AppDiagnosticsFileStore _fileStore;
    private readonly AppDiagnosticsMetadata _metadata;

    private FreeWDiagnostics(AppDiagnosticsFileStore fileStore, AppDiagnosticsMetadata metadata)
    {
        _fileStore = fileStore;
        _metadata = metadata;
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

    /// <summary>Records a usage event (best-effort; only whitelisted properties are persisted).</summary>
    public void RecordEvent(string eventName, IReadOnlyDictionary<string, string?>? properties = null)
    {
        try
        {
            _fileStore.RecordEvent(eventName, _metadata, properties);
        }
        catch
        {
            // Diagnostics are best-effort and must never affect app behavior.
        }
    }

    /// <summary>Writes a local crash report + a crash event; returns the report path (empty on failure).</summary>
    public string RecordCrash(Exception exception, string source)
    {
        try
        {
            return _fileStore.RecordCrash(exception, source, _metadata);
        }
        catch
        {
            // Local crash reporting is best-effort; preserve the original failure path.
            return string.Empty;
        }
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
