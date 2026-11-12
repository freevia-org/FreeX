using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class ReleaseGateEvidenceTests
{
    private const string CommitSha = "0123456789abcdef0123456789abcdef01234567";
    private const string FreshNow = "2026-08-29T00:00:00Z";
    private static readonly string[] ExpectedJobs =
    [
        "FreeX windows host-a",
        "FreeX windows host-b",
        "FreeX linux render-a",
        "FreeW macos render-b"
    ];

    [RequiresExternalToolFact(ExternalToolPreconditions.PowerShell7)]
    public void Evidence_IsReusedOnlyWhenEveryExpectedGateSucceededOnTheExactCommit()
    {
        using var temp = Prepare(runSha: CommitSha, jobs: ExpectedJobs.Select(name => (name, "success")).ToArray());

        var result = Run(temp.Path, FreshNow);

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        File.ReadAllText(Path.Combine(temp.Path, "out.txt")).Should().Contain("reuse=true").And.Contain("evidence_run_id=77");
    }

    [RequiresExternalToolFact(ExternalToolPreconditions.PowerShell7)]
    public void Evidence_IsNotReusedWhenAnyGateIsRedMissingOrDuplicated()
    {
        var allGreen = ExpectedJobs.Select(name => (name, "success")).ToArray();
        var cases = new (string Reason, (string Name, string Conclusion)[] Jobs)[]
        {
            ("red", allGreen.Select(j => j.name == ExpectedJobs[2] ? (j.name, "failure") : j).ToArray()),
            ("cancelled", allGreen.Select(j => j.name == ExpectedJobs[0] ? (j.name, "cancelled") : j).ToArray()),
            ("skipped", allGreen.Select(j => j.name == ExpectedJobs[1] ? (j.name, "skipped") : j).ToArray()),
            ("missing", allGreen.Where(j => j.name != ExpectedJobs[3]).ToArray()),
            ("duplicated", allGreen.Concat([(ExpectedJobs[0], "success")]).ToArray()),
        };

        foreach (var (reason, jobs) in cases)
        {
            using var temp = Prepare(runSha: CommitSha, jobs: jobs);

            var result = Run(temp.Path, FreshNow);

            result.ExitCode.Should().Be(0, "a lookup problem must degrade to 'run everything', never fail the job: " + result.CombinedOutput);
            File.ReadAllText(Path.Combine(temp.Path, "out.txt")).Should().Contain("reuse=false", reason);
            result.CombinedOutput.Should().Contain("NOT reused", reason);
        }
    }

    [RequiresExternalToolFact(ExternalToolPreconditions.PowerShell7)]
    public void Evidence_IsNotReusedFromAnAncestorStaleOrIncompleteRun()
    {
        var allGreen = ExpectedJobs.Select(name => (name, "success")).ToArray();

        using (var ancestor = Prepare(runSha: new string('f', 40), jobs: allGreen))
        {
            Run(ancestor.Path, FreshNow).CombinedOutput.Should().Contain("no successful completed release-readiness.yml run exists for exact commit");
            File.ReadAllText(Path.Combine(ancestor.Path, "out.txt")).Should().Contain("reuse=false");
        }

        using (var stale = Prepare(runSha: CommitSha, jobs: allGreen))
        {
            // 2026-08-28 + 8 days exceeds the 7 day default.
            Run(stale.Path, "2026-09-05T00:00:00Z").CombinedOutput.Should().Contain("day(s) old");
            File.ReadAllText(Path.Combine(stale.Path, "out.txt")).Should().Contain("reuse=false");
        }

        using (var future = Prepare(runSha: CommitSha, jobs: allGreen))
        {
            Run(future.Path, "2026-08-20T00:00:00Z").CombinedOutput.Should().Contain("dated in the future");
            File.ReadAllText(Path.Combine(future.Path, "out.txt")).Should().Contain("reuse=false");
        }

        using (var truncated = Prepare(runSha: CommitSha, jobs: allGreen, totalCount: ExpectedJobs.Length + 5))
        {
            Run(truncated.Path, FreshNow).CombinedOutput.Should().Contain("incomplete listing");
            File.ReadAllText(Path.Combine(truncated.Path, "out.txt")).Should().Contain("reuse=false");
        }

        using (var failedRun = Prepare(runSha: CommitSha, jobs: allGreen, runConclusion: "failure"))
        {
            File.ReadAllText(Path.Combine(failedRun.Path, "out.txt")).Should().NotContain("reuse=true");
            Run(failedRun.Path, FreshNow);
            File.ReadAllText(Path.Combine(failedRun.Path, "out.txt")).Should().Contain("reuse=false");
        }
    }

    [RequiresExternalToolFact(ExternalToolPreconditions.PowerShell7)]
    public void Evidence_FailsClosedWhenTheLookupItselfBreaks()
    {
        using var temp = new TestTemporaryDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "matrix.json"), ExpectedMatrix());
        File.WriteAllText(Path.Combine(temp.Path, "runs.json"), "this is not json");
        File.WriteAllText(Path.Combine(temp.Path, "out.txt"), string.Empty);

        var result = Run(temp.Path, FreshNow);

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        File.ReadAllText(Path.Combine(temp.Path, "out.txt")).Should().Contain("reuse=false");
        result.CombinedOutput.Should().Contain("evidence lookup failed");
    }

    private static PowerShellResult Run(string directory, string now) =>
        PowerShellScriptRunner.RunToolScriptWithPwsh(
            "Resolve-ReleaseGateEvidence.ps1",
            directory,
            $"-Repository owner/repo -CommitSha {CommitSha} -Now {now} " +
            $"-ExpectedMatrixPath \"{Path.Combine(directory, "matrix.json")}\" " +
            $"-RunsMetadataPath \"{Path.Combine(directory, "runs.json")}\" " +
            $"-JobsMetadataRoot \"{Path.Combine(directory, "jobs")}\" " +
            $"-GitHubOutputPath \"{Path.Combine(directory, "out.txt")}\"");

    private static string ExpectedMatrix()
    {
        var include = ExpectedJobs.Select(name =>
        {
            var parts = name.Split(' ');
            return new { app = parts[0], platform = parts[1], displayGateId = parts[2] };
        });
        return JsonSerializer.Serialize(new { include });
    }

    private static TestTemporaryDirectory Prepare(
        string runSha,
        (string Name, string Conclusion)[] jobs,
        int? totalCount = null,
        string runConclusion = "success")
    {
        var temp = new TestTemporaryDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "matrix.json"), ExpectedMatrix());
        File.WriteAllText(Path.Combine(temp.Path, "out.txt"), string.Empty);
        File.WriteAllText(
            Path.Combine(temp.Path, "runs.json"),
            JsonSerializer.Serialize(new
            {
                workflow_runs = new[]
                {
                    new
                    {
                        id = 77,
                        head_sha = runSha,
                        status = "completed",
                        conclusion = runConclusion,
                        updated_at = "2026-08-28T00:00:00Z"
                    }
                }
            }));
        Directory.CreateDirectory(Path.Combine(temp.Path, "jobs"));
        File.WriteAllText(
            Path.Combine(temp.Path, "jobs", "77.json"),
            JsonSerializer.Serialize(new
            {
                total_count = totalCount ?? jobs.Length,
                jobs = jobs.Select(j => new { name = j.Name, status = "completed", conclusion = j.Conclusion })
            }));
        return temp;
    }
}
