namespace Freexcel.App.Host;

public static class CrashAnalyticsConsentPlanner
{
    public static bool ShouldPrompt(FreexcelOptions options, AppCrashAnalyticsOptions crashAnalyticsOptions) =>
        !options.CrashAnalyticsPrompted && !string.IsNullOrWhiteSpace(crashAnalyticsOptions.Dsn);

    public static void ApplyConsent(FreexcelOptions options, bool enabled)
    {
        options.CrashAnalyticsEnabled = enabled;
        options.CrashAnalyticsPrompted = true;
    }
}
