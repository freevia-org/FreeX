param(
    [string]$ComparisonPath = "docs\parity\freew-dialog-harness\freew_dialog_visual_comparison.json",
    [string]$ReadmePath = "docs\parity\freew-dialog-harness\README.md",
    [string]$AuditNotePath = "docs\parity\avalonia-parity-wave157-freew-evidence-20260805.md",
    [string]$DashboardPath = "docs\parity\avalonia-wpf-cross-app-dashboard.json",
    [switch]$Check
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

function Assert-EvidenceCondition {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Get-ClassificationCounts {
    param([Parameter(Mandatory = $true)][object[]]$Rows)

    $counts = @{}
    foreach ($row in $Rows) {
        $classification = [string]$row.classification
        if ([string]::IsNullOrWhiteSpace($classification)) {
            throw "FreeW comparison row '$($row.scenarioId)' is missing a classification."
        }

        if ($counts.ContainsKey($classification)) {
            $counts[$classification]++
        }
        else {
            $counts[$classification] = 1
        }
    }

    return $counts
}

$comparison = Read-ToolJson -Path $ComparisonPath -RepoRoot $repoRoot -MissingMessage "Required FreeW canonical comparison is missing"
$readme = Get-Content -LiteralPath (Resolve-ToolRepoPath -Path $ReadmePath -RepoRoot $repoRoot) -Raw
$auditNote = Get-Content -LiteralPath (Resolve-ToolRepoPath -Path $AuditNotePath -RepoRoot $repoRoot) -Raw
$dashboard = Read-ToolJson -Path $DashboardPath -RepoRoot $repoRoot -MissingMessage "Required cross-app dashboard is missing"

$rows = @($comparison.rows)
$derivedCounts = Get-ClassificationCounts -Rows $rows
$reportedCounts = @{}
foreach ($property in $comparison.counts.PSObject.Properties) {
    $reportedCounts[[string]$property.Name] = [int]$property.Value
}

Assert-EvidenceCondition ($rows.Count -eq (($reportedCounts.Values | Measure-Object -Sum).Sum)) "FreeW comparison Counts do not sum to the number of rows."
$allClassifications = @($derivedCounts.Keys + $reportedCounts.Keys | Sort-Object -Unique)
foreach ($classification in $allClassifications) {
    $derived = if ($derivedCounts.ContainsKey($classification)) { [int]$derivedCounts[$classification] } else { 0 }
    $reported = if ($reportedCounts.ContainsKey($classification)) { [int]$reportedCounts[$classification] } else { 0 }
    Assert-EvidenceCondition ($derived -eq $reported) "FreeW canonical count drift for '$classification': rows=$derived, report=$reported."
}

Assert-EvidenceCondition ([int]$comparison.wpfCaptureCount -eq 221) "FreeW must retain all 221 app-owned WPF dialog captures."
Assert-EvidenceCondition ([int]$comparison.avaloniaCaptureCount -eq 291) "FreeW must retain all 291 app-owned Avalonia dialog captures."
Assert-EvidenceCondition (-not $reportedCounts.ContainsKey("pending-wpf-factory")) "FreeW must not retain pending WPF-factory capture rows."
Assert-EvidenceCondition (-not $reportedCounts.ContainsKey("invalid-capture-content")) "FreeW must not retain invalid-content capture rows."

Assert-EvidenceCondition ($null -ne $comparison.scope) "FreeW canonical comparison must declare its evidence scope."
Assert-EvidenceCondition ([string]$comparison.scope.kind -eq "canonical-inputs-only") "FreeW canonical comparison scope kind must be canonical-inputs-only."
Assert-EvidenceCondition ([string]$comparison.scope.description -match "only the inventory and WPF/Avalonia capture manifests") "FreeW canonical comparison scope must identify its exact inputs."
Assert-EvidenceCondition ([string]$comparison.scope.refreshInstruction -match "baseline.*refresh-route") "FreeW canonical comparison scope must explain route refresh."

$summaryMatch = [regex]::Match($readme, "Comparison: (?<mismatch>\d+) genuine visual mismatches, (?<pass>\d+) visual passes, (?<extension>\d+) Avalonia extensions, and (?<notApplicable>\d+) state-not-applicable rows")
Assert-EvidenceCondition $summaryMatch.Success "FreeW README must contain the current generated comparison summary."
foreach ($entry in @(
    @{ Name = "genuine-visual-mismatch"; Text = "mismatch" },
    @{ Name = "pass"; Text = "pass" },
    @{ Name = "avalonia-extension"; Text = "extension" },
    @{ Name = "state-not-applicable"; Text = "notApplicable" }
)) {
    $expected = if ($reportedCounts.ContainsKey($entry.Name)) { $reportedCounts[$entry.Name] } else { 0 }
    $actual = [int]$summaryMatch.Groups[$entry.Text].Value
    Assert-EvidenceCondition ($actual -eq $expected) "FreeW README summary drift for '$($entry.Name)': prose=$actual, canonical=$expected."
}

foreach ($requiredText in @(
    "Wave 154",
    "Wave 155",
    "Wave 156",
    "table-properties",
    "options",
    "page-setup",
    "legal-notices",
    "outside the canonical aggregate",
    "no FreeW dialog route capture"
)) {
    Assert-EvidenceCondition ($auditNote.Contains($requiredText)) "FreeW evidence audit note is missing '$requiredText'."
}

$freeW = @($dashboard.apps | Where-Object { [string]$_.app -eq "FreeW" })
Assert-EvidenceCondition ($freeW.Count -eq 1) "Cross-app dashboard must contain exactly one FreeW app entry."
$freeWPairedEvidence = $freeW[0].renderedEvidence.pairedEvidence
Assert-EvidenceCondition ([int]$freeWPairedEvidence.mismatchCount -eq $(if ($reportedCounts.ContainsKey("genuine-visual-mismatch")) { $reportedCounts["genuine-visual-mismatch"] } else { 0 })) "Cross-app dashboard FreeW mismatch count drifted from the canonical comparison."
Assert-EvidenceCondition ([int]$freeWPairedEvidence.passCount -eq $(if ($reportedCounts.ContainsKey("pass")) { $reportedCounts["pass"] } else { 0 })) "Cross-app dashboard FreeW pass count drifted from the canonical comparison."
Assert-EvidenceCondition ([int]$freeW[0].renderedEvidence.artifactCoverage.pairedWpfArtifactRowCount -eq [int]$comparison.wpfCaptureCount) "Cross-app dashboard FreeW WPF artifact count drifted from the canonical comparison."
Assert-EvidenceCondition (([int]$freeW[0].renderedEvidence.artifactCoverage.pairedAvaloniaArtifactRowCount + [int]$freeW[0].renderedEvidence.artifactCoverage.avaloniaOnlyArtifactRowCount) -eq [int]$comparison.avaloniaCaptureCount) "Cross-app dashboard FreeW Avalonia artifact count drifted from the canonical comparison."

Write-Host ("FreeW evidence consistency passed: {0} rows; {1} genuine visual mismatches; {2} passes; {3} Avalonia extensions; {4} not-applicable." -f `
    $rows.Count,
    $(if ($reportedCounts.ContainsKey("genuine-visual-mismatch")) { $reportedCounts["genuine-visual-mismatch"] } else { 0 }),
    $(if ($reportedCounts.ContainsKey("pass")) { $reportedCounts["pass"] } else { 0 }),
    $(if ($reportedCounts.ContainsKey("avalonia-extension")) { $reportedCounts["avalonia-extension"] } else { 0 }),
    $(if ($reportedCounts.ContainsKey("state-not-applicable")) { $reportedCounts["state-not-applicable"] } else { 0 }))
