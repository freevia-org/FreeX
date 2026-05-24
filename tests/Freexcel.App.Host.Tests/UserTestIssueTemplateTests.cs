using System.IO;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class UserTestIssueTemplateTests
{
    [Fact]
    public void UserTestIssueTemplate_AsksForBuildAndOptionalDiagnostics()
    {
        var templatePath = WorkspaceFileLocator.Find(".github", "ISSUE_TEMPLATE", "user-test-report.yml");
        var template = File.ReadAllText(templatePath);

        template.Should().Contain("Freexcel user test report");
        template.Should().Contain("App version/build");
        template.Should().Contain("%LOCALAPPDATA%\\Freexcel\\Diagnostics");
        template.Should().Contain("CrashReports");
        template.Should().Contain("Expected result");
        template.Should().Contain("Actual result");
    }
}
