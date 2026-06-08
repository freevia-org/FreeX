using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class MacOsPublicPreviewPromotionPreflightTests
{
    [Fact]
    public void PromotionPreflight_DocumentsArtifactAndHumanValidationGate()
    {
        var script = WorkspaceFileLocator.ReadAllText("tools", "Test-MacOsPublicPreviewPromotion.ps1");
        var distributionPlan = WorkspaceFileLocator.ReadAllText("docs", "release", "test-distribution.md");
        var signingRunbook = WorkspaceFileLocator.ReadAllText("docs", "release", "macos-signing-notarization.md");
        var humanChecklist = WorkspaceFileLocator.ReadAllText("docs", "release", "macos-public-preview-checklist.md");

        script.Should().Contain("Test-MacOsPublicPreviewReadiness.ps1");
        script.Should().Contain("Test-MacOsHumanValidationChecklist.ps1");
        script.Should().Contain("-DistributionCandidate");
        script.Should().Contain("-RequireSeparateDiagnosticsArtifact");
        script.Should().Contain("-RequireReleasePublicationArtifact");
        script.Should().Contain("PrepareHumanValidationHandoff");
        script.Should().Contain("macOS public-preview human validation handoff");
        script.Should().Contain("Validate completed checklist: powershell.exe");
        script.Should().Contain("Final promotion command after all completed checklists pass");
        script.Should().Contain("completed-macos-public-preview-checklist-$Runtime.md");
        script.Should().Contain("macOS public-preview promotion preflight passed");

        distributionPlan.Should().Contain("tools/Test-MacOsPublicPreviewPromotion.ps1");
        distributionPlan.Should().Contain("completed-macos-public-preview-checklist-osx-arm64.md");
        distributionPlan.Should().Contain("completed-macos-public-preview-checklist-osx-x64.md");
        signingRunbook.Should().Contain("tools/Test-MacOsPublicPreviewPromotion.ps1");
        humanChecklist.Should().Contain("completed-macos-public-preview-checklist-osx-arm64.md");
        humanChecklist.Should().Contain("completed-macos-public-preview-checklist-osx-x64.md");
        humanChecklist.Should().Contain("-PrepareHumanValidationHandoff");
    }

    [Fact]
    public void PromotionPreflight_PassesRunIdentityAndStrictFlagsToChildValidators()
    {
        using var temp = new TestTemporaryDirectory();
        var artifactRoot = Path.Combine(temp.Path, "artifacts");
        var checklistRoot = Path.Combine(temp.Path, "checklists");
        Directory.CreateDirectory(artifactRoot);
        Directory.CreateDirectory(checklistRoot);
        CreateCompletedChecklistFiles(checklistRoot);
        var evidenceScript = CreateEvidenceStub(temp.Path);
        var humanScript = CreateHumanChecklistStub(temp.Path);

        var result = RunPromotionPreflight(
            artifactRoot,
            checklistRoot,
            evidenceScript,
            humanScript,
            "-ExpectedRunId 42 -ExpectedRunAttempt 1");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("evidence:42:1:osx-arm64,osx-x64");
        result.Output.Should().Contain("distribution=True");
        result.Output.Should().Contain("diagnostics=True");
        result.Output.Should().Contain("release=True");
        result.Output.Should().Contain("human:osx-arm64:42:1:completed-macos-public-preview-checklist-osx-arm64.md");
        result.Output.Should().Contain("human:osx-x64:42:1:completed-macos-public-preview-checklist-osx-x64.md");
        result.Output.Should().Contain("macOS public-preview promotion preflight passed");
    }

    [Fact]
    public void PromotionPreflight_HandoffModeSkipsCompletedChecklistRequirementAndPrintsCommands()
    {
        using var temp = new TestTemporaryDirectory();
        var artifactRoot = Path.Combine(temp.Path, "artifacts");
        var checklistRoot = Path.Combine(temp.Path, "checklists");
        Directory.CreateDirectory(artifactRoot);
        Directory.CreateDirectory(checklistRoot);
        var evidenceScript = CreateEvidenceStub(temp.Path);
        var humanScript = CreateHumanChecklistStub(temp.Path);

        var result = RunPromotionPreflight(
            artifactRoot,
            checklistRoot,
            evidenceScript,
            humanScript,
            "-ExpectedRunId 42 -ExpectedRunAttempt 1 -PrepareHumanValidationHandoff");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("evidence:42:1:osx-arm64,osx-x64");
        result.Output.Should().Contain("distribution=True");
        result.Output.Should().Contain("diagnostics=True");
        result.Output.Should().Contain("release=True");
        result.Output.Should().NotContain("human:");
        result.Output.Should().Contain("macOS public-preview human validation handoff");
        result.Output.Should().Contain("Hosted evidence passed for run 42 attempt 1.");
        result.Output.Should().Contain("Checklist template:");
        result.Output.Should().Contain("macos-public-preview-checklist.md");
        result.Output.Should().Contain("Expected completed checklist:");
        result.Output.Should().Contain("completed-macos-public-preview-checklist-osx-arm64.md");
        result.Output.Should().Contain("completed-macos-public-preview-checklist-osx-x64.md");
        result.Output.Should().Contain("freex-42-1-osx-arm64-macos-app");
        result.Output.Should().Contain("freex-42-1-osx-arm64-macos-diagnostics");
        result.Output.Should().Contain("freex-42-1-osx-x64-macos-app");
        result.Output.Should().Contain("freex-42-1-osx-x64-macos-diagnostics");
        result.Output.Should().Contain("freex-42-1-macos-release-assets");
        result.Output.Should().Contain("Validate completed checklist: powershell.exe -NoProfile -ExecutionPolicy Bypass -File");
        result.Output.Should().Contain("-ExpectedRuntime osx-arm64 -ExpectedRunId 42 -ExpectedRunAttempt 1");
        result.Output.Should().Contain("-ExpectedRuntime osx-x64 -ExpectedRunId 42 -ExpectedRunAttempt 1");
        result.Output.Should().Contain("Final promotion command after all completed checklists pass: powershell.exe -NoProfile -ExecutionPolicy Bypass -File");
        result.Output.Should().Contain("-ArtifactRoot");
        result.Output.Should().Contain("-ChecklistRoot");
    }

    [Fact]
    public void PromotionPreflight_FailsWhenRunIdentityIsMissing()
    {
        using var temp = new TestTemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "artifacts"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "checklists"));

        var result = RunPromotionPreflight(
            Path.Combine(temp.Path, "artifacts"),
            Path.Combine(temp.Path, "checklists"),
            CreateEvidenceStub(temp.Path),
            CreateHumanChecklistStub(temp.Path),
            "-ExpectedRunAttempt 1");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("ExpectedRunId is required");
        result.CombinedOutput.Should().Contain("stale hosted artifacts or human checklists");
    }

    [Fact]
    public void PromotionPreflight_FailsWhenRuntimeChecklistIsMissing()
    {
        using var temp = new TestTemporaryDirectory();
        var artifactRoot = Path.Combine(temp.Path, "artifacts");
        var checklistRoot = Path.Combine(temp.Path, "checklists");
        Directory.CreateDirectory(artifactRoot);
        Directory.CreateDirectory(checklistRoot);
        File.WriteAllText(
            Path.Combine(checklistRoot, "completed-macos-public-preview-checklist-osx-arm64.md"),
            "completed arm64 checklist");

        var result = RunPromotionPreflight(
            artifactRoot,
            checklistRoot,
            CreateEvidenceStub(temp.Path),
            CreateHumanChecklistStub(temp.Path),
            "-ExpectedRunId 42 -ExpectedRunAttempt 1");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("Completed macOS public-preview human checklist for osx-x64 was not found");
        result.CombinedOutput.Should().Contain("completed-macos-public-preview-checklist-osx-x64.md");
    }

    private static PowerShellResult RunPromotionPreflight(
        string artifactRoot,
        string checklistRoot,
        string evidenceScript,
        string humanScript,
        string arguments)
    {
        var repoRoot = WorkspaceFileLocator.FindWorkspaceRoot();
        return PowerShellScriptRunner.RunToolScript(
            "Test-MacOsPublicPreviewPromotion.ps1",
            repoRoot,
            $"-ArtifactRoot \"{artifactRoot}\" " +
            $"-ChecklistRoot \"{checklistRoot}\" " +
            $"-EvidencePreflightScriptPath \"{evidenceScript}\" " +
            $"-HumanChecklistScriptPath \"{humanScript}\" " +
            arguments);
    }

    private static void CreateCompletedChecklistFiles(string checklistRoot)
    {
        File.WriteAllText(
            Path.Combine(checklistRoot, "completed-macos-public-preview-checklist-osx-arm64.md"),
            "completed arm64 checklist");
        File.WriteAllText(
            Path.Combine(checklistRoot, "completed-macos-public-preview-checklist-osx-x64.md"),
            "completed x64 checklist");
    }

    private static string CreateEvidenceStub(string directory)
    {
        var path = Path.Combine(directory, "Synthetic-MacOsPublicPreviewReadiness.ps1");
        File.WriteAllText(
            path,
            """
            param(
                [string]$ArtifactRoot,
                [string[]]$Runtimes,
                [string]$ExpectedRunId,
                [string]$ExpectedRunAttempt,
                [switch]$DistributionCandidate,
                [switch]$RequireSeparateDiagnosticsArtifact,
                [switch]$RequireReleasePublicationArtifact
            )

            $ErrorActionPreference = "Stop"
            if (-not (Test-Path -LiteralPath $ArtifactRoot -PathType Container)) {
                throw "artifact root missing"
            }

            if (-not $DistributionCandidate.IsPresent) {
                throw "distribution candidate flag missing"
            }

            if (-not $RequireSeparateDiagnosticsArtifact.IsPresent) {
                throw "diagnostics flag missing"
            }

            if (-not $RequireReleasePublicationArtifact.IsPresent) {
                throw "release publication flag missing"
            }

            Write-Host ("evidence:{0}:{1}:{2}:distribution={3}:diagnostics={4}:release={5}" -f $ExpectedRunId, $ExpectedRunAttempt, ($Runtimes -join ","), $DistributionCandidate.IsPresent, $RequireSeparateDiagnosticsArtifact.IsPresent, $RequireReleasePublicationArtifact.IsPresent)
            """);
        return path;
    }

    private static string CreateHumanChecklistStub(string directory)
    {
        var path = Path.Combine(directory, "Synthetic-MacOsHumanValidationChecklist.ps1");
        File.WriteAllText(
            path,
            """
            param(
                [string]$ChecklistPath,
                [string]$ExpectedRuntime,
                [string]$ExpectedRunId,
                [string]$ExpectedRunAttempt
            )

            $ErrorActionPreference = "Stop"
            if (-not (Test-Path -LiteralPath $ChecklistPath -PathType Leaf)) {
                throw "checklist missing"
            }

            Write-Host ("human:{0}:{1}:{2}:{3}" -f $ExpectedRuntime, $ExpectedRunId, $ExpectedRunAttempt, (Split-Path -Leaf $ChecklistPath))
            """);
        return path;
    }
}
