using FluentAssertions;
using Freexcel.App.Host;

namespace Freexcel.App.Host.Tests;

public sealed class AppInfoTests
{
    [Fact]
    public void ProjectUrls_PointAtFreexcelRepository()
    {
        AppInfo.HelpUrl.Should().Be("https://github.com/tony-xmelon/Freexcel");
        AppInfo.FeedbackUrl.Should().Be("https://github.com/tony-xmelon/Freexcel/issues/new");
        AppInfo.LatestReleaseUrl.Should().Be("https://github.com/tony-xmelon/Freexcel/releases/latest");
        AppInfo.LatestTesterDownloadUrl.Should().Be("https://github.com/tony-xmelon/Freexcel/releases/latest/download/Freexcel-latest-win-x64.exe");
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
