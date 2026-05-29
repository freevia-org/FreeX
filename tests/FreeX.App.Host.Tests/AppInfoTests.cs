using FluentAssertions;
using FreeX.App.Host;

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
    public void AboutText_UsesCurrentVersionAndDoesNotNameTooling()
    {
        AppInfo.VersionText.Should().Be("Version 0.5 (Tester Release)");
        AppInfo.AboutText.Should().Contain(AppInfo.VersionText);
        AppInfo.AboutText.Should().Contain("Built with .NET 10, WPF, ClosedXML, OxyPlot.");
        AppInfo.AboutText.Should().NotContain("Claude Code");
    }
}
