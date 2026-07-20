param(
    [string]$CommandInventoryScriptPath = "tools\Generate-CommandInventoryDocs.ps1",
    [string]$DialogParityInventoryScriptPath = "tools\Generate-DialogParityInventory.ps1",
    [string]$DialogVisualEvidenceSummaryScriptPath = "tools\Generate-DialogVisualEvidenceSummary.ps1",
    [string]$ConditionalFormatOpenedStateEvidenceScriptPath = "tools\Generate-ConditionalFormatOpenedStateEvidence.ps1",
    [string]$CrossAppParityDashboardScriptPath = "tools\Generate-CrossAppParityDashboard.ps1",
    [string]$FreePCommandParityInventoryScriptPath = "tools\Generate-FreePCommandParityInventory.ps1",
    [string]$FreePDialogPaneParityInventoryScriptPath = "tools\Generate-FreePDialogPaneParityInventory.ps1",
    [string]$FreeWEditingReferenceParityEvidenceScriptPath = "tools\Generate-FreeWEditingReferenceParityEvidence.ps1",
    [string]$FreeWCommandInventoryScriptPath = "tools\Generate-FreeWCommandInventory.ps1"
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
    & $resolvedScriptPath -Check
}

Invoke-GeneratedDocsCheck -ScriptPath $CommandInventoryScriptPath -Label "command inventory"
Invoke-GeneratedDocsCheck -ScriptPath $DialogParityInventoryScriptPath -Label "dialog parity inventory"
Invoke-GeneratedDocsCheck -ScriptPath $DialogVisualEvidenceSummaryScriptPath -Label "dialog visual evidence summary"
Invoke-GeneratedDocsCheck -ScriptPath $ConditionalFormatOpenedStateEvidenceScriptPath -Label "conditional-format opened-state evidence"
Invoke-GeneratedDocsCheck -ScriptPath $CrossAppParityDashboardScriptPath -Label "cross-app parity dashboard"
Invoke-GeneratedDocsCheck -ScriptPath $FreePCommandParityInventoryScriptPath -Label "FreeP command parity inventory"
Invoke-GeneratedDocsCheck -ScriptPath $FreePDialogPaneParityInventoryScriptPath -Label "FreeP dialog/pane parity inventory"
Invoke-GeneratedDocsCheck -ScriptPath $FreeWEditingReferenceParityEvidenceScriptPath -Label "FreeW editing/reference parity evidence"
Invoke-GeneratedDocsCheck -ScriptPath $FreeWCommandInventoryScriptPath -Label "FreeW command inventory"

Write-Host "Generated documentation checks passed."
