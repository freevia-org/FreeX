[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$Repository = 'freevia-org/FreeX',

    # The branch whose history is searched, newest commit first.
    [string]$Branch = 'main',

    # Start from this commit instead of the tip of Branch (it must be on Branch's history).
    [ValidatePattern('^([0-9a-fA-F]{40})?$')]
    [string]$HeadSha = '',

    # How many commits back from the head are considered (the GitHub API page limit is 100).
    [ValidateRange(1, 100)]
    [int]$MaxAncestors = 100,

    [ValidateRange(1, 30)]
    [int]$MaxAgeDays = 7,

    [string]$Workflow = 'release-readiness.yml',

    # Emit one JSON object instead of the bare SHA.
    [switch]$Json,

    # Offline testing (same convention as Resolve-ReleaseGateEvidence.ps1). AncestorMetadataPath is a
    # `gh api repos/<repo>/commits` listing; RunsMetadataPath is a workflow-runs listing that serves
    # both the candidate filter here and the per-commit evidence check.
    [string]$AncestorMetadataPath = '',
    [string]$ExpectedMatrixPath = '',
    [string]$RunsMetadataPath = '',
    [string]$JobsMetadataRoot = '',
    [datetime]$Now = [datetime]::UtcNow
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Finds the newest commit of main that a full-release.yml dispatch can release through its
# `source_sha` input WITHOUT re-running the release-only test gates: the newest ancestor of the head
# whose release-readiness run proved every expected gate (fresh, each job once, all green).
#
# It does not re-implement that proof. Each candidate is judged by Resolve-ReleaseGateEvidence.ps1,
# the very script the release workflow uses, so this helper and the workflow cannot disagree about
# what "readiness-green" means, and anything ambiguous is simply not a candidate (fail closed).
#
# The answer is advisory. full-release.yml re-verifies the SHA itself (ancestry of the dispatched
# main head, the evidence against that commit's own gate manifest, and the CI/CodeQL attestation)
# and runs every gate when anything is short.
#
# full-release.yml's `resolve` job calls this by default (empty source_sha, dispatched from main) with
# -HeadSha <dispatched head> -Json, and releases the head with every gate when it exits 1.
# Usage:  pwsh -NoProfile -File tools/Get-LatestReadinessGreenSha.ps1      (preview the default choice)
#         gh workflow run full-release.yml -f release_version=0.8.193 -f release_head=true ...  (override)
# Exit code 0 with the SHA on stdout, or 1 when no candidate exists (release from the head instead).

function Read-JsonDocument([string]$OfflinePath, [scriptblock]$Query, [string]$What) {
    if (-not [string]::IsNullOrWhiteSpace($OfflinePath)) {
        $text = Get-Content -LiteralPath $OfflinePath -Raw
    }
    else {
        $text = & $Query
        if ($LASTEXITCODE -ne 0) { throw "Could not query $What." }
    }

    try { return ($text | ConvertFrom-Json) }
    catch { throw "$What is not valid JSON: $($_.Exception.Message)" }
}

function Get-Ancestors {
    $start = if ([string]::IsNullOrWhiteSpace($HeadSha)) { $Branch } else { $HeadSha.ToLowerInvariant() }
    $document = Read-JsonDocument $AncestorMetadataPath {
        & gh api --method GET "repos/$Repository/commits" -f "sha=$start" -f "per_page=$MaxAncestors"
    } "the commit listing of $start"

    $shas = @(@($document) | ForEach-Object { ([string]$_.sha).ToLowerInvariant() } | Where-Object { $_ -match '^[0-9a-f]{40}$' })
    if ($shas.Count -eq 0) { throw "No commits were returned for $start." }
    return $shas
}

try {
    $ancestors = Get-Ancestors

    # One listing of the successful readiness runs, so only commits that have one cost a jobs lookup.
    $runs = Read-JsonDocument $RunsMetadataPath {
        & gh api --method GET "repos/$Repository/actions/workflows/$([System.Uri]::EscapeDataString($Workflow))/runs" `
            -f 'status=success' -f 'per_page=100'
    } "the successful $Workflow runs"
    $greenRunShas = @(@($runs.workflow_runs) |
            Where-Object { [string]$_.status -eq 'completed' -and [string]$_.conclusion -eq 'success' } |
            ForEach-Object { ([string]$_.head_sha).ToLowerInvariant() } |
            Select-Object -Unique)

    $resolver = Join-Path $PSScriptRoot 'Resolve-ReleaseGateEvidence.ps1'
    $found = $null
    $index = 0
    foreach ($sha in $ancestors) {
        if ($greenRunShas -contains $sha) {
            $arguments = @{ Repository = $Repository; CommitSha = $sha; MaxAgeDays = $MaxAgeDays; Workflow = $Workflow; Now = $Now }
            if ($ExpectedMatrixPath) { $arguments.ExpectedMatrixPath = $ExpectedMatrixPath }
            if ($RunsMetadataPath) { $arguments.RunsMetadataPath = $RunsMetadataPath }
            if ($JobsMetadataRoot) { $arguments.JobsMetadataRoot = $JobsMetadataRoot }

            # Only the decision object is wanted; the resolver's own narration goes to the host stream.
            $decision = @(& $resolver @arguments 6>$null) | Select-Object -Last 1
            if ($null -ne $decision -and $decision.reuse -eq $true) {
                $found = [pscustomobject]@{
                    sha = $sha
                    evidenceRunId = [string]$decision.evidenceRunId
                    commitsBehindHead = $index
                    reason = [string]$decision.reason
                }
                break
            }
        }
        $index++
    }
}
catch {
    [Console]::Error.WriteLine("No readiness-green SHA could be determined (failing closed): $($_.Exception.Message)")
    exit 1
}

if ($null -eq $found) {
    [Console]::Error.WriteLine("None of the newest $($ancestors.Count) commit(s) of $Branch has a fully green, fresh $Workflow run. Release from the head and let the gates run.")
    exit 1
}

[Console]::Error.WriteLine("Readiness-green: $($found.sha) ($($found.commitsBehindHead) commit(s) behind the head): $($found.reason)")
if ($Json) { $found | ConvertTo-Json -Compress } else { $found.sha }
exit 0
