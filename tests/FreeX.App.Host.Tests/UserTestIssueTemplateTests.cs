using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class UserTestIssueTemplateTests
{
    [Fact]
    public void UserTestIssueTemplate_AsksForBuildAndOptionalDiagnostics()
    {
        var template = WorkspaceFileLocator.ReadAllText(".github", "ISSUE_TEMPLATE", "user-test-report.yml");

        template.Should().Contain("FreeX user test report");
        template.Should().Contain("App version/build");
        template.Should().Contain("tester-release");
        template.Should().NotContain("phase-5");
        template.Should().Contain("%LOCALAPPDATA%\\FreeX\\Diagnostics");
        template.Should().Contain("CrashReports");
        template.Should().Contain("Expected result");
        template.Should().Contain("Actual result");
    }
}
