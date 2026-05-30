using System.IO;
using System.Text.RegularExpressions;
using System.Text.Json;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class ReleaseAutomationWorkflowTests
{
    [Fact]
    public void TesterReleaseWorkflow_BuildsTestsPublishesAndUploadsLatestExe()
    {
        var workflowPath = WorkspaceFileLocator.Find(".github", "workflows", "tester-release.yml");
        var workflow = File.ReadAllText(workflowPath);

        workflow.Should().Contain("workflow_dispatch:");
        workflow.Should().Contain("release_notes:");
        workflow.Should().Contain("public_preview_candidate:");
        workflow.Should().Contain("accessibility_keyboard_only:");
        workflow.Should().Contain("accessibility_screen_reader:");
        workflow.Should().Contain("accessibility_uia_catalog:");
        workflow.Should().Contain("accessibility_known_issues:");
        workflow.Should().Contain("permissions:");
        workflow.Should().Contain("contents: write");
        workflow.Should().NotContain("FORCE_JAVASCRIPT_ACTIONS_TO_NODE24");
        workflow.Should().Contain("actions/checkout@v6");
        workflow.Should().Contain("actions/setup-dotnet@v5");
        workflow.Should().Contain("name: Repository preflight");
        workflow.Should().Contain("powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\\Test-RepositoryPreflight.ps1");
        workflow.Should().Contain("dotnet restore FreeX.slnx");
        workflow.Should().Contain("dotnet build FreeX.slnx --configuration Release --no-restore --disable-build-servers -p:UseSharedCompilation=false -p:NodeReuse=false /nr:false -m:1");
        workflow.Should().Contain("dotnet test FreeX.slnx --configuration Release --no-build --logger \"trx;LogFileName=tests.trx\" --disable-build-servers -p:UseSharedCompilation=false -p:NodeReuse=false /nr:false -m:1");
        workflow.Should().Contain("if: always()");
        workflow.Should().Contain("name: freex-${{ github.run_id }}-${{ github.run_attempt }}-test-results");
        workflow.Should().Contain("path: \"**/TestResults/tests.trx\"");
        workflow.Should().Contain("if-no-files-found: warn");
        workflow.Should().Contain("tools/Publish-UserTestBuild.ps1");
        workflow.Should().Contain("-RuntimeIdentifier win-x64");
        workflow.Should().Contain("-PublishMode SingleFile");
        workflow.Should().Contain("Publish MSIX package");
        workflow.Should().Contain("secrets.FREEX_MSIX_CERTIFICATE_BASE64");
        workflow.Should().Contain("secrets.FREEX_MSIX_CERTIFICATE_PASSWORD");
        workflow.Should().Contain("vars.FREEX_MSIX_TIMESTAMP_URL");
        workflow.Should().Contain("-MsixCertificatePath");
        workflow.Should().Contain("-PublishMode Msix");
        workflow.Should().Contain("FreeX-latest-win-x64.exe");
        workflow.Should().Contain("FreeX-latest-win-x64.exe.sha256");
        workflow.Should().Contain("FreeX-latest-win-x64.msix");
        workflow.Should().Contain("actions/upload-artifact@v7");
        workflow.Should().Contain("gh release create");
        workflow.Should().Contain("gh release upload");
        workflow.Should().Contain("$runNumber = [int]$env:GITHUB_RUN_NUMBER");
        workflow.Should().Contain("$runAttempt = [int]$env:GITHUB_RUN_ATTEMPT");
        workflow.Should().Contain("$progressPath = \"release/progress.json\"");
        workflow.Should().Contain("$releaseProgress = Get-Content -LiteralPath $progressPath -Raw | ConvertFrom-Json");
        workflow.Should().Contain("$overallCompletion = [int]$releaseProgress.overallCompletion");
        workflow.Should().Contain("$releasePatchBase = [int]$releaseProgress.releasePatchBase");
        workflow.Should().Contain("$channel = [string]$releaseProgress.channel");
        workflow.Should().Contain("release/progress.json major must be non-negative.");
        workflow.Should().Contain("release/progress.json overallCompletion must be between 0 and 100.");
        workflow.Should().Contain("release/progress.json releasePatchBase must be non-negative.");
        workflow.Should().Contain("Unsupported release channel '$channel'.");
        workflow.Should().Contain("elseif ($overallCompletion -ge 93) { $minor = 7 }");
        workflow.Should().Contain("elseif ($overallCompletion -ge 90) { $minor = 6 }");
        workflow.Should().Contain("$versionLabel = \"$major.$minor.$releasePatch\"");
        workflow.Should().Contain("$releaseStamp = Get-Date -AsUTC -Format \"yyyy-MM-dd-HH-mm-ss\"");
        workflow.Should().Contain("$releaseId = \"$versionSlug-$releaseStamp-run$runNumber-attempt$runAttempt\"");
        workflow.Should().Contain("$tag = \"v$releaseId+$shortSha\"");
        workflow.Should().Contain("$displayVersion = $versionLabel.Trim()");
        workflow.Should().Contain("$releaseName = \"FreeX (Test Release) $displayVersion ($releaseStamp) Run $runNumber Attempt $runAttempt ($shortSha)\"");
        workflow.Should().Contain("\"release_id=$releaseId\" >> $env:GITHUB_OUTPUT");
        workflow.Should().Contain("name: freex-${{ steps.meta.outputs.release_id }}-${{ steps.meta.outputs.short_sha }}-win-x64-singlefile");
        workflow.Should().Contain("name: freex-${{ steps.meta.outputs.release_id }}-${{ steps.meta.outputs.short_sha }}-win-x64-msix");
        workflow.Should().Contain("name: freex-${{ steps.meta.outputs.release_id }}-${{ steps.meta.outputs.short_sha }}-win-x64-singlefile-sha256");
        workflow.Should().Contain("path: artifacts/upload/freex-*-win-x64-msix.msix");
        workflow.Should().Contain("path: artifacts/upload/*.exe.sha256");
        workflow.Should().Contain("path: artifacts/upload/*.msix.sha256");
        workflow.Should().Contain("$assetPaths = @(");
        workflow.Should().Contain("\"artifacts/upload/*.exe.sha256\"");
        workflow.Should().Contain("\"artifacts/upload/*.msix.sha256\"");
        workflow.Should().Contain("gh release create $tag @assetPaths --target $env:GITHUB_SHA --title $title --notes $notes --draft @prereleaseArgs");
        workflow.Should().Contain("gh release edit $tag --draft=false @latestArgs");
        workflow.Should().Contain("gh release upload $tag @assetPaths --clobber");
        workflow.Should().NotContain("gh release create $tag --target $env:GITHUB_SHA --title $title --notes $notes @prereleaseArgs");
        workflow.Should().Contain("$latestArgs += \"--latest\"");
        workflow.Should().Contain("Additional tester notes:");
        workflow.Should().Contain("FREEX_RELEASE_NOTES: ${{ inputs.release_notes }}");
        workflow.Should().Contain("$extraNotes = $env:FREEX_RELEASE_NOTES");
        workflow.Should().Contain("Public-preview accessibility gate:");
        workflow.Should().Contain("$publicPreviewCandidate = \"${{ inputs.public_preview_candidate }}\" -eq \"true\"");
        workflow.Should().Contain("\"Keyboard-only smoke validation\" = \"${{ inputs.accessibility_keyboard_only }}\" -eq \"true\"");
        workflow.Should().Contain("\"Screen-reader smoke validation\" = \"${{ inputs.accessibility_screen_reader }}\" -eq \"true\"");
        workflow.Should().Contain("\"UI Automation catalog review\" = \"${{ inputs.accessibility_uia_catalog }}\" -eq \"true\"");
        workflow.Should().Contain("\"Known accessibility issues reviewed/listed\" = \"${{ inputs.accessibility_known_issues }}\" -eq \"true\"");
        workflow.Should().Contain("Public-preview promotion requires completed accessibility gate inputs");
        workflow.Should().Contain("Keyboard-only smoke validation: $keyboardOnlyStatus.");
        workflow.Should().Contain("Screen-reader smoke validation: $screenReaderStatus.");
        workflow.Should().Contain("UI Automation catalog review: $uiaCatalogStatus.");
        workflow.Should().Contain("Known accessibility issues reviewed/listed: $knownIssuesStatus.");
        workflow.Should().Contain("this build is public-preview eligible");
        workflow.Should().Contain("This build is internal-only unless release notes separately document a completed public-preview accessibility gate.");
    }

    [Fact]
    public void UserTestPublishScript_PublishesFrameworkDependentRuntimeSpecificBuild()
    {
        var scriptPath = WorkspaceFileLocator.Find("tools", "Publish-UserTestBuild.ps1");
        var script = File.ReadAllText(scriptPath);

        script.Should().Contain("[string]$RuntimeIdentifier = \"win-x64\"");
        script.Should().Contain("\"-r\", $RuntimeIdentifier");
        script.Should().Contain("\"--self-contained\", \"false\"");
        script.Should().Contain("$artifactExeHashPath = \"$artifactExePath.sha256\"");
        script.Should().Contain("Set-Content -LiteralPath $artifactExeHashPath");
        script.Should().Contain("FreeX is not affiliated with, endorsed by, or sponsored by Microsoft.");
        script.Should().Contain("Microsoft Excel is a trademark of Microsoft Corporation.");
        script.Should().Contain("docs/PRIVACY.md");
        script.Should().Contain("THIRD_PARTY_NOTICES.md");
    }

    [Fact]
    public void UserTestPublishScript_CanPackageAndOptionallySignLocalMsix()
    {
        var scriptPath = WorkspaceFileLocator.Find("tools", "Publish-UserTestBuild.ps1");
        var script = File.ReadAllText(scriptPath);

        script.Should().Contain("[ValidateSet(\"SingleFile\", \"Folder\", \"Msix\")]");
        script.Should().Contain("[string]$MsixCertificatePath = $env:FREEX_MSIX_CERTIFICATE_PATH");
        script.Should().Contain("[string]$MsixCertificatePassword = $env:FREEX_MSIX_CERTIFICATE_PASSWORD");
        script.Should().Contain("[string]$MsixTimestampUrl = $env:FREEX_MSIX_TIMESTAMP_URL");
        script.Should().Contain("$artifactMsixPath = Join-Path $artifactRoot \"$artifactName.msix\"");
        script.Should().Contain("function ConvertTo-MsixPackageVersion");
        script.Should().Contain("$msixVersion = ConvertTo-MsixPackageVersion -DisplayVersion $Version");
        script.Should().Contain("$msixParts[$i] = $msixParts[$i] % 65536");
        script.Should().Contain("<Identity Name=\"FreeX.Tester\" Publisher=\"CN=FreeXLocal\" Version=\"$msixVersion\" />");
        script.Should().Contain("EntryPoint=\"Windows.FullTrustApplication\"");
        script.Should().Contain("<rescap:Capability Name=\"runFullTrust\" />");
        script.Should().Contain("Get-Command makeappx.exe");
        script.Should().Contain("makeappx.exe was not found. Install the Windows SDK");
        script.Should().Contain("pack /d $publishDir /p $artifactMsixPath /o");
        script.Should().Contain("Get-Command signtool.exe");
        script.Should().Contain("signtool.exe was not found. Install the Windows SDK to sign MSIX packages.");
        script.Should().Contain("$signArgs = @(\"sign\", \"/fd\", \"SHA256\", \"/f\", $MsixCertificatePath)");
        script.Should().Contain("Created unsigned local MSIX; pass -MsixCertificatePath to sign it.");
        script.Should().Contain("$artifactMsixHashPath = \"$artifactMsixPath.sha256\"");
        script.Should().Contain("Set-Content -LiteralPath $artifactMsixHashPath");
    }

    [Fact]
    public void TesterReleaseWorkflow_DefaultsToStableReleaseWhenAdvertisingLatestDownload()
    {
        var workflowPath = WorkspaceFileLocator.Find(".github", "workflows", "tester-release.yml");
        var workflow = File.ReadAllText(workflowPath);

        workflow.Should().Contain("Download the stable latest asset: FreeX-latest-win-x64.exe");
        workflow.Should().Contain("Checksum for the latest single-file asset: FreeX-latest-win-x64.exe.sha256");
        workflow.Should().Contain("MSIX package: FreeX-latest-win-x64.msix");
        workflow.Should().Contain("signed when the release workflow has certificate secrets configured");
        workflow.Should().Contain("otherwise it remains unsigned for local packaging validation");

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
        root.GetProperty("overallCompletion").GetInt32().Should().BeInRange(93, 95);
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
        plan.Should().Contain("FreeX-latest-win-x64.exe");
        plan.Should().Contain("https://github.com/tony-xmelon/FreeX/releases/latest/download/FreeX-latest-win-x64.exe");
        plan.Should().Contain("FREEX_MSIX_CERTIFICATE_BASE64");
        plan.Should().Contain("Installer trust validation and Store-style submission remain release-gate work.");
    }
}
