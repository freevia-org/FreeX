using FluentAssertions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Screenshots, capture manifests and raw parity evidence are local-only and were purged from the
/// repository history. These tests pin the preflight guard that keeps them from being re-committed.
/// </summary>
public sealed class LocalOnlyEvidencePathsPreflightTests
{
    [Fact]
    public void RepositoryPreflight_RunsTheLocalOnlyEvidencePathsGuard()
    {
        var preflight = WorkspaceFileLocator.ReadAllText("tools", "Test-RepositoryPreflight.ps1");

        preflight.Should().Contain("[string]$LocalOnlyEvidencePathsScriptPath = \"tools/Test-LocalOnlyEvidencePaths.ps1\"");
        preflight.Should().Contain("-Label \"local-only evidence paths\" -ScriptPath $LocalOnlyEvidencePathsScriptPath");
    }

    [Fact]
    public void Guard_PassesForTheRepository()
    {
        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory("Test-LocalOnlyEvidencePaths.ps1");

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        result.Output.Should().Contain("No local-only evidence paths are tracked.");
    }

    [Theory]
    [InlineData("screenshots/tour/capture.png")]
    [InlineData("screenshots_excel/tour/capture.png")]
    [InlineData("tools/screenshots/ribbon.png")]
    [InlineData("tools/foreground-captures/open-dialog/capture.png")]
    [InlineData("artifacts/parity/manifest.json")]
    [InlineData(".metrics-data/project-history-tokens-MACHINE.json")]
    [InlineData("docs/parity/dialog-visual-assets/wpf-capture/manifest.json")]
    [InlineData("freew/docs/parity/evidence/raw-evidence.json")]
    public void Guard_FailsWhenAnEvidencePathIsTracked(string evidencePath)
    {
        using var temp = new TestTemporaryDirectory();
        TestProcessRunner.Run("git", "init", temp.Path).ExitCode.Should().Be(0);
        AddGitIndexEntry(temp.Path, "docs/parity/command-surface.md");
        AddGitIndexEntry(temp.Path, evidencePath);

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-LocalOnlyEvidencePaths.ps1",
            $"-RepoRoot \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("1 local-only evidence file(s) are tracked");
        result.CombinedOutput.Should().Contain(evidencePath);
    }

    [Fact]
    public void Guard_AllowsDocumentsDirectlyUnderDocsParity()
    {
        using var temp = new TestTemporaryDirectory();
        TestProcessRunner.Run("git", "init", temp.Path).ExitCode.Should().Be(0);
        AddGitIndexEntry(temp.Path, "docs/parity/command-surface.md");
        AddGitIndexEntry(temp.Path, "tools/screenshot_ribbon.ps1");

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-LocalOnlyEvidencePaths.ps1",
            $"-RepoRoot \"{temp.Path}\"");

        result.ExitCode.Should().Be(0, result.CombinedOutput);
    }

    [Fact]
    public void Guard_RejectsBranchesThatStillCarryThePreRewriteHistory()
    {
        var script = WorkspaceFileLocator.ReadAllText("tools", "Test-LocalOnlyEvidencePaths.ps1");

        script.Should().Contain("$preRewriteCommit = \"ed45d43fe7486bebeaa78dbfc286b43d6cd5d267\"");
        script.Should().Contain("merge-base --is-ancestor $preRewriteCommit HEAD");
        script.Should().Contain("HEAD still carries the history from before the 2026-11-12 rewrite");
    }

    private static void AddGitIndexEntry(string repositoryPath, string fileName)
    {
        const string EmptyBlobSha = "e69de29bb2d1d6434b8b29ae775ad8c2e48c5391";

        TestProcessRunner.Run("git", $"update-index --add --cacheinfo 100644,{EmptyBlobSha},{fileName}", repositoryPath)
            .ExitCode.Should().Be(0);
    }
}
