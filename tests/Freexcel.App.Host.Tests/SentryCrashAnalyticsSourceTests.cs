using System.IO;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class SentryCrashAnalyticsSourceTests
{
    [Fact]
    public void HostProject_ReferencesSentrySdk()
    {
        var project = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "Freexcel.App.Host.csproj"));

        project.Should().Contain("<PackageReference Include=\"Sentry\" Version=\"6.5.0\"");
    }

    [Fact]
    public void AppStartup_RegistersCrashAnalyticsAndInitializesIt()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "App.xaml.cs"));

        source.Should().Contain("AppCrashAnalyticsOptions.CreateDefault(options.CrashAnalyticsEnabled)");
        source.Should().Contain("PromptForCrashAnalyticsConsentIfNeeded(options, crashAnalyticsOptions)");
        source.Should().Contain("AddSingleton<ICrashAnalytics, SentryCrashAnalytics>()");
        source.Should().Contain("crashAnalytics.Initialize(crashAnalyticsOptions, diagnosticsMetadata)");
    }

    [Fact]
    public void SentryCrashAnalytics_ConfiguresPrivacySafeCrashEvents()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "SentryCrashAnalytics.cs"));

        source.Should().Contain("options.SendDefaultPii = false");
        source.Should().Contain("options.Dsn = crashAnalyticsOptions.Dsn");
        source.Should().Contain("options.Release = metadata.AppVersion");
        source.Should().Contain("options.Environment = crashAnalyticsOptions.Environment");
        source.Should().Contain("SentrySdk.CaptureException(exception)");
        source.Should().Contain("SentrySdk.AddBreadcrumb");
    }
}
