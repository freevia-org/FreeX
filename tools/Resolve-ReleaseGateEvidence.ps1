[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$Repository,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string]$CommitSha,

    # Evidence older than this is not reused: it would not notice runner-image drift.
    [ValidateRange(1, 30)]
    [int]$MaxAgeDays = 7,

    [string]$Workflow = 'release-readiness.yml',

    [string]$GitHubOutputPath = '',

    # Offline testing. ExpectedMatrixPath (a file holding the matrix JSON) replaces the Get-TestGateMatrix.ps1 call, RunsMetadataPath
    # replaces the workflow-runs query, and JobsMetadataRoot (one <runId>.json per run) replaces the
    # per-run jobs query. Now pins the clock.
    [string]$ExpectedMatrixPath = '',
    [string]$RunsMetadataPath = '',
    [string]$JobsMetadataRoot = '',
    [datetime]$Now = [datetime]::UtcNow
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Why this exists.
#
# release-readiness.yml runs every release-only test gate on a schedule, and full-release.yml used to
# run the same gates again on the same commit. This decides whether a real release may SKIP that rerun
# because a readiness run already proved the gates on exactly this commit.
#
# It is deliberately narrow, and it fails CLOSED: the only answer that skips the gates is
# reuse=true, and reuse=true is emitted only when ALL of these hold, for one single readiness run:
#   1. the run is a completed, successful run of release-readiness.yml whose head_sha is EXACTLY the
#      release commit (not an ancestor -- see below);
#   2. it finished no more than MaxAgeDays ago and not in the future;
#   3. EVERY release-gate job the release commit's own manifest expects (the Get-TestGateMatrix.ps1
#      -Gate release matrix: app + platform + display gate id) exists in that run exactly once, in
#      its latest attempt, completed and successful;
#   4. the jobs listing is complete (total_count is not larger than what was returned).
# Any lookup error, missing gate, duplicate, unparseable value or ambiguity yields reuse=false, and
# the workflow then runs all of the gates. This script never throws on a lookup failure and never
# turns an error into a skip.
#
# Why exact-SHA only and not "the newest readiness-attested ancestor plus the gates the diff could
# affect": the repository has no map from a changed path to the RELEASE gates that read it, and one
# cannot be derived safely. The release gates include source-contract tests that pin workflow, doc,
# tool and resx text, and the 30 csproj files that Compile-Include sources from outside their
# directory defeat directory ownership, so almost any path can reach almost any gate. A partial
# selection built on an incomplete map would be a silent false green on the path that publishes.

function Write-Decision([bool]$Reuse, [string]$RunId, [string]$Reason) {
    if ($Reuse) {
        Write-Host "Release-gate evidence REUSED from $Workflow run ${RunId}: $Reason"
    }
    else {
        Write-Host "Release-gate evidence NOT reused ($Reason). Every release-only gate will run."
    }

    if (-not [string]::IsNullOrWhiteSpace($GitHubOutputPath)) {
        "reuse=$($Reuse.ToString().ToLowerInvariant())" | Add-Content -LiteralPath $GitHubOutputPath -Encoding utf8
        "evidence_run_id=$RunId" | Add-Content -LiteralPath $GitHubOutputPath -Encoding utf8
    }

    [pscustomobject]@{
        reuse = $Reuse
        evidenceRunId = $RunId
        reason = $Reason
    }
}

function ConvertTo-UtcOffset($Value) {
    if ($Value -is [datetime]) {
        return [DateTimeOffset]($Value.ToUniversalTime())
    }

    return [DateTimeOffset]::Parse(
        [string]$Value,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [System.Globalization.DateTimeStyles]::AssumeUniversal)
}

function Get-ExpectedJobNames {
    if (-not [string]::IsNullOrWhiteSpace($ExpectedMatrixPath)) {
        $json = Get-Content -LiteralPath $ExpectedMatrixPath -Raw
    }
    else {
        $output = & (Join-Path $PSScriptRoot 'Get-TestGateMatrix.ps1') -Gate release
        $json = [string](@($output) | Select-Object -Last 1)
    }

    $matrix = $json | ConvertFrom-Json
    $names = @(@($matrix.include) | ForEach-Object { "$($_.app) $($_.platform) $($_.displayGateId)" })
    if ($names.Count -eq 0) {
        throw 'The release gate matrix is empty; there is nothing to attest.'
    }
    if (@($names | Select-Object -Unique).Count -ne $names.Count) {
        throw 'The release gate matrix contains duplicate job names; the evidence would be ambiguous.'
    }

    return $names
}

function Get-JsonDocument([string]$OfflinePath, [scriptblock]$Query, [string]$What) {
    if (-not [string]::IsNullOrWhiteSpace($OfflinePath)) {
        if (-not (Test-Path -LiteralPath $OfflinePath -PathType Leaf)) {
            throw "Offline $What metadata was not found: $OfflinePath"
        }
        $text = Get-Content -LiteralPath $OfflinePath -Raw
    }
    else {
        $text = & $Query
        if ($LASTEXITCODE -ne 0) {
            throw "Could not query $What."
        }
    }

    try {
        return ($text | ConvertFrom-Json)
    }
    catch {
        throw "$What metadata is not valid JSON: $($_.Exception.Message)"
    }
}

function Find-ReusableEvidence {
    $sha = $CommitSha.ToLowerInvariant()
    $expected = Get-ExpectedJobNames

    # Query fields go through -f rather than inline in the URL (see Test-GitHubReleaseCandidate.ps1).
    $runsDocument = Get-JsonDocument $RunsMetadataPath {
        & gh api --method GET "repos/$Repository/actions/workflows/$([System.Uri]::EscapeDataString($Workflow))/runs" `
            -f "head_sha=$sha" -f 'status=completed' -f 'per_page=20'
    } "completed $Workflow runs for $sha"

    $runs = @(
        @($runsDocument.workflow_runs) |
            Where-Object {
                ([string]$_.head_sha).ToLowerInvariant() -eq $sha -and
                [string]$_.status -eq 'completed' -and
                [string]$_.conclusion -eq 'success'
            } |
            Sort-Object -Property @{ Expression = { (ConvertTo-UtcOffset $_.updated_at) }; Descending = $true }
    )
    if ($runs.Count -eq 0) {
        return [pscustomobject]@{ Reuse = $false; RunId = ''; Reason = "no successful completed $Workflow run exists for exact commit $sha" }
    }

    $lastReason = ''
    # The newest few only: an older green run is also older evidence, and a long walk adds API calls
    # without adding confidence.
    foreach ($run in @($runs | Select-Object -First 3)) {
        $runId = [string][long]$run.id
        $age = $Now.ToUniversalTime() - (ConvertTo-UtcOffset $run.updated_at).UtcDateTime
        if ($age.TotalMinutes -lt -5) {
            $lastReason = "run $runId is dated in the future"
            continue
        }
        if ($age.TotalDays -gt $MaxAgeDays) {
            $lastReason = "run $runId is $([math]::Floor($age.TotalDays)) day(s) old (limit $MaxAgeDays)"
            continue
        }

        $jobsPath = if ([string]::IsNullOrWhiteSpace($JobsMetadataRoot)) { '' } else { Join-Path $JobsMetadataRoot "$runId.json" }
        $jobsDocument = Get-JsonDocument $jobsPath {
            & gh api --method GET "repos/$Repository/actions/runs/$runId/jobs" -f 'filter=latest' -f 'per_page=100'
        } "jobs of $Workflow run $runId"

        $jobs = @($jobsDocument.jobs)
        if ([int]$jobsDocument.total_count -gt $jobs.Count) {
            $lastReason = "run $runId lists $($jobs.Count) of $($jobsDocument.total_count) jobs (incomplete listing)"
            continue
        }

        $problems = [System.Collections.Generic.List[string]]::new()
        foreach ($name in $expected) {
            $named = @($jobs | Where-Object { [string]$_.name -ceq $name })
            if ($named.Count -ne 1) {
                $problems.Add("$name (found $($named.Count))")
            }
            elseif ([string]$named[0].status -ne 'completed' -or [string]$named[0].conclusion -ne 'success') {
                $problems.Add("$name ($($named[0].status)/$($named[0].conclusion))")
            }
        }
        if ($problems.Count -gt 0) {
            $lastReason = "run $runId does not prove every expected gate: $($problems -join '; ')"
            continue
        }

        return [pscustomobject]@{
            Reuse = $true
            RunId = $runId
            Reason = "all $($expected.Count) release gates succeeded on exactly $sha, $([math]::Round($age.TotalHours, 1)) hour(s) ago"
        }
    }

    return [pscustomobject]@{ Reuse = $false; RunId = ''; Reason = $lastReason }
}

try {
    $decision = Find-ReusableEvidence
}
catch {
    $decision = [pscustomobject]@{ Reuse = $false; RunId = ''; Reason = "evidence lookup failed: $($_.Exception.Message)" }
}

Write-Decision -Reuse ([bool]$decision.Reuse) -RunId ([string]$decision.RunId) -Reason ([string]$decision.Reason)
