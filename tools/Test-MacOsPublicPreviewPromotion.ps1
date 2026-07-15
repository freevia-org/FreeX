param(
    [string]$ArtifactRoot = "artifacts/macos-preview",
    [string]$ChecklistRoot = "artifacts/macos-preview",
    [string[]]$Runtimes = @("osx-arm64", "osx-x64"),
    [string]$ExpectedRunId,
    [string]$ExpectedRunAttempt,
    [string]$EvidencePreflightScriptPath,
    [string]$HumanChecklistScriptPath,
    [switch]$PrepareHumanValidationHandoff
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

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

function Get-HumanChecklistTemplatePath {
    return Join-Path $repoRoot "docs/release/macos-public-preview-checklist.md"
}

function Format-CommandArgument {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Value)

    if ($Value -match '^[A-Za-z0-9_.:/\\-]+$') {
        return $Value
    }

    return '"' + $Value.Replace('"', '\"') + '"'
}

function Format-RuntimesArgument {
    param([Parameter(Mandatory = $true)][string[]]$RuntimeValues)

    return ($RuntimeValues | ForEach-Object { Format-CommandArgument -Value $_ }) -join ","
}

function Write-HumanValidationHandoff {
    param(
        [Parameter(Mandatory = $true)][string]$ArtifactRoot,
        [Parameter(Mandatory = $true)][string]$ChecklistRoot,
        [Parameter(Mandatory = $true)][string]$TemplatePath,
        [Parameter(Mandatory = $true)][string[]]$RuntimeValues
    )

    Write-Host "macOS public-preview human validation handoff"
    Write-Host "Hosted evidence passed for run $ExpectedRunId attempt $ExpectedRunAttempt."
    Write-Host "Checklist template: $TemplatePath"
    Write-Host "Release assets artifact: freex-$ExpectedRunId-$ExpectedRunAttempt-macos-release-assets"

    foreach ($runtime in $RuntimeValues) {
        $checklistPath = Get-HumanChecklistPath -Root $ChecklistRoot -Runtime $runtime
        $appArtifactName = "freex-$ExpectedRunId-$ExpectedRunAttempt-$runtime-macos-app"
        $diagnosticsArtifactName = "freex-$ExpectedRunId-$ExpectedRunAttempt-$runtime-macos-diagnostics"

        Write-Host ""
        Write-Host "Runtime: $runtime"
        Write-Host "Expected completed checklist: $checklistPath"
        Write-Host "Template path: $TemplatePath"
        Write-Host "Expected app artifact wrapper: $appArtifactName"
        Write-Host "Expected diagnostics artifact wrapper: $diagnosticsArtifactName"
        Write-Host "Expected release-assets wrapper: freex-$ExpectedRunId-$ExpectedRunAttempt-macos-release-assets"
        Write-Host ("Validate completed checklist: powershell.exe -NoProfile -ExecutionPolicy Bypass -File {0} -ChecklistPath {1} -ExpectedRuntime {2} -ExpectedRunId {3} -ExpectedRunAttempt {4}" -f
            (Format-CommandArgument -Value $resolvedHumanChecklistScriptPath),
            (Format-CommandArgument -Value $checklistPath),
            (Format-CommandArgument -Value $runtime),
            (Format-CommandArgument -Value $ExpectedRunId),
            (Format-CommandArgument -Value $ExpectedRunAttempt))
    }

    Write-Host ""
    Write-Host ("Final promotion command after all completed checklists pass: powershell.exe -NoProfile -ExecutionPolicy Bypass -File {0} -ArtifactRoot {1} -ChecklistRoot {2} -Runtimes {3} -ExpectedRunId {4} -ExpectedRunAttempt {5}" -f
        (Format-CommandArgument -Value $PSCommandPath),
        (Format-CommandArgument -Value $ArtifactRoot),
        (Format-CommandArgument -Value $ChecklistRoot),
        (Format-RuntimesArgument -RuntimeValues $RuntimeValues),
        (Format-CommandArgument -Value $ExpectedRunId),
        (Format-CommandArgument -Value $ExpectedRunAttempt))
}

Assert-RequiredValue -Value $ExpectedRunId -Name "ExpectedRunId"
Assert-RequiredValue -Value $ExpectedRunAttempt -Name "ExpectedRunAttempt"

if ([string]::IsNullOrWhiteSpace($EvidencePreflightScriptPath)) {
    $EvidencePreflightScriptPath = Join-Path $PSScriptRoot "Test-MacOsPublicPreviewReadiness.ps1"
}

if ([string]::IsNullOrWhiteSpace($HumanChecklistScriptPath)) {
    $HumanChecklistScriptPath = Join-Path $PSScriptRoot "Test-MacOsHumanValidationChecklist.ps1"
}

$resolvedArtifactRoot = Resolve-InputPath -Path $ArtifactRoot -RepoRoot $repoRoot
$resolvedChecklistRoot = Resolve-InputPath -Path $ChecklistRoot -RepoRoot $repoRoot
$resolvedEvidencePreflightScriptPath = Resolve-InputPath -Path $EvidencePreflightScriptPath -RepoRoot $repoRoot
$resolvedHumanChecklistScriptPath = Resolve-InputPath -Path $HumanChecklistScriptPath -RepoRoot $repoRoot
$resolvedHumanChecklistTemplatePath = Get-HumanChecklistTemplatePath

if (-not (Test-Path -LiteralPath $resolvedArtifactRoot -PathType Container)) {
    throw "macOS public-preview artifact root was not found: $resolvedArtifactRoot"
}

if (-not (Test-Path -LiteralPath $resolvedChecklistRoot -PathType Container)) {
    throw "macOS public-preview checklist root was not found: $resolvedChecklistRoot"
}

Assert-ScriptExists -Path $resolvedEvidencePreflightScriptPath -Name "macOS public-preview evidence preflight script"
Assert-ScriptExists -Path $resolvedHumanChecklistScriptPath -Name "macOS human validation checklist script"
Assert-ScriptExists -Path $resolvedHumanChecklistTemplatePath -Name "macOS public-preview human validation checklist template"

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
    -RequireAggregateReadinessArtifact `
    -RequireReleasePublicationArtifact

if ($PrepareHumanValidationHandoff) {
    Write-HumanValidationHandoff `
        -ArtifactRoot $ArtifactRoot `
        -ChecklistRoot $resolvedChecklistRoot `
        -TemplatePath $resolvedHumanChecklistTemplatePath `
        -RuntimeValues $Runtimes
    return
}

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
