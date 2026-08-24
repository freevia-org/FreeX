using System.Reflection;

namespace Free.Shared.AppServices;

/// <summary>
/// Product-neutral remote crash reporting contract. Remote reporting is opt-in and is disabled
/// unless both a Sentry DSN and an explicit per-product consent flag are configured.
/// </summary>
public interface IAppCrashAnalytics : IDisposable
{
    bool IsEnabled { get; }

    void RecordBreadcrumb(string eventName, IReadOnlyDictionary<string, string?>? properties = null);

    void CaptureCrash(Exception exception, string source);

    bool SendTestReport();
}

public enum CrashAnalyticsTestReportResult
{
    Sent,
    Disabled,
    Failed,
}

/// <summary>
/// Process-local entry point used by Help commands to send a harmless, metadata-only test event.
/// A sender is installed only by an analytics instance that already passed DSN and consent gates.
/// </summary>
public static class AppCrashAnalyticsRuntime
{
    private static readonly object Sync = new();
    private static Func<bool>? _sendTestReport;

    public static CrashAnalyticsTestReportResult SendTestReport()
    {
        Func<bool>? sender;
        lock (Sync)
            sender = _sendTestReport;
        if (sender is null)
            return CrashAnalyticsTestReportResult.Disabled;

        try
        {
            return sender()
                ? CrashAnalyticsTestReportResult.Sent
                : CrashAnalyticsTestReportResult.Failed;
        }
        catch
        {
            return CrashAnalyticsTestReportResult.Failed;
        }
    }

    public static string UserMessage(CrashAnalyticsTestReportResult result) => result switch
    {
        CrashAnalyticsTestReportResult.Sent =>
            "A privacy-safe test report was sent. It contains app/platform metadata only and no document data.",
        CrashAnalyticsTestReportResult.Disabled =>
            "Crash reporting is off or no release endpoint is configured. Enable it in Options, restart the app, and try again.",
        _ => "The test report could not be sent. Your documents and local diagnostics were not uploaded.",
    };

    public static IDisposable Register(Func<bool> sender)
    {
        ArgumentNullException.ThrowIfNull(sender);
        lock (Sync)
            _sendTestReport = sender;
        return new Registration(sender);
    }

    private sealed class Registration(Func<bool> sender) : IDisposable
    {
        public void Dispose()
        {
            lock (Sync)
            {
                if (_sendTestReport == sender)
                    _sendTestReport = null;
            }
        }
    }
}

public sealed record AppCrashAnalyticsOptions(
    string? Dsn,
    bool IsEnabled,
    string Environment,
    string DsnEnvironmentVariable,
    string ConsentEnvironmentVariable,
    string EnvironmentNameEnvironmentVariable)
{
    public static AppCrashAnalyticsOptions CreateDefault(bool? userConsent = null) =>
        CreateDefault(
            AppProduct.Current,
            System.Environment.GetEnvironmentVariable,
            AppCrashAnalyticsBuildConfiguration.Read,
            userConsent ?? CrashAnalyticsConsentStore.Load().Enabled);

    internal static AppCrashAnalyticsOptions CreateDefault(
        AppProductIdentity identity,
        Func<string, string?> getEnvironmentVariable,
        Func<(string? Dsn, string? Environment)>? getBuildConfiguration = null,
        bool persistedConsent = false)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

        var prefix = identity.DiagnosticsEnvironmentVariable.EndsWith("_DIAGNOSTICS", StringComparison.OrdinalIgnoreCase)
            ? identity.DiagnosticsEnvironmentVariable[..^"_DIAGNOSTICS".Length]
            : identity.ProductDirectoryName.ToUpperInvariant();
        var dsnVariable = prefix + "_SENTRY_DSN";
        var consentVariable = prefix + "_CRASH_ANALYTICS";
        var environmentVariable = prefix + "_SENTRY_ENVIRONMENT";
        var buildConfiguration = getBuildConfiguration?.Invoke() ?? default;
        var dsn = Normalize(getEnvironmentVariable(dsnVariable)) ?? Normalize(buildConfiguration.Dsn);
        var consent = getEnvironmentVariable(consentVariable);
        var environmentDisables =
            string.Equals(consent, "0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(consent, "false", StringComparison.OrdinalIgnoreCase);
        var environmentEnables =
            string.Equals(consent, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(consent, "true", StringComparison.OrdinalIgnoreCase);
        var enabled = dsn is not null
            && !environmentDisables
            && (persistedConsent || environmentEnables);

        return new AppCrashAnalyticsOptions(
            dsn,
            enabled,
            Normalize(getEnvironmentVariable(environmentVariable)) ??
                Normalize(buildConfiguration.Environment) ??
                "production",
            dsnVariable,
            consentVariable,
            environmentVariable);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal static class AppCrashAnalyticsBuildConfiguration
{
    private const string DsnKey = "FreeFamilySentryDsn";
    private const string EnvironmentKey = "FreeFamilySentryEnvironment";

    public static (string? Dsn, string? Environment) Read()
    {
        var metadata = typeof(AppCrashAnalyticsBuildConfiguration).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value, StringComparer.OrdinalIgnoreCase);
        metadata.TryGetValue(DsnKey, out var dsn);
        metadata.TryGetValue(EnvironmentKey, out var environment);
        return (dsn, environment);
    }
}

public sealed class CrashAnalyticsConsentSettings
{
    public bool Enabled { get; set; }
}

/// <summary>Persists the user's per-product remote crash-reporting choice under app data.</summary>
public static class CrashAnalyticsConsentStore
{
    public const string FileName = "crash-analytics-consent.json";

    public static CrashAnalyticsConsentSettings Load(
        IApplicationDataPathProvider? pathProvider = null) =>
        JsonSettingsStore<CrashAnalyticsConsentSettings>
            .ForProductFile(FileName, pathProvider)
            .Load();

    public static bool Save(
        bool enabled,
        IApplicationDataPathProvider? pathProvider = null) =>
        JsonSettingsStore<CrashAnalyticsConsentSettings>
            .ForProductFile(FileName, pathProvider)
            .Save(new CrashAnalyticsConsentSettings { Enabled = enabled });
}

internal sealed class DisabledAppCrashAnalytics : IAppCrashAnalytics
{
    public static DisabledAppCrashAnalytics Instance { get; } = new();

    public bool IsEnabled => false;

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
