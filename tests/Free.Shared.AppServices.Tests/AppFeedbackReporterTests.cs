namespace Free.Shared.AppServices.Tests;

public sealed class AppFeedbackReporterTests
{
    [Fact]
    public void CreateIssueUrl_PrefillsEncodedAppVersionAndPlatformWithoutPaths()
    {
        var metadata = new AppDiagnosticsMetadata(
            "1.2.3 preview+sha",
            "session-not-published",
            ".NET 10 & runtime",
            "Windows 11 / x64",
            "X64");

        var url = AppFeedbackReporter.CreateIssueUrl("FreeP & Slides", metadata);
        var decoded = Uri.UnescapeDataString(url);

        url.Should().Contain("template=user-test-report.yml");
        decoded.Should().Contain("[FreeP & Slides 1.2.3 preview+sha | Windows 11 / x64 | X64]");
        decoded.Should().NotContain("session-not-published");
        decoded.Should().NotContain("C:\\");
        decoded.Should().NotContain("body=");
    }

    [Fact]
    public void CreateIssueUrl_AppendsToExistingQueryString()
    {
        var metadata = AppDiagnosticsMetadata.Create("1.0.0");

        var url = AppFeedbackReporter.CreateIssueUrl(
            "FreeW",
            metadata,
            "https://example.invalid/issues/new?template=feedback.yml");

        url.Should().Contain("?template=feedback.yml&title=");
        url.Should().NotContain("user-test-report.yml");
    }
}
