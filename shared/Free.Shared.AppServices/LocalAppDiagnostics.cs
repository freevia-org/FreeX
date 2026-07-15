namespace Free.Shared.AppServices;

/// <summary>
/// Shared service for a sister app's local diagnostics: usage/crash events written to the app's own
/// diagnostics folder (<c>%LOCALAPPDATA%\&lt;app&gt;\Diagnostics</c>) through the shared
/// <see cref="AppDiagnosticsFileStore"/>. Holds the best-effort <see cref="RecordEvent"/> /
/// <see cref="RecordCrash"/> wrappers that swallow their own failures so diagnostics can never disrupt
/// launch, save, or shutdown. UI hosts pass their dispatcher subscription into
/// <see cref="RegisterCrashHandlers"/> so this neutral tier does not reference WPF or Avalonia.
/// </summary>
public class LocalAppDiagnostics
{
    private readonly AppDiagnosticsFileStore _fileStore;
    private readonly AppDiagnosticsMetadata _metadata;

    /// <summary>Builds a diagnostics service over a file store and this build's metadata.</summary>
    public LocalAppDiagnostics(AppDiagnosticsFileStore fileStore, AppDiagnosticsMetadata metadata)
    {
        _fileStore = fileStore;
        _metadata = metadata;
    }

    protected LocalAppDiagnostics(LocalAppDiagnostics other)
    {
        ArgumentNullException.ThrowIfNull(other);
        _fileStore = other._fileStore;
        _metadata = other._metadata;
    }

    public AppDiagnosticsMetadata Metadata => _metadata;

    public bool IsEnabled => _fileStore.IsEnabled;

    public static LocalAppDiagnostics Create(
        string appVersion,
        string? diagnosticsDirectory = null)
    {
        var defaults = AppDiagnosticsOptions.CreateDefault();
        var options = new AppDiagnosticsOptions(
            string.IsNullOrWhiteSpace(diagnosticsDirectory)
                ? defaults.DiagnosticsDirectory
                : diagnosticsDirectory,
            defaults.IsEnabled);
        return new LocalAppDiagnostics(
            new AppDiagnosticsFileStore(options),
            AppDiagnosticsMetadata.Create(appVersion));
    }

    /// <summary>Builds the default local diagnostics service for the current <see cref="AppProduct"/>.</summary>
    public static LocalAppDiagnostics CreateDefault(
        string appVersion,
        IAppDiagnosticsPathProvider? pathProvider = null)
    {
        var options = pathProvider is null
            ? AppDiagnosticsOptions.CreateDefault()
            : AppDiagnosticsOptions.CreateDefault(pathProvider);
        return new LocalAppDiagnostics(
            new AppDiagnosticsFileStore(options),
            AppDiagnosticsMetadata.Create(appVersion));
    }

    /// <summary>
    /// Wires process-wide crash hooks, with the UI dispatcher subscription supplied by the host renderer.
    /// </summary>
    public void RegisterCrashHandlers(
        Action<Action<Exception>>? subscribeDispatcher = null,
        Action? onAfterFault = null) =>
        AppCrashHandlers.Register(
            recordCrash: (exception, source) => RecordCrash(exception, source),
            subscribeDispatcher,
            onAfterFault);

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
}
