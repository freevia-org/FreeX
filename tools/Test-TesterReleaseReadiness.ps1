param(
    [string]$ProgressPath = "release/progress.json",
    [string]$WorkflowPath = ".github/workflows/full-release.yml",
    [string]$DistributionPlanPath = "docs/release/test-distribution.md",
    [string]$ChecklistPath = "docs/release/tester-release-checklist.md",
    [int]$RunNumber = 0,
    [switch]$PublicPreviewCandidate,
    [switch]$AccessibilityKeyboardOnly,
    [switch]$AccessibilityScreenReader,
    [switch]$AccessibilityUiaCatalog,
    [switch]$AccessibilityKnownIssues
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

function Assert-Contains {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$Expected,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if (-not $Text.Contains($Expected)) {
        throw "$Label is missing required release-readiness marker: $Expected"
    }
}

function Get-TesterMinorVersion {
    param([Parameter(Mandatory = $true)][int]$OverallCompletion)

    if ($OverallCompletion -ge 99) { return 9 }
    if ($OverallCompletion -ge 95) { return 8 }
    if ($OverallCompletion -ge 93) { return 7 }
    if ($OverallCompletion -ge 90) { return 6 }
    return 5
}

$progressFile = Resolve-ToolRepoPath -Path $ProgressPath -RepoRoot $repoRoot
$workflowFile = Resolve-ToolRepoPath -Path $WorkflowPath -RepoRoot $repoRoot
$distributionPlanFile = Resolve-ToolRepoPath -Path $DistributionPlanPath -RepoRoot $repoRoot
$checklistFile = Resolve-ToolRepoPath -Path $ChecklistPath -RepoRoot $repoRoot

foreach ($path in @($progressFile, $workflowFile, $distributionPlanFile, $checklistFile)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Release-readiness input was not found: $path"
    }
}

$progress = Get-Content -LiteralPath $progressFile -Raw | ConvertFrom-Json
foreach ($propertyName in @("major", "overallCompletion", "releasePatchBase", "releasePatchSource", "channel")) {
    if (-not $progress.PSObject.Properties.Name.Contains($propertyName)) {
        throw "release/progress.json is missing required property '$propertyName'."
    }
}

$major = [int]$progress.major
$overallCompletion = [int]$progress.overallCompletion
$releasePatchBase = [int]$progress.releasePatchBase
$releasePatchSource = [string]$progress.releasePatchSource
$channel = [string]$progress.channel

if ($major -lt 0) {
    throw "release/progress.json major must be non-negative."
}
if ($overallCompletion -lt 0 -or $overallCompletion -gt 100) {
    throw "release/progress.json overallCompletion must be between 0 and 100."
}
if ($releasePatchBase -lt 0) {
    throw "release/progress.json releasePatchBase must be non-negative."
}
if ($releasePatchSource -ne "github_run_number") {
    throw "Unsupported releasePatchSource '$releasePatchSource'."
}
if ($channel -ne "test") {
    throw "Unsupported release channel '$channel'."
}
if ($RunNumber -lt 0) {
    throw "RunNumber must be non-negative."
}

$minor = Get-TesterMinorVersion -OverallCompletion $overallCompletion
$patch = $releasePatchBase + $RunNumber
$version = "$major.$minor.$patch"
$stream = "v$major.$minor.<run>"

$workflow = Get-Content -LiteralPath $workflowFile -Raw
foreach ($marker in @(
    "name: Full Signed Release",
    "app:",
    "platform:",
    "release_version:",
    "prerelease:",
    "contents: write",
    "id-token: write",
    "group: full-signed-release-",
    "Validate latest release source",
    "refs/heads/main",
    "dotnet-version: 10.0.400",
    "tools/Test-GitHubReleaseCandidate.ps1",
    "-RequiredWorkflows ci.yml,codeql.yml",
    "tools/Get-TestGateMatrix.ps1 -Gate release",
    "Validate complete release inventory",
    "gh release create",
    "Authenticate to Azure Artifact Signing",
    "azure/login@532459ea530d8321f2fb9bb10d1e0bcf23869a43",
    "Publish-WindowsVelopackPackage.ps1",
    "New-FreeSuiteWindowsBootstrapper.ps1",
    "MACOS_CODESIGN_CERTIFICATE_P12",
    "MACOS_DEVELOPER_ID_APPLICATION",
    "New-SignedMacOsReleasePackages.ps1"
)) {
    Assert-Contains -Text $workflow -Expected $marker -Label "Full Signed Release workflow"
}

$distributionPlan = Get-Content -LiteralPath $distributionPlanFile -Raw
Assert-Contains -Text $distributionPlan -Expected "Full Signed Release" -Label "Test distribution plan"
Assert-Contains -Text $distributionPlan -Expected "Azure Artifact Signing" -Label "Test distribution plan"
Assert-Contains -Text $distributionPlan -Expected "Developer ID" -Label "Test distribution plan"
Assert-Contains -Text $distributionPlan -Expected "notarization" -Label "Test distribution plan"
Assert-Contains -Text $distributionPlan -Expected "SBOM" -Label "Test distribution plan"

$checklist = Get-Content -LiteralPath $checklistFile -Raw
Assert-Contains -Text $checklist -Expected "exact-SHA CI and CodeQL" -Label "Tester release checklist"
Assert-Contains -Text $checklist -Expected "standalone executables" -Label "Tester release checklist"
Assert-Contains -Text $checklist -Expected "Velopack" -Label "Tester release checklist"
Assert-Contains -Text $checklist -Expected "Free Suite bootstrapper" -Label "Tester release checklist"
Assert-Contains -Text $checklist -Expected "Developer ID" -Label "Tester release checklist"
Assert-Contains -Text $checklist -Expected "notarization" -Label "Tester release checklist"
Assert-Contains -Text $checklist -Expected "checksums, SBOMs, and manifests" -Label "Tester release checklist"
Assert-Contains -Text $checklist -Expected "Known accessibility issues" -Label "Tester release checklist"

$missingAccessibilityGate = @()
if (-not $AccessibilityKeyboardOnly) { $missingAccessibilityGate += "Keyboard-only smoke validation" }
if (-not $AccessibilityScreenReader) { $missingAccessibilityGate += "Screen-reader smoke validation" }
if (-not $AccessibilityUiaCatalog) { $missingAccessibilityGate += "UI Automation catalog review" }
if (-not $AccessibilityKnownIssues) { $missingAccessibilityGate += "Known accessibility issues reviewed/listed" }

if ($PublicPreviewCandidate -and $missingAccessibilityGate.Count -gt 0) {
    throw "Public-preview preflight requires completed accessibility gate inputs: $($missingAccessibilityGate -join ', ')."
}

$status = if ($PublicPreviewCandidate) { "public-preview eligible" } else { "internal-only" }
Write-Host "Tester release readiness preflight passed."
Write-Host "Default tester version for run ${RunNumber}: v$version"
Write-Host "Tester stream: $stream"
Write-Host "Promotion status: $status"
