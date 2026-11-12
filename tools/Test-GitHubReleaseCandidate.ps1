[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$Repository,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string]$CommitSha,

    [string[]]$RequiredWorkflows = @('ci.yml', 'codeql.yml'),

    [string]$RunMetadataDirectory = '',

    # Workflows whose runs may be impact-reduced and therefore must prove FULL coverage (directly,
    # or through a full-coverage anchor commit). See tools/CiImpactSupport.ps1.
    [string[]]$CoverageWorkflows = @('ci.yml'),

    # The commit and its ancestors, newest first (as Resolve-GitHubReleaseCandidate.ps1 lists
    # them). Used to prove a coverage anchor is an ancestor; without it the compare API is asked.
    [string[]]$AncestorShas = @()
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ($RequiredWorkflows.Count -eq 0) {
    throw 'At least one required workflow must be specified.'
}

. (Join-Path $PSScriptRoot 'CiImpactSupport.ps1')

$normalizedSha = $CommitSha.ToLowerInvariant()
$verifiedRuns = [System.Collections.Generic.List[object]]::new()
$normalizedAncestors = @($AncestorShas | ForEach-Object { ([string]$_).ToLowerInvariant() })

function Get-WorkflowRunsResponse {
    param([string]$Workflow, [string]$Sha, [string]$MetadataDirectory)

    if ([string]::IsNullOrWhiteSpace($MetadataDirectory)) {
        $encodedWorkflow = [System.Uri]::EscapeDataString($Workflow)
        # Query fields go through -F rather than inline in the URL: on Windows gh resolves via a
        # .cmd shim, and an unquoted "&" there is handed to cmd.exe as a command separator.
        $text = & gh api --method GET "repos/$Repository/actions/workflows/$encodedWorkflow/runs" `
            -f "head_sha=$Sha" -f "status=completed" -f "per_page=20"
        if ($LASTEXITCODE -ne 0) {
            throw "Could not query completed '$Workflow' runs for $Sha."
        }
    }
    else {
        $metadataPath = Join-Path $MetadataDirectory "$Workflow.json"
        if (-not (Test-Path -LiteralPath $metadataPath -PathType Leaf)) {
            throw "Offline workflow metadata was not found: $metadataPath"
        }
        $text = Get-Content -LiteralPath $metadataPath -Raw
    }

    try {
        return ($text | ConvertFrom-Json)
    }
    catch {
        throw "Workflow metadata for '$Workflow' is not valid JSON: $($_.Exception.Message)"
    }
}

function Get-SuccessfulRuns {
    param([object]$Response, [string]$Sha)

    return @(
        @($Response.workflow_runs) |
            Where-Object {
                ([string]$_.head_sha).ToLowerInvariant() -eq $Sha -and
                [string]$_.status -eq 'completed' -and
                [string]$_.conclusion -eq 'success'
            } |
            Sort-Object -Property @{ Expression = { [datetime]$_.updated_at }; Descending = $true }
    )
}

# What one successful run claims to have covered. Throws when the evidence cannot be fetched: an
# unreadable claim is never treated as a full one.
function Get-RunCoverage {
    param([string]$Workflow, [object]$Run, [string]$MetadataDirectory)

    if ([string]::IsNullOrWhiteSpace($MetadataDirectory)) {
        $text = & gh api --method GET "repos/$Repository/actions/runs/$($Run.id)/jobs" -f 'per_page=100' -f 'filter=latest'
        if ($LASTEXITCODE -ne 0) {
            throw "Could not list jobs of '$Workflow' run $($Run.id)."
        }
        return (Get-CiRunCoverage -JobsResponse ($text | ConvertFrom-Json))
    }

    # Offline fixtures: <workflow>.<runId>.jobs.json. No file = a run from before impact selection.
    $jobsPath = Join-Path $MetadataDirectory "$Workflow.$($Run.id).jobs.json"
    if (-not (Test-Path -LiteralPath $jobsPath -PathType Leaf)) {
        return [pscustomobject]@{ Mode = 'legacy'; Anchor = '' }
    }
    return (Get-CiRunCoverage -JobsResponse ((Get-Content -LiteralPath $jobsPath -Raw) | ConvertFrom-Json))
}

# $null when the run is an acceptable attestation of the whole commit; otherwise the reason it is not.
function Get-RunCoverageProblem {
    param([string]$Workflow, [object]$Run, [string]$Sha, [string]$MetadataDirectory)

    $coverage = Get-RunCoverage -Workflow $Workflow -Run $Run -MetadataDirectory $MetadataDirectory
    if ($coverage.Mode -eq 'full' -or $coverage.Mode -eq 'legacy') {
        return $null
    }
    if ($coverage.Mode -ne 'inert') {
        return "run $($Run.id) has an unreadable coverage record"
    }

    $anchor = $coverage.Anchor
    if ($anchor -eq $Sha) {
        return "run $($Run.id) names its own commit as its coverage anchor"
    }

    # The anchor must itself be a commit with a successful run that covered everything.
    $anchorDirectory = ''
    if (-not [string]::IsNullOrWhiteSpace($MetadataDirectory)) {
        $anchorDirectory = Join-Path (Split-Path -Parent $MetadataDirectory) $anchor
    }
    $anchorResponse = Get-WorkflowRunsResponse -Workflow $Workflow -Sha $anchor -MetadataDirectory $anchorDirectory
    $anchorProven = $false
    foreach ($anchorRun in (Get-SuccessfulRuns -Response $anchorResponse -Sha $anchor)) {
        $anchorCoverage = Get-RunCoverage -Workflow $Workflow -Run $anchorRun -MetadataDirectory $anchorDirectory
        if ($anchorCoverage.Mode -eq 'full' -or $anchorCoverage.Mode -eq 'legacy') {
            $anchorProven = $true
            break
        }
    }
    if (-not $anchorProven) {
        return "run $($Run.id) is an inert-only run anchored on $($anchor.Substring(0, 9)), which has no successful full-coverage run"
    }

    # ...and an ancestor of this commit, otherwise its green says nothing about this tree.
    $own = [array]::IndexOf($normalizedAncestors, $Sha)
    $anchorIndex = [array]::IndexOf($normalizedAncestors, $anchor)
    if ($own -ge 0 -and $anchorIndex -gt $own) {
        return $null
    }
    if (-not [string]::IsNullOrWhiteSpace($MetadataDirectory)) {
        return "run $($Run.id) anchor $($anchor.Substring(0, 9)) is not shown to be an ancestor"
    }
    $compare = & gh api --method GET "repos/$Repository/compare/$anchor...$Sha" -f 'per_page=1'
    if ($LASTEXITCODE -ne 0) {
        throw "Could not compare anchor $anchor with $Sha."
    }
    $status = [string]($compare | ConvertFrom-Json).status
    if ($status -eq 'ahead' -or $status -eq 'identical') {
        return $null
    }
    return "run $($Run.id) anchor $($anchor.Substring(0, 9)) is not an ancestor of $Sha (compare status '$status')"
}

foreach ($workflow in $RequiredWorkflows) {
    if ([string]::IsNullOrWhiteSpace($workflow) -or $workflow -match '[\\/]') {
        throw "Required workflow '$workflow' must be a workflow file name."
    }

    $response = Get-WorkflowRunsResponse -Workflow $workflow -Sha $normalizedSha -MetadataDirectory $RunMetadataDirectory
    $matchingRuns = @(Get-SuccessfulRuns -Response $response -Sha $normalizedSha)
    if ($matchingRuns.Count -eq 0) {
        throw "No successful completed '$workflow' run exists for exact commit $normalizedSha."
    }

    # A green run only attests the WHOLE commit when it covered the whole matrix. An impact-reduced
    # ci.yml run is green by construction, so it must additionally name a full-coverage ancestor
    # anchor (tools/CiImpactSupport.ps1). Take the newest run that proves it.
    if ($CoverageWorkflows -contains $workflow) {
        $coverageProblems = [System.Collections.Generic.List[string]]::new()
        $provenRun = $null
        foreach ($candidateRun in $matchingRuns) {
            try {
                $problem = Get-RunCoverageProblem -Workflow $workflow -Run $candidateRun -Sha $normalizedSha -MetadataDirectory $RunMetadataDirectory
            }
            catch {
                # Evidence that cannot be read is evidence that does not exist.
                $problem = "coverage of run $($candidateRun.id) could not be verified ($($_.Exception.Message))"
            }
            if ($null -eq $problem) {
                $provenRun = $candidateRun
                break
            }
            $coverageProblems.Add($problem)
        }
        if ($null -eq $provenRun) {
            throw "No successful '$workflow' run for $normalizedSha proves full coverage: $($coverageProblems -join '; ')."
        }
        $matchingRuns = @($provenRun)
    }

    $run = $matchingRuns[0]
    $verifiedRuns.Add([pscustomobject]@{
        workflow = $workflow
        runId = [long]$run.id
        commitSha = $normalizedSha
        url = [string]$run.html_url
    })
    Write-Host "Verified $workflow run $($run.id) for ${normalizedSha}: $($run.html_url)"
}

[pscustomobject]@{
    repository = $Repository
    commitSha = $normalizedSha
    workflows = $verifiedRuns.ToArray()
} | ConvertTo-Json -Depth 4
