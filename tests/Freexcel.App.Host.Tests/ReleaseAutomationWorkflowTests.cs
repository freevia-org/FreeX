using System.IO;
using System.Text.Json;
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
        workflow.Should().Contain("$runNumber = [int]$env:GITHUB_RUN_NUMBER");
        workflow.Should().Contain("$runAttempt = [int]$env:GITHUB_RUN_ATTEMPT");
        workflow.Should().Contain("$progressPath = \"release/progress.json\"");
        workflow.Should().Contain("$releaseProgress = Get-Content -LiteralPath $progressPath -Raw | ConvertFrom-Json");
        workflow.Should().Contain("$overallCompletion = [int]$releaseProgress.overallCompletion");
        workflow.Should().Contain("elseif ($overallCompletion -ge 90) { $minor = 6 }");
        workflow.Should().Contain("$versionLabel = \"$major.$minor.$releasePatch\"");
        workflow.Should().Contain("$releaseStamp = Get-Date -AsUTC -Format \"yyyy-MM-dd-HH-mm-ss\"");
        workflow.Should().Contain("$releaseId = \"$versionSlug-$releaseStamp-run$runNumber-attempt$runAttempt\"");
        workflow.Should().Contain("$tag = \"v$releaseId+$shortSha\"");
        workflow.Should().Contain("$displayVersion = $versionLabel.Trim()");
        workflow.Should().Contain("$releaseName = \"Freexcel (Test Release) $displayVersion ($releaseStamp) Run $runNumber Attempt $runAttempt ($shortSha)\"");
        workflow.Should().Contain("\"release_id=$releaseId\" >> $env:GITHUB_OUTPUT");
        workflow.Should().Contain("name: freexcel-${{ steps.meta.outputs.release_id }}-${{ steps.meta.outputs.short_sha }}-win-x64-singlefile");
    }

    [Fact]
    public void ReleaseProgressJson_DefinesAutomaticTesterVersionBand()
    {
        var progressPath = WorkspaceFileLocator.Find("release", "progress.json");
        using var document = JsonDocument.Parse(File.ReadAllText(progressPath));
        var root = document.RootElement;

        root.GetProperty("major").GetInt32().Should().Be(0);
        root.GetProperty("overallCompletion").GetInt32().Should().BeInRange(90, 92);
        root.GetProperty("releasePatchBase").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        root.GetProperty("releasePatchSource").GetString().Should().Be("github_run_number");
        root.GetProperty("channel").GetString().Should().Be("test");
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
