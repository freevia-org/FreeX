using Xunit.Sdk;

namespace FreeX.App.Host.Tests;

internal static class UiE2ePreconditions
{
    public static void SkipUnlessEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("FREEX_UIE2E"), "1", StringComparison.OrdinalIgnoreCase))
            throw SkipException.ForSkip("UIE2E tests require explicit opt-in with FREEX_UIE2E=1.");

        if (!OperatingSystem.IsWindows())
            throw SkipException.ForSkip("UIE2E tests require Windows.");

        if (!Environment.UserInteractive)
            throw SkipException.ForSkip("UIE2E tests require an interactive desktop session.");
    }
}
