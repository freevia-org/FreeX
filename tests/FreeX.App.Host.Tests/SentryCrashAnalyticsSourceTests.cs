using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed class SentryCrashAnalyticsSourceTests
{
    [Fact]
    public void HostProject_ReferencesSentrySdk()
    {
        var project = DialogSourceTestSupport.ReadHostSources("FreeX.App.Host.csproj");

        project.Should().Contain("<PackageReference Include=\"Sentry\"");
    }

    [Fact]
    public void AppStartup_RegistersCrashAnalyticsAndInitializesIt()
    {
        var source = DialogSourceTestSupport.ReadHostSources("App.xaml.cs");

        source.Should().Contain("AppCrashAnalyticsOptions.CreateDefault(options.CrashAnalyticsEnabled)");
        source.Should().Contain("PromptForCrashAnalyticsConsentIfNeeded(optionsRuntimeSession, crashAnalyticsOptions)");
        source.Should().Contain("UiText.Get(\"Startup_CrashReportsConsentPrompt\")");
        UiText.Get("Startup_CrashReportsConsentPrompt").Should().Contain("exception message, and stack trace");
        UiText.Get("Startup_CrashReportsConsentPrompt").Should().Contain("exception details can occasionally include sensitive values");
        source.Should().Contain("AddSingleton<ICrashAnalytics, SentryCrashAnalytics>()");
        source.Should().Contain("crashAnalytics.Initialize(crashAnalyticsOptions, diagnosticsMetadata)");
    }

    [Fact]
    public void SentryCrashAnalytics_ConfiguresPrivacySafeCrashEvents()
    {
        var source = DialogSourceTestSupport.ReadHostSources("SentryCrashAnalytics.cs");

        source.Should().Contain("options.SendDefaultPii = false");
        source.Should().Contain("options.Dsn = crashAnalyticsOptions.Dsn");
        source.Should().Contain("options.Release = metadata.AppVersion");
        source.Should().Contain("options.Environment = crashAnalyticsOptions.Environment");
        source.Should().Contain("SentrySdk.CaptureException(exception)");
        source.Should().Contain("SentrySdk.AddBreadcrumb");
    }
}
