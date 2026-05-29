namespace FreeX.App.Host;

public static class CrashAnalyticsConsentPlanner
{
    public static bool ShouldPrompt(FreeXOptions options, AppCrashAnalyticsOptions crashAnalyticsOptions) =>
        !options.CrashAnalyticsPrompted &&
        !crashAnalyticsOptions.IsDisabledByEnvironment &&
        !string.IsNullOrWhiteSpace(crashAnalyticsOptions.Dsn);

    public static void ApplyConsent(FreeXOptions options, bool enabled)
    {
        options.CrashAnalyticsEnabled = enabled;
        options.CrashAnalyticsPrompted = true;
    }
}
