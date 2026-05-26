using FluentAssertions;
using Freexcel.App.Host;

namespace Freexcel.App.Host.Tests;

public sealed class AppIssueReportTests
{
    [Fact]
    public void CreateIssueUrl_PrefillsGitHubIssueWithoutWorkbookContent()
    {
        var metadata = new AppDiagnosticsMetadata(
            AppVersion: "Version Test",
            SessionId: "session-123",
            RuntimeDescription: ".NET Test",
            OperatingSystemDescription: "Windows Test",
            ProcessArchitecture: "X64");
        var context = new AppIssueReportContext(
            AppInfo.FeedbackUrl,
            metadata,
            CommitHash: "abcdef12",
            DiagnosticsEnabled: true);

        var url = AppIssueReporter.CreateIssueUrl(context);

        url.Should().StartWith("https://github.com/tony-xmelon/Freexcel/issues/new?");
        var unescaped = Uri.UnescapeDataString(url);
        unescaped.Should().Contain("title=Tester issue: ");
        unescaped.Should().Contain("App version: Version Test");
        unescaped.Should().Contain("Commit: abcdef12");
        unescaped.Should().Contain("OS: Windows Test");
        unescaped.Should().Contain(".NET runtime: .NET Test");
        unescaped.Should().Contain("Diagnostics enabled: yes");
        unescaped.Should().Contain("Session ID: session-123");
        unescaped.Should().NotContain("Book1.xlsx");
        unescaped.Should().NotContain("C:\\");
        unescaped.Should().NotContain("=SUM(");
    }

    [Fact]
    public void CreateDiagnosticsText_UsesCopyablePrivacySafeTemplate()
    {
        var metadata = new AppDiagnosticsMetadata(
            AppVersion: "Version Test",
            SessionId: "session-123",
            RuntimeDescription: ".NET Test",
            OperatingSystemDescription: "Windows Test",
            ProcessArchitecture: "X64");
        var context = new AppIssueReportContext(
            AppInfo.FeedbackUrl,
            metadata,
            CommitHash: "",
            DiagnosticsEnabled: false);

        var text = AppIssueReporter.CreateDiagnosticsText(context);

        text.Should().Contain("Freexcel Diagnostics");
        text.Should().Contain("App version: Version Test");
        text.Should().Contain("Commit: unknown");
        text.Should().Contain("Diagnostics enabled: no");
        text.Should().Contain("Session ID: session-123");
        text.Should().Contain("Do not include workbook contents, formulas, file paths, or private data unless you choose to share them.");
    }

    [Theory]
    [InlineData("0.5.0+a7f74a06bfdc9d446ebca306298a0ea4c18bba6c", "a7f74a06")]
    [InlineData("0.5.0", "unknown")]
    [InlineData("", "unknown")]
    public void ResolveCommitHash_ReadsShortShaFromInformationalVersion(string informationalVersion, string expected)
    {
        AppIssueReporter.ResolveCommitHash(informationalVersion).Should().Be(expected);
    }
}
