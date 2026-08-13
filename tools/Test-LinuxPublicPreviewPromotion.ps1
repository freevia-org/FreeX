param(
    [string]$ArtifactRoot = "artifacts",
    [string[]]$Runtimes = @("linux-x64", "linux-arm64"),
    [string]$ExpectedRunId,
    [string]$ExpectedRunAttempt,
    [string]$ReadinessScriptPath,
    [switch]$PublicPreviewCandidate,
    [switch]$AccessibilityKeyboardOnly,
    [switch]$AccessibilityScreenReader,
    [switch]$AccessibilityX11,
    [switch]$AccessibilityWayland,
    [switch]$AccessibilityKnownIssuesReviewed,
    [string]$ChecklistPath,
    [string]$ManifestPath = "artifacts/linux-preview-promotion-manifest.json"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$validationErrors = New-Object System.Collections.Generic.List[string]

. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

function Add-ValidationError {
    param([Parameter(Mandatory = $true)][string]$Message)

    Add-ToolValidationError -Errors $validationErrors -Message $Message -GitHubTitle "Linux public-preview promotion"
}

# 1. Artifact readiness must pass first (evidence contract + checksum integrity).
if (-not $ReadinessScriptPath) {
    $ReadinessScriptPath = Join-Path $PSScriptRoot "Test-LinuxPublicPreviewReadiness.ps1"
}
if (-not (Test-Path -LiteralPath $ReadinessScriptPath)) {
    Add-ValidationError "Readiness script '$ReadinessScriptPath' was not found."
}
else {
    $readinessArgs = @{
        ArtifactRoot = $ArtifactRoot
        Runtimes     = $Runtimes
        ManifestPath = "artifacts/linux-preview-readiness-manifest.json"
    }
    if ($ExpectedRunId) { $readinessArgs.ExpectedRunId = $ExpectedRunId }
    if ($ExpectedRunAttempt) { $readinessArgs.ExpectedRunAttempt = $ExpectedRunAttempt }

    $global:LASTEXITCODE = 0
    & $ReadinessScriptPath @readinessArgs
    if ($LASTEXITCODE -ne 0) {
        Add-ValidationError "Artifact readiness validation failed; promotion cannot proceed."
    }
}

# 2. Accessibility evidence gates a public-preview candidate (mirrors the Windows
#    tester-release accessibility gate and the macOS human-validation checklist).
$accessibility = [ordered]@{
    keyboard_only         = [bool]$AccessibilityKeyboardOnly
    screen_reader_at_spi  = [bool]$AccessibilityScreenReader
    x11_session           = [bool]$AccessibilityX11
    wayland_session       = [bool]$AccessibilityWayland
    known_issues_reviewed = [bool]$AccessibilityKnownIssuesReviewed
}

$channel = "internal-preview"
if ($PublicPreviewCandidate) {
    $channel = "public-preview-candidate"
    foreach ($key in $accessibility.Keys) {
        if (-not $accessibility[$key]) {
            Add-ValidationError "Public-preview candidate requires accessibility evidence: $key was not recorded."
        }
    }

    # When a completed human-validation checklist is supplied, it must validate too.
    if ($ChecklistPath) {
        $checklistScript = Join-Path $PSScriptRoot "Test-LinuxHumanValidationChecklist.ps1"
        if (-not (Test-Path -LiteralPath $checklistScript)) {
            Add-ValidationError "Human-validation script '$checklistScript' was not found."
        }
        else {
            $checklistArgs = @{ ChecklistPath = $ChecklistPath }
            if ($ExpectedRunId) { $checklistArgs.ExpectedRunId = $ExpectedRunId }
            if ($ExpectedRunAttempt) { $checklistArgs.ExpectedRunAttempt = $ExpectedRunAttempt }
            $global:LASTEXITCODE = 0
            & $checklistScript @checklistArgs
            if ($LASTEXITCODE -ne 0) {
                Add-ValidationError "Human-validation checklist failed; public-preview promotion cannot proceed."
            }
        }
    }
}

$status = if ($validationErrors.Count -eq 0) {
    if ($PublicPreviewCandidate) { "public_preview_ready" } else { "internal_preview_ready" }
}
else { "blocked" }

$manifest = [ordered]@{
    schema = "io.github.tony-xmelon.freex.linux-promotion.v1"
    channel = $channel
    status = $status
    repository = $env:GITHUB_REPOSITORY
    run_id = $env:GITHUB_RUN_ID
    run_attempt = $env:GITHUB_RUN_ATTEMPT
    commit = $env:GITHUB_SHA
    runtimes = $Runtimes
    accessibility_evidence = $accessibility
}

$manifestFull = Resolve-InputPath -Path $ManifestPath -RepoRoot $repoRoot
$manifestDir = Split-Path -Parent $manifestFull
if (-not (Test-Path -LiteralPath $manifestDir)) {
    New-Item -ItemType Directory -Path $manifestDir -Force | Out-Null
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestFull -Encoding ascii

if ($validationErrors.Count -gt 0) {
    Write-Host ""
    Write-Host "Linux promotion BLOCKED with $($validationErrors.Count) issue(s)."
    exit 1
}

Write-Host "Linux promotion status: $status (channel: $channel)."
exit 0
