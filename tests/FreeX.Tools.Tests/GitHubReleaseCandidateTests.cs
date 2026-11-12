using System.IO;
using System.Text.Json;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class GitHubReleaseCandidateTests
{
    private const string CommitSha = "0123456789abcdef0123456789abcdef01234567";

    [RequiresExternalToolFact(ExternalToolPreconditions.PowerShell7)]
    public void CandidateGate_AcceptsSuccessfulExactShaRunsForEveryRequiredWorkflow()
    {
        using var temp = new TestTemporaryDirectory();
        WriteRunMetadata(temp.Path, "ci.yml", CommitSha, "success", 101);
        WriteRunMetadata(temp.Path, "codeql.yml", CommitSha, "success", 102);

        var result = PowerShellScriptRunner.RunToolScriptWithPwsh(
            "Test-GitHubReleaseCandidate.ps1",
            temp.Path,
            $"-Repository owner/repo -CommitSha {CommitSha} -RunMetadataDirectory \"{temp.Path}\"");

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        result.CombinedOutput.Should().Contain("Verified ci.yml run 101");
        result.CombinedOutput.Should().Contain("Verified codeql.yml run 102");
    }

    [RequiresExternalToolFact(ExternalToolPreconditions.PowerShell7)]
    public void CandidateGate_RejectsAWorkflowRunFromAnotherCommit()
    {
        using var temp = new TestTemporaryDirectory();
        WriteRunMetadata(temp.Path, "ci.yml", CommitSha, "success", 101);
        WriteRunMetadata(temp.Path, "codeql.yml", new string('f', 40), "success", 102);

        var result = PowerShellScriptRunner.RunToolScriptWithPwsh(
            "Test-GitHubReleaseCandidate.ps1",
            temp.Path,
            $"-Repository owner/repo -CommitSha {CommitSha} -RunMetadataDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain(
            $"No successful completed 'codeql.yml' run exists for exact commit {CommitSha}.");
    }

    [RequiresExternalToolFact(ExternalToolPreconditions.PowerShell7)]
    public void CandidateGate_RejectsAnImpactReducedRunWhoseCoverageAnchorIsNotProven()
    {
        // A reduced (inert-documentation-only) ci.yml run is green by construction, so it must
        // never attest a commit by itself: it has to name a full-coverage ancestor anchor.
        using var temp = new TestTemporaryDirectory();
        var anchor = new string('c', 40);
        var runDirectory = Path.Combine(temp.Path, CommitSha);
        Directory.CreateDirectory(runDirectory);
        WriteRunMetadata(runDirectory, "ci.yml", CommitSha, "success", 101);
        File.WriteAllText(
            Path.Combine(runDirectory, "ci.yml.101.jobs.json"),
            JsonSerializer.Serialize(new
            {
                jobs = new[]
                {
                    new { name = $"CI coverage: inert anchor={anchor}", status = "completed", conclusion = "success" }
                }
            }));

        var result = PowerShellScriptRunner.RunToolScriptWithPwsh(
            "Test-GitHubReleaseCandidate.ps1",
            temp.Path,
            $"-Repository owner/repo -CommitSha {CommitSha} -RequiredWorkflows ci.yml -RunMetadataDirectory \"{runDirectory}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("proves full coverage");
    }

    private static void WriteRunMetadata(string directory, string workflow, string sha, string conclusion, long id)
    {
        var payload = new
        {
            workflow_runs = new[]
            {
                new
                {
                    id,
                    head_sha = sha,
                    status = "completed",
                    conclusion,
                    updated_at = "2026-08-28T00:00:00Z",
                    html_url = $"https://github.example/runs/{id}"
                }
            }
        };
        File.WriteAllText(
            Path.Combine(directory, $"{workflow}.json"),
            JsonSerializer.Serialize(payload));
    }
}
