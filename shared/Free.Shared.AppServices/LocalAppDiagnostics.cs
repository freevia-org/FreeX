namespace Free.Shared.AppServices;

/// <summary>
/// Shared base for a sister app's local diagnostics: usage/crash events written to the app's own
/// diagnostics folder (<c>%LOCALAPPDATA%\&lt;app&gt;\Diagnostics</c>) through the shared
/// <see cref="AppDiagnosticsFileStore"/>. Holds the best-effort <see cref="RecordEvent"/> /
/// <see cref="RecordCrash"/> wrappers that swallow their own failures so diagnostics can never disrupt
/// launch, save, or shutdown. The WPF process-wide crash-handler wiring stays in each app's thin subclass
/// (it depends on <c>System.Windows.Application</c>, which this neutral tier does not reference).
/// </summary>
public abstract class LocalAppDiagnostics
{
    private readonly AppDiagnosticsFileStore _fileStore;
    private readonly AppDiagnosticsMetadata _metadata;

    /// <summary>Builds a diagnostics service over a file store and this build's metadata.</summary>
    protected LocalAppDiagnostics(AppDiagnosticsFileStore fileStore, AppDiagnosticsMetadata metadata)
    {
        _fileStore = fileStore;
        _metadata = metadata;
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
}
