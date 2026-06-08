param(
    [string]$ArtifactRoot = "artifacts/macos-preview",
    [string]$ChecklistRoot = "artifacts/macos-preview",
    [string[]]$Runtimes = @("osx-arm64", "osx-x64"),
    [string]$ExpectedRunId,
    [string]$ExpectedRunAttempt,
    [string]$EvidencePreflightScriptPath,
    [string]$HumanChecklistScriptPath
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

function Resolve-InputPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    $currentDirectoryCandidate = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Path))
    if (Test-Path -LiteralPath $currentDirectoryCandidate) {
        return $currentDirectoryCandidate
    }

    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
}

function Assert-RequiredValue {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Value,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw "$Name is required for macOS public-preview promotion validation so stale hosted artifacts or human checklists cannot be accepted."
    }
}

function Assert-ScriptExists {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Name was not found: $Path"
    }
}

function Get-HumanChecklistPath {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Runtime
    )

    return Join-Path $Root "completed-macos-public-preview-checklist-$Runtime.md"
}

Assert-RequiredValue -Value $ExpectedRunId -Name "ExpectedRunId"
Assert-RequiredValue -Value $ExpectedRunAttempt -Name "ExpectedRunAttempt"

if ([string]::IsNullOrWhiteSpace($EvidencePreflightScriptPath)) {
    $EvidencePreflightScriptPath = Join-Path $PSScriptRoot "Test-MacOsPublicPreviewReadiness.ps1"
}

if ([string]::IsNullOrWhiteSpace($HumanChecklistScriptPath)) {
    $HumanChecklistScriptPath = Join-Path $PSScriptRoot "Test-MacOsHumanValidationChecklist.ps1"
}

$resolvedArtifactRoot = Resolve-InputPath $ArtifactRoot
$resolvedChecklistRoot = Resolve-InputPath $ChecklistRoot
$resolvedEvidencePreflightScriptPath = Resolve-InputPath $EvidencePreflightScriptPath
$resolvedHumanChecklistScriptPath = Resolve-InputPath $HumanChecklistScriptPath

if (-not (Test-Path -LiteralPath $resolvedArtifactRoot -PathType Container)) {
    throw "macOS public-preview artifact root was not found: $resolvedArtifactRoot"
}

if (-not (Test-Path -LiteralPath $resolvedChecklistRoot -PathType Container)) {
    throw "macOS public-preview checklist root was not found: $resolvedChecklistRoot"
}

Assert-ScriptExists -Path $resolvedEvidencePreflightScriptPath -Name "macOS public-preview evidence preflight script"
Assert-ScriptExists -Path $resolvedHumanChecklistScriptPath -Name "macOS human validation checklist script"

foreach ($runtime in $Runtimes) {
    if ($runtime -ne "osx-arm64" -and $runtime -ne "osx-x64") {
        throw "Unsupported macOS runtime '$runtime'. Expected osx-arm64 or osx-x64."
    }
}

Write-Host "Running macOS public-preview hosted evidence validation..."
& $resolvedEvidencePreflightScriptPath `
    -ArtifactRoot $resolvedArtifactRoot `
    -Runtimes $Runtimes `
    -ExpectedRunId $ExpectedRunId `
    -ExpectedRunAttempt $ExpectedRunAttempt `
    -DistributionCandidate `
    -RequireSeparateDiagnosticsArtifact `
    -RequireReleasePublicationArtifact

foreach ($runtime in $Runtimes) {
    $checklistPath = Get-HumanChecklistPath -Root $resolvedChecklistRoot -Runtime $runtime
    if (-not (Test-Path -LiteralPath $checklistPath -PathType Leaf)) {
        throw "Completed macOS public-preview human checklist for $runtime was not found. Expected: $checklistPath"
    }

    Write-Host "Running macOS human validation checklist for $runtime..."
    & $resolvedHumanChecklistScriptPath `
        -ChecklistPath $checklistPath `
        -ExpectedRuntime $runtime `
        -ExpectedRunId $ExpectedRunId `
        -ExpectedRunAttempt $ExpectedRunAttempt
}

Write-Host "macOS public-preview promotion preflight passed for run $ExpectedRunId attempt $ExpectedRunAttempt and runtime(s): $($Runtimes -join ', ')."
