namespace Free.Shared.AppServices;

/// <summary>
/// Shared service for a sister app's local diagnostics: usage/crash events written to the app's own
/// diagnostics folder (<c>%LOCALAPPDATA%\&lt;app&gt;\Diagnostics</c>) through the shared
/// <see cref="AppDiagnosticsFileStore"/>. Holds the best-effort <see cref="RecordEvent"/> /
/// <see cref="RecordCrash"/> wrappers that swallow their own failures so diagnostics can never disrupt
/// launch, save, or shutdown. UI hosts pass their dispatcher subscription into
/// <see cref="RegisterCrashHandlers"/> so this neutral tier does not reference WPF or Avalonia.
/// </summary>
public class LocalAppDiagnostics : IDisposable
{
    private readonly AppDiagnosticsFileStore _fileStore;
    private readonly AppDiagnosticsMetadata _metadata;
    private readonly IAppCrashAnalytics _crashAnalytics;

    /// <summary>Builds a diagnostics service over a file store and this build's metadata.</summary>
    public LocalAppDiagnostics(
        AppDiagnosticsFileStore fileStore,
        AppDiagnosticsMetadata metadata,
        IAppCrashAnalytics? crashAnalytics = null)
    {
        _fileStore = fileStore;
        _metadata = metadata;
        _crashAnalytics = crashAnalytics ?? DisabledAppCrashAnalytics.Instance;
    }

    protected LocalAppDiagnostics(LocalAppDiagnostics other)
    {
        ArgumentNullException.ThrowIfNull(other);
        _fileStore = other._fileStore;
        _metadata = other._metadata;
        _crashAnalytics = other._crashAnalytics;
    }

    public AppDiagnosticsMetadata Metadata => _metadata;

    public bool IsEnabled => _fileStore.IsEnabled;

    public static LocalAppDiagnostics Create(
        string appVersion,
        string? diagnosticsDirectory = null,
        bool? remoteAnalyticsConsent = null)
    {
        var defaults = AppDiagnosticsOptions.CreateDefault();
        var options = new AppDiagnosticsOptions(
            string.IsNullOrWhiteSpace(diagnosticsDirectory)
                ? defaults.DiagnosticsDirectory
                : diagnosticsDirectory,
            defaults.IsEnabled);
        return CreateWithRemoteAnalytics(options, appVersion, remoteAnalyticsConsent);
    }

    /// <summary>Builds the default local diagnostics service for the current <see cref="AppProduct"/>.</summary>
    public static LocalAppDiagnostics CreateDefault(
        string appVersion,
        IAppDiagnosticsPathProvider? pathProvider = null,
        bool? remoteAnalyticsConsent = null)
    {
        var options = pathProvider is null
            ? AppDiagnosticsOptions.CreateDefault()
            : AppDiagnosticsOptions.CreateDefault(pathProvider);
        return CreateWithRemoteAnalytics(options, appVersion, remoteAnalyticsConsent);
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
            var safeProperties = AppDiagnosticsFileStore.SanitizeProperties(properties)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            _fileStore.RecordEvent(eventName, _metadata, safeProperties);
            _crashAnalytics.RecordBreadcrumb(eventName, safeProperties);
        }
        catch
        {
            // Diagnostics are best-effort and must never affect app behavior.
        }
    }

    /// <summary>Writes a local crash report + a crash event; returns the report path (empty on failure).</summary>
    public string RecordCrash(Exception exception, string source)
    {
        var reportPath = string.Empty;
        try
        {
            reportPath = _fileStore.RecordCrash(exception, source, _metadata);
        }
        catch
        {
            // Local crash reporting is best-effort; preserve the original failure path.
        }

        try
        {
            _crashAnalytics.CaptureCrash(exception, source);
        }
        catch
        {
            // Remote crash reporting must never suppress the local report or original failure.
        }

        return reportPath;
    }

    public void Dispose() => _crashAnalytics.Dispose();

    private static LocalAppDiagnostics CreateWithRemoteAnalytics(
        AppDiagnosticsOptions options,
        string appVersion,
        bool? remoteAnalyticsConsent)
    {
        var metadata = AppDiagnosticsMetadata.Create(appVersion);
        return new LocalAppDiagnostics(
            new AppDiagnosticsFileStore(options),
            metadata,
            SentryAppCrashAnalytics.CreateDefault(metadata, remoteAnalyticsConsent));
    }
}
