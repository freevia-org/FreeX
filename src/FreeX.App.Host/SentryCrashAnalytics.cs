using FreeX.App.Services;
using Sentry;

namespace FreeX.App.Host;

public sealed class SentryCrashAnalytics : ICrashAnalytics, Free.Shared.AppServices.IAppCrashAnalytics
{
    private IDisposable? _sentry;
    private IDisposable? _runtimeRegistration;
    private bool _isEnabled;

    // r196: the LIVE opt-in, separate from _isEnabled (which records that the SDK was
    // initialised). Volatile because a crash can be captured on any thread while the Options
    // dialog commits on the UI one.
    private volatile bool _optedIn;

    public bool IsEnabled => _isEnabled && _optedIn;

    public void ApplyOptIn(bool enabled) => _optedIn = enabled;

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
                // r196: the last gate before anything leaves the process. Checked HERE as well as in
                // the methods below so an event the SDK captured on its own -- an unhandled
                // exception -- is dropped too once the user has opted out mid-session.
                if (!_optedIn)
                    return null;

                Free.Shared.AppServices.AppCrashDataRedactor.Redact(sentryEvent);
                return sentryEvent;
            });
        });
        _isEnabled = true;
        _optedIn = true;
        _runtimeRegistration = Free.Shared.AppServices.AppCrashAnalyticsRuntime.Register(this);
    }

    public void RecordBreadcrumb(string eventName, IReadOnlyDictionary<string, string?>? properties = null)
    {
        if (!IsEnabled)
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
        if (!IsEnabled)
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
        if (!IsEnabled)
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
