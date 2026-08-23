using Sentry;

namespace Free.Shared.AppServices;

/// <summary>Privacy-minimized Sentry transport shared by every Free-family desktop renderer.</summary>
public sealed class SentryAppCrashAnalytics : IAppCrashAnalytics
{
    private static readonly string UserProfilePath =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static readonly string UserName = Environment.UserName;

    private readonly IDisposable? _sdk;

    private SentryAppCrashAnalytics(
        AppCrashAnalyticsOptions options,
        AppDiagnosticsMetadata metadata,
        AppProductIdentity product)
    {
        if (!options.IsEnabled || string.IsNullOrWhiteSpace(options.Dsn))
            return;

        _sdk = SentrySdk.Init(sentry =>
        {
            sentry.Dsn = options.Dsn;
            sentry.Release = $"{product.ProductDirectoryName}@{metadata.AppVersion}";
            sentry.Environment = $"{options.Environment}-{product.ProductDirectoryName.ToLowerInvariant()}";
            sentry.SendDefaultPii = false;
            sentry.SetBeforeSend((sentryEvent, _) =>
            {
                sentryEvent.SetTag("freeapp.product", product.ProductName);
                sentryEvent.SetTag("freeapp.session_id", metadata.SessionId);
                sentryEvent.SetTag("freeapp.runtime", metadata.RuntimeDescription);
                sentryEvent.SetTag("freeapp.os", metadata.OperatingSystemDescription);
                sentryEvent.SetTag("freeapp.architecture", metadata.ProcessArchitecture);
                RedactPersonalData(sentryEvent);
                return sentryEvent;
            });
        });
    }

    public bool IsEnabled => _sdk is not null;

    public static IAppCrashAnalytics CreateDefault(
        AppDiagnosticsMetadata metadata,
        bool? userConsent = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var options = AppCrashAnalyticsOptions.CreateDefault(userConsent);
        return options.IsEnabled
            ? new SentryAppCrashAnalytics(options, metadata, AppProduct.Current)
            : DisabledAppCrashAnalytics.Instance;
    }

    public void RecordBreadcrumb(string eventName, IReadOnlyDictionary<string, string?>? properties = null)
    {
        if (!IsEnabled)
            return;

        var safeProperties = AppDiagnosticsFileStore.SanitizeProperties(properties)
            .Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value!, StringComparer.OrdinalIgnoreCase);
        SentrySdk.AddBreadcrumb(eventName, "freeapp", "default", safeProperties);
    }

    public void CaptureCrash(Exception exception, string source)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (!IsEnabled)
            return;

        SentrySdk.ConfigureScope(scope => scope.SetTag("freeapp.crash_source", source));
        SentrySdk.CaptureException(exception);
        // Crash handlers may terminate the process immediately after this method returns. Give the
        // transport a short bounded chance to deliver the envelope; failures remain best-effort.
        SentrySdk.FlushAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
    }

    public void Dispose() => _sdk?.Dispose();

    private static void RedactPersonalData(SentryEvent sentryEvent)
    {
        if (sentryEvent.Message is { } message)
        {
            message.Message = Redact(message.Message);
            message.Formatted = Redact(message.Formatted);
        }

        if (sentryEvent.SentryExceptions is not { } exceptions)
            return;

        foreach (var exception in exceptions)
        {
            exception.Value = Redact(exception.Value);
            if (exception.Stacktrace?.Frames is not { } frames)
                continue;

            foreach (var frame in frames)
            {
                frame.FileName = Redact(frame.FileName);
                frame.AbsolutePath = Redact(frame.AbsolutePath);
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
