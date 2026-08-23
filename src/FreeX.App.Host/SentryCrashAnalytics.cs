using FreeX.App.Services;
using Sentry;

namespace FreeX.App.Host;

public sealed class SentryCrashAnalytics : ICrashAnalytics
{
    // Captured once so crash events can have the local user profile path and username scrubbed
    // before they leave the machine — exception messages/stack frames routinely embed
    // C:\Users\<username>\... paths that would otherwise disclose PII even with SendDefaultPii=false.
    private static readonly string UserProfilePath =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static readonly string UserName = Environment.UserName;

    private IDisposable? _sentry;
    private bool _isEnabled;

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
                RedactPersonalData(sentryEvent);
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
        SentrySdk.FlushAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _sentry?.Dispose();
    }

    /// <summary>
    /// Scrub the local user profile path and username out of an outgoing event's message,
    /// exception values, and stack-frame paths so crash reports do not disclose PII.
    /// </summary>
    private static void RedactPersonalData(SentryEvent sentryEvent)
    {
        if (sentryEvent.Message is { } message)
        {
            message.Message = Redact(message.Message);
            message.Formatted = Redact(message.Formatted);
        }

        if (sentryEvent.SentryExceptions is { } exceptions)
        {
            foreach (var exception in exceptions)
            {
                exception.Value = Redact(exception.Value);
                var frames = exception.Stacktrace?.Frames;
                if (frames is null)
                    continue;

                foreach (var frame in frames)
                {
                    frame.FileName = Redact(frame.FileName);
                    frame.AbsolutePath = Redact(frame.AbsolutePath);
                }
            }
        }
    }

    private static string? Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        if (!string.IsNullOrEmpty(UserProfilePath))
            text = text.Replace(UserProfilePath, "<user-profile>", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(UserName))
            text = text.Replace(UserName, "<user>", StringComparison.OrdinalIgnoreCase);

        return text;
    }
}
