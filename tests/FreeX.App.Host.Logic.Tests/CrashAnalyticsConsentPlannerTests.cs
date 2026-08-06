using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Host.Tests;

public sealed class CrashAnalyticsConsentWorkflowPlannerTests
{
    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    public void ShouldPrompt_OnlyWhenEndpointExistsAndUserHasNotAnswered(
        bool prompted,
        bool endpointMissing,
        bool disabledByEnvironment,
        bool expected)
    {
        CrashAnalyticsConsentWorkflowPlanner.ShouldPrompt(
                prompted,
                endpointMissing ? null : "https://public@example.ingest.sentry.io/1",
                disabledByEnvironment)
            .Should().Be(expected);
    }

    [Fact]
    public void AppStartup_MarksUserPromptedAndStoresChoice()
    {
        var source = DialogSourceTestSupport.ReadHostSources("App.xaml.cs");

        source.Should().Contain("options.CrashAnalyticsEnabled = accepted;");
        source.Should().Contain("options.CrashAnalyticsPrompted = true;");
        source.Should().Contain("CrashAnalyticsConsentWorkflowPlanner.ShouldPrompt(");
    }
}
