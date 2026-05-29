using Sentry;

namespace FreeX.App.Host;

public sealed class SentryCrashAnalytics : ICrashAnalytics
{
    private IDisposable? _sentry;
    private bool _isEnabled;

    public void Initialize(AppCrashAnalyticsOptions crashAnalyticsOptions, AppDiagnosticsMetadata metadata)
    {
        if (!crashAnalyticsOptions.IsEnabled || string.IsNullOrWhiteSpace(crashAnalyticsOptions.Dsn))
            return;

        _sentry = SentrySdk.Init(options =>
        {
            options.Dsn = crashAnalyticsOptions.Dsn;
            options.Release = metadata.AppVersion;
            options.Environment = crashAnalyticsOptions.Environment;
            options.SendDefaultPii = false;
            options.SetBeforeSend((sentryEvent, _) =>
            {
                sentryEvent.SetTag("freex.session_id", metadata.SessionId);
                sentryEvent.SetTag("freex.runtime", metadata.RuntimeDescription);
                sentryEvent.SetTag("freex.os", metadata.OperatingSystemDescription);
                sentryEvent.SetTag("freex.architecture", metadata.ProcessArchitecture);
                return sentryEvent;
            });
        });
        _isEnabled = true;
    }

    public void RecordBreadcrumb(string eventName, IReadOnlyDictionary<string, string?>? properties = null)
    {
        if (!_isEnabled)
            return;

        var data = properties?
            .Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value!, StringComparer.OrdinalIgnoreCase);
        SentrySdk.AddBreadcrumb(
            message: eventName,
            category: "freex",
            type: "default",
            data: data);
    }

    public void CaptureCrash(Exception exception, string source)
    {
        if (!_isEnabled)
            return;

        SentrySdk.ConfigureScope(scope =>
        {
            scope.SetTag("freex.crash_source", source);
        });
        SentrySdk.CaptureException(exception);
    }

    public void Dispose()
    {
        _sentry?.Dispose();
    }
}
