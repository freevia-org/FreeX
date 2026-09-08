[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$Repository,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9]+$')]
    [string]$RunId,

    [string]$ManifestPath = 'eng/test-gates.json',

    # Ordering only has to be roughly right, so this is deliberately loose: it reports a hint that has
    # stopped describing the job at all, not one that is a minute out.
    [ValidateRange(1.5, 10)]
    [double]$ToleranceFactor = 2.0,

    # Offline testing: a JSON file shaped like the runs/{id}/jobs response.
    [string]$JobMetadataPath = ''
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Why this exists.
#
# Get-TestGateMatrix.ps1 dispatches the matrix longest-first, because whatever goes last is what
# queues when the runner pool saturates. It reads costHintMinutes from the manifest to do that, and
# nothing ever checked those numbers against reality. They rotted exactly the way every other
# release-only thing in this repository rots: eleven gates declared no hint at all, so they sorted
# as zero and the longest release gate was dispatched LAST, and nine more carried stale
# overestimates (freep-avalonia-desktop declared 7.3 minutes against 4.8 measured).
#
# So the hints are now checked against the run that just executed them. This reports; it is not a
# merge gate, because a slow runner should never fail anyone's build.

$repoRoot = Split-Path -Parent $PSScriptRoot
$manifest = Get-Content -LiteralPath (Join-Path $repoRoot $ManifestPath) -Raw | ConvertFrom-Json
$gateIds = @($manifest.gates.id)

if ([string]::IsNullOrWhiteSpace($JobMetadataPath)) {
    $jobsText = & gh api --method GET "repos/$Repository/actions/runs/$RunId/jobs" -f 'per_page=100'
    if ($LASTEXITCODE -ne 0) {
        throw "Could not list jobs for run $RunId in $Repository."
    }
}
else {
    $jobsText = Get-Content -LiteralPath $JobMetadataPath -Raw
}

$measured = @{}
foreach ($job in @(($jobsText | ConvertFrom-Json).jobs)) {
    if (-not $job.started_at -or -not $job.completed_at -or $job.conclusion -ne 'success') { continue }

    # Job names end in the display gate id, which is the gate id plus a partition suffix when the
    # gate is partitioned.
    $tail = @($job.name -split ' ')[-1]
    $gateId = $gateIds | Where-Object { $tail -eq $_ -or $tail -match ('^' + [regex]::Escape($_) + '-\d+of\d+$') } | Select-Object -First 1
    if (-not $gateId) { continue }

    $minutes = ([datetime]$job.completed_at - [datetime]$job.started_at).TotalMinutes
    # The slowest platform of a gate is what the hint has to describe: that is the lane whose
    # queueing would hurt.
    if (-not $measured.ContainsKey($gateId) -or $measured[$gateId] -lt $minutes) {
        $measured[$gateId] = $minutes
    }
}

if ($measured.Count -eq 0) {
    throw "No test-gate jobs were found in run $RunId, so the cost hints cannot be checked. Has the job naming changed?"
}

$drifted = @()
foreach ($gate in $manifest.gates) {
    if (-not $measured.ContainsKey($gate.id)) { continue }

    $actual = $measured[$gate.id]
    $declared = if ($gate.PSObject.Properties.Name -contains 'costHintMinutes') { [double]$gate.costHintMinutes } else { 0.0 }

    if ($declared -le 0) {
        $drifted += ('{0}: no costHintMinutes, so it sorts as zero and dispatches last; measured {1:n1} min' -f $gate.id, $actual)
        continue
    }

    # Sub-minute gates round harshly, so give them a floor before comparing ratios.
    $floor = 1.0
    $ratio = [math]::Max($actual, $floor) / [math]::Max($declared, $floor)
    if ($ratio -gt $ToleranceFactor -or $ratio -lt (1 / $ToleranceFactor)) {
        $drifted += ('{0}: declared {1:n0} min, measured {2:n1} min' -f $gate.id, $declared, $actual)
    }
}

Write-Host ("Checked costHintMinutes for {0} gate(s) against run {1}." -f $measured.Count, $RunId)
foreach ($gate in ($manifest.gates | Sort-Object id)) {
    if (-not $measured.ContainsKey($gate.id)) { continue }
    $declared = if ($gate.PSObject.Properties.Name -contains 'costHintMinutes') { [double]$gate.costHintMinutes } else { 0.0 }
    Write-Host ('  {0,-34} declared {1,4:n0}  measured {2,5:n1}' -f $gate.id, $declared, $measured[$gate.id])
}

if ($drifted.Count -gt 0) {
    throw ("Test-gate cost hints no longer describe these gates, so the matrix is dispatching in the " +
        "wrong order and the longest job may be the one that queues:" + [Environment]::NewLine +
        '  ' + ($drifted -join ([Environment]::NewLine + '  ')))
}

Write-Host 'Every cost hint still describes its gate; the matrix dispatches longest-first.'
