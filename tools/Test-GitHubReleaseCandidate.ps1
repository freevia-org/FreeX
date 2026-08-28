[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$Repository,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string]$CommitSha,

    [string[]]$RequiredWorkflows = @('ci.yml', 'codeql.yml'),

    [string]$RunMetadataDirectory = ''
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ($RequiredWorkflows.Count -eq 0) {
    throw 'At least one required workflow must be specified.'
}

$normalizedSha = $CommitSha.ToLowerInvariant()
$verifiedRuns = [System.Collections.Generic.List[object]]::new()

foreach ($workflow in $RequiredWorkflows) {
    if ([string]::IsNullOrWhiteSpace($workflow) -or $workflow -match '[\\/]') {
        throw "Required workflow '$workflow' must be a workflow file name."
    }

    if ([string]::IsNullOrWhiteSpace($RunMetadataDirectory)) {
        $encodedWorkflow = [System.Uri]::EscapeDataString($workflow)
        $responseText = & gh api "repos/$Repository/actions/workflows/$encodedWorkflow/runs?head_sha=$normalizedSha&status=completed&per_page=20"
        if ($LASTEXITCODE -ne 0) {
            throw "Could not query completed '$workflow' runs for $normalizedSha."
        }
    }
    else {
        $metadataPath = Join-Path $RunMetadataDirectory "$workflow.json"
        if (-not (Test-Path -LiteralPath $metadataPath -PathType Leaf)) {
            throw "Offline workflow metadata was not found: $metadataPath"
        }
        $responseText = Get-Content -LiteralPath $metadataPath -Raw
    }

    try {
        $response = $responseText | ConvertFrom-Json
    }
    catch {
        throw "Workflow metadata for '$workflow' is not valid JSON: $($_.Exception.Message)"
    }

    $matchingRuns = @(
        @($response.workflow_runs) |
            Where-Object {
                ([string]$_.head_sha).ToLowerInvariant() -eq $normalizedSha -and
                [string]$_.status -eq 'completed' -and
                [string]$_.conclusion -eq 'success'
            } |
            Sort-Object -Property @{ Expression = { [datetime]$_.updated_at }; Descending = $true }
    )
    if ($matchingRuns.Count -eq 0) {
        throw "No successful completed '$workflow' run exists for exact commit $normalizedSha."
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
