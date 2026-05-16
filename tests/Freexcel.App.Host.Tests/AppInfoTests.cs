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
    }

    [Fact]
    public void AboutText_UsesCurrentVersionAndDoesNotNameTooling()
    {
        AppInfo.VersionText.Should().Be("Version 0.5 (Phase 5)");
        AppInfo.AboutText.Should().Contain(AppInfo.VersionText);
        AppInfo.AboutText.Should().Contain("Built with .NET 10, WPF, ClosedXML, OxyPlot.");
        AppInfo.AboutText.Should().NotContain("Claude Code");
    }
}
