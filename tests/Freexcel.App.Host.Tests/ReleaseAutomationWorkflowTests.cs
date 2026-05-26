using System.IO;
using System.Text.RegularExpressions;
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
        workflow.Should().NotContain("FORCE_JAVASCRIPT_ACTIONS_TO_NODE24");
        workflow.Should().Contain("actions/checkout@v6");
        workflow.Should().Contain("actions/setup-dotnet@v5");
        workflow.Should().Contain("dotnet restore Freexcel.slnx");
        workflow.Should().Contain("dotnet build Freexcel.slnx --configuration Release --no-restore");
        workflow.Should().Contain("dotnet test Freexcel.slnx --configuration Release --no-build");
        workflow.Should().Contain("tools/Publish-UserTestBuild.ps1");
        workflow.Should().Contain("-PublishMode SingleFile");
        workflow.Should().Contain("Freexcel-latest-win-x64.exe");
        workflow.Should().Contain("actions/upload-artifact@v7");
        workflow.Should().Contain("gh release create");
        workflow.Should().Contain("gh release upload");
        workflow.Should().Contain("$releaseStamp = Get-Date -AsUTC -Format \"yyyyMMdd-HHmmss\"");
        workflow.Should().Contain("$releaseId = \"$versionSlug-$releaseStamp-run${{ github.run_number }}-attempt${{ github.run_attempt }}\"");
        workflow.Should().Contain("$tag = \"v$releaseId+$shortSha\"");
        workflow.Should().Contain("$releaseName = \"Freexcel tester $releaseId ($shortSha)\"");
        workflow.Should().Contain("\"release_id=$releaseId\" >> $env:GITHUB_OUTPUT");
        workflow.Should().Contain("name: freexcel-${{ steps.meta.outputs.release_id }}-${{ steps.meta.outputs.short_sha }}-win-x64-singlefile");
    }

    [Fact]
    public void TesterReleaseWorkflow_DefaultsToStableReleaseWhenAdvertisingLatestDownload()
    {
        var workflowPath = WorkspaceFileLocator.Find(".github", "workflows", "tester-release.yml");
        var workflow = File.ReadAllText(workflowPath);

        workflow.Should().Contain("Download the stable latest asset: Freexcel-latest-win-x64.exe");

        var prereleaseInput = Regex.Match(workflow, @"(?ms)^\s+prerelease:\s*$.*?^\s+type:\s+boolean\s*$");
        prereleaseInput.Success.Should().BeTrue("the workflow should expose a prerelease dispatch input");
        prereleaseInput.Value.Should().Contain(
            "default: false",
            "GitHub releases/latest excludes prereleases, so the advertised stable latest asset must be backed by stable releases by default");
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
