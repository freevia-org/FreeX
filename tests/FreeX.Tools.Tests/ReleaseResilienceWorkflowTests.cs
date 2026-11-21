using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Source contracts for the release pipeline's early-fail and resilience machinery. They pin the
/// properties that must not silently regress: a real failure stops the run, an infrastructure failure
/// never does, nothing can publish from a stopped run, and every job has a bounded lifetime.
/// </summary>
public sealed class ReleaseResilienceWorkflowTests
{
    private static string FullRelease() => WorkspaceFileLocator.ReadAllText(".github", "workflows", "full-release.yml");

    private static string Job(string workflow, string jobId)
    {
        var match = Regex.Match(workflow, $@"(?ms)^  {Regex.Escape(jobId)}:\r?\n.*?(?=^  [A-Za-z][A-Za-z0-9_-]*:\r?$|\z)");
        match.Success.Should().BeTrue($"the workflow should define the '{jobId}' job");
        return match.Value;
    }

    [Fact]
    public void FullRelease_StopsTheRunOnTheFirstRealFailureButNeverOnInfrastructureFlakes()
    {
        var workflow = FullRelease();

        // The switch exists and defaults on, so the early stop is the normal behaviour.
        Regex.IsMatch(workflow, @"(?s)early_abort:\s+description:.*?default: true\s+type: boolean").Should().BeTrue();

        var watcher = Job(workflow, "early-abort");
        watcher.Should().Contain("name: Watch for a real failure and stop the run early");
        watcher.Should().Contain("needs: [candidate]");
        watcher.Should().Contain("if: ${{ inputs.early_abort }}");
        watcher.Should().Contain("actions: write", "cancelling a run needs the actions write scope and nothing else may widen");
        watcher.Should().Contain("contents: read");
        watcher.Should().NotContain("contents: write");
        // A watcher that errors must never turn a healthy release red.
        watcher.Should().Contain("continue-on-error: true");
        watcher.Should().Contain("timeout-minutes: 240");
        // It decides through the shared classifier, so infrastructure flakes do not abort.
        watcher.Should().Contain("tools/Get-ReleaseRunFailureClass.ps1");
        watcher.Should().Contain("$state.hasRealFailure");
        watcher.Should().Contain("gh run cancel $env:GITHUB_RUN_ID");
        watcher.Should().Contain("$state.reachedPublicationGate");

        // Nothing may depend on the watcher: its absence, skip or failure cannot change a gate result.
        Regex.IsMatch(workflow, @"needs:\s*\[[^\]]*early-abort").Should().BeFalse();
        Regex.IsMatch(workflow, @"(?m)^\s+-\s*early-abort\s*$").Should().BeFalse();
    }

    [Fact]
    public void FullRelease_PublicationStillRequiresEveryUpstreamJobToSucceed()
    {
        var workflow = FullRelease();

        // A run cancelled by the watcher publishes nothing because the gate is fail-closed on every
        // upstream result, not because of any cancellation special case.
        var gate = Job(workflow, "publication-gate");
        gate.Should().Contain("if: always()");
        gate.Should().Contain("needs: [prepare, release-gate, package, suite-package]");
        gate.Should().Contain("$releaseGateResult -ne 'success'");
        gate.Should().Contain("$packageResult -ne 'success'");
        Job(workflow, "publish").Should().Contain("needs: [prepare, publication-gate]");
        Job(workflow, "publish-suite").Should().Contain("needs: [suite-package, publication-gate]");
        Job(workflow, "release-gate").Should().Contain("-not $releaseTestsAcceptable");
    }

    [Fact]
    public void FullRelease_EveryJobHasABoundedLifetime()
    {
        var workflow = FullRelease();
        var ids = Regex.Matches(workflow, @"(?m)^  ([A-Za-z][A-Za-z0-9_-]*):\r?$")
            .Select(m => m.Groups[1].Value)
            .Where(id => id is not ("workflow_dispatch" or "inputs"))
            .ToList();
        ids.Should().Contain(new[] { "prepare", "candidate", "release-tests", "package", "suite-package", "publish", "early-abort" });

        // Jobs waiting for an environment approval are not counted by timeout-minutes, but a running
        // job must always be bounded: history holds a run that idled for 4469 minutes.
        var jobBlocks = ids.Where(id => Job(workflow, id).Contains("    runs-on:")).ToList();
        jobBlocks.Should().NotBeEmpty();
        foreach (var id in jobBlocks)
        {
            var match = Regex.Match(Job(workflow, id), @"(?m)^    timeout-minutes:\s*(\d+)\s*$");
            match.Success.Should().BeTrue($"job '{id}' must declare timeout-minutes");
            int.Parse(match.Groups[1].Value).Should().BeLessThanOrEqualTo(240, $"job '{id}' must not be able to hang for hours");
        }

        Regex.Match(Job(workflow, "release-tests"), @"timeout-minutes:\s*(\d+)").Groups[1].Value.Should().Be("60");
        Regex.Match(Job(workflow, "suite-package"), @"timeout-minutes:\s*(\d+)").Groups[1].Value.Should().Be("30");
    }

    [Fact]
    public void AutoRetry_ReRunsOnlyAFirstAttemptFailureWhoseEveryFailedJobIsInfrastructure()
    {
        var retry = WorkspaceFileLocator.ReadAllText(".github", "workflows", "release-auto-retry.yml");

        // Triggered only by the end of a dispatched Full Signed Release run, never by a push or schedule.
        retry.Should().Contain("workflow_run:");
        retry.Should().Contain("workflows: [\"Full Signed Release\"]");
        retry.Should().NotContain("push:");
        retry.Should().NotContain("schedule:");
        retry.Should().NotContain("pull_request");

        // The pre-filter and the authoritative API re-check both pin 'failure' and the first attempt,
        // so a re-run is never retried and a cancelled run (the early-abort case) is never retried.
        retry.Should().Contain("github.event.workflow_run.conclusion == 'failure'");
        retry.Should().Contain("github.event.workflow_run.run_attempt == 1");
        retry.Should().Contain("github.event.workflow_run.path == '.github/workflows/full-release.yml'");
        retry.Should().Contain("github.event.workflow_run.repository.full_name == github.repository");
        retry.Should().Contain("[string]$run.conclusion -ne \"failure\"");
        retry.Should().Contain("[int]$run.run_attempt -ne 1");

        // Only the classifier's own verdict can authorise the re-run, and only of failed jobs.
        retry.Should().Contain("tools/Get-ReleaseRunFailureClass.ps1");
        retry.Should().Contain("if (-not $state.retryable)");
        retry.Should().Contain("gh run rerun $runId --repo $env:GITHUB_REPOSITORY --failed");
        Regex.Matches(retry, @"gh run rerun").Count.Should().Be(1, "exactly one re-run call exists and it is the guarded --failed form");

        // Least privilege: it re-runs jobs and reads the repository, nothing else.
        retry.Should().Contain("actions: write");
        retry.Should().Contain("contents: read");
        retry.Should().NotContain("contents: write");
        retry.Should().NotContain("environment:");
        retry.Should().NotContain("secrets.");
        retry.Should().Contain("timeout-minutes: 10");
    }

    [Fact]
    public void FullRelease_PublicationResumesWithoutSilentlyOverwritingAndRetriesTransientServiceSteps()
    {
        var workflow = FullRelease();

        // Both publish jobs resume through the plan-based uploader; blanket --clobber is gone, so a
        // published asset can never be replaced by a re-run without an explicit refusal.
        workflow.Should().NotContain("--clobber");
        Regex.Matches(workflow, Regex.Escape("& tools/Send-ReleaseAssets.ps1 -Repository $env:GITHUB_REPOSITORY -Tag $tag -AssetPaths $assets")).Count.Should().Be(2);
        Regex.Matches(workflow, Regex.Escape("elseif (\"$view\" -match 'release not found')")).Count.Should().Be(2,
            "only a definite 'release not found' may be read as an absent release");

        // The uploader refuses a conflicting published asset and only replaces interrupted debris.
        var uploader = WorkspaceFileLocator.ReadAllText("tools", "Send-ReleaseAssets.ps1");
        uploader.Should().Contain("refusing to overwrite them");
        Regex.IsMatch(uploader, @"gh release upload[^\r\n]*--clobber").Should().BeFalse("the uploader must never replace a published asset blindly");
        uploader.Should().Contain("[string]$asset.state -ne 'uploaded'");
        uploader.Should().Contain("Action = 'conflict'");

        // Service-only steps retry a bounded number of times and stay idempotent across retries.
        workflow.Should().NotContain("dotnet tool install --global vpk");
        workflow.Should().NotContain("dotnet tool install --tool-path artifacts/sbom-tool");
        Regex.Matches(workflow, Regex.Escape("dotnet tool update --global vpk --version 1.2.0")).Count.Should().Be(2);
        Regex.Matches(workflow, Regex.Escape("dotnet tool update --tool-path artifacts/sbom-tool Microsoft.Sbom.DotNetTool --version 4.1.5")).Count.Should().Be(2);
        Regex.Matches(workflow, @"Invoke-WithRetry -Description ""Obtaining an Artifact Signing access token").Count.Should().Be(2);
        WorkspaceFileLocator.ReadAllText("tools", "Invoke-WithRetry.ps1").Should().Contain("if ($null -eq $output) { return }");

        // Pinned offline by the test-gate contract that repository preflight runs.
        WorkspaceFileLocator.ReadAllText("tools", "Test-TestGateContract.ps1")
            .Should().Contain("Test-ReleaseAssetPublishing.ps1");
    }

    [Fact]
    public void FullRelease_ValidatesSigningMetadataInSecondsBeforeAnyPackageIsBuilt()
    {
        var workflow = FullRelease();
        var candidate = Job(workflow, "candidate");
        candidate.Should().Contain("name: Verify the Artifact Signing metadata before any package is built");
        candidate.Should().Contain("if: ${{ !inputs.dry_run && (inputs.platform == 'all' || inputs.platform == 'windows') }}");
        Job(workflow, "package").Should().Contain("needs: [prepare, candidate]");
        Job(workflow, "release-tests").Should().Contain("needs: [prepare, candidate]");

        // The early check must apply exactly the rules the signing script applies at the end.
        var signing = WorkspaceFileLocator.ReadAllText("tools", "Invoke-WindowsArtifactSigning.ps1");
        foreach (var rule in new[]
        {
            "-match 'REPLACE-WITH|<|>'",
            "-notmatch '^https://[a-z0-9-]+\\.codesigning\\.azure\\.net/?$'",
            "\"free-software-signing\"",
            "\"freevia-public-signing\"",
            "@(\"Endpoint\", \"CodeSigningAccountName\", \"CertificateProfileName\")",
        })
        {
            signing.Should().Contain(rule);
            candidate.Should().Contain(rule);
        }
    }

    [Fact]
    public void FullRelease_SourceShaReleasesAReadinessProvenAncestorWithoutWeakeningTheCandidateChain()
    {
        var workflow = FullRelease();

        // Optional, empty by default: the default dispatch behaves exactly as before.
        Regex.IsMatch(workflow, @"(?s)source_sha:\s+description:.*?required: false\s+default: ''\s+type: string").Should().BeTrue();
        workflow.Should().Contain("RELEASE_SHA: ${{ inputs.source_sha != '' && inputs.source_sha || github.sha }}");

        // The untrusted input reaches only the env expression and checkout depth, never script text.
        var uses = workflow.Split('\n').Where(line => line.Contains("inputs.source_sha") && line.Contains("${{")).Select(line => line.Trim()).ToList();
        uses.Should().BeEquivalentTo(new[]
        {
            "RELEASE_SHA: ${{ inputs.source_sha != '' && inputs.source_sha || github.sha }}",
            "fetch-depth: ${{ inputs.source_sha != '' && 0 || 1 }}",
        });

        // The candidate job owns validation, and everything that builds or releases needs it.
        var candidate = Job(workflow, "candidate");
        candidate.Should().Contain("ref: ${{ env.RELEASE_SHA }}");
        candidate.Should().Contain("$isMainRelease = $env:GITHUB_REF -eq \"refs/heads/main\"");
        candidate.Should().Contain("source_sha is only allowed on a dispatch from refs/heads/main");
        candidate.Should().Contain("-cnotmatch '^[0-9a-f]{40}$'");
        candidate.Should().Contain("git merge-base --is-ancestor $env:RELEASE_SHA $env:GITHUB_SHA");
        candidate.Should().Contain("tools/Resolve-GitHubReleaseCandidate.ps1");
        candidate.Should().Contain("-CommitSha $env:RELEASE_SHA");
        candidate.Should().Contain("tools/Resolve-ReleaseGateEvidence.ps1");
        Job(workflow, "package").Should().Contain("needs: [prepare, candidate]");
        Job(workflow, "release-tests").Should().Contain("needs: [prepare, candidate]");

        // Evidence reuse stays the only way to skip the gates: a source_sha without proof runs them all.
        Job(workflow, "release-tests").Should().Contain("needs.candidate.outputs.gate_evidence_reuse != 'true'");

        // The dispatched head is consulted only by the candidate job's ancestry proof.
        foreach (var job in new[] { "prepare", "publish-permission", "early-abort", "release-tests", "release-gate", "package", "suite-package", "publication-gate", "publish", "publish-suite", "verify-publication" })
        {
            Job(workflow, job).Should().NotContain("GITHUB_SHA", $"{job} must use RELEASE_SHA");
            Job(workflow, job).Should().NotContain("github.sha", $"{job} must use RELEASE_SHA");
        }

        // Every job that builds, tests or publishes the tree checks out exactly RELEASE_SHA.
        foreach (var job in new[] { "release-tests", "package", "suite-package", "publish", "publish-suite" })
        {
            Job(workflow, job).Should().Contain("ref: ${{ env.RELEASE_SHA }}", $"{job} must build the release commit");
        }
    }

    [Fact]
    public void LatestReadinessGreenSha_DelegatesTheProofToTheReleaseEvidenceResolverAndFailsClosed()
    {
        var helper = WorkspaceFileLocator.ReadAllText("tools", "Get-LatestReadinessGreenSha.ps1");

        helper.Should().Contain("Resolve-ReleaseGateEvidence.ps1");
        helper.Should().Contain("$decision.reuse -eq $true");
        helper.Should().Contain("exit 1");
        helper.Should().Contain("failing closed");
    }

    [Fact]
    public void FailureClassifier_FailsClosedAndIsProvenOffline()
    {
        var classifier = WorkspaceFileLocator.ReadAllText("tools", "Get-ReleaseRunFailureClass.ps1");

        // Real unless positively infrastructure, and a hang is never retried.
        classifier.Should().Contain("Class = 'real'");
        classifier.Should().Contain("conclusion -eq 'timed_out'");
        classifier.Should().Contain("retryable = ($failed.Count -gt 0 -and $real.Count -eq 0)");
        classifier.Should().Contain("return $null");

        // The offline contract runs with the test-gate contract (and therefore repository preflight).
        WorkspaceFileLocator.ReadAllText("tools", "Test-TestGateContract.ps1")
            .Should().Contain("Test-ReleaseFailureClassification.ps1");
        WorkspaceFileLocator.ReadAllText("tools", "Test-ReleaseFailureClassification.ps1")
            .Should().Contain("Assert-Case 'mixed' $r $true $false 2");
    }
}
