extern alias ProductionWpf;

using FluentAssertions;
using FreeX.App.Services;
using AppInfo = ProductionWpf::FreeX.App.Host.AppInfo;

namespace FreeX.App.Host.Tests;

public sealed class AppInfoTests
{
    [Fact]
    public void ProjectUrls_PointAtFreeXRepository()
    {
        AppInfo.HelpUrl.Should().Be("https://github.com/tony-xmelon/FreeX");
        AppInfo.FeedbackUrl.Should().Be("https://github.com/tony-xmelon/FreeX/issues/new");
        AppInfo.LatestReleaseUrl.Should().Be("https://github.com/tony-xmelon/FreeX/releases/latest");
        AppInfo.LatestTesterDownloadUrl.Should().Be("https://github.com/tony-xmelon/FreeX/releases/latest/download/FreeX-latest-win-x64.exe");
    }

    [Fact]
    public void SharedNotices_DelegateToAppHelpInfo()
    {
        AppInfo.ReleaseChannel.Should().Be(AppHelpInfo.ReleaseChannel);
        AppInfo.HelpUrl.Should().Be(AppHelpInfo.HelpUrl);
        AppInfo.FeedbackUrl.Should().Be(AppHelpInfo.FeedbackUrl);
        AppInfo.LatestReleaseUrl.Should().Be(AppHelpInfo.LatestReleaseUrl);
        AppInfo.TrademarkNotice.Should().Be(AppHelpInfo.TrademarkNotice);
        AppInfo.ProjectLicenseNotice.Should().Be(AppHelpInfo.ProjectLicenseNotice);
        AppInfo.PrivacyNotice.Should().Be(AppHelpInfo.PrivacyNotice);
        AppInfo.CompatibilityNotice.Should().Be(AppHelpInfo.CompatibilityNotice);
        AppInfo.SourceNotice.Should().Be(AppHelpInfo.SourceNotice);
    }

    [Fact]
    public void AboutText_UsesCurrentVersionAndDoesNotNameTooling()
    {
        AppInfo.VersionText.Should().Be(AppHelpInfo.GetVersionText(typeof(AppInfo).Assembly));
        AppInfo.ExactVersionText.Should().Be(AppHelpInfo.GetBuildVersionText(typeof(AppInfo).Assembly));
        AppInfo.AboutText.Should().Contain(AppInfo.VersionText);
        AppInfo.AboutText.Should().Contain("A free spreadsheet app for XLSX editing with open-only legacy XLS/XLSB import.");
        AppInfo.AboutText.Should().Contain("Built with .NET 10, WPF, ClosedXML, OxyPlot.");
        AppInfo.AboutText.Should().Contain(AppInfo.TrademarkNotice);
        AppInfo.AboutText.Should().Contain(AppInfo.CompatibilityNotice);
        AppInfo.AboutText.Should().Contain(AppInfo.ProjectLicenseNotice);
        AppInfo.AboutText.Should().Contain(AppInfo.PrivacyNotice);
        AppInfo.AboutText.Should().Contain(AppInfo.ThirdPartyRuntimeNotice);
        AppInfo.AboutText.Should().Contain(AppInfo.SourceNotice);
        AppInfo.AboutText.Should().Contain("Help > Legal Notices");
        AppInfo.SourceNotice.Should().Contain("Full project license, legal notice, privacy notice, third-party notices, and bundled third-party license texts");
        AppInfo.AboutText.Should().Contain("ClosedXML");
        AppInfo.ThirdPartyRuntimeNotice.Should().Contain("LGPL-licensed components");
        AppInfo.ThirdPartyRuntimeNotice.Should().Contain("distribution requirements");
        AppInfo.ThirdPartyRuntimeNotice.Should().Contain("Release packaging must preserve those materials.");
        AppInfo.AboutText.Should().Contain("local desktop app");
        AppInfo.AboutText.Should().NotContain("%LOCALAPPDATA%");
        AppInfo.AboutText.Should().NotContain("Claude Code");
    }

    [Fact]
    public void VersionText_UsesAssemblyInformationalVersionWithoutCommitMetadata()
    {
        AppInfo.FormatVersionText("0.8.42+abcdef12").Should().Be("Version 0.8.42 (Tester Release)");
        AppInfo.FormatVersionText("0.5.0").Should().Be("Version 0.5 (Tester Release)");
    }
}
