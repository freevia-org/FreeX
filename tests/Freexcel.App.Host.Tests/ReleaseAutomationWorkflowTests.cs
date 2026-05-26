using System.IO;
using System.Text.RegularExpressions;
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
        workflow.Should().Contain("release_notes:");
        workflow.Should().Contain("permissions:");
        workflow.Should().Contain("contents: write");
        workflow.Should().NotContain("FORCE_JAVASCRIPT_ACTIONS_TO_NODE24");
        workflow.Should().Contain("actions/checkout@v6");
        workflow.Should().Contain("actions/setup-dotnet@v5");
        workflow.Should().Contain("dotnet restore Freexcel.slnx");
        workflow.Should().Contain("dotnet build Freexcel.slnx --configuration Release --no-restore");
        workflow.Should().Contain("dotnet test Freexcel.slnx --configuration Release --no-build");
        workflow.Should().Contain("tools/Publish-UserTestBuild.ps1");
        workflow.Should().Contain("-RuntimeIdentifier win-x64");
        workflow.Should().Contain("-PublishMode SingleFile");
        workflow.Should().Contain("Publish unsigned local MSIX");
        workflow.Should().Contain("-PublishMode Msix");
        workflow.Should().Contain("Freexcel-latest-win-x64.exe");
        workflow.Should().Contain("Freexcel-latest-win-x64.msix");
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
        workflow.Should().Contain("name: freexcel-${{ steps.meta.outputs.release_id }}-${{ steps.meta.outputs.short_sha }}-win-x64-msix");
        workflow.Should().Contain("path: artifacts/upload/freexcel-*-win-x64-msix.msix");
        workflow.Should().Contain("path: artifacts/upload/*.msix.sha256");
        workflow.Should().Contain("$assetPaths = @(");
        workflow.Should().Contain("\"artifacts/upload/*.msix.sha256\"");
        workflow.Should().Contain("gh release create $tag @assetPaths --target $env:GITHUB_SHA --title $title --notes $notes --draft @prereleaseArgs");
        workflow.Should().Contain("gh release edit $tag --draft=false @latestArgs");
        workflow.Should().Contain("gh release upload $tag @assetPaths --clobber");
        workflow.Should().NotContain("gh release create $tag --target $env:GITHUB_SHA --title $title --notes $notes @prereleaseArgs");
        workflow.Should().Contain("$latestArgs += \"--latest\"");
        workflow.Should().Contain("Additional tester notes:");
        workflow.Should().Contain("FREEXCEL_RELEASE_NOTES: ${{ inputs.release_notes }}");
        workflow.Should().Contain("$extraNotes = $env:FREEXCEL_RELEASE_NOTES");
    }

    [Fact]
    public void UserTestPublishScript_PublishesFrameworkDependentRuntimeSpecificBuild()
    {
        var scriptPath = WorkspaceFileLocator.Find("tools", "Publish-UserTestBuild.ps1");
        var script = File.ReadAllText(scriptPath);

        script.Should().Contain("[string]$RuntimeIdentifier = \"win-x64\"");
        script.Should().Contain("\"-r\", $RuntimeIdentifier");
        script.Should().Contain("\"--self-contained\", \"false\"");
    }

    [Fact]
    public void UserTestPublishScript_CanPackageUnsignedLocalMsix()
    {
        var scriptPath = WorkspaceFileLocator.Find("tools", "Publish-UserTestBuild.ps1");
        var script = File.ReadAllText(scriptPath);

        script.Should().Contain("[ValidateSet(\"SingleFile\", \"Folder\", \"Msix\")]");
        script.Should().Contain("$artifactMsixPath = Join-Path $artifactRoot \"$artifactName.msix\"");
        script.Should().Contain("<Identity Name=\"Freexcel.Tester\" Publisher=\"CN=FreexcelLocal\" Version=\"$msixVersion\" />");
        script.Should().Contain("EntryPoint=\"Windows.FullTrustApplication\"");
        script.Should().Contain("<rescap:Capability Name=\"runFullTrust\" />");
        script.Should().Contain("Get-Command makeappx.exe");
        script.Should().Contain("makeappx.exe was not found. Install the Windows SDK");
        script.Should().Contain("pack /d $publishDir /p $artifactMsixPath /o");
        script.Should().Contain("Set-Content -LiteralPath \"$artifactMsixPath.sha256\"");
    }

    [Fact]
    public void TesterReleaseWorkflow_DefaultsToStableReleaseWhenAdvertisingLatestDownload()
    {
        var workflowPath = WorkspaceFileLocator.Find(".github", "workflows", "tester-release.yml");
        var workflow = File.ReadAllText(workflowPath);

        workflow.Should().Contain("Download the stable latest asset: Freexcel-latest-win-x64.exe");
        workflow.Should().Contain("Unsigned local MSIX package: Freexcel-latest-win-x64.msix");

        var prereleaseInput = Regex.Match(workflow, @"(?ms)^\s+prerelease:\s*$.*?^\s+type:\s+boolean\s*$");
        prereleaseInput.Success.Should().BeTrue("the workflow should expose a prerelease dispatch input");
        prereleaseInput.Value.Should().Contain(
            "default: false",
            "GitHub releases/latest excludes prereleases, so the advertised stable latest asset must be backed by stable releases by default");
    }

    [Fact]
    public void TesterReleaseWorkflow_RefreshesReleaseNotesWhenReleaseAlreadyExists()
    {
        var workflowPath = WorkspaceFileLocator.Find(".github", "workflows", "tester-release.yml");
        var workflow = File.ReadAllText(workflowPath);
        var existingReleaseBlock = Regex.Match(workflow, @"(?ms)if \(\$releaseExists\) \{.*?\} else \{");

        existingReleaseBlock.Success.Should().BeTrue("the rerun path should be explicit and guarded separately from first release creation");
        existingReleaseBlock.Value.Should().Contain("gh release upload $tag @assetPaths --clobber");
        existingReleaseBlock.Value.Should().Contain("gh release edit $tag --title $title --notes $notes @prereleaseArgs @latestArgs");
        existingReleaseBlock.Value.Should().NotContain("gh release edit $tag --title $title @prereleaseArgs @latestArgs");
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
