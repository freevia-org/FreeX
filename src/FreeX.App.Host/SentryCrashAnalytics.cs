using FreeX.App.Services;
using Sentry;

namespace FreeX.App.Host;

public sealed class SentryCrashAnalytics : ICrashAnalytics, Free.Shared.AppServices.IAppCrashAnalytics
{
    private IDisposable? _sentry;
    private IDisposable? _runtimeRegistration;
    private bool _isEnabled;

    public bool IsEnabled => _isEnabled;

    public void Initialize(AppCrashAnalyticsOptions crashAnalyticsOptions, AppDiagnosticsMetadata metadata)
    {
        if (!crashAnalyticsOptions.IsEnabled || string.IsNullOrWhiteSpace(crashAnalyticsOptions.Dsn))
            return;

        _sentry = SentrySdk.Init(options =>
        {
            options.Dsn = crashAnalyticsOptions.Dsn;
            options.Release = $"FreeX@{metadata.AppVersion}";
            options.Environment = $"{crashAnalyticsOptions.Environment}-freex";
            options.SendDefaultPii = false;
            options.SetBeforeSend((sentryEvent, _) =>
            {
                sentryEvent.SetTag("freex.session_id", metadata.SessionId);
                sentryEvent.SetTag("freeapp.product", "FreeX");
                sentryEvent.SetTag("freex.runtime", metadata.RuntimeDescription);
                sentryEvent.SetTag("freex.os", metadata.OperatingSystemDescription);
                sentryEvent.SetTag("freex.architecture", metadata.ProcessArchitecture);
                Free.Shared.AppServices.AppCrashDataRedactor.Redact(sentryEvent);
                return sentryEvent;
            });
        });
        _isEnabled = true;
        _runtimeRegistration = Free.Shared.AppServices.AppCrashAnalyticsRuntime.Register(this);
    }

    public void RecordBreadcrumb(string eventName, IReadOnlyDictionary<string, string?>? properties = null)
    {
        if (!_isEnabled)
            return;

        var data = Free.Shared.AppServices.AppDiagnosticsFileStore.SanitizeProperties(properties)
            .Where(pair => pair.Value is not null)
            .ToDictionary(
                pair => pair.Key,
                pair => Free.Shared.AppServices.AppCrashDataRedactor.RedactText(pair.Value)!,
                StringComparer.OrdinalIgnoreCase);
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
        SentrySdk.FlushAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
    }

    public bool SendTestReport()
    {
        if (!_isEnabled)
            return false;

        var sentryEvent = new SentryEvent
        {
            Level = SentryLevel.Info,
            Message = "User-requested crash analytics test report. No document data is included.",
        };
        sentryEvent.SetTag("freeapp.test_report", "true");
        var eventId = SentrySdk.CaptureEvent(sentryEvent);
        SentrySdk.FlushAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        return eventId != SentryId.Empty;
    }

    public void Dispose()
    {
        _runtimeRegistration?.Dispose();
        _sentry?.Dispose();
    }
}
