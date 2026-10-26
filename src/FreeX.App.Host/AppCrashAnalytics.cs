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

    /// <summary>
    /// Applies the Trust Center's "send opt-in crash reports" choice for the REST OF THIS SESSION.
    ///
    /// r196: the opt-in used to be read once, at Initialize, and the Options checkbox carried no
    /// restart notice. Unticking it therefore changed nothing until the app was restarted -- the
    /// user withdrew consent and reports kept being sent, which is the one direction that must
    /// never lag. Every other side effect the Options commit handler drives (gridlines, headings,
    /// the QAT, calculation mode) already takes effect immediately; this now does too.
    ///
    /// Turning it back ON mid-session re-enables reporting only if the SDK was initialised at
    /// startup. It is not re-initialised here, because that would need the DSN and environment
    /// again and the failure mode of getting it wrong is sending data the user did not ask to send.
    /// </summary>
    void ApplyOptIn(bool enabled);
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

    // r196: nothing to apply -- this implementation never sends anything either way.
    public void ApplyOptIn(bool enabled)
    {
    }

    public void Dispose()
    {
    }
}
