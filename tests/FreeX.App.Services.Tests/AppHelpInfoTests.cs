using FreeX.App.Services;
using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class AppHelpInfoTests
{
    [Fact]
    public void Links_ExposeStableProjectHelpTargets()
    {
        AppHelpInfo.HelpUrl.Should().Be("https://github.com/tony-xmelon/FreeX");
        AppHelpInfo.FeedbackUrl.Should().Be("https://github.com/tony-xmelon/FreeX/issues/new");
        AppHelpInfo.LatestReleaseUrl.Should().Be("https://github.com/tony-xmelon/FreeX/releases/latest");
        AppHelpInfo.ReleaseChannel.Should().Be("test");
    }

    [Fact]
    public void AboutText_UsesPortableDesktopNotice()
    {
        var text = AppHelpInfo.BuildAboutText(
            AppHelpInfo.FormatVersionText("0.8.42+abcdef12"),
            "Built with .NET 10, Avalonia, ClosedXML.");

        text.Should().Contain("FreeX");
        text.Should().Contain("Version 0.8.42 (Tester Release)");
        text.Should().Contain("A free spreadsheet app for XLSX editing with open-only legacy XLS/XLSB import.");
        text.Should().Contain("Built with .NET 10, Avalonia, ClosedXML.");
        text.Should().Contain(AppHelpInfo.TrademarkNotice);
        text.Should().Contain(AppHelpInfo.CompatibilityNotice);
        text.Should().Contain(AppHelpInfo.ProjectLicenseNotice);
        text.Should().Contain(AppHelpInfo.PrivacyNotice);
        text.Should().Contain(AppHelpInfo.SourceNotice);
        text.Should().Contain("Help > Legal Notices");
        text.Should().NotContain("WPF");
        text.Should().NotContain("%LOCALAPPDATA%");
    }

    [Fact]
    public void FormatVersionText_TrimsBuildMetadataAndCompressesPatchZero()
    {
        AppHelpInfo.FormatVersionText("0.8.42+abcdef12").Should().Be("Version 0.8.42 (Tester Release)");
        AppHelpInfo.FormatVersionText("0.5.0").Should().Be("Version 0.5 (Tester Release)");
        AppHelpInfo.FormatVersionText(null).Should().Be("Version 0.5 (Tester Release)");
    }
}
