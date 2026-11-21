using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Offline proof of tools/Get-LatestReadinessGreenSha.ps1: it must pick the newest ancestor whose
/// readiness run proves every expected gate, skip anything short of that, and otherwise exit non-zero.
/// </summary>
public sealed class ReadinessGreenShaTests
{
    private static readonly string Head = new('a', 40);
    private static readonly string Middle = new('b', 40);
    private static readonly string Oldest = new('c', 40);
    private const string FreshNow = "2026-08-29T00:00:00Z";
    private static readonly string[] ExpectedJobs = ["FreeX windows host-a", "FreeX linux render-a"];

    [RequiresExternalToolFact(ExternalToolPreconditions.PowerShell7)]
    public void PicksTheNewestAncestorWhoseReadinessRunProvedEveryGate()
    {
        // The head's run is green but missed one expected gate; the middle commit proved both.
        using var temp = Prepare(
            runs: [(1, Head), (2, Middle), (3, Oldest)],
            jobsByRun: new()
            {
                [1] = [(ExpectedJobs[0], "success")],
                [2] = ExpectedJobs.Select(n => (n, "success")).ToArray(),
                [3] = ExpectedJobs.Select(n => (n, "success")).ToArray(),
            });

        var result = Run(temp.Path, FreshNow);

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        result.Output.Trim().Should().Be(Middle);
    }

    [RequiresExternalToolFact(ExternalToolPreconditions.PowerShell7)]
    public void FailsClosedWhenNoAncestorHasFullFreshEvidence()
    {
        using var red = Prepare(
            runs: [(1, Middle)],
            jobsByRun: new() { [1] = [(ExpectedJobs[0], "success"), (ExpectedJobs[1], "failure")] });
        var redResult = Run(red.Path, FreshNow);
        redResult.ExitCode.Should().Be(1, redResult.CombinedOutput);
        redResult.Output.Trim().Should().BeEmpty();

        using var stale = Prepare(
            runs: [(1, Middle)],
            jobsByRun: new() { [1] = ExpectedJobs.Select(n => (n, "success")).ToArray() });
        // Run is dated 2026-08-28; eight days later it is past the seven day limit.
        var staleResult = Run(stale.Path, "2026-09-05T00:00:00Z");
        staleResult.ExitCode.Should().Be(1, staleResult.CombinedOutput);
        staleResult.Output.Trim().Should().BeEmpty();

        using var noRuns = Prepare(runs: [], jobsByRun: new());
        Run(noRuns.Path, FreshNow).ExitCode.Should().Be(1);
    }

    private static PowerShellResult Run(string directory, string now) =>
        PowerShellScriptRunner.RunToolScriptWithPwsh(
            "Get-LatestReadinessGreenSha.ps1",
            directory,
            $"-Repository owner/repo -Now {now} " +
            $"-AncestorMetadataPath \"{Path.Combine(directory, "ancestors.json")}\" " +
            $"-ExpectedMatrixPath \"{Path.Combine(directory, "matrix.json")}\" " +
            $"-RunsMetadataPath \"{Path.Combine(directory, "runs.json")}\" " +
            $"-JobsMetadataRoot \"{Path.Combine(directory, "jobs")}\"");

    private static TestTemporaryDirectory Prepare(
        (int Id, string Sha)[] runs,
        Dictionary<int, (string Name, string Conclusion)[]> jobsByRun)
    {
        var temp = new TestTemporaryDirectory();
        File.WriteAllText(
            Path.Combine(temp.Path, "ancestors.json"),
            JsonSerializer.Serialize(new[] { Head, Middle, Oldest }.Select(sha => new { sha })));
        File.WriteAllText(
            Path.Combine(temp.Path, "matrix.json"),
            JsonSerializer.Serialize(new
            {
                include = ExpectedJobs.Select(name =>
                {
                    var parts = name.Split(' ');
                    return new { app = parts[0], platform = parts[1], displayGateId = parts[2] };
                })
            }));
        File.WriteAllText(
            Path.Combine(temp.Path, "runs.json"),
            JsonSerializer.Serialize(new
            {
                workflow_runs = runs.Select(run => new
                {
                    id = run.Id,
                    head_sha = run.Sha,
                    status = "completed",
                    conclusion = "success",
                    updated_at = "2026-08-28T00:00:00Z"
                })
            }));
        Directory.CreateDirectory(Path.Combine(temp.Path, "jobs"));
        foreach (var (runId, jobs) in jobsByRun)
        {
            File.WriteAllText(
                Path.Combine(temp.Path, "jobs", $"{runId}.json"),
                JsonSerializer.Serialize(new
                {
                    total_count = jobs.Length,
                    jobs = jobs.Select(job => new { name = job.Name, status = "completed", conclusion = job.Conclusion })
                }));
        }

        return temp;
    }
}
