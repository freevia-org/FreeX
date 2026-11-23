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
        gate.Should().Contain("needs: [resolve, prepare, release-gate, package, suite-package]");
        gate.Should().Contain("$releaseGateResult -ne 'success'");
        gate.Should().Contain("$packageResult -ne 'success'");
        Job(workflow, "publish").Should().Contain("needs: [resolve, prepare, publication-gate]");
        Job(workflow, "publish-suite").Should().Contain("needs: [resolve, suite-package, publication-gate]");
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

        // Started only by an operator's workflow_dispatch with a run_id, never by an event.
        retry.Should().Contain("workflow_dispatch:");
        retry.Should().Contain("run_id:");
        retry.Should().NotContain("workflow_run");
        retry.Should().NotContain("push:");
        retry.Should().NotContain("schedule:");
        retry.Should().NotContain("pull_request");

        // The authoritative API re-check pins the workflow, 'failure' and the first attempt,
        // so a re-run is never retried and a cancelled run (the early-abort case) is never retried.
        retry.Should().Contain("[string]$run.path -ne \".github/workflows/full-release.yml\"");
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
        Job(workflow, "package").Should().Contain("needs: [resolve, prepare, candidate]");
        Job(workflow, "release-tests").Should().Contain("needs: [resolve, prepare, candidate]");

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

        // Optional, empty by default; an explicit value always wins over the default selection.
        Regex.IsMatch(workflow, @"(?s)source_sha:\s+description:.*?required: false\s+default: ''\s+type: string").Should().BeTrue();
        Regex.IsMatch(workflow, @"(?s)release_head:\s+description:.*?required: false\s+default: false\s+type: boolean").Should().BeTrue("release_head must default to false");

        // RELEASE_SHA is no longer a workflow-level expression: the resolve job decides it and every
        // consumer takes it from that job's output through its own env.
        workflow.Should().NotContain("RELEASE_SHA: ${{ inputs.source_sha != '' && inputs.source_sha || github.sha }}");
        Regex.IsMatch(workflow, @"(?m)^env:\r?\n(?:  .*\r?\n)*?  RELEASE_SHA:").Should().BeFalse("RELEASE_SHA must not be a workflow-level env");

        // The untrusted input reaches only env expressions, never script text.
        var uses = workflow.Split('\n').Where(line => line.Contains("inputs.source_sha") && line.Contains("${{")).Select(line => line.Trim()).ToList();
        uses.Should().BeEquivalentTo(new[] { "SOURCE_SHA: ${{ inputs.source_sha }}" });
        var headUses = workflow.Split('\n').Where(line => line.Contains("inputs.release_head") && line.Contains("${{")).Select(line => line.Trim()).ToList();
        headUses.Should().BeEquivalentTo(new[] { "RELEASE_HEAD: ${{ inputs.release_head }}" });

        // The resolve job: first, no needs, default = newest readiness-green ancestor via the helper,
        // fail closed to the head, validated before it is published as an output.
        var resolve = Job(workflow, "resolve");
        resolve.Should().NotContain("needs:");
        resolve.Should().Contain("release_sha: ${{ steps.resolve.outputs.release_sha }}");
        resolve.Should().Contain("tools/Get-LatestReadinessGreenSha.ps1");
        resolve.Should().Contain("-HeadSha $head");
        resolve.Should().Contain("-Json");
        resolve.Should().Contain("$LASTEXITCODE -ne 0");
        resolve.Should().Contain("$mode = 'fallback-head'");
        resolve.Should().Contain("$mode = 'default-readiness-green'");
        resolve.Should().Contain("$mode = 'release-head'");
        resolve.Should().Contain("$mode = 'explicit-source-sha'");
        resolve.Should().Contain("$mode = 'tag-continuation'");
        resolve.Should().Contain("-cnotmatch '^[0-9a-f]{40}$'");
        resolve.Should().Contain("source_sha is only allowed on a dispatch from refs/heads/main");
        resolve.Should().Contain("fetch-depth: 1");
        resolve.Should().NotContain("sparse-checkout", "the helper needs its sibling scripts from a real checkout");
        resolve.Should().NotContain("${{ github.sha }}");
        // The summary names the commit, how far behind the head it is, the evidence run and warns.
        resolve.Should().Contain("Commits behind the head");
        resolve.Should().Contain("Readiness evidence run");
        resolve.Should().Contain("WARNING: THIS RELEASE IS AN ANCESTOR OF MAIN");
        resolve.Should().Contain("WARNING: FALLBACK TO THE HEAD");
        resolve.Should().Contain("GITHUB_STEP_SUMMARY");
        // Fallback precedes publication of the output and the output is only written after validation.
        resolve.IndexOf("The resolved release commit is not a 40-character lowercase SHA", StringComparison.Ordinal)
            .Should().BeLessThan(resolve.IndexOf("\"release_sha=$sha\"", StringComparison.Ordinal));

        // The candidate job owns validation, and everything that builds or releases needs it.
        var candidate = Job(workflow, "candidate");
        candidate.Should().Contain("ref: ${{ env.RELEASE_SHA }}");
        candidate.Should().Contain("fetch-depth: ${{ needs.resolve.outputs.needs_history == 'true' && 0 || 1 }}");
        candidate.Should().Contain("$isMainRelease = $env:GITHUB_REF -eq \"refs/heads/main\"");
        candidate.Should().Contain("source_sha is only allowed on a dispatch from refs/heads/main");
        candidate.Should().Contain("-cnotmatch '^[0-9a-f]{40}$'");
        candidate.Should().Contain("git merge-base --is-ancestor $env:RELEASE_SHA $env:GITHUB_SHA");

        // The job checks out the OLDER commit, so the dispatched head is absent locally: it must be
        // fetched (via env, never script interpolation) and fail closed BEFORE the ancestry test.
        var fetchIndex = candidate.IndexOf("git fetch --no-tags origin $env:GITHUB_SHA", StringComparison.Ordinal);
        var mergeBaseIndex = candidate.IndexOf("git merge-base --is-ancestor $env:RELEASE_SHA $env:GITHUB_SHA", StringComparison.Ordinal);
        fetchIndex.Should().BeGreaterThan(0);
        fetchIndex.Should().BeLessThan(mergeBaseIndex);
        candidate.Should().Contain("Could not fetch the dispatched main head $env:GITHUB_SHA");
        candidate.Should().Contain("git cat-file -e \"$($env:GITHUB_SHA)^{commit}\"");
        candidate.Should().NotContain("${{ github.sha }}");
        candidate.Should().Contain("tools/Resolve-GitHubReleaseCandidate.ps1");
        candidate.Should().Contain("-CommitSha $env:RELEASE_SHA");
        candidate.Should().Contain("tools/Resolve-ReleaseGateEvidence.ps1");
        Job(workflow, "package").Should().Contain("needs: [resolve, prepare, candidate]");
        Job(workflow, "release-tests").Should().Contain("needs: [resolve, prepare, candidate]");

        // Evidence reuse stays the only way to skip the gates: an ancestor without proof runs them all.
        Job(workflow, "release-tests").Should().Contain("needs.candidate.outputs.gate_evidence_reuse != 'true'");

        // The dispatched head is consulted only by the resolve job and the candidate's ancestry proof.
        foreach (var job in new[] { "prepare", "publish-permission", "early-abort", "release-tests", "release-gate", "package", "suite-package", "publication-gate", "publish", "publish-suite", "verify-publication" })
        {
            Job(workflow, job).Should().NotContain("GITHUB_SHA", $"{job} must use RELEASE_SHA");
            Job(workflow, job).Should().NotContain("github.sha", $"{job} must use RELEASE_SHA");
        }

        // Every job that uses RELEASE_SHA gets it from the resolve output through its own env, and
        // waits for the resolve job.
        foreach (var job in new[] { "candidate", "fault-injection", "release-tests", "package", "suite-package", "publication-gate", "publish", "publish-suite", "verify-publication" })
        {
            var body = Job(workflow, job);
            body.Should().Contain("RELEASE_SHA: ${{ needs.resolve.outputs.release_sha }}", $"{job} must take RELEASE_SHA from the resolve job");
            Regex.IsMatch(body, @"(?m)^    needs: \[[^\]]*\bresolve\b").Should().BeTrue($"{job} must need resolve");
        }
        foreach (var job in new[] { "prepare", "publish-permission", "early-abort", "release-gate" })
        {
            Job(workflow, job).Should().NotContain("RELEASE_SHA", $"{job} does not use the release commit");
        }

        // Every job that builds, tests or publishes the tree checks out exactly RELEASE_SHA.
        foreach (var job in new[] { "release-tests", "package", "suite-package", "publish", "publish-suite" })
        {
            Job(workflow, job).Should().Contain("ref: ${{ env.RELEASE_SHA }}", $"{job} must build the release commit");
        }
    }

    [Fact]
    public void ReleasePackagingRehearsal_ReleasesTheHeadSoThePackagingPathStaysRehearsedAtHead()
    {
        var rehearsal = WorkspaceFileLocator.ReadAllText(".github", "workflows", "release-packaging-rehearsal.yml");
        rehearsal.Should().Contain("-f dry_run=true");
        rehearsal.Should().Contain("-f release_head=true");
    }
    [Fact]
    public void FullRelease_FaultInjectionIsDryRunOnlyMainOnlyAndOffByDefault()
    {
        var workflow = FullRelease();

        // Off by default, a closed choice.
        Regex.IsMatch(workflow, @"(?s)inject_fault:\s+description:.*?default: none\s+type: choice\s+options:\s+- none\s+- real\s+- infra\s+- selftest")
            .Should().BeTrue("inject_fault must default to none and offer exactly none/real/infra/selftest");

        // The candidate job rejects it first, fail closed, outside a main dry run.
        var candidate = Job(workflow, "candidate");
        var guardIndex = candidate.IndexOf("name: Reject fault injection outside a main dry run", StringComparison.Ordinal);
        guardIndex.Should().BeGreaterThan(0);
        guardIndex.Should().BeLessThan(candidate.IndexOf("- name: Checkout", StringComparison.Ordinal), "the guard runs before anything is checked out");
        candidate.Should().Contain("$env:INJECT_DRY_RUN -cne 'true'");
        candidate.Should().Contain("$env:GITHUB_REF -cne 'refs/heads/main'");
        candidate.Should().Contain("-cnotin @('', 'none', 'real', 'infra', 'selftest')");

        // The injected job is itself gated on dry run + main, runs nothing it does not name, and the
        // injected failures apply to the first attempt only so the automatic re-run can pass.
        var job = Job(workflow, "fault-injection");
        job.Should().Contain("needs: [resolve, candidate]");
        job.Should().Contain("if: ${{ inputs.dry_run && inputs.inject_fault != 'none' && github.ref == 'refs/heads/main' }}");
        job.Should().Contain("contents: read");
        job.Should().NotContain("contents: write");
        job.Should().NotContain("secrets.");
        job.Should().Contain("if: ${{ env.INJECT_FAULT == 'real' && github.run_attempt == 1 }}");
        job.Should().Contain("if: ${{ env.INJECT_FAULT == 'infra' && github.run_attempt == 1 }}");
        // The infrastructure signal is the step name the classifier treats as a service-transfer step.
        job.Should().Contain("name: Download injected rehearsal inputs");
        Regex.IsMatch(job, @"(?m)^      - name: Injected real failure$").Should().BeTrue();

        // Nothing can depend on it, so it cannot change a gate or publication.
        Regex.IsMatch(workflow, @"needs:\s*\[[^\]]*fault-injection").Should().BeFalse();

        // The value reaches scripts only through env: every use of the input is an env assignment, the
        // job condition, or the input declaration, never script text.
        var uses = workflow.Split('\n')
            .Where(l => l.Contains("inputs.inject_fault", StringComparison.Ordinal))
            .Select(l => l.Trim())
            .ToList();
        uses.Should().NotBeEmpty();
        foreach (var use in uses)
        {
            (use.StartsWith("INJECT_FAULT: ${{ inputs.inject_fault }}", StringComparison.Ordinal)
                || use.StartsWith("if: ${{ inputs.dry_run && inputs.inject_fault != 'none'", StringComparison.Ordinal))
                .Should().BeTrue($"inputs.inject_fault must only reach the job through env or the job condition: '{use}'");
        }
        workflow.Should().Contain("INJECT_DRY_RUN: ${{ inputs.dry_run }}");

        // Only the packaging rehearsal and the dry-run switch itself may mention it elsewhere; the
        // publishing jobs never read it.
        foreach (var publishJob in new[] { "publish", "publish-suite", "verify-publication" })
        {
            Job(workflow, publishJob).Should().NotContain("inject_fault");
        }
    }

    [Fact]
    public void ReleaseResilienceSelfTest_RunsRetryAndResumePlanWithoutTouchingAnyRelease()
    {
        var selfTest = WorkspaceFileLocator.ReadAllText("tools", "Test-ReleaseResilienceSelfTest.ps1");
        selfTest.Should().Contain("Invoke-WithRetry");
        selfTest.Should().Contain("-PlanOnly");
        selfTest.Should().Contain("-ExistingReleaseJsonPath");
        selfTest.Should().NotContain("gh ");
        selfTest.Should().NotContain("gh.exe");
        Job(FullRelease(), "fault-injection").Should().Contain("tools/Test-ReleaseResilienceSelfTest.ps1");
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
