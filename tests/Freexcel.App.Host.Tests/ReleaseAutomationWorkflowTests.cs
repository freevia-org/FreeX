using System.IO;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class ReleaseAutomationWorkflowTests
{
    [Fact]
    public void TesterReleaseWorkflow_BuildsTestsPublishesAndUploadsLatestExe()
    {
        var workflowPath = WorkspaceFileLocator.Find(".github", "workflows", "tester-release.yml");
        var workflow = File.ReadAllText(workflowPath);

        workflow.Should().Contain("workflow_dispatch:");
        workflow.Should().Contain("permissions:");
        workflow.Should().Contain("contents: write");
        workflow.Should().Contain("FORCE_JAVASCRIPT_ACTIONS_TO_NODE24: \"true\"");
        workflow.Should().Contain("dotnet restore Freexcel.slnx");
        workflow.Should().Contain("dotnet build Freexcel.slnx --configuration Release --no-restore");
        workflow.Should().Contain("dotnet test Freexcel.slnx --configuration Release --no-build");
        workflow.Should().Contain("tools/Publish-UserTestBuild.ps1");
        workflow.Should().Contain("-PublishMode SingleFile");
        workflow.Should().Contain("Freexcel-latest-win-x64.exe");
        workflow.Should().Contain("actions/upload-artifact@v4");
        workflow.Should().Contain("gh release create");
        workflow.Should().Contain("gh release upload");
        workflow.Should().Contain("v$versionSlug+$shortSha");
    }

    [Fact]
    public void TestDistributionPlan_LinksToLatestTesterDownload()
    {
        var planPath = WorkspaceFileLocator.Find("docs", "TEST_DISTRIBUTION_PLAN.md");
        var plan = File.ReadAllText(planPath);

        plan.Should().Contain("Latest tester download");
        plan.Should().Contain("Freexcel-latest-win-x64.exe");
        plan.Should().Contain("https://github.com/tony-xmelon/Freexcel/releases/latest/download/Freexcel-latest-win-x64.exe");
    }
}
