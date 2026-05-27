using System.Diagnostics;

namespace Freexcel.App.Host;

public sealed record AppCrashAnalyticsOptions(
    string? Dsn,
    bool IsEnabled,
    string Environment = "tester",
    bool IsDisabledByEnvironment = false)
{
    public static AppCrashAnalyticsOptions CreateDefault(bool crashAnalyticsEnabled) =>
        CreateDefault(
            () => global::System.Environment.GetEnvironmentVariable("FREEXCEL_SENTRY_DSN"),
            crashAnalyticsEnabled);

    internal static AppCrashAnalyticsOptions CreateDefault(
        Func<string?> sentryDsnProvider,
        bool crashAnalyticsEnabled)
    {
        var disabledByEnvironment = string.Equals(
            global::System.Environment.GetEnvironmentVariable("FREEXCEL_CRASH_ANALYTICS"),
            "0",
            StringComparison.OrdinalIgnoreCase);
        var dsn = sentryDsnProvider();
        var enabled = crashAnalyticsEnabled
            && !disabledByEnvironment
            && !string.IsNullOrWhiteSpace(dsn);

        return new AppCrashAnalyticsOptions(
            string.IsNullOrWhiteSpace(dsn) ? null : dsn,
            enabled,
            IsDisabledByEnvironment: disabledByEnvironment);
    }
}

public interface ICrashAnalytics : IDisposable
{
    void Initialize(AppCrashAnalyticsOptions options, AppDiagnosticsMetadata metadata);

    void RecordBreadcrumb(string eventName, IReadOnlyDictionary<string, string?>? properties = null);

    void CaptureCrash(Exception exception, string source);
}

public sealed class DisabledCrashAnalytics : ICrashAnalytics
{
    public void Initialize(AppCrashAnalyticsOptions options, AppDiagnosticsMetadata metadata)
    {
    }

    public void RecordBreadcrumb(string eventName, IReadOnlyDictionary<string, string?>? properties = null)
    {
    }

    public void CaptureCrash(Exception exception, string source)
    {
    }

    public void Dispose()
    {
    }
}
