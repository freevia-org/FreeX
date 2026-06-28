namespace FreeX.App.Services;

public static class CrashAnalyticsConsentWorkflowPlanner
{
    public static bool ShouldPrompt(
        bool hasPrompted,
        string? dsn,
        bool isDisabledByEnvironment) =>
        !hasPrompted &&
        !isDisabledByEnvironment &&
        !string.IsNullOrWhiteSpace(dsn);
}
