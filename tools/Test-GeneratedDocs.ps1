param(
    [string]$CommandInventoryScriptPath = "tools/Generate-CommandInventoryDocs.ps1",
    [string]$DialogParityInventoryScriptPath = "tools/Generate-DialogParityInventory.ps1",
    [string]$DialogVisualEvidenceSummaryScriptPath = "tools/Generate-DialogVisualEvidenceSummary.ps1",
    [string]$ConditionalFormatOpenedStateEvidenceScriptPath = "tools/Generate-ConditionalFormatOpenedStateEvidence.ps1",
    [string]$CrossAppParityDashboardScriptPath = "tools/Generate-CrossAppParityDashboard.ps1",
    [string]$FreePCommandParityInventoryScriptPath = "tools/Generate-FreePCommandParityInventory.ps1",
    [string]$FreePDialogPaneParityInventoryScriptPath = "tools/Generate-FreePDialogPaneParityInventory.ps1",
    [string]$FreePDialogPaneVisualEvidenceManifestScriptPath = "tools/Generate-FreePDialogPaneVisualEvidenceManifest.ps1",
    [string]$FreePWholeWindowVisualEvidenceManifestScriptPath = "tools/Generate-FreePWholeWindowVisualEvidenceManifest.ps1",
    [string]$FreePPowerPointChromeEvidenceScriptPath = "tools/Test-FreePPowerPointChromeEvidence.ps1",
    [string]$FreeWShellVisualEvidenceScriptPath = "tools/Test-FreeWShellVisualEvidence.ps1",
    [string]$FreeWWordChromeEvidenceScriptPath = "tools/Test-FreeWWordChromeEvidence.ps1",
    [string]$FreeWEditingReferenceParityEvidenceScriptPath = "tools/Generate-FreeWEditingReferenceParityEvidence.ps1",
    [string]$FreeWDesignDialogParityEvidenceScriptPath = "tools/Generate-FreeWDesignDialogParityEvidence.ps1",
    [string]$FreeWMailMergeDialogParityEvidenceScriptPath = "tools/Generate-FreeWMailMergeDialogParityEvidence.ps1",
    [string]$FreeWMediaDialogParityEvidenceScriptPath = "tools/Generate-FreeWMediaDialogParityEvidence.ps1",
    [string]$FreeWPageLayoutDialogParityEvidenceScriptPath = "tools/Generate-FreeWPageLayoutDialogParityEvidence.ps1",
    [string]$FreeWShellPlatformParityEvidenceScriptPath = "tools/Generate-FreeWShellPlatformParityEvidence.ps1",
    [string]$FreeWCommandInventoryScriptPath = "tools/Generate-FreeWCommandInventory.ps1",
    [string]$CrossAppParityDashboardBehaviorScriptPath = "tools/Test-CrossAppParityDashboard.ps1",
    [string]$FreeWDialogVisualEvidenceCheckScriptPath = "tools/Test-FreeWDialogVisualEvidence.ps1"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

function Invoke-GeneratedDocsCheck {
    param(
        [Parameter(Mandatory = $true)][string]$ScriptPath,
        [Parameter(Mandatory = $true)][string]$Label
    )

$resolvedScriptPath = Resolve-ToolRepoPath -Path $ScriptPath -RepoRoot $repoRoot
    if (-not (Test-Path -LiteralPath $resolvedScriptPath)) {
        throw "$Label generated-docs check script was not found: $resolvedScriptPath"
    }

    Write-Host "Checking $Label generated docs..."
    # These generators must run under PowerShell 7. Windows PowerShell 5.1 serializes JSON
    # differently (2-space vs 4-space indent, ": " vs ":  ", and local vs UTC timestamp offsets),
    # so running them under 5.1 reports every generated doc as "out of date" and, if regenerated,
    # rewrites the whole file in a shape CI then rejects. Fail with an actionable message rather
    # than silently falling back to a host that produces different bytes.
    $pwshPath = Get-Command pwsh -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $pwshPath) {
        throw "PowerShell 7 (pwsh) is required for the generated-docs checks but was not found. " +
            "Windows PowerShell 5.1 produces different JSON formatting and would report false " +
            "staleness. Install it with: winget install --id Microsoft.PowerShell"
    }

    & $pwshPath.Source -NoProfile -File $resolvedScriptPath -Check
    if ($LASTEXITCODE -ne 0) {
        throw "$Label generated-docs check failed with exit code $LASTEXITCODE."
    }
}

Invoke-GeneratedDocsCheck -ScriptPath $CommandInventoryScriptPath -Label "command inventory"
Invoke-GeneratedDocsCheck -ScriptPath $DialogParityInventoryScriptPath -Label "dialog parity inventory"
Invoke-GeneratedDocsCheck -ScriptPath $DialogVisualEvidenceSummaryScriptPath -Label "dialog visual evidence summary"
Invoke-GeneratedDocsCheck -ScriptPath $ConditionalFormatOpenedStateEvidenceScriptPath -Label "conditional-format opened-state evidence"
Invoke-GeneratedDocsCheck -ScriptPath $CrossAppParityDashboardScriptPath -Label "cross-app parity dashboard"
Invoke-GeneratedDocsCheck -ScriptPath $FreePCommandParityInventoryScriptPath -Label "FreeP command parity inventory"
Invoke-GeneratedDocsCheck -ScriptPath $FreePDialogPaneParityInventoryScriptPath -Label "FreeP dialog/pane parity inventory"
Invoke-GeneratedDocsCheck -ScriptPath $FreePDialogPaneVisualEvidenceManifestScriptPath -Label "FreeP dialog/pane visual evidence"
Invoke-GeneratedDocsCheck -ScriptPath $FreePWholeWindowVisualEvidenceManifestScriptPath -Label "FreeP whole-window visual evidence"
Invoke-GeneratedDocsCheck -ScriptPath $FreePPowerPointChromeEvidenceScriptPath -Label "FreeP PowerPoint chrome evidence"
Invoke-GeneratedDocsCheck -ScriptPath $FreeWShellVisualEvidenceScriptPath -Label "FreeW shell visual evidence"
Invoke-GeneratedDocsCheck -ScriptPath $FreeWWordChromeEvidenceScriptPath -Label "FreeW Word chrome evidence"
Invoke-GeneratedDocsCheck -ScriptPath $FreeWEditingReferenceParityEvidenceScriptPath -Label "FreeW editing/reference parity evidence"
Invoke-GeneratedDocsCheck -ScriptPath $FreeWDesignDialogParityEvidenceScriptPath -Label "FreeW design-dialog parity evidence"
Invoke-GeneratedDocsCheck -ScriptPath $FreeWMailMergeDialogParityEvidenceScriptPath -Label "FreeW mail-merge dialog parity evidence"
Invoke-GeneratedDocsCheck -ScriptPath $FreeWMediaDialogParityEvidenceScriptPath -Label "FreeW media-dialog parity evidence"
Invoke-GeneratedDocsCheck -ScriptPath $FreeWPageLayoutDialogParityEvidenceScriptPath -Label "FreeW page-layout dialog parity evidence"
Invoke-GeneratedDocsCheck -ScriptPath $FreeWShellPlatformParityEvidenceScriptPath -Label "FreeW shell platform parity evidence"
Invoke-GeneratedDocsCheck -ScriptPath $FreeWCommandInventoryScriptPath -Label "FreeW command inventory"
Invoke-GeneratedDocsCheck -ScriptPath $FreeWMailMergeDialogParityEvidenceScriptPath -Label "FreeW mail-merge dialog parity evidence"
Invoke-GeneratedDocsCheck -ScriptPath $FreeWMediaDialogParityEvidenceScriptPath -Label "FreeW media dialog parity evidence"

$resolvedCrossAppParityDashboardBehaviorScriptPath = Resolve-ToolRepoPath -Path $CrossAppParityDashboardBehaviorScriptPath -RepoRoot $repoRoot
if (-not (Test-Path -LiteralPath $resolvedCrossAppParityDashboardBehaviorScriptPath)) {
    throw "Cross-app parity dashboard behavior guard was not found: $resolvedCrossAppParityDashboardBehaviorScriptPath"
}

Write-Host "Checking cross-app parity dashboard aggregation guards..."
& $resolvedCrossAppParityDashboardBehaviorScriptPath

$resolvedFreeWDialogVisualEvidenceCheckScriptPath = Resolve-ToolRepoPath -Path $FreeWDialogVisualEvidenceCheckScriptPath -RepoRoot $repoRoot
if (-not (Test-Path -LiteralPath $resolvedFreeWDialogVisualEvidenceCheckScriptPath)) {
    throw "FreeW dialog visual evidence consistency guard was not found: $resolvedFreeWDialogVisualEvidenceCheckScriptPath"
}

Write-Host "Checking FreeW canonical dialog evidence counts and scope..."
& $resolvedFreeWDialogVisualEvidenceCheckScriptPath -Check

Write-Host "Generated documentation checks passed."
