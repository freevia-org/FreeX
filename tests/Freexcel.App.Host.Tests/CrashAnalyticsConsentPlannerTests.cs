using FluentAssertions;
using Freexcel.App.Host;

namespace Freexcel.App.Host.Tests;

public sealed class CrashAnalyticsConsentPlannerTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void ShouldPrompt_OnlyWhenEndpointExistsAndUserHasNotAnswered(
        bool prompted,
        bool endpointMissing,
        bool expected)
    {
        var options = new FreexcelOptions { CrashAnalyticsPrompted = prompted };
        var crashOptions = new AppCrashAnalyticsOptions(
            endpointMissing ? null : "https://public@example.ingest.sentry.io/1",
            IsEnabled: false);

        CrashAnalyticsConsentPlanner.ShouldPrompt(options, crashOptions).Should().Be(expected);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ApplyConsent_MarksUserPromptedAndStoresChoice(bool enabled)
    {
        var options = new FreexcelOptions();

        CrashAnalyticsConsentPlanner.ApplyConsent(options, enabled);

        options.CrashAnalyticsEnabled.Should().Be(enabled);
        options.CrashAnalyticsPrompted.Should().BeTrue();
    }
}
