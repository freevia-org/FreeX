using Xunit.Sdk;

namespace FreeX.App.Host.Tests;

internal sealed class UiE2eFactAttribute : FactAttribute
{
    public UiE2eFactAttribute()
    {
        Skip = UiE2ePreconditions.SkipReason;
    }
}

internal static class UiE2ePreconditions
{
    public const string OptInEnvironmentVariable = "FREEX_UIE2E";
    public const string OptInEnvironmentValue = "1";

    public static string? SkipReason
    {
        get
        {
            if (!string.Equals(Environment.GetEnvironmentVariable(OptInEnvironmentVariable), OptInEnvironmentValue, StringComparison.OrdinalIgnoreCase))
                return "UIE2E tests require explicit opt-in with FREEX_UIE2E=1.";

            if (!OperatingSystem.IsWindows())
                return "UIE2E tests require Windows.";

            if (!Environment.UserInteractive)
                return "UIE2E tests require an interactive desktop session.";

            return null;
        }
    }

    public static void SkipUnlessEnabled()
    {
        var skipReason = SkipReason;
        if (skipReason is not null)
            throw SkipException.ForSkip(skipReason);
    }
}
