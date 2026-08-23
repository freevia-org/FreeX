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

        url.Should().Contain("title=FreeP%20%26%20Slides%20feedback%3A%20");
        decoded.Should().Contain("App: FreeP & Slides");
        decoded.Should().Contain("Version: 1.2.3 preview+sha");
        decoded.Should().Contain("OS: Windows 11 / x64");
        decoded.Should().NotContain("session-not-published");
        decoded.Should().NotContain("C:\\");
        decoded.Should().Contain("do not include document contents, filenames, file paths");
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
    }
}
