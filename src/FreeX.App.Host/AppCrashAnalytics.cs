using FreeX.App.Services;

namespace FreeX.App.Host;

public sealed record AppCrashAnalyticsOptions(
    string? Dsn,
    bool IsEnabled,
    string Environment = "tester",
    bool IsDisabledByEnvironment = false)
{
    public static AppCrashAnalyticsOptions CreateDefault(bool crashAnalyticsEnabled) =>
        CreateDefault(
            () => global::System.Environment.GetEnvironmentVariable("FREEX_SENTRY_DSN"),
            crashAnalyticsEnabled);

    internal static AppCrashAnalyticsOptions CreateDefault(
        Func<string?> sentryDsnProvider,
        bool crashAnalyticsEnabled)
    {
        var consentOverride = global::System.Environment.GetEnvironmentVariable("FREEX_CRASH_ANALYTICS");
        var disabledByEnvironment =
            string.Equals(consentOverride, "0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(consentOverride, "false", StringComparison.OrdinalIgnoreCase);
        var enabledByEnvironment =
            string.Equals(consentOverride, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(consentOverride, "true", StringComparison.OrdinalIgnoreCase);
        var sharedOptions = Free.Shared.AppServices.AppCrashAnalyticsOptions.CreateDefault(
            userConsent: crashAnalyticsEnabled);
        var dsn = sentryDsnProvider();
        var usesSharedBuildConfiguration = string.IsNullOrWhiteSpace(dsn);
        if (usesSharedBuildConfiguration)
            dsn = sharedOptions.Dsn;
        var enabled = (crashAnalyticsEnabled || enabledByEnvironment)
            && !disabledByEnvironment
            && !string.IsNullOrWhiteSpace(dsn);

        return new AppCrashAnalyticsOptions(
            string.IsNullOrWhiteSpace(dsn) ? null : dsn,
            enabled,
            sharedOptions.Environment,
            IsDisabledByEnvironment: disabledByEnvironment);
    }
}

public interface ICrashAnalytics : IDisposable
{
    void Initialize(AppCrashAnalyticsOptions options, AppDiagnosticsMetadata metadata);

    void RecordBreadcrumb(string eventName, IReadOnlyDictionary<string, string?>? properties = null);

    void CaptureCrash(Exception exception, string source);

    bool SendTestReport();
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

    public bool SendTestReport() => false;

    public void Dispose()
    {
    }
}
