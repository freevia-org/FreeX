[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Offline proof of the failure classifier that decides when a release run is stopped early and when it
# is retried once. Every case is a synthetic jobs listing; nothing reaches GitHub. The cases that
# matter most are the fail-closed ones: whatever is not positively an infrastructure failure must be
# 'real', because 'real' is what stops the run and what forbids an automatic retry.

$script = Join-Path $PSScriptRoot 'Get-ReleaseRunFailureClass.ps1'
$root = Join-Path ([System.IO.Path]::GetTempPath()) ("freex-failure-class-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path (Join-Path $root 'annotations') | Out-Null
$errors = [System.Collections.Generic.List[string]]::new()

function New-Job([long]$Id, [string]$Name, [string]$Status, [string]$Conclusion, [object[]]$Steps) {
    return [ordered]@{ id = $Id; name = $Name; status = $Status; conclusion = $Conclusion; steps = $Steps }
}

function New-Step([string]$Name, [string]$Conclusion) {
    return [ordered]@{ name = $Name; status = 'completed'; conclusion = $Conclusion }
}

function Invoke-Classifier([object[]]$Jobs, [hashtable]$Annotations = @{}) {
    $jobsPath = Join-Path $root 'jobs.json'
    [ordered]@{ total_count = @($Jobs).Count; jobs = @($Jobs) } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jobsPath -Encoding utf8
    Get-ChildItem -LiteralPath (Join-Path $root 'annotations') -File | Remove-Item -Force
    foreach ($key in $Annotations.Keys) {
        @(@{ title = ''; message = $Annotations[$key] }) | ConvertTo-Json -Depth 4 -AsArray |
            Set-Content -LiteralPath (Join-Path $root "annotations/$key.json") -Encoding utf8
    }

    $json = & $script -Repository 'o/r' -RunId '1' -JobsMetadataPath $jobsPath -AnnotationsRoot (Join-Path $root 'annotations')
    return ($json | ConvertFrom-Json)
}

function Assert-Case([string]$Name, [object]$Result, [bool]$Real, [bool]$Retryable, [int]$Failed) {
    if ([bool]$Result.hasRealFailure -ne $Real) { $errors.Add("${Name}: hasRealFailure was $($Result.hasRealFailure), expected $Real.") }
    if ([bool]$Result.retryable -ne $Retryable) { $errors.Add("${Name}: retryable was $($Result.retryable), expected $Retryable.") }
    if (@($Result.failedJobs).Count -ne $Failed) { $errors.Add("${Name}: $(@($Result.failedJobs).Count) failed job(s), expected $Failed.") }
}

try {
    $ok = New-Job 1 'Package A' 'completed' 'success' @((New-Step 'Build' 'success'))

    # A step ran and failed: a real failure, never retried.
    $r = Invoke-Classifier @($ok, (New-Job 2 'Release test X' 'completed' 'failure' @((New-Step 'Set up job' 'success'), (New-Step 'Run isolated release test gate' 'failure'))))
    Assert-Case 'real step failure' $r $true $false 1
    if ($r.firstRealFailure -notmatch 'Release test X') { $errors.Add("real step failure: firstRealFailure did not name the job: '$($r.firstRealFailure)'.") }

    # No step failed although the job did: the runner died mid-job.
    $r = Invoke-Classifier @($ok, (New-Job 3 'Package B' 'completed' 'failure' @((New-Step 'Set up job' 'success'), [ordered]@{ name = 'Build'; status = 'in_progress'; conclusion = $null })))
    Assert-Case 'runner lost mid-job' $r $false $true 1

    # A job that never acquired a runner has no steps at all.
    $r = Invoke-Classifier @((New-Job 4 'Package C' 'completed' 'failure' @()))
    Assert-Case 'never acquired a runner' $r $false $true 1

    # Only a service-transfer step failed.
    $r = Invoke-Classifier @((New-Job 5 'Package D' 'completed' 'failure' @((New-Step 'Set up job' 'success'), (New-Step 'Setup .NET' 'failure'))))
    Assert-Case 'setup step only' $r $false $true 1

    # A runner annotation names a platform fault even though a normal step is the one that failed.
    $r = Invoke-Classifier @((New-Job 6 'Package E' 'completed' 'failure' @((New-Step 'Build' 'failure')))) @{ '6' = 'The hosted runner lost communication with the server.' }
    Assert-Case 'annotated lost runner' $r $false $true 1

    # Infrastructure failure next to a real one: the run is NOT retryable, so a real failure can
    # never be re-rolled by a lucky second attempt.
    $r = Invoke-Classifier @(
        (New-Job 7 'Package F' 'completed' 'failure' @()),
        (New-Job 8 'Release test Y' 'completed' 'failure' @((New-Step 'Run isolated release test gate' 'failure'))))
    Assert-Case 'mixed' $r $true $false 2

    # A timeout is a hang; it is real and not retried.
    $r = Invoke-Classifier @((New-Job 9 'Package G' 'completed' 'timed_out' @((New-Step 'Build' 'cancelled'))))
    Assert-Case 'timeout' $r $true $false 1

    # An unreadable annotation listing is "no evidence", so a failed ordinary step stays real.
    $r = Invoke-Classifier @((New-Job 10 'Package H' 'completed' 'failure' @((New-Step 'Build' 'failure'))))
    Assert-Case 'no evidence' $r $true $false 1

    # Aggregators never count, cancelled siblings never count, and nothing failed means not retryable.
    $r = Invoke-Classifier @($ok,
        (New-Job 11 'FreeX full release gate' 'completed' 'failure' @((New-Step 'Aggregate' 'failure'))),
        (New-Job 12 'Package I' 'completed' 'cancelled' @((New-Step 'Build' 'cancelled'))))
    Assert-Case 'consequences only' $r $false $false 0

    # Pending jobs are counted, and the publication gate is recognised.
    $r = Invoke-Classifier @((New-Job 13 'Package J' 'in_progress' $null @()), (New-Job 14 'Validate complete release inventory' 'in_progress' $null @()))
    if ([int]$r.pendingJobs -ne 1) { $errors.Add("pending: counted $($r.pendingJobs), expected 1.") }
    if (-not [bool]$r.reachedPublicationGate) { $errors.Add('publication gate: not recognised.') }

    # The two shapes full-release.yml's inject_fault produces (job 'Inject a rehearsal fault'): the
    # live rehearsal is only meaningful while the classifier keeps reading them this way.
    $r = Invoke-Classifier @((New-Job 15 'Inject a rehearsal fault' 'completed' 'failure' @((New-Step 'Set up job' 'success'), (New-Step 'Checkout' 'success'), (New-Step 'Injected real failure' 'failure'))))
    Assert-Case 'injected real' $r $true $false 1
    $r = Invoke-Classifier @((New-Job 16 'Inject a rehearsal fault' 'completed' 'failure' @((New-Step 'Set up job' 'success'), (New-Step 'Checkout' 'success'), (New-Step 'Injected real failure' 'skipped'), (New-Step 'Download injected rehearsal inputs' 'failure'))))
    Assert-Case 'injected infra' $r $false $true 1
}
finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

if ($errors.Count -gt 0) {
    throw ("Release failure classification contract failed:`n - " + ($errors -join "`n - "))
}

Write-Host 'Release failure classification contract passed: 12 offline cases (real, infrastructure, mixed, timeout, consequences, gate, injected faults).'
