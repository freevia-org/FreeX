param(
    [string]$ProgressPath = "release\progress.json",
    [string]$WorkflowPath = ".github\workflows\tester-release.yml",
    [string]$DistributionPlanPath = "docs\TEST_DISTRIBUTION_PLAN.md",
    [string]$ChecklistPath = "docs\TESTER_RELEASE_CHECKLIST.md",
    [int]$RunNumber = 0,
    [switch]$PublicPreviewCandidate,
    [switch]$AccessibilityKeyboardOnly,
    [switch]$AccessibilityScreenReader,
    [switch]$AccessibilityUiaCatalog,
    [switch]$AccessibilityKnownIssues
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

function Resolve-RepoPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $repoRoot $Path
}

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
    if ($OverallCompletion -ge 96) { return 8 }
    if ($OverallCompletion -ge 93) { return 7 }
    if ($OverallCompletion -ge 90) { return 6 }
    return 5
}

$progressFile = Resolve-RepoPath $ProgressPath
$workflowFile = Resolve-RepoPath $WorkflowPath
$distributionPlanFile = Resolve-RepoPath $DistributionPlanPath
$checklistFile = Resolve-RepoPath $ChecklistPath

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
    "public_preview_candidate:",
    "accessibility_keyboard_only:",
    "accessibility_screen_reader:",
    "accessibility_uia_catalog:",
    "accessibility_known_issues:",
    "Public-preview promotion requires completed accessibility gate inputs",
    "gh release create",
    "Freexcel-latest-win-x64.exe",
    "Freexcel-latest-win-x64.msix"
)) {
    Assert-Contains -Text $workflow -Expected $marker -Label "Tester Release workflow"
}

$distributionPlan = Get-Content -LiteralPath $distributionPlanFile -Raw
Assert-Contains -Text $distributionPlan -Expected "At $overallCompletion% completion, default tester releases use the ``$stream`` stream." -Label "Test distribution plan"
Assert-Contains -Text $distributionPlan -Expected "Keyboard-only smoke validation" -Label "Test distribution plan"
Assert-Contains -Text $distributionPlan -Expected "Screen-reader smoke validation" -Label "Test distribution plan"
Assert-Contains -Text $distributionPlan -Expected "UI Automation catalog review" -Label "Test distribution plan"

$checklist = Get-Content -LiteralPath $checklistFile -Raw
Assert-Contains -Text $checklist -Expected "release/progress.json" -Label "Tester release checklist"
Assert-Contains -Text $checklist -Expected "Versioned ``.exe``, latest ``.exe``, versioned MSIX, latest MSIX, and checksum artifacts" -Label "Tester release checklist"
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
