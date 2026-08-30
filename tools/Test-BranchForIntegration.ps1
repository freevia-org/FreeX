[CmdletBinding()]
param(
    [string]$BaseRef = 'origin/main',

    [switch]$AllowRedMainFix,

    [switch]$SkipMainHealthCheck,

    [switch]$SkipPreflight,

    [switch]$SkipBuild,

    [switch]$SkipAffectedTests
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$branch = (& git -C $repoRoot branch --show-current).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($branch)) {
    throw 'The integration gate must run from a named Git branch.'
}
if ($branch -eq 'main') {
    throw 'Run the integration gate in the task branch before merging it into main.'
}

$dirty = @(& git -C $repoRoot status --porcelain)
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to inspect the task worktree.'
}
if ($dirty.Count -ne 0) {
    throw 'Commit or remove task-worktree changes before running the integration gate.'
}

$baseSha = (& git -C $repoRoot rev-parse --verify "$BaseRef^{commit}").Trim()
if ($LASTEXITCODE -ne 0) {
    throw "Base ref '$BaseRef' does not resolve to a commit. Fetch origin/main first."
}

if (-not $SkipMainHealthCheck) {
    $ghCommand = Get-Command gh -ErrorAction SilentlyContinue
    if ($null -eq $ghCommand) {
        throw "GitHub CLI is required to verify the exact '$BaseRef' CI result. Use -SkipMainHealthCheck only for offline diagnosis, never for integration."
    }

    $runJson = & gh run list --workflow ci.yml --commit $baseSha --limit 20 --json databaseId,status,conclusion,headSha,createdAt 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to query GitHub CI for $baseSha. $($runJson -join [Environment]::NewLine)"
    }
    $runs = @($runJson -join [Environment]::NewLine | ConvertFrom-Json) |
        Where-Object headSha -EQ $baseSha |
        Sort-Object { [DateTimeOffset]$_.createdAt } -Descending
    $latestRun = @($runs | Select-Object -First 1)
    $mainIsGreen = $latestRun.Count -eq 1 -and $latestRun[0].status -eq 'completed' -and $latestRun[0].conclusion -eq 'success'
    if (-not $mainIsGreen) {
        $state = if ($latestRun.Count -eq 0) { 'no exact-SHA CI run' } else { "$($latestRun[0].status)/$($latestRun[0].conclusion) in run $($latestRun[0].databaseId)" }
        if (-not $AllowRedMainFix) {
            throw "Refusing ordinary integration because $BaseRef ($baseSha) has $state. Only a branch that fixes that failure may use -AllowRedMainFix."
        }
        Write-Warning "Proceeding as an explicit red-main fix; $BaseRef ($baseSha) has $state."
    }
    else {
        Write-Host "Exact base CI is green: run $($latestRun[0].databaseId) at $baseSha."
    }
}

$selectionJson = & (Join-Path $PSScriptRoot 'Get-ImpactedTestGates.ps1') -BaseRef $BaseRef -HeadRef HEAD -OutputFormat Json
$selection = $selectionJson | ConvertFrom-Json
Write-Host "Changed paths: $(@($selection.changedPaths).Count); affected local commit gates: $(@($selection.gateIds).Count)."

if (-not $SkipPreflight) {
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Test-RepositoryPreflight.ps1')
    if ($LASTEXITCODE -ne 0) {
        throw "Repository preflight failed with exit code $LASTEXITCODE."
    }
}

if (-not $SkipBuild) {
    & dotnet build (Join-Path $repoRoot 'FreeX.slnx') --configuration Release
    if ($LASTEXITCODE -ne 0) {
        throw "Release build failed with exit code $LASTEXITCODE."
    }
}

if (-not $SkipAffectedTests) {
    foreach ($gateId in @($selection.gateIds)) {
        & (Join-Path $PSScriptRoot 'Invoke-TestGate.ps1') `
            -Gate commit `
            -Platform windows `
            -GateId ([string]$gateId) `
            -Configuration Release `
            -HangTimeout 15m `
            -ResultsDirectory 'artifacts/pre-merge-test-gates'
        if ($LASTEXITCODE -ne 0) {
            throw "Affected commit gate '$gateId' failed with exit code $LASTEXITCODE."
        }
    }
}

Write-Host 'Branch integration gate passed. Full cross-platform integration remains owned by GitHub CI; UI/render/release-only gates remain owned by App Tester Release.'
