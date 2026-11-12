[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$Repository,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string]$Sha,

    [string]$EventName = 'push',

    # Force the full matrix (scheduled drift run, dispatch with full=true).
    [switch]$Full,

    [string]$GitHubOutputPath = '',

    # Offline testing: JSON file holding the ci.yml runs response (head_sha/id) for ancestors, and a
    # directory of <runId>.json jobs responses. Both absent = ask GitHub.
    [string]$RunsJsonPath = '',
    [string]$JobsDirectory = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Chooses what a push to main must prove. Read tools/CiImpactSupport.ps1 first: it holds the safety
# argument. In short, anything not provably inert runs the FULL matrix, and EVERY failure to
# decide (API error, missing history, parse problem, unexpected exception) also runs the full
# matrix. The only way to get a reduced run is a positive, evidenced chain of reasoning.

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'CiImpactSupport.ps1')

$normalizedSha = $Sha.ToLowerInvariant()
$decision = $null
try {
    $settings = Get-CiImpactSettings -RepoRoot $repoRoot

    $ancestors = @()
    if ($EventName -eq 'push' -and -not $Full) {
        $listed = @(& git -C $repoRoot rev-list --max-count=$($settings.MaxAncestors + 1) $normalizedSha)
        if ($LASTEXITCODE -ne 0) {
            throw "git rev-list failed for $normalizedSha (history not fetched?)."
        }
        $ancestors = @($listed | Where-Object { $_ -match '^[0-9a-f]{40}$' -and $_ -ne $normalizedSha })
    }

    $successRunsBySha = $null
    $loadRuns = {
        if ($null -ne $script:successRunsBySha) { return }
        if (-not [string]::IsNullOrWhiteSpace($RunsJsonPath)) {
            $text = Get-Content -LiteralPath $RunsJsonPath -Raw
        }
        else {
            $text = & gh api --method GET "repos/$Repository/actions/workflows/ci.yml/runs" `
                -f 'branch=main' -f 'status=success' -f 'per_page=100'
            if ($LASTEXITCODE -ne 0) { throw 'Could not list successful ci.yml runs.' }
        }
        $script:successRunsBySha = @{}
        foreach ($run in @(($text | ConvertFrom-Json).workflow_runs)) {
            if ([string]$run.status -ne 'completed' -or [string]$run.conclusion -ne 'success') { continue }
            $key = ([string]$run.head_sha).ToLowerInvariant()
            if (-not $script:successRunsBySha.ContainsKey($key)) {
                $script:successRunsBySha[$key] = [System.Collections.Generic.List[object]]::new()
            }
            $script:successRunsBySha[$key].Add($run)
        }
    }

    $findFullEvidence = {
        param($candidate)
        & $loadRuns
        if (-not $script:successRunsBySha.ContainsKey($candidate)) { return $null }
        foreach ($run in @($script:successRunsBySha[$candidate] | Sort-Object -Property @{ Expression = { [datetime]$_.updated_at }; Descending = $true })) {
            if (-not [string]::IsNullOrWhiteSpace($JobsDirectory)) {
                $jobsText = Get-Content -LiteralPath (Join-Path $JobsDirectory "$($run.id).json") -Raw
            }
            else {
                $jobsText = & gh api --method GET "repos/$Repository/actions/runs/$($run.id)/jobs" -f 'per_page=100' -f 'filter=latest'
                if ($LASTEXITCODE -ne 0) { throw "Could not list jobs for run $($run.id)." }
            }
            $coverage = Get-CiRunCoverage -JobsResponse ($jobsText | ConvertFrom-Json)
            if ($coverage.Mode -eq 'full' -or $coverage.Mode -eq 'legacy') {
                return [pscustomobject]@{ Sha = $candidate; RunId = [long]$run.id; Mode = $coverage.Mode }
            }
        }
        return $null
    }

    $getChangedPaths = {
        param($anchor)
        # -z keeps odd file names unquoted; --no-renames reports a rename as delete + add so BOTH
        # sides are classified. Blob-less history is enough for a name-only tree comparison.
        $raw = & git -C $repoRoot -c core.quotepath=off diff -z --name-status --no-renames $anchor $normalizedSha
        if ($LASTEXITCODE -ne 0) { throw "git diff $anchor $normalizedSha failed." }
        $tokens = @(([string]($raw -join "`n")).Split([char]0) | Where-Object { $_ -ne '' })
        $paths = [System.Collections.Generic.List[string]]::new()
        for ($i = 0; $i -lt $tokens.Count; $i++) {
            $token = $tokens[$i].Trim("`n")
            if ($token -match '^[ACDMRTUXB][0-9]*$') {
                if ($i + 1 -ge $tokens.Count) { throw 'Malformed git diff -z output.' }
                $paths.Add($tokens[$i + 1])
                $i++
            }
            else {
                throw "Unrecognized git diff status token '$token'."
            }
        }
        return @($paths)
    }

    $decision = Resolve-CiCoverageDecision `
        -EventName $EventName `
        -ForceFull ([bool]$Full) `
        -Ancestors $ancestors `
        -InertPrefixes $settings.InertPrefixes `
        -FindFullEvidence $findFullEvidence `
        -GetChangedPaths $getChangedPaths
}
catch {
    $decision = [pscustomobject]@{
        Mode = 'full'
        Anchor = ''
        Reason = "selector failed closed to the full matrix: $($_.Exception.Message)"
        NonInertPaths = @()
    }
}

$coverageName = if ($decision.Mode -eq 'inert') { "inert anchor=$($decision.Anchor)" } else { 'full' }
Write-Host "CI coverage decision: $($decision.Mode). $($decision.Reason)"
if (-not [string]::IsNullOrWhiteSpace($GitHubOutputPath)) {
    "mode=$($decision.Mode)" | Add-Content -LiteralPath $GitHubOutputPath -Encoding utf8
    "coverage=$coverageName" | Add-Content -LiteralPath $GitHubOutputPath -Encoding utf8
}

[pscustomobject]@{
    mode = $decision.Mode
    anchor = $decision.Anchor
    reason = $decision.Reason
    nonInertPaths = @($decision.NonInertPaths | Select-Object -First 20)
} | ConvertTo-Json -Depth 3
