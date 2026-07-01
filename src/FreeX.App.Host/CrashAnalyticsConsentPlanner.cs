using FreeX.App.Services;

namespace FreeX.App.Host;

// Host adapter: startup owns FreeXOptions/AppCrashAnalyticsOptions persistence;
// CrashAnalyticsConsentWorkflowPlanner owns the portable prompt decision.
public static class CrashAnalyticsConsentPlanner
{
    public static bool ShouldPrompt(FreeXOptions options, AppCrashAnalyticsOptions crashAnalyticsOptions) =>
        CrashAnalyticsConsentWorkflowPlanner.ShouldPrompt(
            options.CrashAnalyticsPrompted,
            crashAnalyticsOptions.Dsn,
            crashAnalyticsOptions.IsDisabledByEnvironment);

    public static void ApplyConsent(FreeXOptions options, bool enabled)
    {
        options.CrashAnalyticsEnabled = enabled;
        options.CrashAnalyticsPrompted = true;
    }
}
