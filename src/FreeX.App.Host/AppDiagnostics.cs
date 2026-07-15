using FreeX.App.Services;

namespace FreeX.App.Host;

public interface IAppDiagnostics
{
    void RecordEvent(string eventName, IReadOnlyDictionary<string, string?>? properties = null);

    string RecordCrash(Exception exception, string source);
}

public sealed class AppDiagnostics : IAppDiagnostics
{
    private readonly LocalAppDiagnostics _local;
    private readonly ICrashAnalytics _crashAnalytics;

    public AppDiagnostics(
        AppDiagnosticsFileStore fileStore,
        AppDiagnosticsMetadata metadata,
        ICrashAnalytics? crashAnalytics = null)
    {
        _local = new LocalAppDiagnostics(fileStore, metadata);
        _crashAnalytics = crashAnalytics ?? new DisabledCrashAnalytics();
    }

    public AppDiagnostics(LocalAppDiagnostics local, ICrashAnalytics? crashAnalytics = null)
    {
        _local = local;
        _crashAnalytics = crashAnalytics ?? new DisabledCrashAnalytics();
    }

    public void RecordEvent(string eventName, IReadOnlyDictionary<string, string?>? properties = null)
    {
        try
        {
            var safeProperties = AppDiagnosticsFileStore.SanitizeProperties(properties)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            _local.RecordEvent(eventName, safeProperties);
            _crashAnalytics.RecordBreadcrumb(eventName, safeProperties);
        }
        catch
        {
            // Diagnostics are best-effort and must never affect app behavior.
        }
    }

    public string RecordCrash(Exception exception, string source)
    {
        var reportPath = string.Empty;
        try
        {
            reportPath = _local.RecordCrash(exception, source);
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
            // Remote crash reporting must never suppress local diagnostics.
        }

        return reportPath;
    }
}
