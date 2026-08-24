using System.Text.RegularExpressions;
using Sentry;

namespace Free.Shared.AppServices;

internal interface IAppCrashAnalyticsTransport : IDisposable
{
    void AddBreadcrumb(string eventName, IDictionary<string, string> properties);
    void CaptureCrash(Exception exception, string source);
    bool SendTestReport();
}

/// <summary>Privacy-minimized Sentry transport shared by every Free-family desktop renderer.</summary>
public sealed class SentryAppCrashAnalytics : IAppCrashAnalytics
{
    private readonly IAppCrashAnalyticsTransport? _transport;
    private readonly IDisposable? _runtimeRegistration;

    private SentryAppCrashAnalytics(IAppCrashAnalyticsTransport transport)
    {
        _transport = transport;
        _runtimeRegistration = AppCrashAnalyticsRuntime.Register(this);
    }

    public bool IsEnabled => _transport is not null;

    public static IAppCrashAnalytics CreateDefault(
        AppDiagnosticsMetadata metadata,
        bool? userConsent = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return Create(
            AppCrashAnalyticsOptions.CreateDefault(userConsent),
            metadata,
            AppProduct.Current,
            static (options, eventMetadata, product) =>
                new SentrySdkAppCrashAnalyticsTransport(options, eventMetadata, product));
    }

    internal static IAppCrashAnalytics Create(
        AppCrashAnalyticsOptions options,
        AppDiagnosticsMetadata metadata,
        AppProductIdentity product,
        Func<AppCrashAnalyticsOptions, AppDiagnosticsMetadata, AppProductIdentity, IAppCrashAnalyticsTransport> transportFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(transportFactory);

        return !options.IsEnabled || string.IsNullOrWhiteSpace(options.Dsn)
            ? DisabledAppCrashAnalytics.Instance
            : new SentryAppCrashAnalytics(transportFactory(options, metadata, product));
    }

    public void RecordBreadcrumb(string eventName, IReadOnlyDictionary<string, string?>? properties = null)
    {
        if (_transport is null)
            return;

        var safeProperties = AppDiagnosticsFileStore.SanitizeProperties(properties)
            .Where(pair => pair.Value is not null)
            .ToDictionary(
                pair => pair.Key,
                pair => AppCrashDataRedactor.RedactText(pair.Value)!,
                StringComparer.OrdinalIgnoreCase);
        _transport.AddBreadcrumb(eventName, safeProperties);
    }

    public void CaptureCrash(Exception exception, string source)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _transport?.CaptureCrash(exception, source);
    }

    public bool SendTestReport() => _transport?.SendTestReport() == true;

    public void Dispose()
    {
        _runtimeRegistration?.Dispose();
        _transport?.Dispose();
    }
}

internal sealed class SentrySdkAppCrashAnalyticsTransport : IAppCrashAnalyticsTransport
{
    private const string TestReportMessage =
        "User-requested crash analytics test report. No document data is included.";
    private readonly IDisposable _sdk;

    public SentrySdkAppCrashAnalyticsTransport(
        AppCrashAnalyticsOptions options,
        AppDiagnosticsMetadata metadata,
        AppProductIdentity product)
    {
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
                AppCrashDataRedactor.Redact(sentryEvent);
                return sentryEvent;
            });
        });
    }

    public void AddBreadcrumb(string eventName, IDictionary<string, string> properties) =>
        SentrySdk.AddBreadcrumb(eventName, "freeapp", "default", properties);

    public void CaptureCrash(Exception exception, string source)
    {
        SentrySdk.ConfigureScope(scope => scope.SetTag("freeapp.crash_source", source));
        SentrySdk.CaptureException(exception);
        Flush();
    }

    public bool SendTestReport()
    {
        var testEvent = new SentryEvent
        {
            Level = SentryLevel.Info,
            Message = TestReportMessage,
        };
        testEvent.SetTag("freeapp.test_report", "true");
        var eventId = SentrySdk.CaptureEvent(testEvent);
        Flush();
        return eventId != SentryId.Empty;
    }

    public void Dispose() => _sdk.Dispose();

    private static void Flush() =>
        SentrySdk.FlushAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
}

public static partial class AppCrashDataRedactor
{
    private static readonly string UserProfilePath =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static readonly string UserName = Environment.UserName;

    // Privacy favors over-redaction here: an arbitrary absolute path in an exception message is
    // less useful than protecting a document name outside the current user's profile directory.
    [GeneratedRegex("(?i)(?:file:///|[a-z]:\\\\|\\\\\\\\)[^\\r\\n\\t\\\"<>|]+")]
    private static partial Regex WindowsOrFileUriPathRegex();

    [GeneratedRegex("(?<![A-Za-z0-9])/(?:[^/\\r\\n\\t\\\"']+/)+[^\\r\\n\\t\\\"']*")]
    private static partial Regex UnixPathRegex();

    [GeneratedRegex("(?i)\\b[^\\s\\\\/:\\\"*?<>|]+\\.(?:xlsx|xlsm|xlsb|xls|csv|ods|docx|doc|odt|rtf|pptx|pptm|ppt|odp|pdf)\\b")]
    private static partial Regex DocumentFileNameRegex();

    public static void Redact(SentryEvent sentryEvent)
    {
        if (sentryEvent.Message is { } message)
        {
            message.Message = RedactText(message.Message);
            message.Formatted = RedactText(message.Formatted);
        }

        if (sentryEvent.SentryExceptions is not { } exceptions)
            return;

        foreach (var exception in exceptions)
        {
            exception.Value = RedactText(exception.Value);
            if (exception.Stacktrace?.Frames is not { } frames)
                continue;

            foreach (var frame in frames)
            {
                frame.FileName = string.IsNullOrWhiteSpace(frame.FileName) ? frame.FileName : "<path>";
                frame.AbsolutePath = string.IsNullOrWhiteSpace(frame.AbsolutePath) ? frame.AbsolutePath : "<path>";
            }
        }
    }

    public static string? RedactText(
        string? text,
        string? userProfilePath = null,
        string? userName = null)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var profile = userProfilePath ?? UserProfilePath;
        var name = userName ?? UserName;
        if (!string.IsNullOrEmpty(profile))
            text = text.Replace(profile, "<path>", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(name))
            text = text.Replace(name, "<user>", StringComparison.OrdinalIgnoreCase);

        text = WindowsOrFileUriPathRegex().Replace(text, "<path>");
        text = UnixPathRegex().Replace(text, "<path>");
        return DocumentFileNameRegex().Replace(text, "<document>");
    }
}
