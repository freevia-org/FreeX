[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$Repository,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9]+$')]
    [string]$RunId,

    # Job names that exist only to aggregate or to watch other jobs. Their failure is a consequence,
    # never a cause, so they never count as a failure of their own.
    [string[]]$IgnoredJobNames = @(
        'FreeX full release gate',
        'Validate complete release inventory',
        'Watch for a real failure and stop the run early',
        # release-readiness.yml's own aggregator: it fails whenever a gate job did, so it is a
        # consequence, never a cause. (The gate jobs and 'README download links' are judged themselves.)
        'Release readiness summary'
    ),

    [string]$GitHubOutputPath = '',

    # Offline testing: a file holding the jobs listing and a directory holding <jobId>.json
    # annotation listings. A missing annotation file means "no annotations".
    [string]$JobsMetadataPath = '',
    [string]$AnnotationsRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Why this exists.
#
# A red release used to be undifferentiated: a macOS runner that never got scheduled looks the same
# as a test that regressed. That matters in two opposite directions, and this script is the single
# place that decides which a failure is:
#
#   * a REAL failure (a step ran and failed, a job timed out, or anything this script cannot prove
#     is the platform's fault) must stop the run early -- the remaining runner minutes cannot change
#     the outcome -- and must never be retried automatically, or a flaky-looking real bug would be
#     masked by a lucky second attempt;
#   * an INFRASTRUCTURE failure (the runner was lost, never acquired, or died between steps, or a
#     step that only talks to GitHub's own services failed) says nothing about the commit, so one
#     automatic re-run of the failed jobs is the right answer.
#
# It fails CLOSED. A job is infrastructure only when positive evidence says so; every doubt -- an
# unreadable annotation listing, an unknown step, a timeout -- classifies as real. The caller turns
# 'real' into an early abort and into NO automatic retry, so a misclassification in this direction
# costs runner minutes, never a false green and never a hidden failure.

# Runner-level messages GitHub attaches to the job's check run when the platform, not the workflow,
# ended it.
$infrastructureAnnotationPatterns = @(
    'lost communication with the server',
    'received a shutdown signal',
    'runner has been shutdown',
    'was not acquired by Runner',
    'The hosted runner encountered an error',
    'hosted runner lost connection',
    'Internal server error\. Correlation ID',
    'The runner has been unexpectedly terminated'
)

# A failed step with one of these names only talks to GitHub's own services (job setup, source and
# artifact transfer) and carries no verdict about the commit.
$infrastructureStepPattern = '^(Set up job|Initialize containers|Checkout\b|Setup \.NET|Download\b|Complete job|Post )'

function Get-JobsDocument {
    if (-not [string]::IsNullOrWhiteSpace($JobsMetadataPath)) {
        return (Get-Content -LiteralPath $JobsMetadataPath -Raw | ConvertFrom-Json)
    }

    $jobs = [System.Collections.Generic.List[object]]::new()
    $page = 1
    while ($true) {
        $text = & gh api --method GET "repos/$Repository/actions/runs/$RunId/jobs" -f 'filter=latest' -f 'per_page=100' -f "page=$page"
        if ($LASTEXITCODE -ne 0) { throw "Could not list the jobs of run $RunId." }
        $document = $text | ConvertFrom-Json
        $batch = @($document.jobs)
        foreach ($job in $batch) { $jobs.Add($job) }
        if ($batch.Count -lt 100 -or $jobs.Count -ge [int]$document.total_count) { break }
        $page++
    }

    return [pscustomobject]@{ jobs = @($jobs) }
}

function Get-JobAnnotationText([object]$Job) {
    # Returns the concatenated annotation messages, or $null when they could not be read; the caller
    # treats $null as "no evidence", which classifies the failure as real.
    try {
        if (-not [string]::IsNullOrWhiteSpace($AnnotationsRoot)) {
            $path = Join-Path $AnnotationsRoot "$([string]$Job.id).json"
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return '' }
            $text = Get-Content -LiteralPath $path -Raw
        }
        else {
            $text = & gh api --method GET "repos/$Repository/check-runs/$([string]$Job.id)/annotations" -f 'per_page=100'
            if ($LASTEXITCODE -ne 0) { return $null }
        }

        return ((@($text | ConvertFrom-Json) | ForEach-Object { "$($_.title) $($_.message)" }) -join "`n")
    }
    catch {
        return $null
    }
}

function Get-PlatformCancellationReason([object]$Job) {
    # GitHub ends a job it could never place (for example "The job was not acquired by Runner of type
    # hosted even after multiple attempts" when macOS arm64 capacity is exhausted) as CANCELLED with
    # no steps, not as failed -- release-readiness run 45222271749 lost three macOS jobs that way. A
    # cancelled job is also what a human, a concurrency group or the early-abort watcher produces, so
    # it counts only with a positive runner annotation; no annotation, or an unreadable listing, means
    # it is a consequence like any other cancelled sibling. Returns the reason or $null.
    $annotations = Get-JobAnnotationText $Job
    if ([string]::IsNullOrEmpty($annotations)) { return $null }
    foreach ($pattern in $infrastructureAnnotationPatterns) {
        if ($annotations -match $pattern) { return "the job was cancelled by the platform: runner annotation matches '$pattern'" }
    }

    return $null
}

function Get-FailureClass([object]$Job) {
    $conclusion = [string]$Job.conclusion
    if ($conclusion -eq 'timed_out') {
        return [pscustomobject]@{ Class = 'real'; Reason = 'the job hit its timeout; a hang is never retried automatically' }
    }

    $steps = @(@($Job.steps))
    $failedSteps = @($steps | Where-Object { [string]$_.conclusion -eq 'failure' })

    if ($steps.Count -eq 0) {
        return [pscustomobject]@{ Class = 'infrastructure'; Reason = 'the job failed before any step ran (never acquired a runner)' }
    }

    if ($failedSteps.Count -eq 0) {
        return [pscustomobject]@{ Class = 'infrastructure'; Reason = 'the job failed but no step failed (runner lost or terminated mid-job)' }
    }

    $annotations = Get-JobAnnotationText $Job
    if (-not [string]::IsNullOrEmpty($annotations)) {
        foreach ($pattern in $infrastructureAnnotationPatterns) {
            if ($annotations -match $pattern) {
                return [pscustomobject]@{ Class = 'infrastructure'; Reason = "runner annotation matches '$pattern'" }
            }
        }
    }

    $nonTransient = @($failedSteps | Where-Object { [string]$_.name -notmatch $infrastructureStepPattern })
    if ($nonTransient.Count -eq 0) {
        return [pscustomobject]@{ Class = 'infrastructure'; Reason = "only service-transfer step(s) failed: $((@($failedSteps | ForEach-Object { [string]$_.name })) -join ', ')" }
    }

    return [pscustomobject]@{ Class = 'real'; Reason = "step '$([string]$nonTransient[0].name)' failed" }
}

$document = Get-JobsDocument
$failed = [System.Collections.Generic.List[object]]::new()
$pending = 0
$reachedPublicationGate = $false
foreach ($job in @($document.jobs)) {
    if ([string]$job.name -eq 'Validate complete release inventory' -and [string]$job.status -in @('in_progress', 'completed')) { $reachedPublicationGate = $true }
    if ($IgnoredJobNames -contains [string]$job.name) { continue }
    if ([string]$job.status -ne 'completed') { $pending++; continue }
    if ([string]$job.conclusion -eq 'cancelled') {
        $cancelReason = Get-PlatformCancellationReason $job
        if ($null -ne $cancelReason) {
            $failed.Add([pscustomobject]@{ id = [string]$job.id; name = [string]$job.name; class = 'infrastructure'; reason = $cancelReason })
        }
        continue
    }
    if ([string]$job.conclusion -notin @('failure', 'timed_out')) { continue }

    $class = Get-FailureClass $job
    $failed.Add([pscustomobject]@{
        id = [string]$job.id
        name = [string]$job.name
        class = $class.Class
        reason = $class.Reason
    })
}

$real = @($failed | Where-Object { $_.class -eq 'real' })
$infrastructure = @($failed | Where-Object { $_.class -eq 'infrastructure' })
$result = [pscustomobject]@{
    hasRealFailure = ($real.Count -gt 0)
    hasInfrastructureFailure = ($infrastructure.Count -gt 0)
    # Retry only when something failed and EVERYTHING that failed is infrastructure.
    retryable = ($failed.Count -gt 0 -and $real.Count -eq 0)
    firstRealFailure = $(if ($real.Count -gt 0) { "$($real[0].name): $($real[0].reason)" } else { '' })
    pendingJobs = $pending
    # Every package and test job has finished by the time the publication gate starts, so nothing is left to stop.
    reachedPublicationGate = $reachedPublicationGate
    failedJobs = @($failed)
}

if (-not [string]::IsNullOrWhiteSpace($GitHubOutputPath)) {
    "has_real_failure=$($result.hasRealFailure.ToString().ToLowerInvariant())" | Add-Content -LiteralPath $GitHubOutputPath -Encoding utf8
    "retryable=$($result.retryable.ToString().ToLowerInvariant())" | Add-Content -LiteralPath $GitHubOutputPath -Encoding utf8
}

$result | ConvertTo-Json -Depth 5
