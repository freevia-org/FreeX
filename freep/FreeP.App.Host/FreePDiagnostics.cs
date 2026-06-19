using Free.Shared.AppServices;

namespace FreeP.App.Host;

/// <summary>
/// FreeP's local diagnostics: usage/crash events written to FreeP's own diagnostics folder
/// (<c>%LOCALAPPDATA%\FreeP\Diagnostics</c>) through the shared <see cref="AppDiagnosticsFileStore"/>.
/// Mirrors FreeW's local-diagnostics wiring (and FreeX's <em>minus Sentry</em>) — FreeP does no remote
/// telemetry. Everything here is best-effort and swallows its own failures, so diagnostics can never disrupt
/// launch, save, or shutdown. Disable for a run with <c>FREEP_DIAGNOSTICS=0</c>.
/// </summary>
public sealed class FreePDiagnostics
{
    private readonly AppDiagnosticsFileStore _fileStore;
    private readonly AppDiagnosticsMetadata _metadata;

    private FreePDiagnostics(AppDiagnosticsFileStore fileStore, AppDiagnosticsMetadata metadata)
    {
        _fileStore = fileStore;
        _metadata = metadata;
    }

    /// <summary>Builds the default FreeP diagnostics service. Never throws.</summary>
    public static FreePDiagnostics CreateDefault(string appVersion)
    {
        var options = AppDiagnosticsOptions.CreateDefault();
        var fileStore = new AppDiagnosticsFileStore(options);
        var metadata = AppDiagnosticsMetadata.Create(appVersion);
        return new FreePDiagnostics(fileStore, metadata);
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
            return string.Empty;
        }
    }

    /// <summary>
    /// Subscribes the process-wide unhandled-exception hooks so crashes land in FreeP's diagnostics folder.
    /// Mirrors FreeW's handler set (dispatcher / appdomain / unobserved task). Safe to call once at startup.
    /// </summary>
    public void RegisterCrashHandlers()
    {
        if (System.Windows.Application.Current is { } app)
        {
            app.DispatcherUnhandledException += (_, args) => RecordCrash(args.Exception, "dispatcher");
        }

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
                RecordCrash(exception, "appdomain");
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
            RecordCrash(args.Exception, "task");
    }
}
