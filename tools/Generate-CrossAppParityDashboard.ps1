param(
    [string]$JsonPath = "docs/parity/avalonia-wpf-cross-app-dashboard.json",
    [string]$MarkdownPath = "docs/parity/avalonia-wpf-cross-app-dashboard.md",
    [switch]$Check,
    [switch]$AcceptanceRefresh,
    [string]$AcceptanceRefreshTestedSourceCommit,
    [string]$AcceptanceRefreshHeadRef = "HEAD"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

# PowerShell Desktop serializes JSON differently from pwsh. Re-run the canonical
# implementation under pwsh so generation and -Check have one byte-level format.
$canonicalHostMarker = "FREEX_CROSS_APP_PARITY_DASHBOARD_CANONICAL_HOST"
$currentCanonicalHostMarker = [Environment]::GetEnvironmentVariable($canonicalHostMarker, "Process")
if ($PSVersionTable.PSEdition -eq "Desktop" -and $currentCanonicalHostMarker -ne "1") {
    $pwshCommand = Get-Command pwsh -ErrorAction Stop
    $forwardedArguments = @()
    if ($PSBoundParameters.ContainsKey("JsonPath")) {
        $forwardedArguments += "-JsonPath"
        $forwardedArguments += [string]$PSBoundParameters["JsonPath"]
    }
    if ($PSBoundParameters.ContainsKey("MarkdownPath")) {
        $forwardedArguments += "-MarkdownPath"
        $forwardedArguments += [string]$PSBoundParameters["MarkdownPath"]
    }
    if ($PSBoundParameters.ContainsKey("Check")) {
        $forwardedArguments += "-Check:$([bool]$PSBoundParameters["Check"])"
    }
    if ($PSBoundParameters.ContainsKey("AcceptanceRefresh")) {
        $forwardedArguments += "-AcceptanceRefresh:$([bool]$PSBoundParameters["AcceptanceRefresh"])"
    }
    if ($PSBoundParameters.ContainsKey("AcceptanceRefreshTestedSourceCommit")) {
        $forwardedArguments += "-AcceptanceRefreshTestedSourceCommit"
        $forwardedArguments += [string]$PSBoundParameters["AcceptanceRefreshTestedSourceCommit"]
    }
    if ($PSBoundParameters.ContainsKey("AcceptanceRefreshHeadRef")) {
        $forwardedArguments += "-AcceptanceRefreshHeadRef"
        $forwardedArguments += [string]$PSBoundParameters["AcceptanceRefreshHeadRef"]
    }

    $previousCanonicalHostMarker = $currentCanonicalHostMarker
    try {
        [Environment]::SetEnvironmentVariable($canonicalHostMarker, "1", "Process")
        & $pwshCommand.Source -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath @forwardedArguments
        $canonicalExitCode = $LASTEXITCODE
    }
    finally {
        [Environment]::SetEnvironmentVariable($canonicalHostMarker, $previousCanonicalHostMarker, "Process")
    }

    exit $canonicalExitCode
}

. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

# Keep acceptance evidence anchored to the source that was actually built and tested.
# The generated docs are committed afterward, so deriving this from the current HEAD
# would make the evidence self-referential and would change the claim on every refresh.
$wave194TestedSourceCommit = "f7cbd8cbe3f1ac5fbaf14da1c2cacc1a3fb7bf3f"
$wave194ReviewedIntegrationHead = "2ee42a45efd651ad9ad1c015403d788570ae02d9"
$wave194AcceptanceRefreshNote = "This dashboard/report is an acceptance-only documentation/tooling refresh; it does not alter the tested source commit."
$wave194FullReleaseBuildMsBuildElapsed = "00:09:49.19"
$wave194FullReleaseBuildWrapperElapsed = "00:09:49.4386774"
$wave194DefaultLaneWrapperElapsed = "00:16:54.2974514"
$wave194DefaultLaneTrxTimestampSpan = "14:03:31.8502271 to 14:20:25.1692656 (+03:00)"
$wave194DefaultLaneTrxDuration = "00:16:53.3190385"
$wave195TestedSourceCommit = "feff4d47c02d57112c6cb191bcc85e1d60ea4e06"
$wave195AcceptanceRefreshNote = "This dashboard/report is an acceptance-only documentation/tooling refresh; it does not alter the tested source commit."
$wave195FullReleaseBuildElapsed = "00:05:22.96"
$wave195ProgressNotePath = "docs/parity/avalonia-parity-wave195-cross-app-integration-20260828.md"
$wave196FreeXEvidenceNotePath = "docs/parity/freex-wave196-ribbon-formatting/README.md"
$wave196FreeXSourceTestPath = "tests/FreeX.App.Avalonia.Tests/Wave196RibbonFormattingPhysicalSourceTests.cs"
$wave196FreeWEvidenceNotePath = "freew/docs/parity/avalonia-parity-wave196-freew-paged-caret-boundary-20260829.md"
$wave196FreeWSourceTestPath = "freew/FreeW.App.Avalonia.Tests/DocumentViewHeadlessTests.cs"
$wave196FreePEvidenceNotePath = "docs/parity/freep-wave196-deck17-light-hinting-20260829.md"
$wave196FreePMetricsPath = "docs/parity/evidence/freep-wave196-deck17-light-hinting-20260829/metrics.json"
$wave196FreePImagesPath = "docs/parity/evidence/freep-wave196-deck17-light-hinting-20260829/images.json"
$wave196FreePSourceTestPaths = @(
    "freep/FreeP.App.Rendering.Avalonia.Tests/Wave196Deck17LightHintingEvidenceTests.cs",
    "freep/FreeP.App.Presentation.Tests/Wave196Deck17Slide02ResolvedModelTests.cs"
)
$wave196PortabilityCorrectionPaths = @(
    "tools/Generate-CrossAppParityDashboard.ps1",
    "tests/FreeX.App.Host.Tests/CrossAppParityDashboardTests.cs"
)
$wave196IntegrationNotePath = "docs/parity/avalonia-parity-wave196-cross-app-integration-20260829.md"
$wave196TestedSourceCommit = "100f4aea399e3bc9d194c15cf962ded7d0cf3772"
$wave196AcceptanceRefreshNote = "This dashboard/report is an acceptance-only documentation/tooling refresh; it does not alter the tested source commit."
$wave196AcceptanceRefreshAllowedPaths = @(
    "tools/Generate-CrossAppParityDashboard.ps1",
    "tools/Test-CrossAppParityDashboard.ps1",
    "tests/FreeX.App.Host.Tests/CrossAppParityDashboardTests.cs",
    "docs/parity/avalonia-wpf-cross-app-dashboard.json",
    "docs/parity/avalonia-wpf-cross-app-dashboard.md",
    "docs/parity/avalonia-parity-wave196-cross-app-integration-20260829.md"
)
$wave196FullReleaseBuildElapsed = "00:10:12.82"
$wave197FreeXEvidenceNotePath = "docs/parity/freex-wave197-ribbon-number-format/README.md"
$wave197FreeXSourceTestPath = "tests/FreeX.App.Avalonia.Tests/Wave197RibbonNumberFormatPhysicalSourceTests.cs"
$wave197FreeWEvidenceNotePath = "freew/docs/parity/avalonia-parity-wave197-freew-legal-notices-template-candidates-20260829.md"
$wave197FreeWSourceTestPath = "freew/FreeW.App.Avalonia.Tests/Wave197LegalNoticesEvidenceTests.cs"
$wave197FreeWRawEvidencePath = "freew/docs/parity/evidence/wave197-freew-legal-notices-raw-evidence.json"
$wave197FreeWChecksumsPath = "freew/docs/parity/evidence/SHA256SUMS.txt"
$wave197FreePLeadingEvidenceNotePath = "docs/parity/freep-wave197-deck17-leading-residual-20260829.md"
$wave197FreePLeadingMetricsPath = "docs/parity/evidence/freep-wave197-deck17-leading-residual-20260829/metrics.json"
$wave197FreePBaselineEvidenceNotePath = "docs/parity/freep-wave197-deck17-baseline-alignment-20260829.md"
$wave197FreePBaselineMetricsPath = "docs/parity/evidence/freep-wave197-deck17-baseline-alignment-20260829/metrics.json"
$wave197FreePBaselineImagesPath = "docs/parity/evidence/freep-wave197-deck17-baseline-alignment-20260829/images.json"
$wave197FreePSourceTestPaths = @(
    "freep/FreeP.App.Rendering.Avalonia.Tests/Wave197Deck17LeadingResidualEvidenceTests.cs",
    "freep/FreeP.App.Rendering.Avalonia.Tests/Wave197BaselineAlignmentEvidenceTests.cs"
)
$wave197IntegrationNotePath = "docs/parity/avalonia-parity-wave197-cross-app-integration-20260829.md"
$wave197TestedSourceCommit = "a6b1f27e02d15a7495644db64c9bda3a839f126a"
$wave197AcceptanceRefreshNote = "This dashboard/report is an acceptance-only documentation/tooling refresh; it does not alter the tested source commit."
$wave197AcceptanceRefreshAllowedPaths = @(
    "tools/Generate-CrossAppParityDashboard.ps1",
    "tools/Test-CrossAppParityDashboard.ps1",
    "tests/FreeX.App.Host.Tests/CrossAppParityDashboardTests.cs",
    "docs/parity/avalonia-wpf-cross-app-dashboard.json",
    "docs/parity/avalonia-wpf-cross-app-dashboard.md",
    $wave197IntegrationNotePath
)
$wave197FullReleaseBuildMsBuildElapsed = "00:07:04.93"
$wave197FullReleaseBuildWrapperElapsed = "00:07:05.2629619"
$wave198FreeXEvidenceNotePath = "docs/parity/freex-wave198-ribbon-font-family/FINAL-EVIDENCE.md"
$wave198FreeXReadmePath = "docs/parity/freex-wave198-ribbon-font-family/README.md"
$wave198FreeXChecksumsPath = "docs/parity/freex-wave198-ribbon-font-family/evidence/SHA256SUMS.txt"
$wave198FreeXSourceTestPath = "tests/FreeX.App.Avalonia.Tests/Wave198RibbonFontFamilyPhysicalSourceTests.cs"
$wave198FreeWEvidenceNotePath = "docs/parity/avalonia-parity-wave198-freew-table-properties-tab-pane-20260829.md"
$wave198FreeWSourceTestPath = "freew/FreeW.App.Avalonia.Tests/Wave198TablePropertiesEvidenceTests.cs"
$wave198FreeWRawEvidencePath = "freew/docs/parity/evidence/wave198-freew-table-properties-tab-pane-raw-evidence.json"
$wave198FreePMetricsPath = "docs/parity/evidence/freep-wave198-deck17-subpixel-antialias-20260829/metrics.json"
$wave198FreePEvidenceNotePath = "docs/parity/freep-wave198-deck17-subpixel-antialias-20260829.md"
$wave198FreePSourceTestPath = "freep/FreeP.App.Rendering.Avalonia.Tests/Wave198Deck17SubpixelAntialiasEvidenceTests.cs"
$wave198IntegrationNotePath = "docs/parity/avalonia-parity-wave198-cross-app-integration-20260829.md"
$wave198TestedSourceCommit = "1c6cb5e8019dd0098465c67f8f0261929a3d3bbc"
$wave198AcceptanceRefreshNote = "This dashboard/report is an acceptance-only documentation/tooling refresh; it does not alter the tested source commit."
$wave198AcceptanceRefreshAllowedPaths = @(
    "tools/Generate-CrossAppParityDashboard.ps1",
    "tools/Test-CrossAppParityDashboard.ps1",
    "tests/FreeX.App.Host.Tests/CrossAppParityDashboardTests.cs",
    "docs/parity/avalonia-wpf-cross-app-dashboard.json",
    "docs/parity/avalonia-wpf-cross-app-dashboard.md",
    $wave198IntegrationNotePath
)
$wave199FreeXEvidenceNotePath = "docs/parity/freex-wave199-ribbon-font-family/FINAL-EVIDENCE.md"
$wave199FreeXReadmePath = "docs/parity/freex-wave199-ribbon-font-family/README.md"
$wave199FreeXChecksumsPath = "docs/parity/freex-wave199-ribbon-font-family/evidence/SHA256SUMS.txt"
$wave199FreeXInteractionPath = "docs/parity/freex-wave199-ribbon-font-family/evidence/interaction-validation.json"
$wave199FreeXPackageProofPath = "docs/parity/freex-wave199-ribbon-font-family/evidence/package-proof.txt"
$wave199FreeXSourceTestPaths = @("tests/FreeX.App.Avalonia.Tests/Wave199RibbonFontFamilyFocusSourceTests.cs", "tests/FreeX.App.Avalonia.Tests/Wave199RibbonFontFamilyEvidenceTests.cs")
$wave199FreeWEvidenceNotePath = "freew/docs/parity/avalonia-parity-wave199-freew-style-dialog.md"
$wave199FreeWRecordPath = "freew/docs/parity/evidence/wave199-freew-style-dialog.json"
$wave199FreeWChecksumsPath = "freew/docs/parity/evidence/wave199-freew-style-dialog-artifacts/SHA256SUMS.txt"
$wave199FreeWSourceTestPath = "freew/FreeW.App.Avalonia.Tests/Wave199StyleDialogEvidenceTests.cs"
$wave199FreePEvidenceNotePath = "docs/parity/freep-wave199-deck17-aptos-resource-raster-20260829.md"
$wave199FreePMetricsPath = "docs/parity/evidence/freep-wave199-deck17-aptos-resource-raster-20260829/metrics.json"
$wave199FreePReferencesPath = "docs/parity/evidence/freep-wave199-deck17-aptos-resource-raster-20260829/references.json"
$wave199FreePImagesPath = "docs/parity/evidence/freep-wave199-deck17-aptos-resource-raster-20260829/images.json"
$wave199FreePBroaderControlsPath = "docs/parity/evidence/freep-wave199-deck17-aptos-resource-raster-20260829/broader-controls.json"
$wave199FreePSourceTestPath = "freep/FreeP.App.Rendering.Avalonia.Tests/Wave199Deck17AptosResourceRasterEvidenceTests.cs"
$wave199IntegrationNotePath = "docs/parity/avalonia-parity-wave199-cross-app-integration-20260829.md"
$wave199TestedSourceCommit = "d25a66612cb89827ad99ad7694e29a72b5984f7a"
$wave199PreDashboardIntegrationCommit = "fb56a0f16e1b6be4703a96b87a118d1de1c3bf4b"
$wave199AcceptanceRefreshNote = "This dashboard/report is an acceptance-only documentation/tooling refresh; it does not alter the tested source commit."
$wave199AcceptanceRefreshAllowedPaths = @("tools/Generate-CrossAppParityDashboard.ps1", "tools/Test-CrossAppParityDashboard.ps1", "tests/FreeX.App.Host.Tests/CrossAppParityDashboardTests.cs", "docs/parity/avalonia-wpf-cross-app-dashboard.json", "docs/parity/avalonia-wpf-cross-app-dashboard.md", $wave199IntegrationNotePath)

function Get-JsonPropertyValue {
    param(
        [Parameter(Mandatory = $true)]$InputObject,
        [Parameter(Mandatory = $true)][string]$Name,
        $Default = 0
    )

    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $Default
    }

    return $property.Value
}

function Get-CommandSurfaceRows {
    param([Parameter(Mandatory = $true)]$Sections)

    $rows = @()
    foreach ($section in @($Sections)) {
        if ($section.PSObject.Properties.Name -contains "groups" -and $section.groups) {
            foreach ($group in @($section.groups)) {
                $rows += @($group.rows)
            }
        }
        else {
            $rows += @($section.rows)
        }
    }

    return $rows
}

function Get-StatusCount {
    param(
        [Parameter(Mandatory = $true)]$Rows,
        [Parameter(Mandatory = $true)][string]$Status
    )

    return @($Rows | Where-Object { $_.status -eq $Status }).Count
}

function Get-UniqueValueCount {
    param(
        [Parameter(Mandatory = $true)]$Values
    )

    return @($Values | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Sort-Object -Unique).Count
}

function Format-CanonicalPercentage {
    param([Parameter(Mandatory = $true)][double]$Value)

    # PowerShell's P format follows the process culture. In globalization-
    # invariant Unix hosts it inserts a space before the percent sign, while
    # the en-US Windows output does not. Generated files need one stable form.
    return ([string]::Format(
            [System.Globalization.CultureInfo]::InvariantCulture,
            "{0:0.0000}%",
            ($Value * 100.0)))
}

function Get-ScenarioRouteId {
    param(
        [Parameter(Mandatory = $true)][string]$ScenarioId
    )

    $lastDot = $ScenarioId.LastIndexOf('.')
    if ($lastDot -lt 1) {
        return $ScenarioId
    }

    return $ScenarioId.Substring(0, $lastDot)
}

function Get-ManifestFileCount {
    param(
        [Parameter(Mandatory = $true)]$Manifest,
        [Parameter(Mandatory = $true)][string]$Pattern
    )

    return @($Manifest.files | Where-Object { [string]$_.path -like $Pattern }).Count
}

function Get-FreeWWave195Metrics {
    param(
        [Parameter(Mandatory = $true)]$ComparisonRows,
        [Parameter(Mandatory = $true)][string]$EvidenceNote
    )

    $legalRows = @($ComparisonRows | Where-Object { [string]$_.scenarioId -like "legal-notices.*" })
    if ($legalRows.Count -eq 0) {
        throw "FreeW Wave195 canonical comparison contains no Legal Notices rows."
    }

    $rowPattern = '(?m)^\|\s*`(?<scenarioId>[^`]+)`\s*\|\s*(?<before>[\d,]+)\s*/[^|]+\|\s*(?<after>[\d,]+)\s*/[^|]+\|\s*(?<delta>[+-]?[\d,]+)\s*\|\s*$'
    $baselineRows = @{}
    foreach ($match in [regex]::Matches($EvidenceNote, $rowPattern)) {
        $scenarioId = [string]$match.Groups["scenarioId"].Value
        $baselineRows[$scenarioId] = [ordered]@{
            before = [int]($match.Groups["before"].Value -replace ',', '')
            after = [int]($match.Groups["after"].Value -replace ',', '')
            delta = [int]($match.Groups["delta"].Value -replace ',', '')
        }
    }

    if ($baselineRows.Count -ne $legalRows.Count) {
        throw "FreeW Wave195 Legal Notices baseline row count ($($baselineRows.Count)) does not match the canonical comparison row count ($($legalRows.Count))."
    }

    $baselineChangedPixels = 0
    $changedPixels = 0
    $aggregateDelta = 0
    foreach ($row in $legalRows) {
        $scenarioId = [string]$row.scenarioId
        if (-not $baselineRows.ContainsKey($scenarioId)) {
            throw "FreeW Wave195 Legal Notices baseline is missing canonical row '$scenarioId'."
        }

        $baseline = $baselineRows[$scenarioId]
        $currentChangedPixels = [int]$row.metrics.changedPixels
        if ($currentChangedPixels -ne $baseline.after) {
            throw "FreeW Wave195 Legal Notices current metric for '$scenarioId' ($currentChangedPixels) disagrees with the canonical evidence note ($($baseline.after))."
        }
        $derivedDelta = $currentChangedPixels - [int]$baseline.before
        if ($derivedDelta -ne [int]$baseline.delta) {
            throw "FreeW Wave195 Legal Notices delta for '$scenarioId' ($derivedDelta) disagrees with the canonical evidence note ($($baseline.delta))."
        }

        $baselineChangedPixels += [int]$baseline.before
        $changedPixels += $currentChangedPixels
        $aggregateDelta += $derivedDelta
    }

    $nonLegalRows = @($ComparisonRows | Where-Object { [string]$_.scenarioId -notlike "legal-notices.*" })
    $unchangedMatch = [regex]::Match($EvidenceNote, '(?m)^All\s+(?<count>[\d,]+)\s+non-`legal-notices\.\*` rows were structurally unchanged\.')
    if (-not $unchangedMatch.Success) {
        throw "FreeW Wave195 evidence note is missing the canonical non-Legal row stability statement."
    }
    $documentedNonLegalRows = [int]($unchangedMatch.Groups["count"].Value -replace ',', '')
    if ($documentedNonLegalRows -ne $nonLegalRows.Count) {
        throw "FreeW Wave195 non-Legal row count ($($nonLegalRows.Count)) disagrees with the canonical evidence note ($documentedNonLegalRows)."
    }

    return [ordered]@{
        catalogRowCount = $ComparisonRows.Count
        passCount = @($ComparisonRows | Where-Object { $_.classification -eq "pass" }).Count
        genuineVisualMismatchCount = @($ComparisonRows | Where-Object { $_.classification -eq "genuine-visual-mismatch" }).Count
        avaloniaExtensionCount = @($ComparisonRows | Where-Object { $_.classification -eq "avalonia-extension" }).Count
        legalNoticesStateCount = $legalRows.Count
        legalNoticesBaselineChangedPixels = $baselineChangedPixels
        legalNoticesChangedPixels = $changedPixels
        legalNoticesAggregateDelta = $aggregateDelta
        nonLegalRowsStructurallyUnchanged = $nonLegalRows.Count
    }
}

function Get-ResidualById {
    param(
        [Parameter(Mandatory = $true)]$Residuals,
        [Parameter(Mandatory = $true)][string]$Id
    )

    return @($Residuals | Where-Object { $_.Id -eq $Id } | Select-Object -First 1)[0]
}

function Get-FreeXNextSlice {
    param(
        [Parameter(Mandatory = $true)]$FunctionalMatrix,
        [Parameter(Mandatory = $true)]$FunctionalClassificationSummary,
        [Parameter(Mandatory = $true)]$DialogRoutes,
        [Parameter(Mandatory = $true)]$DialogVisualEvidence
    )

    $realBehaviorGaps = [int](Get-JsonPropertyValue $FunctionalClassificationSummary "real-behavior-gap")
    $pseudoGalleryItems = [int](Get-JsonPropertyValue $FunctionalClassificationSummary "pseudo-command-gallery-item")
    $nonClickControlRows = [int](Get-JsonPropertyValue $FunctionalClassificationSummary "non-click-control-inventory-row")
    $conditionalFormatPopupGalleryRows = [int](Get-JsonPropertyValue $FunctionalClassificationSummary "conditional-format-popup-gallery-row")
    $conditionalFormatPopupCatalogItems = [int](Get-JsonPropertyValue $FunctionalClassificationSummary "conditional-format-popup-catalog-item")
    $accountingSymbolPopupGalleryRows = [int](Get-JsonPropertyValue $FunctionalClassificationSummary "accounting-symbol-popup-gallery-row")
    $fontBorderPopupGalleryRows = [int](Get-JsonPropertyValue $FunctionalClassificationSummary "font-border-popup-gallery-row")
    $handlerQualifiedHelpRoutes = [int](Get-JsonPropertyValue $FunctionalClassificationSummary "handler-qualified-help-route")
    $sharedRibbonComboBoxControls = [int](Get-JsonPropertyValue $FunctionalClassificationSummary "shared-ribbon-combo-box-control")
    $catalogBackedPseudoGalleryRows = $conditionalFormatPopupGalleryRows + $accountingSymbolPopupGalleryRows + $fontBorderPopupGalleryRows
    $allDialogRoutesCaptured = $DialogRoutes.totalRoutes -eq $DialogRoutes.wpfCaptures -and
        $DialogRoutes.totalRoutes -eq $DialogRoutes.avaloniaCaptures
    $allDialogManifestSurfacesPaired = $DialogVisualEvidence.additionalAvaloniaCapturedSurfaceIds -eq 0 -and
        $DialogVisualEvidence.wpfManifestIdsWithoutAvaloniaPair -eq 0
    $visualReviewCandidateCount = [int]$DialogVisualEvidence.visualReviewCandidateCount
    $visualReviewTriageThreshold = [double]$DialogVisualEvidence.visualReviewTriageThreshold
    $highestTriageScore = [double]$DialogVisualEvidence.highestTriageScore

    if ($FunctionalMatrix.avaloniaMissing -eq 0 -and
        $realBehaviorGaps -eq 0 -and
        $allDialogRoutesCaptured -and
        $allDialogManifestSurfacesPaired -and
        $catalogBackedPseudoGalleryRows -eq $pseudoGalleryItems) {
        return "Command/dialog route coverage is complete for the generated inputs: all $pseudoGalleryItems pseudo-gallery rows are catalog-backed in classifier evidence ($conditionalFormatPopupGalleryRows conditional-format rows over $conditionalFormatPopupCatalogItems runtime catalog items, $fontBorderPopupGalleryRows font/border rows, and $accountingSymbolPopupGalleryRows accounting-symbol rows), and dialog screenshot evidence has $($DialogVisualEvidence.pairedCapturedSurfaceIds) paired WPF/Avalonia manifest surface ids with $($DialogVisualEvidence.additionalAvaloniaCapturedSurfaceIds) Avalonia-only ids. This is coverage and size-comparability evidence, not visual parity. The paired screenshots retain $visualReviewCandidateCount unresolved high-delta visual review candidates at triage score >= $visualReviewTriageThreshold (highest $highestTriageScore); keep those candidates separate from $($DialogVisualEvidence.pairedDimensionMismatches) scale-aware dimension mismatch rows and $($DialogVisualEvidence.pairedRawPixelDimensionMismatches) raw PNG pixel dimension mismatches."
    }

    if ($FunctionalMatrix.avaloniaMissing -eq 0 -and $realBehaviorGaps -eq 0 -and $allDialogRoutesCaptured) {
        return "Command/dialog route coverage is complete for the generated inputs; $catalogBackedPseudoGalleryRows of $pseudoGalleryItems pseudo-gallery rows are catalog-backed in classifier evidence, and dialog screenshot evidence currently has $($DialogVisualEvidence.additionalAvaloniaCapturedSurfaceIds) Avalonia-only manifest ids plus $($DialogVisualEvidence.wpfManifestIdsWithoutAvaloniaPair) WPF-only manifest ids. Continue catalog/evidence cleanup and visual review before claiming full visual parity."
    }

    if ($FunctionalMatrix.avaloniaMissing -gt 0 -or $realBehaviorGaps -gt 0) {
        return "Resolve generated Avalonia command-binding or real behavior gaps before taking additional evidence slices."
    }

    return "Refresh paired WPF/Avalonia dialog evidence until every generated dialog route has current captures."
}

$resolvedJsonPath = Resolve-ToolRepoPath -Path $JsonPath -RepoRoot $repoRoot
$resolvedMarkdownPath = Resolve-ToolRepoPath -Path $MarkdownPath -RepoRoot $repoRoot
$tempRoot = New-ToolTemporaryDirectory -Prefix "freex-cross-app-parity-dashboard-"
$tempJsonPath = Join-Path $tempRoot "avalonia-wpf-cross-app-dashboard.json"
$tempMarkdownPath = Join-Path $tempRoot "avalonia-wpf-cross-app-dashboard.md"

try {
    $commandInventory = Read-ToolJson -Path "docs/parity/command-inventory.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $functional = Read-ToolJson -Path "docs/parity/functional-parity.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $functionalClassification = Read-ToolJson -Path "docs/parity/functional-parity-classification.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $dialogInventory = Read-ToolJson -Path "docs/parity/dialog-parity-inventory.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $dialogVisualEvidence = Read-ToolJson -Path "docs/parity/dialog-visual-evidence-summary.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $freeXWpfManifest = Read-ToolJson -Path "docs/parity/dialog-visual-assets/wpf-capture/manifest.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $freeXAvaloniaManifest = Read-ToolJson -Path "docs/parity/dialog-visual-assets/avalonia-capture/manifest.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $freeXOfficeBaseline = Read-ToolJson -Path "docs/parity/freex-excel-com-baseline-2026-08-14/manifest.json" -RepoRoot $repoRoot -MissingMessage "Required Excel Office baseline manifest is missing"
    $freeXAvaloniaGridCorpus = Read-ToolJson -Path "docs/parity/freex-avalonia-grid-corpus-2026-08-16/manifest.json" -RepoRoot $repoRoot -MissingMessage "Required FreeX Avalonia grid corpus manifest is missing"
    $freeXWpfRibbonManifest = Read-ToolJson -Path "tools/screenshots/screenshot_manifest.json" -RepoRoot $repoRoot -MissingMessage "Required FreeX WPF ribbon capture manifest is missing"
    $freeXAvaloniaRibbonManifest = Read-ToolJson -Path "tools/screenshots_avalonia_ribbon/screenshot_manifest.json" -RepoRoot $repoRoot -MissingMessage "Required FreeX Avalonia ribbon capture manifest is missing"
    $freeXWave193PhysicalResult = Read-ToolJson -Path "docs/parity/evidence/wave193-freex-autofilter-no-fill-20260823/physical-result.json" -RepoRoot $repoRoot -MissingMessage "Required Wave193 FreeX physical result is missing"
    $freeXWave193Manifest = Read-ToolJson -Path "docs/parity/evidence/wave193-freex-autofilter-no-fill-20260823/manifest.json" -RepoRoot $repoRoot -MissingMessage "Required Wave193 FreeX evidence manifest is missing"
    $freeXWave194PhysicalResult = Read-ToolJson -Path "docs/parity/evidence/wave194-freex-autofilter-mixed-type-20260823/physical-result.json" -RepoRoot $repoRoot -MissingMessage "Required Wave194 FreeX physical result is missing"
    $freeXWave194Manifest = Read-ToolJson -Path "docs/parity/evidence/wave194-freex-autofilter-mixed-type-20260823/manifest.json" -RepoRoot $repoRoot -MissingMessage "Required Wave194 FreeX evidence manifest is missing"
    $freeXWave195Manifest = Read-ToolJson -Path "docs/parity/evidence/wave195-freex-autofilter-criteria-workflows-20260828/manifest.json" -RepoRoot $repoRoot -MissingMessage "Required Wave195 FreeX physical evidence manifest is missing"
    $freeXWave196EvidenceNote = Get-Content -LiteralPath (Join-Path $repoRoot $wave196FreeXEvidenceNotePath) -Raw
    $freeXWave197EvidenceNote = Get-Content -LiteralPath (Join-Path $repoRoot $wave197FreeXEvidenceNotePath) -Raw
    $freeXWave198EvidenceNote = Get-Content -LiteralPath (Join-Path $repoRoot $wave198FreeXEvidenceNotePath) -Raw
    $freeXWave199EvidenceNote = Get-Content -LiteralPath (Join-Path $repoRoot $wave199FreeXEvidenceNotePath) -Raw
    $freeXWave199Interaction = Read-ToolJson -Path $wave199FreeXInteractionPath -RepoRoot $repoRoot -MissingMessage "Required Wave199 FreeX interaction evidence is missing"
    $freeXWave199PackageProof = Get-Content -LiteralPath (Join-Path $repoRoot $wave199FreeXPackageProofPath) -Raw
    $wave193PhysicalRun = $freeXWave193Manifest.physicalRun
    $wave193ExpectedArtifactCount = 18
    $wave193PopupOpenChangeThreshold = 300
    $wave193PopupRestoredChangeMaximum = 100
    if ($null -eq $wave193PhysicalRun) {
        throw "Wave193 FreeX evidence manifest is missing physicalRun transition metrics."
    }
    $requiredWave193PhysicalRunProperties = @(
        "popupOpenChangedPixels",
        "popupDismissedChangedPixels",
        "popupRestoredChangedPixels",
        "clickAcknowledged"
    )
    foreach ($propertyName in $requiredWave193PhysicalRunProperties) {
        if ($null -eq $wave193PhysicalRun.PSObject.Properties[$propertyName]) {
            throw "Wave193 FreeX evidence manifest is missing physicalRun.$propertyName."
        }
    }
    $wave193ArtifactCount = @($freeXWave193Manifest.files).Count
    if ($wave193ArtifactCount -ne $wave193ExpectedArtifactCount) {
        throw "Wave193 FreeX evidence manifest must contain $wave193ExpectedArtifactCount artifacts; found $wave193ArtifactCount."
    }
    if ([int]$wave193PhysicalRun.popupOpenChangedPixels -lt $wave193PopupOpenChangeThreshold -or
        [int]$wave193PhysicalRun.popupDismissedChangedPixels -lt $wave193PopupOpenChangeThreshold -or
        [int]$wave193PhysicalRun.popupRestoredChangedPixels -gt $wave193PopupRestoredChangeMaximum -or
        -not [bool]$wave193PhysicalRun.clickAcknowledged) {
        throw "Wave193 FreeX popup transition metrics did not satisfy open/dismiss/restoration thresholds."
    }
    if ([int]$freeXWave195Manifest.wave -ne 195 -or
        [string]$freeXWave195Manifest.status -ne "passed" -or
        [string]$freeXWave195Manifest.validationMode -ne "physical-only") {
        throw "Wave195 FreeX evidence manifest must be a passed physical-only Wave195 result."
    }
    $wave195SessionCount = @($freeXWave195Manifest.sessions).Count
    $wave195ReloadWitnessCount = @($freeXWave195Manifest.sessions | Where-Object { $_.reloadWitnessPassed }).Count
    $wave195ArtifactCount = @($freeXWave195Manifest.files).Count
    $wave195ScreenshotCount = @($freeXWave195Manifest.files | Where-Object { [string]$_.path -like "*.png" }).Count
    if ($wave195SessionCount -ne 2 -or $wave195ReloadWitnessCount -ne 2 -or $wave195ArtifactCount -ne 75 -or $wave195ScreenshotCount -ne 58) {
        throw "Wave195 FreeX evidence must contain two passing sessions, 75 artifacts, 58 screenshots, and two reload witnesses."
    }
    $freew = Read-ToolJson -Path "docs/parity/freew-command-inventory.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $freeWRouteInventory = Read-ToolJson -Path "docs/parity/freew-dialog-harness/freew_dialog_route_inventory.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $freeWVisualComparison = Read-ToolJson -Path "docs/parity/freew-dialog-harness/freew_dialog_visual_comparison.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $freeWWave195EvidenceNote = Get-Content -LiteralPath (Join-Path $repoRoot "freew/docs/parity/avalonia-parity-wave195-freew-legal-notices-20260828.md") -Raw
    $freeWWave196EvidenceNote = Get-Content -LiteralPath (Join-Path $repoRoot $wave196FreeWEvidenceNotePath) -Raw
    $freeWWave197EvidenceNote = Get-Content -LiteralPath (Join-Path $repoRoot $wave197FreeWEvidenceNotePath) -Raw
    $freeWWave197RawEvidence = Read-ToolJson -Path $wave197FreeWRawEvidencePath -RepoRoot $repoRoot -MissingMessage "Required Wave197 FreeW raw evidence is missing"
    $freeWWave198EvidenceNote = Get-Content -LiteralPath (Join-Path $repoRoot $wave198FreeWEvidenceNotePath) -Raw
    $freeWWave198RawEvidence = Read-ToolJson -Path $wave198FreeWRawEvidencePath -RepoRoot $repoRoot -MissingMessage "Required Wave198 FreeW raw evidence is missing"
    $freeWWave199EvidenceNote = Get-Content -LiteralPath (Join-Path $repoRoot $wave199FreeWEvidenceNotePath) -Raw
    $freeWWave199Record = Read-ToolJson -Path $wave199FreeWRecordPath -RepoRoot $repoRoot -MissingMessage "Required Wave199 FreeW evidence record is missing"
    $freeWFontProvenance = Read-ToolJson -Path "docs/parity/freew-dialog-harness/freew_font_visual_provenance.json" -RepoRoot $repoRoot -MissingMessage "Required FreeW Font provenance is missing"
    if ($null -eq $freeWVisualComparison.scope -or [string]$freeWVisualComparison.scope.kind -ne "canonical-inputs-only") {
        throw "FreeW visual comparison must declare canonical-inputs-only scope before the cross-app dashboard can be generated."
    }
    $freeWOfficeBaseline = Read-ToolJson -Path "docs/parity/freew-word-baseline-2026-08-16/manifest.json" -RepoRoot $repoRoot -MissingMessage "Required Word Office baseline manifest is missing"
    $freeWShellVisualEvidence = Read-ToolJson -Path "docs/parity/freew-shell-visual-2026-08-16/freew_shell_visual_evidence.json" -RepoRoot $repoRoot -MissingMessage "Required FreeW shell visual evidence is missing"
    $freep = Read-ToolJson -Path "docs/parity/freep-command-parity-inventory.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $freePDialogVisualEvidence = Read-ToolJson -Path "docs/parity/freep-dialog-pane-visual-evidence/summary.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $freePDialogArtifactManifest = Read-ToolJson -Path "docs/parity/freep-dialog-pane-visual-evidence/artifact-manifest.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $freePWholeWindowVisualEvidence = Read-ToolJson -Path "docs/parity/freep-whole-window-visual-evidence/summary.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $freePWholeWindowArtifactManifest = Read-ToolJson -Path "docs/parity/freep-whole-window-visual-evidence/artifact-manifest.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $freePRenderParity = Read-ToolJson -Path "docs/parity/freep-render-slideshow-media-parity-20260720.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $freePNativePickerEvidence = Read-ToolJson -Path "docs/parity/freep-native-picker-human-evidence.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $freePOfficeBaseline = Read-ToolJson -Path "docs/parity/freep-powerpoint-baseline-2026-08-14.json" -RepoRoot $repoRoot -MissingMessage "Required PowerPoint Office baseline manifest is missing"
    $freePOfficeRecalibration = Read-ToolJson -Path "docs/parity/freep-powerpoint-recalibration-2026-08-15.json" -RepoRoot $repoRoot -MissingMessage "Required PowerPoint current-source recalibration is missing"
    $freePWave193Metrics = Read-ToolJson -Path "docs/parity/evidence/avalonia-parity-wave193-freep-evidence-20260823/metrics.json" -RepoRoot $repoRoot -MissingMessage "Required Wave193 FreeP current-source metrics are missing"
    $freePWave193References = Read-ToolJson -Path "docs/parity/evidence/avalonia-parity-wave193-freep-evidence-20260823/references.json" -RepoRoot $repoRoot -MissingMessage "Required Wave193 FreeP Office references are missing"
    $freePWave193Images = Read-ToolJson -Path "docs/parity/evidence/avalonia-parity-wave193-freep-evidence-20260823/images.json" -RepoRoot $repoRoot -MissingMessage "Required Wave193 FreeP retained images are missing"
    $freePWave194Topology = Read-ToolJson -Path "docs/parity/evidence/freep-wave194-deck17-slide02-topology-20260823/topology.json" -RepoRoot $repoRoot -MissingMessage "Required Wave194 FreeP topology evidence is missing"
    $freePWave196Metrics = Read-ToolJson -Path $wave196FreePMetricsPath -RepoRoot $repoRoot -MissingMessage "Required Wave196 FreeP metrics are missing"
    $freePWave196Images = Read-ToolJson -Path $wave196FreePImagesPath -RepoRoot $repoRoot -MissingMessage "Required Wave196 FreeP image hashes are missing"
    $freePWave197LeadingMetrics = Read-ToolJson -Path $wave197FreePLeadingMetricsPath -RepoRoot $repoRoot -MissingMessage "Required Wave197 FreeP leading metrics are missing"
    $freePWave197BaselineMetrics = Read-ToolJson -Path $wave197FreePBaselineMetricsPath -RepoRoot $repoRoot -MissingMessage "Required Wave197 FreeP baseline metrics are missing"
    $freePWave197BaselineImages = Read-ToolJson -Path $wave197FreePBaselineImagesPath -RepoRoot $repoRoot -MissingMessage "Required Wave197 FreeP baseline image hashes are missing"
    $freePWave198Metrics = Read-ToolJson -Path $wave198FreePMetricsPath -RepoRoot $repoRoot -MissingMessage "Required Wave198 FreeP metrics are missing"
    $freePWave199Metrics = Read-ToolJson -Path $wave199FreePMetricsPath -RepoRoot $repoRoot -MissingMessage "Required Wave199 FreeP metrics are missing"
    $freePWave199References = Read-ToolJson -Path $wave199FreePReferencesPath -RepoRoot $repoRoot -MissingMessage "Required Wave199 FreeP references are missing"
    $freePWave199Images = Read-ToolJson -Path $wave199FreePImagesPath -RepoRoot $repoRoot -MissingMessage "Required Wave199 FreeP image hashes are missing"
    $freePWave199BroaderControls = Read-ToolJson -Path $wave199FreePBroaderControlsPath -RepoRoot $repoRoot -MissingMessage "Required Wave199 FreeP broader-control inventory is missing"

    $wave196RequiredEvidencePaths = @(
        $wave196FreeXEvidenceNotePath,
        $wave196FreeXSourceTestPath,
        $wave196FreeWEvidenceNotePath,
        $wave196FreeWSourceTestPath,
        $wave196FreePEvidenceNotePath,
        $wave196FreePMetricsPath,
        $wave196FreePImagesPath
    ) + $wave196FreePSourceTestPaths + $wave196PortabilityCorrectionPaths
    foreach ($evidencePath in $wave196RequiredEvidencePaths) {
        if (-not (Test-Path -LiteralPath (Join-Path $repoRoot $evidencePath) -PathType Leaf)) {
            throw "Required committed Wave196 evidence path is missing: $evidencePath"
        }
    }
    if ($freeXWave196EvidenceNote -notmatch "Focused Avalonia tests: 22 passed, 0 failed" -or
        $freeXWave196EvidenceNote -notmatch "Production Docker/X11 probe: 1 passed, 0 failed\." -or
        $freeXWave196EvidenceNote -notmatch "style-id=1\|font-id=1\|bold=true.*save-clean=true") {
        throw "Wave196 FreeX evidence note does not contain the committed production probe and persisted Bold facts."
    }
    if ($freeWWave196EvidenceNote -notmatch 'Focused regressions: `DocumentViewHeadlessTests\.TrailingInlineFlowBreak_PlacesCaretOnThePostBreakPageOrColumn` and `DocumentViewHeadlessTests\.ConsecutiveTrailingInlineFlowBreaks_PlaceCaretAtTheFinalPostBreakBoundary`') {
        throw "Wave196 FreeW evidence note does not name both committed trailing flow-break regressions."
    }
    if ([string]$freePWave196Metrics.schema -ne "freep.parity.wave196.deck17-light-hinting.v1" -or
        [string]$freePWave196Metrics.target.deck -ne "17-bullets-autofit" -or
        [string]$freePWave196Metrics.target.slide -ne "slide-02" -or
        [string]$freePWave196Metrics.acceptedCorrection.textHintingModeAfter -ne "Light") {
        throw "Wave196 FreeP metrics do not describe the committed deck17 light-hinting evidence."
    }
    $wave197RequiredEvidencePaths = @(
        $wave197FreeXEvidenceNotePath,
        $wave197FreeXSourceTestPath,
        $wave197FreeWEvidenceNotePath,
        $wave197FreeWSourceTestPath,
        $wave197FreeWRawEvidencePath,
        $wave197FreeWChecksumsPath,
        $wave197FreePLeadingEvidenceNotePath,
        $wave197FreePLeadingMetricsPath,
        $wave197FreePBaselineEvidenceNotePath,
        $wave197FreePBaselineMetricsPath,
        $wave197FreePBaselineImagesPath,
        $wave197IntegrationNotePath
    ) + $wave197FreePSourceTestPaths
    foreach ($evidencePath in $wave197RequiredEvidencePaths) {
        if (-not (Test-Path -LiteralPath (Join-Path $repoRoot $evidencePath) -PathType Leaf)) {
            throw "Required committed Wave197 evidence path is missing: $evidencePath"
        }
    }
    if ($freeXWave197EvidenceNote -notmatch "style-id=1\|numFmtId=2\|number-format=true" -or
        $freeXWave197EvidenceNote -notmatch "save-clean=true") {
        throw "Wave197 FreeX evidence note does not contain the committed number-format package facts."
    }
    if ($freeWWave197EvidenceNote -notmatch "no candidate accepted" -or
        $freeWWave197EvidenceNote -notmatch "regressed all" -or
        $freeWWave197EvidenceNote -notmatch "two target rows regressed") {
        throw "Wave197 FreeW evidence note does not retain both rejected candidate outcomes."
    }
    if (@($freeWWave197RawEvidence.extraction.scenarioIds).Count -ne 6 -or
        @($freeWWave197RawEvidence.extraction.scenarioIds | Sort-Object -Unique).Count -ne 6) {
        throw "Wave197 FreeW raw evidence must contain exactly six unique scenarios."
    }
    if ([string]$freePWave197LeadingMetrics.status -ne "candidate-refuted" -or
        [string]$freePWave197BaselineMetrics.status -ne "candidate-refuted" -or
        [string]$freePWave197LeadingMetrics.sourceProvenance.generationLinkage -ne "not-independently-proven" -or
        [string]$freePWave197BaselineMetrics.imageIntegrity.status -ne "incomplete-missing-tracked-images" -or
        @($freePWave197BaselineMetrics.imageIntegrity.missingImages).Count -ne 4) {
        throw "Wave197 FreeP evidence does not retain the rejected-candidate and image-provenance boundaries."
    }
    $wave198RequiredEvidencePaths = @(
        $wave198FreeXReadmePath,
        $wave198FreeXEvidenceNotePath,
        $wave198FreeXSourceTestPath,
        $wave198FreeWEvidenceNotePath,
        $wave198FreeWSourceTestPath,
        $wave198FreeWRawEvidencePath,
        $wave198FreePEvidenceNotePath,
        $wave198FreePMetricsPath,
        $wave198FreePSourceTestPath,
        $wave198IntegrationNotePath
    )
    foreach ($evidencePath in $wave198RequiredEvidencePaths) {
        if (-not (Test-Path -LiteralPath (Join-Path $repoRoot $evidencePath) -PathType Leaf)) {
            throw "Required committed Wave198 evidence path is missing: $evidencePath"
        }
    }
    if ($freeXWave198EvidenceNote -notmatch "font-name=Arial\|font-family=true" -or
        $freeXWave198EvidenceNote -notmatch "save-clean=true" -or
        $freeXWave198EvidenceNote -notmatch "automatic combo-close focus.*not-measured") {
        throw "Wave198 FreeX evidence note does not retain the Arial package facts and unresolved focus boundary."
    }
    if (@($freeWWave198RawEvidence.scenarios).Count -ne 10 -or
        [int]$freeWWave198RawEvidence.totals.targetChangedPixelsReduction -ne 3497 -or
        [int]$freeWWave198RawEvidence.totals.controlChangedPixelsReduction -ne 1608 -or
        $freeWWave198EvidenceNote -notmatch "auditable metadata linkage" -or
        $freeWWave198EvidenceNote -notmatch "141.*genuine visual") {
        throw "Wave198 FreeW evidence does not retain the route metrics and metadata-only boundary."
    }
    if ([string]$freePWave198Metrics.status -ne "candidate-refuted" -or
        [double]$freePWave198Metrics.measurements.slide02Target.avaloniaOfficeDeltaPercentagePoints -ne -0.0237 -or
        [double]$freePWave198Metrics.measurements.slide02Target.wpfAvaloniaDeltaPercentagePoints -ne 0.0092 -or
        [string]$freePWave198Metrics.sourceProvenance.generationLinkage -ne "not-independently-proven") {
        throw "Wave198 FreeP evidence does not retain the rejected subpixel candidate boundaries."
    }
    $wave199RequiredEvidencePaths = @($wave199FreeXReadmePath, $wave199FreeXEvidenceNotePath, $wave199FreeXChecksumsPath, $wave199FreeXInteractionPath, $wave199FreeXPackageProofPath, $wave199FreeWEvidenceNotePath, $wave199FreeWRecordPath, $wave199FreeWChecksumsPath, $wave199FreeWSourceTestPath, $wave199FreePEvidenceNotePath, $wave199FreePMetricsPath, $wave199FreePReferencesPath, $wave199FreePImagesPath, $wave199FreePBroaderControlsPath, $wave199FreePSourceTestPath, $wave199IntegrationNotePath) + $wave199FreeXSourceTestPaths
    foreach ($evidencePath in $wave199RequiredEvidencePaths) { if (-not (Test-Path -LiteralPath (Join-Path $repoRoot $evidencePath) -PathType Leaf)) { throw "Required committed Wave199 evidence path is missing: $evidencePath" } }
    $freeXWave199ChecksumCount = @(Get-Content -LiteralPath (Join-Path $repoRoot $wave199FreeXChecksumsPath)).Count
    if ($freeXWave199ChecksumCount -ne 15 -or [string]$freeXWave199Interaction.results[0].status -ne "failed" -or [string]$freeXWave199Interaction.results[0].evidence -notmatch "automatic-focus-after-combo=false" -or [string]$freeXWave199Interaction.results[0].evidence -notmatch "worksheet-focus-after-reselect=true" -or $freeXWave199PackageProof -notmatch "style-id=0\|font-id=0\|font-name=Calibri\|font-family=false" -or $freeXWave199PackageProof -notmatch "save-clean=false" -or $freeXWave199EvidenceNote -notmatch "candidate was not retained in production source") { throw "Wave199 FreeX evidence facts are incomplete." }
    $freeWWave199ChecksumCount = @(Get-Content -LiteralPath (Join-Path $repoRoot $wave199FreeWChecksumsPath)).Count
    if ([string]$freeWWave199Record.decision -ne "style-width-candidate-rejected" -or $freeWWave199ChecksumCount -ne 32 -or [double]$freeWWave199Record.baseline.metrics.'style.initial'.changedRatio -ne 0.07602988091227932 -or [double]$freeWWave199Record.candidate.metrics.'style.initial'.changedRatio -ne 0.13318273987622276 -or [double]$freeWWave199Record.baseline.metrics.'style.populated'.changedRatio -ne 0.07702062734063844 -or [double]$freeWWave199Record.candidate.metrics.'style.populated'.changedRatio -ne 0.13413385644744752 -or [int]$freeWWave199Record.scope.canonicalCounts.comparisonScenarioCount -ne 291 -or [int]$freeWWave199Record.scope.canonicalCounts.genuineVisualMismatch -ne 141 -or [int]$freeWWave199Record.scope.canonicalCounts.passes -ne 80 -or [int]$freeWWave199Record.scope.canonicalCounts.avaloniaExtensions -ne 70 -or $freeWWave199EvidenceNote -notmatch "50 ms" -or $freeWWave199EvidenceNote -notmatch "15 second") { throw "Wave199 FreeW evidence facts are incomplete." }
    if ([string]$freePWave199Metrics.status -ne "diagnostic-rejected-no-production-change" -or @($freePWave199Metrics.candidateMeasurements).Count -ne 6 -or @($freePWave199Images.PSObject.Properties).Count -ne 12 -or [string]$freePWave199Metrics.sourceProvenance.generationLinkage -ne "not-independently-proven" -or [string]$freePWave199Metrics.imageIntegrity.status -ne "tracked-byte-hashes-verified" -or [string]$freePWave199Metrics.broaderCorpusControl.status -ne "not-independently-auditable" -or @($freePWave199BroaderControls.controls).Count -ne 18 -or [int]$freePWave199BroaderControls.retainedCandidateRenderCount -ne 0 -or @($freePWave199References.images).Count -ne 6) { throw "Wave199 FreeP evidence facts are incomplete." }
    $freePPowerPointChrome = Read-ToolJson -Path "docs/parity/freep-powerpoint-chrome-2026-08-16/manifest.json" -RepoRoot $repoRoot -MissingMessage "Required PowerPoint chrome capture manifest is missing"
    $freePResponsiveChrome = Read-ToolJson -Path "docs/parity/freep-responsive-chrome-2026-08-16/manifest.json" -RepoRoot $repoRoot -MissingMessage "Required FreeP responsive chrome capture manifest is missing"

    $commandRows = Get-CommandSurfaceRows $commandInventory.commandSurfaceRows
    $freeXFunctionalMatrix = [ordered]@{
        totalCommands = [int]$functional.summary.totalCommands
        parity = [int]$functional.summary.parity
        avaloniaMissing = [int]$functional.summary.avaloniaMissing
        wpfMissing = [int]$functional.summary.wpfMissing
        bothMissing = [int]$functional.summary.bothMissing
        intentionalLinuxOmissions = [int]$functional.summary.intentionalLinuxOmissions
        realBehaviorGaps = [int](Get-JsonPropertyValue $functionalClassification.summary "real-behavior-gap")
        nonClickControlInventoryRows = [int](Get-JsonPropertyValue $functionalClassification.summary "non-click-control-inventory-row")
        pseudoCommandGalleryItems = [int](Get-JsonPropertyValue $functionalClassification.summary "pseudo-command-gallery-item")
        conditionalFormatPopupGalleryRows = [int](Get-JsonPropertyValue $functionalClassification.summary "conditional-format-popup-gallery-row")
        conditionalFormatPopupCatalogItems = [int](Get-JsonPropertyValue $functionalClassification.summary "conditional-format-popup-catalog-item")
        accountingSymbolPopupGalleryRows = [int](Get-JsonPropertyValue $functionalClassification.summary "accounting-symbol-popup-gallery-row")
        accountingSymbolPopupCatalogItems = [int](Get-JsonPropertyValue $functionalClassification.summary "accounting-symbol-popup-catalog-item")
        fontBorderPopupGalleryRows = [int](Get-JsonPropertyValue $functionalClassification.summary "font-border-popup-gallery-row")
        fontBorderPopupCatalogItems = [int](Get-JsonPropertyValue $functionalClassification.summary "font-border-popup-catalog-item")
        handlerQualifiedHelpRoutes = [int](Get-JsonPropertyValue $functionalClassification.summary "handler-qualified-help-route")
        sharedRibbonComboBoxControls = [int](Get-JsonPropertyValue $functionalClassification.summary "shared-ribbon-combo-box-control")
    }
    $freeXDialogRoutes = [ordered]@{
        totalRoutes = [int]$dialogInventory.summary.totalRoutes
        wpfCaptures = [int]$dialogInventory.summary.wpfCaptures
        avaloniaCaptures = [int]$dialogInventory.summary.avaloniaCaptures
        avaloniaHarnessRoutes = [int]$dialogInventory.summary.avaloniaHarnessRoutes
        sharedOrPresentationBacked = [int]$dialogInventory.summary.sharedOrPresentationBacked
    }
    $dimensionMismatchBuckets = $dialogVisualEvidence.summary.dimensionMismatchBuckets
    $visualReviewCandidates = Get-JsonPropertyValue $dialogVisualEvidence "visualReviewCandidates" @()
    $freeXDialogVisualEvidence = [ordered]@{
        wpfCapturedManifestSurfaces = [int]$dialogVisualEvidence.summary.wpfCapturedManifestSurfaces
        avaloniaCapturedManifestSurfaces = [int]$dialogVisualEvidence.summary.avaloniaCapturedManifestSurfaces
        pairedCapturedSurfaceIds = [int]$dialogVisualEvidence.summary.pairedCapturedSurfaceIds
        wpfManifestIdsWithoutAvaloniaPair = [int]$dialogVisualEvidence.summary.wpfManifestIdsWithoutAvaloniaPair
        additionalAvaloniaCapturedSurfaceIds = [int]$dialogVisualEvidence.summary.additionalAvaloniaCapturedSurfaceIds
        pairedDimensionMismatches = [int]$dialogVisualEvidence.summary.pairedDimensionMismatches
        pairedRawPixelDimensionMismatches = [int]$dialogVisualEvidence.summary.pairedRawPixelDimensionMismatches
        pairedCaptureScaleNormalizedDimensionMatches = [int]$dialogVisualEvidence.summary.pairedCaptureScaleNormalizedDimensionMatches
        pairedExpectedSizeMismatches = [int]$dialogVisualEvidence.summary.pairedExpectedSizeMismatches
        stalePromotedExpectedSizeEvidence = [int]$dialogVisualEvidence.summary.stalePromotedExpectedSizeEvidence
        contentVisualDimensionMismatchRows = [int](Get-JsonPropertyValue $dimensionMismatchBuckets "content/visual mismatch")
        evidenceLimitationDimensionMismatchRows = [int](Get-JsonPropertyValue $dimensionMismatchBuckets "evidence limitation")
        expectedPlatformNativeDimensionMismatchRows = [int](Get-JsonPropertyValue $dimensionMismatchBuckets "expected platform/native difference")
        realLogicalSizeMismatchRows = [int](Get-JsonPropertyValue $dimensionMismatchBuckets "real logical-size mismatch")
        policyAcceptedNativeDifferences = [int](Get-JsonPropertyValue $dialogVisualEvidence.summary "policyAcceptedNativeDifferences")
        visualReviewTriageThreshold = [double](Get-JsonPropertyValue $dialogVisualEvidence.summary "visualReviewTriageThreshold")
        visualReviewTriageThresholdRationale = [string](Get-JsonPropertyValue $dialogVisualEvidence.summary "visualReviewTriageThresholdRationale" "")
        visualReviewCandidateCount = [int](Get-JsonPropertyValue $dialogVisualEvidence.summary "visualReviewCandidateCount")
        highestTriageScore = [double](Get-JsonPropertyValue $dialogVisualEvidence.summary "highestTriageScore")
        visualReviewCandidateSurfaceIds = @($visualReviewCandidates | ForEach-Object { [string]$_.id })
        visualReviewCandidates = @($visualReviewCandidates | ForEach-Object {
                [ordered]@{
                    id = [string]$_.id
                    triageScore = [double]$_.triageScore
                    reviewStatus = [string]$_.reviewStatus
                    logicalDimensionMatch = [bool]$_.logicalDimensionMatch
                    dimensionMismatchBucket = $_.dimensionMismatchBucket
                }
            })
    }
    $freeXRenderedEvidence = [ordered]@{
        sourceFiles = @(
            "docs/parity/dialog-parity-inventory.json",
            "docs/parity/dialog-visual-evidence-summary.json",
            "docs/parity/dialog-visual-assets/wpf-capture/manifest.json",
            "docs/parity/dialog-visual-assets/avalonia-capture/manifest.json",
            "docs/parity/freex-excel-foreground-capture-2026-08-16.md",
            "docs/parity/freex-excel-chrome-comparison.md",
            "tools/screenshots/screenshot_manifest.json",
            "tools/screenshots_avalonia_ribbon/screenshot_manifest.json",
            "docs/parity/freex-excel-com-baseline-2026-08-14/manifest.json",
            "docs/parity/freex-avalonia-grid-corpus-2026-08-16/manifest.json",
            "docs/parity/avalonia-parity-wave183-freex-namebox-overlay-20260823.md",
            "docs/parity/avalonia-parity-wave184-freex-autofilter-20260823.md",
            "docs/parity/avalonia-parity-wave185-freex-autofilter-sort-20260823.md",
            "docs/parity/avalonia-parity-wave186-freex-autofilter-text-20260823.md",
            "docs/parity/avalonia-parity-wave187-freex-autofilter-numeric-20260823.md",
            "docs/parity/avalonia-parity-wave188-freex-autofilter-numeric-20260823.md",
            "docs/parity/avalonia-parity-wave189-freex-autofilter-date-20260823.md",
            "docs/parity/avalonia-parity-wave190-freex-autofilter-date-20260823.md",
            "docs/parity/evidence/wave190-freex-autofilter-date-20260823/manifest.json",
            "docs/parity/avalonia-parity-wave191-freex-autofilter-color-20260823.md",
            "docs/parity/evidence/wave191-freex-autofilter-color-20260823/manifest.json",
            "docs/parity/avalonia-parity-wave192-freex-autofilter-font-color-20260823.md",
            "docs/parity/evidence/wave192-freex-autofilter-font-color-20260823/manifest.json",
            "docs/parity/avalonia-parity-wave193-freex-autofilter-no-fill-20260823.md",
            "docs/parity/evidence/wave193-freex-autofilter-no-fill-20260823/physical-result.json",
            "docs/parity/evidence/wave193-freex-autofilter-no-fill-20260823/manifest.json",
            "docs/parity/avalonia-parity-wave194-freex-autofilter-mixed-type-20260823.md",
            "docs/parity/evidence/wave194-freex-autofilter-mixed-type-20260823/physical-result.json",
            "docs/parity/evidence/wave194-freex-autofilter-mixed-type-20260823/manifest.json",
            "docs/parity/evidence/wave195-freex-autofilter-criteria-workflows-20260828/manifest.json",
            $wave195ProgressNotePath,
            $wave196FreeXEvidenceNotePath,
            $wave196FreeXSourceTestPath,
            $wave196IntegrationNotePath
        )
        routeCoverage = [ordered]@{
            inventoryRouteCount = $freeXDialogRoutes.totalRoutes
            wpfRouteEvidenceCount = $freeXDialogRoutes.wpfCaptures
            avaloniaRouteEvidenceCount = $freeXDialogRoutes.avaloniaCaptures
            pairedRouteEvidenceCount = [Math]::Min($freeXDialogRoutes.wpfCaptures, $freeXDialogRoutes.avaloniaCaptures)
        }
        artifactCoverage = [ordered]@{
            wpfManifestSurfaceCount = @($freeXWpfManifest.surfaces).Count
            avaloniaManifestSurfaceCount = @($freeXAvaloniaManifest.surfaces).Count
            pairedManifestSurfaceCount = $freeXDialogVisualEvidence.pairedCapturedSurfaceIds
            nonBlankPngFailures = [int](Get-JsonPropertyValue $dialogVisualEvidence.summary "nonBlankPngFailures")
            rawPngDimensionMismatchRows = $freeXDialogVisualEvidence.pairedRawPixelDimensionMismatches
            dpiNormalizedDimensionMatches = $freeXDialogVisualEvidence.pairedCaptureScaleNormalizedDimensionMatches
        }
        pairedEvidence = [ordered]@{
            pairedSurfaceCount = $freeXDialogVisualEvidence.pairedCapturedSurfaceIds
            scaleAwareDimensionMismatchCount = $freeXDialogVisualEvidence.pairedDimensionMismatches
            unresolvedVisualReviewCandidateCount = $freeXDialogVisualEvidence.visualReviewCandidateCount
            highestTriageScore = $freeXDialogVisualEvidence.highestTriageScore
            evidenceClassification = "paired WPF/Avalonia app-owned dialog screenshots and manifest metadata; not a visual-parity result"
        }
        chromeCapture = [ordered]@{
            excelReferenceCount = 36
            wpfCaptureCount = @($freeXWpfRibbonManifest.Captures).Count
            avaloniaCaptureCount = @($freeXAvaloniaRibbonManifest.Captures).Count
            fixedViewportComparisonCount = 27
            coverageOnlyComparisonCount = 9
            wpfMeanPixelDiffPercent = 13.936596740910273
            avaloniaMeanPixelDiffPercent = 15.639134449147328
            evidenceClassification = "complete foreground Excel/WPF/Avalonia ribbon matrices; fixed-width deltas are triage only"
        }
        gridCorpus = [ordered]@{
            captureStatus = [string]$freeXAvaloniaGridCorpus.captureStatus
            chartCaptureCount = [int]$freeXAvaloniaGridCorpus.counts.chart
            cellStyleCaptureCount = [int]$freeXAvaloniaGridCorpus.counts.'cell-style'
            pivotCaptureCount = [int]$freeXAvaloniaGridCorpus.counts.pivot
            totalAvaloniaCaptureCount = [int]$freeXAvaloniaGridCorpus.counts.total
            evidenceClassification = [string]$freeXAvaloniaGridCorpus.comparisonBoundary
        }
        physicalEvidence = [ordered]@{
            status = "available-interactive-foreground"
            captureMode = "foreground-owned Excel ribbon/dialog capture, generated WPF/Avalonia dialog render manifests, and authoritative Linux X11 input probes"
            noComStatus = "not-applicable"
            linuxNameBoxParityPassed = 1
            linuxNameBoxParityTotal = 1
            linuxNameBoxInteractionPassed = 8
            linuxNameBoxInteractionTotal = 8
            linuxAutoFilterRecalculationPassed = 1
            linuxAutoFilterRecalculationTotal = 1
            linuxAutoFilterSortPersistencePassed = 1
            linuxAutoFilterSortPersistenceTotal = 1
            linuxAutoFilterTextCriteriaPassed = 2
            linuxAutoFilterTextCriteriaTotal = 2
            linuxAutoFilterNumericCriteriaPassed = 2
            linuxAutoFilterNumericCriteriaTotal = 2
            linuxAutoFilterNumericCriteriaStatus = "passed-production-x11"
            linuxAutoFilterDateCriteriaPassed = 2
            linuxAutoFilterDateCriteriaTotal = 2
            linuxAutoFilterDateCriteriaStatus = "passed-production-x11"
            linuxAutoFilterFillColorPassed = 1
            linuxAutoFilterFillColorTotal = 1
            linuxAutoFilterFillColorStatus = "passed-production-x11"
            linuxAutoFilterFontColorPassed = 1
            linuxAutoFilterFontColorTotal = 1
            linuxAutoFilterFontColorStatus = "passed-production-x11"
            linuxAutoFilterNoFillPassed = [int]$freeXWave193PhysicalResult.summary.passed
            linuxAutoFilterNoFillTotal = [int]$freeXWave193PhysicalResult.summary.total
            linuxAutoFilterNoFillStatus = "passed-production-x11"
            wave193FocusedAvaloniaGuardPassed = 3
            wave193FocusedCoreIoGuardPassed = 8
            wave193EvidenceArtifactCount = $wave193ArtifactCount
            wave193EvidenceArtifactExpectedCount = $wave193ExpectedArtifactCount
            wave193EvidenceProvenanceFileCount = @($freeXWave193Manifest.provenanceFiles).Count
            wave193PopupTransitions = [ordered]@{
                popupOpen = [ordered]@{
                    changedPixels = [int]$wave193PhysicalRun.popupOpenChangedPixels
                    minimumChangedPixels = $wave193PopupOpenChangeThreshold
                    passed = [int]$wave193PhysicalRun.popupOpenChangedPixels -ge $wave193PopupOpenChangeThreshold
                }
                popupDismissed = [ordered]@{
                    changedPixels = [int]$wave193PhysicalRun.popupDismissedChangedPixels
                    minimumChangedPixels = $wave193PopupOpenChangeThreshold
                    passed = [int]$wave193PhysicalRun.popupDismissedChangedPixels -ge $wave193PopupOpenChangeThreshold
                }
                restoration = [ordered]@{
                    changedPixels = [int]$wave193PhysicalRun.popupRestoredChangedPixels
                    maximumChangedPixels = $wave193PopupRestoredChangeMaximum
                    passed = [int]$wave193PhysicalRun.popupRestoredChangedPixels -le $wave193PopupRestoredChangeMaximum
                }
                clickAcknowledged = [bool]$wave193PhysicalRun.clickAcknowledged
                summary = "popup-open $([int]$wave193PhysicalRun.popupOpenChangedPixels) changed pixels (minimum $wave193PopupOpenChangeThreshold); popup-dismissed $([int]$wave193PhysicalRun.popupDismissedChangedPixels) changed pixels (minimum $wave193PopupOpenChangeThreshold); restoration $([int]$wave193PhysicalRun.popupRestoredChangedPixels) changed pixels (maximum $wave193PopupRestoredChangeMaximum); click acknowledged"
            }
            wave193PackageSemantics = "SourcePatch retained for the no-row-delta criterion-only case; package colorFilter/DXF semantics are verified after save and reopen."
            wave194 = [ordered]@{
                status = "passed"
                physicalPassed = [int]$freeXWave194PhysicalResult.summary.passed
                physicalTotal = [int]$freeXWave194PhysicalResult.summary.total
                focusedAvaloniaGuardPassed = 9
                focusedAvaloniaGuardTotal = 9
                focusedPresentationPassed = 1
                focusedPresentationTotal = 1
                focusedCoreIoPassed = 8
                focusedCoreIoTotal = 8
                evidenceArtifactCount = @($freeXWave194Manifest.files).Count
                evidenceArtifactExpectedCount = 20
                reachableProvenanceFileCount = @($freeXWave194Manifest.provenanceFiles).Count
                validationFileCount = @($freeXWave194Manifest.validationFiles).Count
                geometry = [ordered]@{
                    bounds = [string]$freeXWave194Manifest.physicalRun.targetBounds
                    click = [string]$freeXWave194Manifest.physicalRun.targetClick
                    contract = "One authoritative geometry contract is consumed by crop, readiness/transition checks, and the actual physical click; mutation guards reject hard-coded or unreachable substitutes."
                }
                visibleReadback = [string]$freeXWave194Manifest.physicalRun.visible
                semanticReadback = [string]$freeXWave194Manifest.physicalRun.semantic
                recalculation = [string]$freeXWave194Manifest.physicalRun.recalculation
                package = [string]$freeXWave194Manifest.physicalRun.package
                reopenedVisibleReadback = [string]$freeXWave194Manifest.physicalRun.reopenedVisible
                reopenedSemanticReadback = [string]$freeXWave194Manifest.physicalRun.reopenedSemantic
                reachableProvenanceSourceCommit = [string]$freeXWave194Manifest.sourceCommit
                physicalCaptureSourceCommit = [string]$freeXWave194Manifest.physicalCaptureSourceCommit
                evidenceUnchangedAfterGeometryRemediation = $true
                claimBoundary = "Bounded physical Linux/X11 evidence for one mixed-type AutoFilter workflow; it does not establish complete AutoFilter or Excel visual parity."
            }
            wave195 = [ordered]@{
                status = [string]$freeXWave195Manifest.status
                validationMode = [string]$freeXWave195Manifest.validationMode
                evidencePath = "docs/parity/evidence/wave195-freex-autofilter-criteria-workflows-20260828/manifest.json"
                physicalPassed = $wave195SessionCount
                physicalTotal = $wave195SessionCount
                sessionCount = $wave195SessionCount
                evidenceArtifactCount = $wave195ArtifactCount
                screenshotCount = $wave195ScreenshotCount
                reloadWitnessPassed = $wave195ReloadWitnessCount
                reloadWitnessTotal = $wave195SessionCount
                productionDockerX11Sessions = "Both named production Docker/X11 sessions pass: multi-column criteria change/clear and color criteria change/clear persistence."
                claimBoundary = [string]$freeXWave195Manifest.claimBoundary
            }
            wave196 = [ordered]@{
                status = "evidence-recorded"
                validationMode = "physical-only"
                evidencePath = $wave196FreeXEvidenceNotePath
                sourceTestPath = $wave196FreeXSourceTestPath
                physicalPassed = 1
                physicalTotal = 1
                focusedSourceTestsPassed = 22
                focusedSourceTestsTotal = 22
                productionDockerX11 = "The committed production Docker/X11 probe passes the Home ribbon Bold key-tip route and persists bold=true in the saved XLSX package."
                persistedStyle = "style-id=1|font-id=1|bold=true"
                saveClean = $true
                claimBoundary = "Committed FreeX production Linux/X11 and source-test evidence for one ribbon-formatting workflow; accepted local integration gates do not establish complete FreeX or visual parity."
            }
            wave197 = [ordered]@{
                status = "evidence-recorded"
                validationMode = "physical-only"
                evidencePath = $wave197FreeXEvidenceNotePath
                sourceTestPath = $wave197FreeXSourceTestPath
                physicalPassed = 1
                physicalTotal = 1
                focusedSourceTestsPassed = 16
                focusedSourceTestsTotal = 16
                productionDockerX11Report = "20260829T013532Z"
                productionDockerX11ProvenanceCommit = "9c1fd10cb61dc5bf324502ba68fc47d939436624"
                persistedStyle = "style-id=1|numFmtId=2|number-format=true"
                saveClean = $true
                ordinaryBubbleKeyRouting = "retained"
                deferredComboDismissFocusRestore = "one deferred combo-dismiss callback rechecks focus immediately and synchronously restores worksheet focus"
                testedSourceBoundary = "The later exact tested head differs only by the FreeW evidence/test commit a6b1f27e02; FreeX production source remains represented by provenance commit 9c1fd10cb61dc5bf324502ba68fc47d939436624."
                claimBoundary = "Committed FreeX production Linux/X11 and source-test evidence for one Home ribbon number-format persistence workflow; accepted local integration gates do not establish complete FreeX or visual parity."
            }
            wave198 = [ordered]@{
                status = "evidence-recorded"
                validationMode = "physical-only"
                evidencePath = $wave198FreeXEvidenceNotePath
                readmePath = $wave198FreeXReadmePath
                checksumsPath = $wave198FreeXChecksumsPath
                sourceTestPath = $wave198FreeXSourceTestPath
                physicalPassed = 1
                physicalTotal = 1
                focusedSourceTestsPassed = 3
                focusedSourceTestsTotal = 3
                productionDockerX11Report = "20260829T040529Z"
                persistedStyle = "style-id=1|font-id=1|font-name=Arial|font-family=true"
                saveClean = $true
                automaticComboCloseFocus = "not-measured"
                worksheetKeyboardVerification = "explicit reselect followed by Right and Ctrl+C copied B1=Unchanged"
                checksumStatus = "tracked evidence SHA-256 values verified in the current integration worktree"
                claimBoundary = "Committed FreeX production Linux/X11 and source-test evidence for one Home ribbon font-family operation/persistence workflow; automatic combo-close focus remains unresolved and accepted local integration gates do not establish complete FreeX or visual parity."
            }
            wave199 = [ordered]@{
                status = "candidate-rejected"; validationMode = "physical-only"; evidencePath = $wave199FreeXEvidenceNotePath; readmePath = $wave199FreeXReadmePath; checksumsPath = $wave199FreeXChecksumsPath; interactionPath = $wave199FreeXInteractionPath; packageProofPath = $wave199FreeXPackageProofPath; sourceTestPaths = $wave199FreeXSourceTestPaths
                productionChangeRetained = $false; physicalPassed = 0; physicalFailed = 1; physicalTotal = 1; focusedSourceTestsPassed = 8; focusedSourceTestsTotal = 8; canonicalEvidenceFileCount = $freeXWave199ChecksumCount
                automaticWorksheetFocus = "failed; focus stayed on A1"; explicitWorksheetReselect = "worked; Right then Ctrl+C copied B1=Unchanged"; persistedStyle = "style-id=0|font-id=0|font-name=Calibri|font-family=false"; saveClean = $false
                claimBoundary = "No production change was retained. Automatic worksheet focus failed physical Linux/X11, explicit worksheet reselect worked, and Calibri/save-clean=false failed persistence; this bounded evidence does not establish complete FreeX or visual parity."
            }
            limitations = @(
                "The 2026-08-16 interactive run captured 36 foreground ribbon states for each of Excel, WPF, and Avalonia, including Draw at all four widths, plus six guarded Excel popup/dialog surfaces. The 27 fixed-viewport triage rows average 13.937% RGB delta for WPF and 15.639% for Avalonia versus Excel; nine maximized rows are coverage-only, not an acceptance threshold.",
                "The ribbon harness creates a blank workbook before tab discovery so the enabled Draw tab is materialized; the current manifest records no unavailable-tab skips.",
                "WPF captured 116/116 app-host surfaces and Avalonia captured 180/181; the managed popup.nameBoxDropdown surface remains diagnostic-only, while the production Linux X11 Name Box crop and interaction lanes pass 1/1 and 8/8 respectively.",
                "The production Linux X11 AutoFilter apply/change/clear workflow passes 1/1 with exact SUBTOTAL postconditions 30 -> 10 -> 20 -> 30.",
                "The production Linux X11 AutoFilter sort/save/reopen workflow passes 1/1 with exact ascending and descending visible-order and package-state postconditions.",
                "The production Linux X11 AutoFilter text-criteria save/reopen workflows pass 2/2 for Begins With and Equals with exact visible-row and package-state postconditions.",
                "The production Linux X11 numeric AutoFilter save/reopen workflows pass 2/2 for Greater Than 50 and Equals 50 through the rendered non-first-column B1 glyph, with exact visible-row and package-state postconditions. Wave188 also makes fixture readiness deterministic and restricts the glyph bridge to left-click while retaining Button.Click for keyboard/accessibility activation.",
                "The production Linux X11 date AutoFilter lane passes 2/2 end to end. Before February 1 retains Jan01/Jan15 and saves lessThan=45323; After February 1 retains Mar15 and saves greaterThan=45323. Both use identity-checked Open Workbook dialogs and reopen with matching rendered-grid and semantic state, with a compact committed evidence bundle.",
                "The production Linux X11 fill-color AutoFilter save/reopen lane passes 1/1. It selects the rendered #00B050 swatch, retains North/East, reopens through the identity-checked production picker, reads semantic A4=East, and saves explicit Excel-compatible cellColor=1 with fill FF00B050. Four rendered captures, the saved package, and hash-verified source/image provenance are committed.",
                "The production Linux X11 font-color AutoFilter save/reopen lane passes 1/1. It pixel-gates and selects the rendered #00B050 font swatch, retains North/East, reopens through the identity-checked production picker, reads semantic A4=East, and independently verifies cellColor=0 with DXF font FF00B050. Four rendered captures, the saved package, and hash-verified source/image provenance are committed.",
                "The production Linux X11 No Fill AutoFilter save/reopen lane passes $($freeXWave193PhysicalResult.summary.passed)/$($freeXWave193PhysicalResult.summary.total). It selects the rendered No Fill swatch, proves popup-open and popup-dismissed transitions at $([int]$wave193PhysicalRun.popupOpenChangedPixels)/$([int]$wave193PhysicalRun.popupDismissedChangedPixels) changed pixels, restores the pre-popup target at $([int]$wave193PhysicalRun.popupRestoredChangedPixels) changed pixels, retains South/West, verifies the empty-DXF colorFilter package semantics, and reopens with matching rendered and semantic state. The committed Wave193 manifest contains $wave193ArtifactCount/$wave193ExpectedArtifactCount artifacts and $(@($freeXWave193Manifest.provenanceFiles).Count)/9 provenance files.",
                "Wave193 focused source/evidence coverage is 3/3 Avalonia guards and 8/8 Core.IO guards; the package tests retain SourcePatch semantics when a criterion-only change produces no row-visibility delta.",
                "Wave194 adds one physical mixed-type AutoFilter workflow at $($freeXWave194PhysicalResult.summary.passed)/$($freeXWave194PhysicalResult.summary.total): Select All is cleared, numeric 42 is selected, SUBTOTAL changes 5 -> 2, visible/readback is $($freeXWave194Manifest.physicalRun.visible), semantic labels are $($freeXWave194Manifest.physicalRun.semantic), and save/reopen preserves the exact package contract. Focused Wave194 source/evidence coverage is 9/9 Avalonia guards, 1/1 Presentation, and 8/8 Core.IO (Wave194 plus five foreground-capture guards).",
                "Wave194 retains $(@($freeXWave194Manifest.files).Count)/20 evidence artifacts, $(@($freeXWave194Manifest.provenanceFiles).Count)/10 reachable provenance files, and $(@($freeXWave194Manifest.validationFiles).Count)/2 current validation files. Geometry is $($freeXWave194Manifest.physicalRun.targetBounds) with click $($freeXWave194Manifest.physicalRun.targetClick), consumed through one authoritative contract; physical evidence bytes are unchanged by the geometry remediation.",
                "Wave195 adds two passing production Docker/X11 sessions for multi-column and color criteria change/clear persistence. The committed manifest contains $wave195ArtifactCount artifacts including $wave195ScreenshotCount screenshots, and both sessions have reload witnesses. This is bounded physical FreeX Avalonia Linux evidence for the named fixtures and retained sessions, not exhaustive parity or WPF evidence.",
                "Wave196 adds one committed production Docker/X11 ribbon-formatting probe with 22/22 focused source tests and exact saved-package Bold evidence; this is a single bounded workflow, not exhaustive parity or a local integration acceptance.",
                "The current-source range corpus retains 35 Avalonia grid captures: eight charts, seven cell styles, and 20 native PivotTable surfaces. These are renderer coverage evidence, not raw-pixel Office acceptance rows.",
                [string]$freeXOfficeBaseline.limitation
            )
        }
        authoritativeMicrosoftOfficeBaseline = [ordered]@{
            product = [string]$freeXOfficeBaseline.product
            available = [bool]$freeXOfficeBaseline.available
            status = [string]$freeXOfficeBaseline.status
            artifactCount = [int]$freeXOfficeBaseline.artifactCount
            limitation = [string]$freeXOfficeBaseline.limitation
            captureMode = [string]$freeXOfficeBaseline.captureMode
        }
        claimBoundary = "Coverage, manifest pairing, and DPI-normalized size comparability only; do not read these fields as visual parity."
    }
    $freeX = [ordered]@{
        app = "FreeX"
        commandSurface = [ordered]@{
            implemented = Get-StatusCount $commandRows "Implemented"
            partial = Get-StatusCount $commandRows "Partial"
            notImplemented = Get-StatusCount $commandRows "Not Implemented"
            deferred = Get-StatusCount $commandRows "Deferred"
            excluded = Get-StatusCount $commandRows "Excluded"
        }
        functionalMatrix = $freeXFunctionalMatrix
        dialogRoutes = $freeXDialogRoutes
        dialogVisualEvidence = $freeXDialogVisualEvidence
        renderedEvidence = $freeXRenderedEvidence
        nextSlice = Get-FreeXNextSlice `
            -FunctionalMatrix $freeXFunctionalMatrix `
            -FunctionalClassificationSummary $functionalClassification.summary `
            -DialogRoutes $freeXDialogRoutes `
            -DialogVisualEvidence $freeXDialogVisualEvidence
    }
    $freeX.nextSlice = "$($freeX.nextSlice) Wave198 records one production Docker/X11 Home ribbon font-family operation/persistence probe with 3/3 focused tests. Automatic combo-close focus was not measured before explicit worksheet reselect and remains unresolved; continue with a pre-reselect focus-routing probe and broader physical coverage."

    $freeWComparisonRows = @($freeWVisualComparison.rows)
    $freeWPairedComparisonRows = @($freeWComparisonRows | Where-Object { $_.captureStatus -eq "captured/captured" })
    $freeWAvaloniaExtensionRows = @($freeWComparisonRows | Where-Object { $_.classification -eq "avalonia-extension" })
    $freeWStateNotApplicableRows = @($freeWComparisonRows | Where-Object { $_.classification -eq "state-not-applicable" })
    $freeWWave195Metrics = Get-FreeWWave195Metrics -ComparisonRows $freeWComparisonRows -EvidenceNote $freeWWave195EvidenceNote
    $freeWPassCount = @($freeWPairedComparisonRows | Where-Object { $_.classification -eq "pass" }).Count
    $freeWVisualMismatchCount = @($freeWPairedComparisonRows | Where-Object { $_.classification -eq "genuine-visual-mismatch" }).Count
    $freeWClassificationTotals = "$freeWVisualMismatchCount mismatch / $freeWPassCount pass / $($freeWAvaloniaExtensionRows.Count) extension"
    $freeWPairedRouteIds = @($freeWPairedComparisonRows | ForEach-Object { Get-ScenarioRouteId ([string]$_.scenarioId) } | Sort-Object -Unique)
    $freeWAvaloniaExtensionRouteIds = @($freeWAvaloniaExtensionRows | ForEach-Object { Get-ScenarioRouteId ([string]$_.scenarioId) } | Sort-Object -Unique)
    $freeWRenderedEvidence = [ordered]@{
        sourceFiles = @(
            "docs/parity/freew-dialog-harness/freew_dialog_route_inventory.json",
            "docs/parity/freew-dialog-harness/freew_dialog_visual_comparison.json",
            "docs/parity/freew-dialog-harness/freew_font_visual_provenance.json",
            "docs/parity/freew-word-baseline-2026-08-16/manifest.json",
            "docs/parity/freew-shell-visual-2026-08-16/freew_shell_visual_evidence.json",
            "docs/parity/freew-word-chrome-2026-08-16/manifest.json",
            "docs/parity/avalonia-parity-wave184-freew-table-properties-cell-20260823.md",
            "freew/docs/parity/avalonia-parity-wave185-freew-page-setup-text-raster-20260823.md",
            "freew/docs/parity/avalonia-parity-wave186-freew-table-properties-text-raster-20260823.md",
            "docs/parity/avalonia-parity-wave187-freew-legal-notices-20260823.md",
            "docs/parity/avalonia-parity-wave188-freew-font-20260823.md",
            "docs/parity/avalonia-parity-wave189-freew-font-antialias-20260823.md",
            "docs/parity/avalonia-parity-wave190-freew-visual-20260823.md",
            "docs/parity/avalonia-parity-wave191-freew-font-template-20260823.md",
            "docs/parity/avalonia-parity-wave192-freew-font-checkbox-20260823.md",
            "docs/parity/avalonia-parity-wave193-freew-font-checkbox-glyph-20260823.md",
            "docs/parity/avalonia-parity-wave194-freew-font-action-border-20260824.md",
            $wave195ProgressNotePath,
            $wave196FreeWEvidenceNotePath,
            $wave196FreeWSourceTestPath,
            $wave196IntegrationNotePath
        )
        canonicalComparison = [ordered]@{
            kind = [string]$freeWVisualComparison.scope.kind
            description = [string]$freeWVisualComparison.scope.description
            refreshInstruction = [string]$freeWVisualComparison.scope.refreshInstruction
        }
        wave193 = [ordered]@{
            baselineAggregateChangedPixels = [int]$freeWFontProvenance.wave192Baseline.aggregateChangedPixels
            aggregateChangedPixels = [int]$freeWFontProvenance.wave193Result.aggregateChangedPixels
            aggregateDelta = [int]$freeWFontProvenance.wave193Result.aggregateDelta
            relativeImprovement = [double]$freeWFontProvenance.wave193Result.relativeImprovement
            changedPixelsByState = [ordered]@{
                initial = [int](@($freeWFontProvenance.rows | Where-Object state -eq "initial")[0].metrics.changedPixels)
                populated = [int](@($freeWFontProvenance.rows | Where-Object state -eq "populated")[0].metrics.changedPixels)
                validationError = [int](@($freeWFontProvenance.rows | Where-Object state -eq "validation-error")[0].metrics.changedPixels)
            }
            perStateImprovement = 445
            paintedBounds = "421 x 321"
            nonFontRowsCompared = [int]$freeWFontProvenance.wave193Result.nonFontRowsCompared
            nonFontRowsChanged = [int]$freeWFontProvenance.wave193Result.nonFontRowsChanged
            classificationTotals = $freeWClassificationTotals
            retainedPolicy = "shared opt-in 14px checkbox indicator, +1px vertical offset, #EBEBEB/#F6F6F6 frame; the 1px stroke probe was a no-op and was removed."
        }
        wave194 = [ordered]@{
            sourceRevision = [string]$freeWFontProvenance.generatedAtSourceRevision
            baselineAggregateChangedPixels = [int]$freeWFontProvenance.wave193Result.aggregateChangedPixels
            aggregateChangedPixels = [int]$freeWFontProvenance.wave194Result.aggregateChangedPixels
            aggregateDelta = [int]$freeWFontProvenance.wave194Result.aggregateDelta
            relativeImprovement = [double]$freeWFontProvenance.wave194Result.relativeImprovement
            changedPixelsByState = [ordered]@{
                initial = [int](@($freeWFontProvenance.rows | Where-Object state -eq "initial")[0].metrics.changedPixels)
                populated = [int](@($freeWFontProvenance.rows | Where-Object state -eq "populated")[0].metrics.changedPixels)
                validationError = [int](@($freeWFontProvenance.rows | Where-Object state -eq "validation-error")[0].metrics.changedPixels)
            }
            perStateImprovement = 183
            paintedBounds = "421 x 321"
            nonFontRowsCompared = [int]$freeWFontProvenance.wave194Result.nonFontRowsCompared
            nonFontRowsChanged = [int]$freeWFontProvenance.wave194Result.nonFontRowsChanged
            classificationTotals = $freeWClassificationTotals
            correction = "Avalonia Font action-button border changed from #707070 to #C8C8C8 to match WPF; no other rows changed."
            claimBoundary = "Canonical FreeW Font-dialog WPF/Avalonia evidence only; remaining text/control raster differences do not establish Word visual parity."
        }
        wave195 = [ordered]@{
            catalogRowCount = $freeWWave195Metrics.catalogRowCount
            passCount = $freeWWave195Metrics.passCount
            genuineVisualMismatchCount = $freeWWave195Metrics.genuineVisualMismatchCount
            avaloniaExtensionCount = $freeWWave195Metrics.avaloniaExtensionCount
            legalNoticesStateCount = $freeWWave195Metrics.legalNoticesStateCount
            legalNoticesBaselineChangedPixels = $freeWWave195Metrics.legalNoticesBaselineChangedPixels
            legalNoticesChangedPixels = $freeWWave195Metrics.legalNoticesChangedPixels
            legalNoticesAggregateDelta = $freeWWave195Metrics.legalNoticesAggregateDelta
            nonLegalRowsStructurallyUnchanged = $freeWWave195Metrics.nonLegalRowsStructurallyUnchanged
            correction = "Fresh current-source Legal Notices evidence is refreshed for $($freeWWave195Metrics.legalNoticesStateCount) states; aggregate changed pixels fall from $($freeWWave195Metrics.legalNoticesBaselineChangedPixels) to $($freeWWave195Metrics.legalNoticesChangedPixels), a delta of $($freeWWave195Metrics.legalNoticesAggregateDelta), while $($freeWWave195Metrics.nonLegalRowsStructurallyUnchanged) non-Legal rows remain structurally unchanged."
            classificationBoundary = "The canonical catalog remains $($freeWWave195Metrics.catalogRowCount) rows: $($freeWWave195Metrics.passCount) pass, $($freeWWave195Metrics.genuineVisualMismatchCount) genuine visual mismatches, and $($freeWWave195Metrics.avaloniaExtensionCount) Avalonia extensions."
            claimBoundary = "Canonical FreeW WPF/Avalonia evidence only; the Legal Notices improvement does not establish Word visual parity."
        }
        wave196 = [ordered]@{
            status = "evidence-recorded"
            evidencePath = $wave196FreeWEvidenceNotePath
            sourceTestPath = $wave196FreeWSourceTestPath
            scenario = "TrailingInlineFlowBreak_PlacesCaretOnThePostBreakPageOrColumn"
            scenarios = @(
                "TrailingInlineFlowBreak_PlacesCaretOnThePostBreakPageOrColumn",
                "ConsecutiveTrailingInlineFlowBreaks_PlaceCaretAtTheFinalPostBreakBoundary"
            )
            focusedSourceTestsPassed = 2
            focusedSourceTestsTotal = 2
            oracle = "A trailing inline page break places the caret on the following page; a trailing inline column break places it in the next column on the current page, with non-zero caret geometry."
            consecutiveBreakCoverage = $true
            claimBoundary = "Committed FreeW Avalonia source-test and exact-oracle evidence for trailing inline flow-break caret placement; accepted local integration gates do not establish complete FreeW or visual parity."
        }
        wave197 = [ordered]@{
            status = "candidate-refuted"
            evidencePath = $wave197FreeWEvidenceNotePath
            sourceTestPath = $wave197FreeWSourceTestPath
            rawEvidencePath = $wave197FreeWRawEvidencePath
            checksumsPath = $wave197FreeWChecksumsPath
            scenarioCount = 6
            uniqueScenarioCount = 6
            focusedSourceTestsPassed = 20
            focusedSourceTestsTotal = 20
            validation = "Clean-checkout tracked raw evidence and checksums; exact unique six-scenario validation passed."
            surfaceMarginCandidate = "Rejected: the one-pixel selected-tab surface margin regressed all six rows."
            lineBoxCandidate = "Rejected: 16px line-box improved two long rows and regressed two long rows; short controls were unchanged."
            productionCandidateRetained = $false
            claimBoundary = "Committed FreeW evidence-only candidate review for the Legal Notices tab-template family; no production candidate is retained, and accepted local integration gates do not establish complete FreeW or visual parity."
        }
        wave198 = [ordered]@{
            status = "evidence-recorded"
            evidencePath = $wave198FreeWEvidenceNotePath
            sourceTestPath = $wave198FreeWSourceTestPath
            rawEvidencePath = $wave198FreeWRawEvidencePath
            targetScenarioCount = [int]$freeWWave198RawEvidence.totals.targetScenarioCount
            controlScenarioCount = [int]$freeWWave198RawEvidence.totals.controlScenarioCount
            scenarioCount = [int]$freeWWave198RawEvidence.totals.scenarioCount
            targetBeforeChangedPixels = [int]$freeWWave198RawEvidence.totals.targetBeforeChangedPixels
            targetAfterChangedPixels = [int]$freeWWave198RawEvidence.totals.targetAfterChangedPixels
            targetChangedPixelsReduction = [int]$freeWWave198RawEvidence.totals.targetChangedPixelsReduction
            controlBeforeChangedPixels = [int]$freeWWave198RawEvidence.totals.controlBeforeChangedPixels
            controlAfterChangedPixels = [int]$freeWWave198RawEvidence.totals.controlAfterChangedPixels
            controlChangedPixelsReduction = [int]$freeWWave198RawEvidence.totals.controlChangedPixelsReduction
            focusedSourceTestsPassed = 6
            focusedSourceTestsTotal = 6
            productionCandidateRetained = $true
            correction = "Shared compact dialog tab chrome preserves a one-pixel WPF trailing pane frame when route authority supplies negative right compensation."
            canonicalClassificationBoundary = "The canonical inventory remains 291 rows: 141 genuine visual mismatches, 80 passes, and 70 Avalonia extensions."
            evidenceBoundary = "Raw evidence is metadata-only: PNGs and route manifests are disposable and untracked, so the recorded metrics and manifest identities cannot independently inspect pixels."
            claimBoundary = "Committed FreeW shared tab-pane correction and metadata linkage for Table Properties and Borders/Shading; this route-local improvement does not establish complete FreeW or Word visual parity."
        }
        wave199 = [ordered]@{
            status = "candidate-refuted"; evidencePath = $wave199FreeWEvidenceNotePath; recordPath = $wave199FreeWRecordPath; checksumsPath = $wave199FreeWChecksumsPath; sourceTestPath = $wave199FreeWSourceTestPath; productionGeometryChangeRetained = $false
            retainedWpfCaptureHardening = "50 ms polling, 15 second timeout, and owned-modal close on timeout"; candidate = "Avalonia Style field-width pin rejected"
            initialChangedRatioBeforePercent = 7.6030; initialChangedRatioAfterPercent = 13.3183; populatedChangedRatioBeforePercent = 7.7021; populatedChangedRatioAfterPercent = 13.4134; validationChangedRatioBeforePercent = 7.6030; validationChangedRatioAfterPercent = 13.3183
            artifactCount = $freeWWave199ChecksumCount; independentlyRecomputedMetrics = @("changedPixels", "changedRatio", "meanChannelDelta", "p95ChannelDelta", "luminanceSimilarity", "perceptualHashDistance")
            canonicalSurfaceCount = [int]$freeWWave199Record.scope.canonicalCounts.comparisonScenarioCount; canonicalMismatchCount = [int]$freeWWave199Record.scope.canonicalCounts.genuineVisualMismatch; canonicalPassCount = [int]$freeWWave199Record.scope.canonicalCounts.passes; canonicalAvaloniaExtensionCount = [int]$freeWWave199Record.scope.canonicalCounts.avaloniaExtensions
            focusedEvidenceTestsPassed = 2; focusedEvidenceTestsTotal = 2; hostGuardTestsPassed = 3; hostGuardTestsTotal = 3
            claimBoundary = "Only WPF visual-capture hardening is retained. Independently recomputed metrics and unchanged canonical counts do not establish complete FreeW or Word visual parity."
        }
        routeCoverage = [ordered]@{
            inventoryScenarioCount = @($freeWRouteInventory.scenarios).Count
            inventoryRouteCount = Get-UniqueValueCount @($freeWRouteInventory.scenarios | ForEach-Object { $_.routeId })
            comparisonScenarioCount = $freeWComparisonRows.Count
            comparedRouteCount = Get-UniqueValueCount @($freeWComparisonRows | ForEach-Object { Get-ScenarioRouteId ([string]$_.scenarioId) })
            pairedRouteCount = $freeWPairedRouteIds.Count
            avaloniaOnlyRouteCount = $freeWAvaloniaExtensionRouteIds.Count
            stateNotApplicableRouteCount = Get-UniqueValueCount @($freeWStateNotApplicableRows | ForEach-Object { Get-ScenarioRouteId ([string]$_.scenarioId) })
        }
        artifactCoverage = [ordered]@{
            evidenceRowCount = $freeWComparisonRows.Count
            pairedComparisonRowCount = $freeWPairedComparisonRows.Count
            pairedWpfArtifactRowCount = $freeWPairedComparisonRows.Count
            pairedAvaloniaArtifactRowCount = $freeWPairedComparisonRows.Count
            avaloniaOnlyArtifactRowCount = $freeWAvaloniaExtensionRows.Count
            stateNotApplicableRowCount = $freeWStateNotApplicableRows.Count
            otherComparisonRowCount = $freeWComparisonRows.Count - $freeWPairedComparisonRows.Count - $freeWAvaloniaExtensionRows.Count - $freeWStateNotApplicableRows.Count
            artifactManifestAvailable = $false
            artifactKind = "embedded comparison-row content metrics and classifications"
        }
        shellChrome = [ordered]@{
            pairedStaticCaptureCount = [int]$freeWShellVisualEvidence.counts.pairedStaticChrome
            pairedContextualCaptureCount = [int]$freeWShellVisualEvidence.counts.pairedContextualChrome
            avaloniaContextualMissingCount = [int]$freeWShellVisualEvidence.counts.avaloniaContextualMissing
            wordOfficeChromeReferenceCount = [int]$freeWShellVisualEvidence.counts.wordOfficeChromeReferences
            wordOfficeChromeStatus = [string]$freeWShellVisualEvidence.nativeWordChrome.captureStatus
            evidenceClassification = "paired static and contextual WPF/Avalonia shell captures plus native Word ribbon references; cross-host pixels remain review-only"
        }
        pairedEvidence = [ordered]@{
            pairedScenarioCount = $freeWPairedComparisonRows.Count
            passCount = @($freeWPairedComparisonRows | Where-Object { $_.classification -eq "pass" }).Count
            mismatchCount = @($freeWPairedComparisonRows | Where-Object { $_.classification -eq "genuine-visual-mismatch" }).Count
            limitationCount = @($freeWPairedComparisonRows | Where-Object { $_.classification -in @("state-not-applicable", "limitation") }).Count
            avaloniaOnlyScenarioCount = $freeWAvaloniaExtensionRows.Count
            evidenceClassification = "paired WPF/Avalonia dialog comparison rows; mismatch classifications remain unresolved visual evidence, not a parity pass/fail"
        }
        physicalEvidence = [ordered]@{
            status = "available-app-owned-shell-captures"
            captureMode = "generated WPF/Avalonia dialog comparison rows plus committed full-window shell capture matrix"
            noComStatus = "not-present-in-inputs"
            limitations = @(
                "The committed shell matrix contains $($freeWShellVisualEvidence.counts.pairedStaticChrome) paired static and $($freeWShellVisualEvidence.counts.pairedContextualChrome) paired contextual WPF/Avalonia captures; no contextual Avalonia gap remains.",
                "Native Word chrome references are complete at $($freeWShellVisualEvidence.counts.wordOfficeChromeReferences) states; they are semantic review references, not raw Office-to-FreeW pixel-equivalence results.",
                "The focused table-properties.tab-cell pair improved from 12.240179% to 12.100893% changed pixels; it remains classified as a genuine visual mismatch.",
                "The six-state Page Setup family improved from 8.317758% to 8.196280% average changed pixels; all six states remain genuine visual mismatches.",
                "The seven-state Table Properties family improved from 8.259991% to 8.136310% average changed pixels; six states remain genuine visual mismatches and one remains a pass.",
                "Fresh current-source Legal Notices captures improved from 326094 to 324936 aggregate changed pixels; all four overflowing states improved, both short states were unchanged, and all six remain genuine visual mismatches.",
                "Fresh current-source Font dialog captures improved from 61396 to 58705 aggregate changed pixels across three states, a 4.383% relative reduction; WPF and Avalonia painted bounds now both measure 421 x 321, and all three states remain genuine visual mismatches.",
                "Wave189's route-local grayscale text raster reduces the three-state Font dialog aggregate from 58705 to 57620 changed pixels, a further 1.848% reduction; all three states improve and remain genuine visual mismatches.",
                "Wave190 aligns the Font dialog's Avalonia-only vertical cadence and reduces the three-state aggregate from 57620 to 44687 changed pixels, a 22.4453% relative reduction; all three states improve in both changed pixels and mean channel delta while retaining exact 421 x 321 painted bounds and genuine-mismatch classification.",
                "Wave191 aligns the Avalonia Font route's selected combo template with the WPF gradient, neutral border, and one-DIP cadence. The three-state aggregate falls from 44687 to 36053 changed pixels, a further 19.321055% reduction; all states improve, exact 421 x 321 painted bounds remain stable, and all three remain genuine mismatches.",
                "Wave192 aligns the Font route's checkbox/effect-lane registration and measured trailing margins. The three-state aggregate falls from 36053 to 34196 changed pixels, a further 5.1508% reduction; all states improve, exact 421 x 321 painted bounds remain stable, and all three remain genuine mismatches.",
                "Wave193 aligns the shared Font checkbox native frame. The three-state aggregate falls from 34196 to 32861 changed pixels, a further 3.9040% reduction; each state improves by 445 pixels, exact 421 x 321 painted bounds remain stable, and the 1px stroke probe was a no-op and was removed.",
                "Wave193's tracked Font provenance binds all three states and six host captures to dimensions, painted bounds, exact canonical comparison rows, source hashes, and external capture-manifest identities. Only the three Font rows changed; all $($freeWFontProvenance.wave193Result.nonFontRowsCompared) non-Font rows remain unchanged. The external PNGs remain uncommitted and require the capture hosts for pixel reproduction.",
                "Wave194 changes only the Avalonia Font action-button border to the WPF-style #C8C8C8 value. Aggregate changed pixels fall from $($freeWFontProvenance.wave193Result.aggregateChangedPixels) to $($freeWFontProvenance.wave194Result.aggregateChangedPixels), a delta of $($freeWFontProvenance.wave194Result.aggregateDelta) and relative improvement $(Format-CanonicalPercentage $freeWFontProvenance.wave194Result.relativeImprovement); painted bounds remain 421 x 321, and all $($freeWFontProvenance.wave194Result.nonFontRowsCompared) non-Font rows remain unchanged.",
                "Avalonia-only route/state rows are reported separately and are outside the WPF-authority pairing set.",
                [string]$freeWOfficeBaseline.limitation
            )
        }
        authoritativeMicrosoftOfficeBaseline = [ordered]@{
            product = [string]$freeWOfficeBaseline.product
            available = [bool]$freeWOfficeBaseline.available
            status = [string]$freeWOfficeBaseline.status
            artifactCount = [int]$freeWOfficeBaseline.artifactCount
            limitation = [string]$freeWOfficeBaseline.limitation
            captureMode = [string]$freeWOfficeBaseline.captureMode
        }
        claimBoundary = "Route and evidence-row coverage only; paired comparison rows and mismatch classifications do not establish Word visual parity."
    }
    $freeW = [ordered]@{
        app = "FreeW"
        commandInventory = [ordered]@{
            totalCommands = [int]$freew.summary.totalCommands
            bothProfiles = [int]$freew.summary.both
            wpfProfileOnly = [int]$freew.summary.wpfOnly
            avaloniaProfileOnly = [int]$freew.summary.avaloniaOnly
            missingWpfProfile = [int]$freew.summary.missingWpf
            missingAvaloniaProfile = [int]$freew.summary.missingAvalonia
            actionableMissingWpf = [int]$freew.summary.actionableMissingWpf
            actionableMissingAvalonia = [int]$freew.summary.actionableMissingAvalonia
            profileShapeOnly = [int]$freew.summary.profileShapeOnly
            commandIdAliases = [int]$freew.summary.commandIdAliases
            platformOnly = [int]$freew.summary.platformOnly
            deferred = [int]$freew.summary.deferred
            actionableGaps = [int]$freew.summary.actionableGaps
            classifiedRows = $true
        }
        renderedEvidence = $freeWRenderedEvidence
        nextSlice = "Wave198 records a shared tab-pane trailing-frame correction: target changed pixels fall 191369 -> 187872 and the control falls 106540 -> 104932, with all states improved. The canonical FreeW catalog remains $($freeWWave195Metrics.catalogRowCount) rows: $($freeWWave195Metrics.passCount) pass, $($freeWWave195Metrics.genuineVisualMismatchCount) genuine visual mismatches, and $($freeWWave195Metrics.avaloniaExtensionCount) Avalonia extensions. Continue with the remaining classified visual residuals, including the next native checkbox/glyph, tab-template, pagination, drawing/object, chart, table, or WordArt surface."
    }

    $freePExternalPowerPointResidual = Get-ResidualById -Residuals $freePRenderParity.Residuals -Id "external-powerpoint-baseline"
    $freePRecordingHardwareResidual = Get-ResidualById -Residuals $freePRenderParity.Residuals -Id "real-recording-hardware"
    $freePDialogArtifactCoverage = [ordered]@{
        lane = "dialog-pane"
        routeInventoryCount = [int]$freePDialogVisualEvidence.routeCount
        renderedScenarioCount = [int]$freePDialogVisualEvidence.scenarioCount
        pairedCaptureCount = [int]$freePDialogVisualEvidence.pairedCaptureCount
        wpfPngCount = Get-ManifestFileCount -Manifest $freePDialogArtifactManifest -Pattern "wpf/*.png"
        avaloniaPngCount = Get-ManifestFileCount -Manifest $freePDialogArtifactManifest -Pattern "avalonia/*.png"
        diffPngCount = Get-ManifestFileCount -Manifest $freePDialogArtifactManifest -Pattern "diff/*.png"
        pngCount = [int]$freePDialogArtifactManifest.pngCount
        fileCount = [int]$freePDialogArtifactManifest.fileCount
    }
    $freePWholeWindowArtifactCoverage = [ordered]@{
        lane = "whole-window"
        routeInventoryCount = $null
        routeInventoryAvailable = $false
        renderedScenarioCount = [int]$freePWholeWindowVisualEvidence.scenarioCount
        pairedCaptureCount = [int]$freePWholeWindowVisualEvidence.pairedCaptureCount
        wpfFullPngCount = Get-ManifestFileCount -Manifest $freePWholeWindowArtifactManifest -Pattern "wpf/full/*.png"
        avaloniaFullPngCount = Get-ManifestFileCount -Manifest $freePWholeWindowArtifactManifest -Pattern "avalonia/full/*.png"
        wpfClientPngCount = Get-ManifestFileCount -Manifest $freePWholeWindowArtifactManifest -Pattern "wpf/client/*.png"
        avaloniaClientPngCount = Get-ManifestFileCount -Manifest $freePWholeWindowArtifactManifest -Pattern "avalonia/client/*.png"
        diffPngCount = Get-ManifestFileCount -Manifest $freePWholeWindowArtifactManifest -Pattern "diff/*.png"
        fullPngCount = [int]$freePWholeWindowArtifactManifest.fullPngCount
        clientPngCount = [int]$freePWholeWindowArtifactManifest.clientPngCount
        pngCount = [int]$freePWholeWindowArtifactManifest.fullPngCount + [int]$freePWholeWindowArtifactManifest.clientPngCount + [int]$freePWholeWindowArtifactManifest.diffPngCount
        fileCount = [int]$freePWholeWindowArtifactManifest.fileCount
    }
    $freePPairedPassCount = [int]$freePDialogVisualEvidence.passCount + [int]$freePWholeWindowVisualEvidence.passCount
    $freePPairedMismatchCount = [int]$freePDialogVisualEvidence.mismatchCount + [int]$freePWholeWindowVisualEvidence.mismatchCount
    $freePPairedLimitationCount = [int]$freePDialogVisualEvidence.limitationCount + [int]$freePWholeWindowVisualEvidence.limitationCount
    $freePPairedScenarioCount = [int]$freePDialogArtifactCoverage.pairedCaptureCount + [int]$freePWholeWindowArtifactCoverage.pairedCaptureCount
    $freePRenderedEvidence = [ordered]@{
        sourceFiles = @(
            "docs/parity/freep-dialog-pane-visual-evidence/summary.json",
            "docs/parity/freep-dialog-pane-visual-evidence/artifact-manifest.json",
            "docs/parity/freep-whole-window-visual-evidence/summary.json",
            "docs/parity/freep-whole-window-visual-evidence/artifact-manifest.json",
            "docs/parity/freep-render-slideshow-media-parity-20260720.json",
            "docs/parity/freep-native-picker-human-evidence.json",
            "docs/parity/freep-powerpoint-baseline-2026-08-14.json",
            "docs/parity/freep-powerpoint-recalibration-2026-08-15.json",
            "docs/parity/freep-powerpoint-chrome-2026-08-16/README.md",
            "docs/parity/freep-powerpoint-chrome-2026-08-16/manifest.json",
            "docs/parity/freep-responsive-chrome-2026-08-16/README.md",
            "docs/parity/freep-responsive-chrome-2026-08-16/manifest.json",
            "docs/parity/freep-wave184-smartart-2026-08-23.md",
            "docs/parity/freep-wave185-bullets-autofit-20260823.md",
            "docs/parity/freep-wave186-surface3d-smartart-20260823.md",
            "docs/parity/avalonia-parity-wave187-freep-surface3d-20260823.md",
            "docs/parity/avalonia-parity-wave188-freep-smartart-text-20260823.md",
            "docs/parity/avalonia-parity-wave189-freep-smartart-text-20260823.md",
            "docs/parity/avalonia-parity-wave190-freep-smartart-text-origin-20260823.md",
            "docs/parity/avalonia-parity-wave190-freep-evidence-20260823/metrics.json",
            "docs/parity/avalonia-parity-wave191-freep-smartart-color-gate-20260823.md",
            "docs/parity/avalonia-parity-wave191-freep-evidence-20260823/metrics.json",
            "docs/parity/avalonia-parity-wave192-freep-render-residual-20260823.md",
            "docs/parity/evidence/avalonia-parity-wave192-freep-evidence-20260823/metrics.json",
            "docs/parity/avalonia-parity-wave193-freep-render-residual-20260823.md",
            "docs/parity/evidence/avalonia-parity-wave193-freep-evidence-20260823/metrics.json",
            "docs/parity/evidence/avalonia-parity-wave193-freep-evidence-20260823/references.json",
            "docs/parity/evidence/avalonia-parity-wave193-freep-evidence-20260823/images.json",
            "docs/parity/freep-wave194-deck17-slide02-topology-20260823.md",
            "docs/parity/evidence/freep-wave194-deck17-slide02-topology-20260823/topology.json",
            $wave195ProgressNotePath,
            $wave196FreePEvidenceNotePath,
            $wave196FreePMetricsPath,
            $wave196FreePImagesPath,
            $wave196FreePSourceTestPaths,
            $wave196IntegrationNotePath
        )
        routeCoverage = [ordered]@{
            laneEntries = @(
                [ordered]@{
                    lane = "dialog-pane"
                    routeInventoryCount = $freePDialogArtifactCoverage.routeInventoryCount
                    renderedScenarioCount = $freePDialogArtifactCoverage.renderedScenarioCount
                    pairedScenarioCount = $freePDialogArtifactCoverage.pairedCaptureCount
                },
                [ordered]@{
                    lane = "whole-window"
                    routeInventoryCount = $null
                    routeInventoryAvailable = $false
                    renderedScenarioCount = $freePWholeWindowArtifactCoverage.renderedScenarioCount
                    pairedScenarioCount = $freePWholeWindowArtifactCoverage.pairedCaptureCount
                }
            )
            renderedScenarioCount = $freePDialogArtifactCoverage.renderedScenarioCount + $freePWholeWindowArtifactCoverage.renderedScenarioCount
            pairedScenarioCount = $freePDialogArtifactCoverage.pairedCaptureCount + $freePWholeWindowArtifactCoverage.pairedCaptureCount
            routeCountBoundary = "Dialog route count is authoritative; whole-window source supplies scenario coverage but no separate route inventory."
        }
        artifactCoverage = [ordered]@{
            laneEntries = @($freePDialogArtifactCoverage, $freePWholeWindowArtifactCoverage)
            wpfPngCount = $freePDialogArtifactCoverage.wpfPngCount + $freePWholeWindowArtifactCoverage.wpfFullPngCount + $freePWholeWindowArtifactCoverage.wpfClientPngCount
            avaloniaPngCount = $freePDialogArtifactCoverage.avaloniaPngCount + $freePWholeWindowArtifactCoverage.avaloniaFullPngCount + $freePWholeWindowArtifactCoverage.avaloniaClientPngCount
            diffPngCount = $freePDialogArtifactCoverage.diffPngCount + $freePWholeWindowArtifactCoverage.diffPngCount
            pngCount = $freePDialogArtifactCoverage.pngCount + $freePWholeWindowArtifactCoverage.pngCount
            fileCount = $freePDialogArtifactCoverage.fileCount + $freePWholeWindowArtifactCoverage.fileCount
        }
        pairedEvidence = [ordered]@{
            pairedScenarioCount = $freePPairedScenarioCount
            passCount = $freePPairedPassCount
            mismatchCount = $freePPairedMismatchCount
            limitationCount = $freePPairedLimitationCount
            duplicateCaptureCount = [int]$freePWholeWindowVisualEvidence.duplicateCaptureCount
            evidenceClassification = "paired WPF/Avalonia app-owned dialog/pane and whole-window render evidence; pass counts are local comparison-gate results, not Microsoft Office parity"
        }
        nativeOfficeChrome = [ordered]@{
            expectedCaptureCount = [int]$freePPowerPointChrome.expectedCaptureCount
            captureStatus = [string]$freePPowerPointChrome.captureStatus
            capturedReferenceCount = [int]$freePPowerPointChrome.actualCaptureCount
            comparisonBoundary = [string]$freePPowerPointChrome.comparisonBoundary
            evidenceClassification = "guarded native PowerPoint ribbon reference lane; complete foreground capture is semantic review evidence, not raw pixel equivalence"
        }
        responsiveAppChrome = [ordered]@{
            expectedCaptureCount = [int]$freePResponsiveChrome.expectedCaptureCount
            captureStatus = [string]$freePResponsiveChrome.captureStatus
            capturedPairCount = [int]($freePResponsiveChrome.actualCaptureCount / 2)
            wpfCaptureCount = @($freePResponsiveChrome.captures | Where-Object { $_.host -eq "wpf" }).Count
            avaloniaCaptureCount = @($freePResponsiveChrome.captures | Where-Object { $_.host -eq "avalonia" }).Count
            tabCount = @($freePResponsiveChrome.mappedFreePTabs).Count
            widthCount = @($freePResponsiveChrome.widths).Count
            evidenceClassification = "complete responsive app-owned WPF/Avalonia ribbon/chrome capture matrix; paired state coverage, not a pixel-equivalence claim"
        }
        wave193Integrity = [ordered]@{
            status = "no-runtime-change-retained"
            evidencePath = "docs/parity/evidence/avalonia-parity-wave193-freep-evidence-20260823/"
            retainedRowCount = @($freePWave193Metrics.rows).Count
            retainedOfficeReferenceCount = @($freePWave193References.rows).Count
            retainedImageCount = @($freePWave193Images.PSObject.Properties).Count
            workerRunWpfAvaloniaRenderCount = [int]$freePWave193Metrics.source.rendererOutputs
            workerRunComparisonCount = [int]$freePWave193Metrics.source.comparisons
            fullRenderArtifactsRetained = $false
            claim = "Retained proof covers 53 rows, 53 Office references, and 6 target images. The 106/106 renders and 159/159 comparisons are worker-run full-render results and their non-target PNGs are not retained."
        }
        wave194Topology = [ordered]@{
            status = "no-runtime-change-retained"
            schema = [string]$freePWave194Topology.schema
            sourceRevision = [string]$freePWave194Topology.sourceRevision
            sourceCorpusPath = [string]$freePWave194Topology.sourceCorpus.path
            sourceCorpusSha256 = [string]$freePWave194Topology.sourceCorpus.sha256
            sourceCorpusHashScope = [string]$freePWave194Topology.sourceCorpus.hashScope
            title = [ordered]@{
                autoFitKind = [string]$freePWave194Topology.model.title.autoFitKind
                effectiveFontFamily = [string]$freePWave194Topology.model.title.effectiveFontFamily
                effectiveFontSizePt = [int]$freePWave194Topology.model.title.effectiveFontSizePt
            }
            body = [ordered]@{
                autoFitKind = [string]$freePWave194Topology.model.body.autoFitKind
                effectiveFontFamily = [string]$freePWave194Topology.model.body.effectiveFontFamily
                effectiveFontSizePt = [int]$freePWave194Topology.model.body.effectiveFontSizePt
                paragraphCount = [int]$freePWave194Topology.model.body.paragraphCount
            }
            retainedMetrics = [ordered]@{
                rendererOutputs = [int]$freePWave194Topology.retainedMetrics.source.rendererOutputs
                comparisons = [int]$freePWave194Topology.retainedMetrics.source.comparisons
                wpfOfficeAverage = [double]$freePWave194Topology.retainedMetrics.aggregate.wpfOfficeAverage
                avaloniaOfficeAverage = [double]$freePWave194Topology.retainedMetrics.aggregate.avaloniaOfficeAverage
                wpfAvaloniaAverage = [double]$freePWave194Topology.retainedMetrics.aggregate.wpfAvaloniaAverage
                targetAvaloniaOffice = [double]$freePWave194Topology.retainedMetrics.target.avaloniaOffice
                targetWpfAvalonia = [double]$freePWave194Topology.retainedMetrics.target.wpfAvalonia
            }
            residualClaim = "The topology evidence rules out the investigated structural, autofit, and theme-inheritance hypotheses; the remaining render residual is unresolved and is not attributed to host fonts or raster behavior."
            claimBoundary = "Pinned Office topology and retained comparison metrics only; no PowerPoint visual-parity claim is made."
        }
        wave195 = [ordered]@{
            wholeWindowScenarioCount = [int]$freePWholeWindowVisualEvidence.scenarioCount
            wholeWindowPassCount = [int]$freePWholeWindowVisualEvidence.passCount
            wholeWindowMismatchCount = [int]$freePWholeWindowVisualEvidence.mismatchCount
            combinedRenderedEvidenceCount = $freePPairedScenarioCount
            combinedRenderedEvidencePassCount = $freePPairedPassCount
            combinedRenderedEvidenceMismatchCount = $freePPairedMismatchCount
            richTextSelection = [ordered]@{
                changedPixelRatioBefore = 0.2185757
                changedPixelRatioAfter = 0.1809518682
                meanChannelDelta = 9.7919313736
                perceptualHashDistance = 11
                cropWidth = 251
                cropHeight = 74
                cropDimensions = "251x74"
            }
            correction = "Wave195 refreshes the whole-window catalog to 36 pass and 0 mismatch and the combined rendered evidence to 64/64 pass and 0 mismatch. Rich-text selection evidence uses exact 251x74 crops and improves the changed-pixel ratio from 0.2185757 to 0.1809518682."
            claimBoundary = "Local WPF/Avalonia rendered evidence only; the native Office deck17 slide02 residual remains unresolved and no PowerPoint visual-parity claim is made."
        }
        wave196 = [ordered]@{
            status = "evidence-recorded"
            evidencePath = $wave196FreePEvidenceNotePath
            metricsPath = $wave196FreePMetricsPath
            imageManifestPath = $wave196FreePImagesPath
            sourceTestPaths = $wave196FreePSourceTestPaths
            target = "17-bullets-autofit / slide-02"
            sourceRevision = [string]$freePWave196Metrics.baseRevision
            correctionScope = [string]$freePWave196Metrics.acceptedCorrection.scope
            fallbackFont = [string]$freePWave196Metrics.acceptedCorrection.fallbackFont
            textHintingModeBefore = [string]$freePWave196Metrics.acceptedCorrection.textHintingModeBefore
            textHintingModeAfter = [string]$freePWave196Metrics.acceptedCorrection.textHintingModeAfter
            avaloniaOfficeBefore = [double]$freePWave196Metrics.measurements.slide02Target.before.avaloniaOffice
            avaloniaOfficeAfter = [double]$freePWave196Metrics.measurements.slide02Target.after.avaloniaOffice
            avaloniaOfficeDelta = [double]$freePWave196Metrics.measurements.slide02Target.delta.avaloniaOffice
            wpfAvaloniaBefore = [double]$freePWave196Metrics.measurements.slide02Target.before.wpfAvalonia
            wpfAvaloniaAfter = [double]$freePWave196Metrics.measurements.slide02Target.after.wpfAvalonia
            wpfAvaloniaDelta = [double]$freePWave196Metrics.measurements.slide02Target.delta.wpfAvalonia
            controlUnchanged = ([double]$freePWave196Metrics.measurements.slide01Control.beforeAfterAvalonia -eq 0)
            imageHashCount = @($freePWave196Images.PSObject.Properties).Count
            claimBoundary = "Committed FreeP deck17 slide02 renderer evidence for a scoped Aptos fallback hinting correction; accepted local integration gates do not establish complete FreeP or visual parity."
        }
        wave197 = [ordered]@{
            status = "candidate-refuted"
            leadingEvidencePath = $wave197FreePLeadingEvidenceNotePath
            leadingMetricsPath = $wave197FreePLeadingMetricsPath
            baselineAlignmentEvidencePath = $wave197FreePBaselineEvidenceNotePath
            baselineAlignmentMetricsPath = $wave197FreePBaselineMetricsPath
            baselineAlignmentImageManifestPath = $wave197FreePBaselineImagesPath
            sourceTestPaths = $wave197FreePSourceTestPaths
            focusedSourceTestsPassed = 4
            focusedSourceTestsTotal = 4
            productionCandidateRetained = $false
            leadingCandidate = "Rejected: scaled leading preserved all 16 body ink-band starts but would accumulate 30.24 DIP of final baseline drift."
            baselineAlignmentCandidate = "Rejected: target worsened from 2.4820% to 2.5116% Avalonia/Office and from 2.8755% to 2.9053% WPF/Avalonia; the slide01 control remained byte-identical."
            trackedImageBytesAndHashes = "Leading-candidate image bytes/hashes and its recorded source commit are verified. The baseline-alignment manifest records four missing untracked candidate images and makes no current byte-integrity claim."
            recordedSourceCommit = [string]$freePWave197LeadingMetrics.sourceProvenance.sourceRevision
            residualBoundary = "The remaining evidence is an unresolved text-raster residual, not a fallback-font diagnosis."
            claimBoundary = "Committed FreeP rejection evidence for leading and baseline-alignment candidates; no production candidate is retained. Leading-candidate bytes and source metadata are verified, while the baseline-alignment images are explicitly recorded as missing and generation linkage is not independently proven."
        }
        wave198 = [ordered]@{
            status = [string]$freePWave198Metrics.status
            evidencePath = $wave198FreePEvidenceNotePath
            metricsPath = $wave198FreePMetricsPath
            sourceTestPath = $wave198FreePSourceTestPath
            target = "17-bullets-autofit / slide-02"
            focusedSourceTestsPassed = 2
            focusedSourceTestsTotal = 2
            productionCandidateRetained = $false
            candidate = [string]$freePWave198Metrics.candidate.description
            acceptedTextRenderingMode = [string]$freePWave198Metrics.candidate.acceptedTextRenderingMode
            candidateTextRenderingMode = [string]$freePWave198Metrics.candidate.candidateTextRenderingMode
            avaloniaOfficeBefore = [double]$freePWave198Metrics.measurements.slide02Target.acceptedAvaloniaOffice
            avaloniaOfficeAfter = [double]$freePWave198Metrics.measurements.slide02Target.candidateAvaloniaOffice
            avaloniaOfficeDeltaPercentagePoints = [double]$freePWave198Metrics.measurements.slide02Target.avaloniaOfficeDeltaPercentagePoints
            wpfAvaloniaBefore = [double]$freePWave198Metrics.measurements.slide02Target.acceptedWpfAvalonia
            wpfAvaloniaAfter = [double]$freePWave198Metrics.measurements.slide02Target.candidateWpfAvalonia
            wpfAvaloniaDeltaPercentagePoints = [double]$freePWave198Metrics.measurements.slide02Target.wpfAvaloniaDeltaPercentagePoints
            slide01ControlUnchanged = [bool]$freePWave198Metrics.measurements.slide01Control.candidateMatchesAcceptedPng
            generationLinkage = [string]$freePWave198Metrics.sourceProvenance.generationLinkage
            correctionDisposition = "Rejected: SubpixelAntialias improves Avalonia/Office by 0.0237 percentage points but worsens WPF/Avalonia by 0.0092 percentage points; preserve Antialias."
            claimBoundary = "Committed FreeP renderer evidence for a rejected fixed-size Aptos subpixel-antialias candidate; provenance is not independently proven and no production candidate is retained."
        }
        wave199 = [ordered]@{
            status = [string]$freePWave199Metrics.status; evidencePath = $wave199FreePEvidenceNotePath; metricsPath = $wave199FreePMetricsPath; referencesPath = $wave199FreePReferencesPath; imagesPath = $wave199FreePImagesPath; broaderControlsPath = $wave199FreePBroaderControlsPath; sourceTestPath = $wave199FreePSourceTestPath
            target = "17-bullets-autofit / slides 01-02"; productionRendererChangeRetained = $false; candidateDisposition = "Aptos substitute candidates rejected"; candidateCount = @($freePWave199Metrics.candidateMeasurements).Count; candidatePngCount = @($freePWave199Images.PSObject.Properties).Count; independentlyRecomputedPixelMetricCount = 30
            imageIntegrity = [string]$freePWave199Metrics.imageIntegrity.status; exactIndexHashBinding = $true; generationLinkage = [string]$freePWave199Metrics.sourceProvenance.generationLinkage; nativeAptosProvenance = "not independently proven"; broaderCorpusStatus = [string]$freePWave199Metrics.broaderCorpusControl.status; officeReferencesInventoried = @($freePWave199BroaderControls.controls).Count; retainedBroaderCandidateRenderCount = [int]$freePWave199BroaderControls.retainedCandidateRenderCount
            focusedSourceTestsPassed = 10; focusedSourceTestsTotal = 10
            claimBoundary = "No production renderer change is retained. Exact index/hash binding supports recomputation, but candidate/native-Aptos provenance and broader-corpus claims are explicitly not proven; 18 Office references are inventory-only."
        }
        physicalEvidence = [ordered]@{
            status = "available-app-and-native-office-chrome"
            captureMode = "visible app-owned render targets with scenario-isolated processes plus guarded native PowerPoint ribbon capture"
            noComStatus = [string]$freePOfficeBaseline.captureMode
            limitations = @(
                [string]$freePOfficeBaseline.limitation,
                "Native PowerPoint ribbon capture status is '$($freePPowerPointChrome.captureStatus)' with $($freePPowerPointChrome.actualCaptureCount)/$($freePPowerPointChrome.expectedCaptureCount) mapped tab/width references.",
                "Responsive FreeP app chrome is '$($freePResponsiveChrome.captureStatus)' with $($freePResponsiveChrome.actualCaptureCount)/$($freePResponsiveChrome.expectedCaptureCount) WPF/Avalonia tab/width captures ($(@($freePResponsiveChrome.mappedFreePTabs).Count) tabs at $(@($freePResponsiveChrome.widths).Count) widths).",
                "The directly measured Wave187 authored-camera Surface3D target improved from 2.7438% to 2.7032% WPF/Office, 2.6220% to 2.5815% Avalonia/Office, and 1.0805% to 1.0804% WPF/Avalonia; deck26 and four ordinary-chart controls were preserved. Diagnostic reconstructed corpus estimates remain outside the canonical recalibration summary.",
                "The directly measured Wave188 imported IncreasingCircleProcess slide-09 target improved from 1.6516% to 0.9662% WPF/Office and from 1.6609% to 1.6009% WPF/Avalonia; Avalonia/Office remained 1.6879%. The WPF raster compensation is gated by an explicit imported-cache semantic flag and exact 12-shape topology, with ordinary authored phase-label controls excluded.",
                "Wave189 consumes the same semantic IncreasingCircle cache flag on Avalonia and improves slide-09 Avalonia/Office from 1.6879% to 1.5440% and WPF/Avalonia from 1.6009% to 1.3657%; WPF/Office remains 0.9662%, and neighboring SmartArt plus Surface3D controls are unchanged.",
                "Wave190 introduced an exact source-signature-gated Avalonia text-origin correction, but its white text-color discriminator did not match the imported cache's resolved black source. Current-source measurement before Wave191 therefore remained 1.6879% Avalonia/Office and 1.6009% WPF/Avalonia rather than activating the intended correction.",
                "Wave191 corrects that semantic color gate to the source-proven black run. Slide-09 Avalonia/Office improves from 1.6879% to 0.8675% and WPF/Avalonia from 1.6009% to 0.8540%; WPF/Office remains 0.9662%. Across all 53 slides, Avalonia/Office improves from 1.0117% to 0.9962% average and WPF/Avalonia from 0.6238% to 0.6097%, with measured controls unchanged.",
                "Wave192 retains no new runtime rendering change. Fresh 27-deck/53-slide evidence confirms the Wave191 aggregate, rejects an IncreasingCircle text-policy probe that regressed Avalonia/Office from 0.8675% to 0.8775%, and records that a general Surface3D correction needs a new Office-authored topology rather than a fixture-specific overlay.",
                "Wave193 retains no runtime rendering change. The worker run produced $($freePWave193Metrics.source.rendererOutputs)/$($freePWave193Metrics.source.rendererOutputs) current-source renders and $($freePWave193Metrics.source.comparisons)/$($freePWave193Metrics.source.comparisons) comparisons; retained integrity proof is limited to $(@($freePWave193Metrics.rows).Count) rows, $(@($freePWave193References.rows).Count) Office references, and $(@($freePWave193Images.PSObject.Properties).Count) target images.",
                [string]$freePRecordingHardwareResidual.Description,
                [string]$freePRecordingHardwareResidual.ArtifactStatus,
                [string]$freePNativePickerEvidence.reason
            )
        }
        authoritativeMicrosoftOfficeBaseline = [ordered]@{
            product = [string]$freePOfficeBaseline.product
            available = [bool]$freePOfficeBaseline.available
            status = [string]$freePOfficeBaseline.status
            artifactCount = [int]$freePOfficeBaseline.artifactCount
            limitation = [string]$freePOfficeBaseline.limitation
            captureMode = [string]$freePOfficeBaseline.captureMode
            referenceReadyDecks = [int]$freePOfficeBaseline.comparison.referenceReadyDecks
            missingReferenceDecks = [int]$freePOfficeBaseline.comparison.missingReferenceDecks
            currentSourceRevision = [string]$freePWave193Metrics.source.baseRevision
            wpfAverageMeanPercent = [double]$freePWave193Metrics.aggregate.wpfOfficeAverage
            wpfMaximumMeanPercent = [double]$freePWave193Metrics.aggregate.wpfOfficeMaximum
            avaloniaAverageMeanPercent = [double]$freePWave193Metrics.aggregate.avaloniaOfficeAverage
            avaloniaMaximumMeanPercent = [double]$freePWave193Metrics.aggregate.avaloniaOfficeMaximum
            rendererPairAverageMeanPercent = [double]$freePWave193Metrics.aggregate.wpfAvaloniaAverage
            rendererPairMaximumMeanPercent = [double]$freePWave193Metrics.aggregate.wpfAvaloniaMaximum
        }
        claimBoundary = "Route/scenario coverage, committed PNG manifests, and local WPF/Avalonia comparison results only; no PowerPoint visual-parity claim is made."
    }

    $freePNextSlice = "Wave198 rejects SubpixelAntialias for the deck17 slide02 residual: Avalonia/Office improves by 0.0237 percentage points but WPF/Avalonia worsens by 0.0092 points, and the slide01 control is unchanged. No production candidate is retained; provenance is not independently proven. Continue with supported native Aptos/resource or independently measured host glyph-raster evidence."

    $freeP = [ordered]@{
        app = "FreeP"
        commandInventory = [ordered]@{
            totalCommands = [int]$freep.summary.totalCommands
            bothProfiles = [int]$freep.summary.both
            wpfOnly = [int]$freep.summary.wpfOnly
            avaloniaOnly = [int]$freep.summary.avaloniaOnly
            actionableMissingWpf = [int]$freep.summary.actionableMissingWpf
            actionableMissingAvalonia = [int]$freep.summary.actionableMissingAvalonia
            avaloniaGaps = [int]$freep.summary.avaloniaGaps
            knownDeferred = [int]$freep.summary.knownDeferred
            platformOnly = [int]$freep.summary.platformOnly
            commandIdAliases = [int]$freep.summary.commandIdAliases
            workflowEvidenceRows = [int](Get-JsonPropertyValue $freep.summary "workflowEvidenceRows")
        }
        renderedEvidence = $freePRenderedEvidence
        nextSlice = $freePNextSlice
    }

    $wave194IntegrationGateEvidence = [ordered]@{
        testedSourceCommit = $wave194TestedSourceCommit
        acceptanceRefreshNote = $wave194AcceptanceRefreshNote
        reintegration = "The current integration branch is anchored to tested source commit ${wave194TestedSourceCommit}; the acceptance refresh records only evidence from that tested source and does not claim that the documentation commit itself was rebuilt."
        focusedTests = "At tested source commit ${wave194TestedSourceCommit}: FreeX Avalonia Wave194 9/9; FreeX Presentation Wave194 1/1; FreeX Core.IO Wave194 plus five foreground-capture guards 8/8; FreeW Avalonia 2,175/2,175; FreeW host 1,835/1,835; FreeW Presentation 2,892/2,892; FreeW Ribbon definitions 62/62; FreeP Avalonia 724/724; FreeP host 2,418/2,418; FreeP Presentation 5,496/5,496; FreeP Ribbon definitions 34/34; FreeP responsive evidence 64/64; FreeP localization focused 1/1; FreeP resources 14/14; FreeP Hide Slide assertions 2/2; FreeP ChartRenderPlanner 264/264."
        initialReintegrationPreflight = "The current acceptance refresh uses the supplied repository-preflight result and the exact tested-source boundary; no additional source paths are allowlisted by this documentation-only change."
        initialIndependentReview = "Recorded: the initial independent review found two P2 findings: FreeX crop/readiness/transition and physical click geometry were duplicated instead of consuming one contract; FreeP topology evidence did not pin the complete source PPTX and initially over-attributed the residual."
        reviewRemediation = "FreeX now uses one authoritative mixed-type geometry contract with mutation coverage and reachable-source provenance; FreeP topology schema v3 pins the complete PPTX SHA-256 and describes the remaining residual as unresolved; the color-geometry guard remediation remains retained in the tested source."
        independentReviewStatus = "passed"
        independentReview = "Passed: an independent final cross-app acceptance review of tested source commit ${wave194TestedSourceCommit} completed in an isolated worktree at integration head ${wave194ReviewedIntegrationHead}; no findings. This review preserves the tested-source boundary, counts, timings, and visual claim boundaries."
        repositoryPreflight = "Passed at tested source commit ${wave194TestedSourceCommit}: powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/Test-RepositoryPreflight.ps1 with isolated SDK C:\Users\quinn\.dotnet-codex-10.0.400 and Git Bash first on PATH exited 0; 294 JSON, 310 XML-backed, 127 PowerShell scripts, 11 GitHub workflows, 12 test gates/48 assigned projects, 13,996 conflict-marker files checked, and all generated docs/evidence current; elapsed 00:01:55.8304515."
        fullReleaseBuildMsBuildElapsed = $wave194FullReleaseBuildMsBuildElapsed
        fullReleaseBuildWrapperElapsed = $wave194FullReleaseBuildWrapperElapsed
        fullReleaseBuild = "Passed at tested source commit ${wave194TestedSourceCommit}: dotnet build FreeX.slnx --configuration Release -m:1 passed with 0 warnings and 0 errors; MSBuild-retained Time Elapsed $wave194FullReleaseBuildMsBuildElapsed; wrapper stopwatch $wave194FullReleaseBuildWrapperElapsed."
        defaultNonUiTestLaneWrapperElapsed = $wave194DefaultLaneWrapperElapsed
        defaultNonUiTestLaneTrxTimestampSpan = $wave194DefaultLaneTrxTimestampSpan
        defaultNonUiTestLaneTrxDuration = $wave194DefaultLaneTrxDuration
        defaultNonUiTestLane = "Passed at tested source commit ${wave194TestedSourceCommit}: final default non-UI lane produced 31 unique TRXs and matching console aggregation: 43,548 passed, 134 intentional skips, 0 failed, 43,682 total; wrapper stopwatch $wave194DefaultLaneWrapperElapsed; independently parsed 31-TRX timestamp span $wave194DefaultLaneTrxTimestampSpan; duration $wave194DefaultLaneTrxDuration."
        initialDefaultLane = "Earlier default-lane remediation history is retained in the Wave194 report; the current rerun is the authoritative 43,548 passed, 134 intentional skips, 0 failed, 43,682 total result."
        sliceAccounting = "582 cumulative app slices (194 per app) remain the processed Wave194 accounting; later wave feature commits are included in the tested source and do not add Wave194 slices."
        sourceTestRemediation = "The current source is accepted only with the focused and full-lane evidence recorded above; generated inventory and visual manifests remain the authority for coverage and comparison counts."
        workerVerification = "Current focused evidence is recorded for FreeW and FreeP above; FreeX physical and generated metrics remain retained below. Functional/source evidence and visual comparison evidence are intentionally separate."
    }
    $wave195IntegrationGateEvidence = [ordered]@{
        status = "accepted-local-gates"
        testedSourceCommit = $wave195TestedSourceCommit
        acceptanceRefreshNote = $wave195AcceptanceRefreshNote
        pendingIntegrationGates = @()
        sliceAccounting = "Wave 195 is three app slices, one each for FreeX, FreeW, and FreeP; cumulative accounting is 585 app slices (195 per app)."
        currentEvidence = "Wave195 evidence is recorded from committed app-specific artifacts and remains separate from the parent-run integration gates."
        localGatePolicy = "Per AGENTS.md, repository preflight and the full Release build are the local branch gates. The manifest-driven integration suite and UI/render/release-only gates are delegated to GitHub after main is pushed."
        delegatedGitHubGates = @("manifest-driven-integration-suite", "ui-render-release-workflow")
        delegatedGitHubGateStatus = "not-run-locally"
        gateBoundary = "Wave195 local acceptance records only the supplied exact-head repository preflight and full Release build. Delegated GitHub gates are not claimed as locally run and remain for GitHub after main is pushed."
        repositoryPreflight = "Passed at tested source commit ${wave195TestedSourceCommit}: powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/Test-RepositoryPreflight.ps1 exited 0; all repository preflight checks passed."
        fullReleaseBuild = "Passed at tested source commit ${wave195TestedSourceCommit}: dotnet build FreeX.slnx --configuration Release passed with 0 warnings and 0 errors; elapsed $wave195FullReleaseBuildElapsed."
        fullReleaseBuildElapsed = $wave195FullReleaseBuildElapsed
        focusedTests = "Passed at tested source commit ${wave195TestedSourceCommit}: FreeX Wave195 physical 2/2 workflows with 75 artifacts, 58 PNGs, and 2 reload witnesses; FreeW canonical 291 rows with 80 pass, 141 genuine visual mismatches, and 70 Avalonia extensions; FreeP whole-window 36/36 and combined 64/64 with zero local-threshold mismatch."
        independentReviewStatus = "passed"
        independentReview = "Passed: the exact tested/reviewed source commit ${wave195TestedSourceCommit} is the acceptance evidence boundary; no delegated GitHub gate is represented as locally run."
        historicalWave194Acceptance = $wave194IntegrationGateEvidence
    }
    $wave196IntegrationGateEvidence = [ordered]@{
        status = "accepted-local-gates"
        acceptanceStatus = "accepted-local-gates"
        testedSourceCommit = $wave196TestedSourceCommit
        acceptanceRefreshNote = $wave196AcceptanceRefreshNote
        pendingIntegrationGates = @()
        acceptedLocalGates = @("repository-preflight", "full-release-build")
        acceptanceRefreshAllowedPaths = $wave196AcceptanceRefreshAllowedPaths
        sliceAccounting = "Wave 196 is three app slices, one each for FreeX, FreeW, and FreeP; cumulative accounting is 588 app slices (196 per app)."
        currentEvidence = "Wave196 evidence is recorded from committed app-specific notes, source tests, and the FreeP image/metrics bundle; local repository preflight and full Release build acceptance is recorded separately below."
        localGatePolicy = "Per AGENTS.md, repository preflight and the full Release build are the local branch gates. The manifest-driven integration suite and UI/render/release-only gates are delegated to GitHub after main is pushed."
        delegatedGitHubGates = @("manifest-driven-integration-suite", "ui-render-release-workflow")
        delegatedGitHubGateStatus = "not-run-locally"
        gateBoundary = "Wave196 local acceptance records only the exact tested source commit ${wave196TestedSourceCommit} and the six allowlisted acceptance/report paths; delegated GitHub gates are not claimed as locally run and full Avalonia/WPF parity is not claimed."
        repositoryPreflight = "Passed at tested source commit ${wave196TestedSourceCommit}: tools/Test-RepositoryPreflight.ps1 repository preflight passed in Mode All with exit code 0."
        fullReleaseBuild = "Passed at tested source commit ${wave196TestedSourceCommit}: dotnet build FreeX.slnx --configuration Release passed with 0 warnings and 0 errors; elapsed ${wave196FullReleaseBuildElapsed}."
        fullReleaseBuildElapsed = $wave196FullReleaseBuildElapsed
        focusedTests = "Passed at tested source commit ${wave196TestedSourceCommit}: FreeX focused 22/22; FreeW focused 2/2; FreeP renderer/evidence 10/10 and resolved model 1/1."
        independentReviewStatus = "passed"
        independentReview = "Passed: independent review found two Wave196 issues; both were fixed and retested. This acceptance refresh preserves the exact tested-source boundary and does not claim any delegated GitHub gate or full parity."
        portabilityCorrection = "Committed portability correction is included in the generator and focused dashboard test; it normalizes repository paths to forward slashes."
        historicalWave195Acceptance = $wave195IntegrationGateEvidence
    }
    $wave197IntegrationGateEvidence = [ordered]@{
        status = "accepted-local-gates"
        acceptanceStatus = "accepted-local-gates"
        testedSourceCommit = $wave197TestedSourceCommit
        acceptanceRefreshNote = $wave197AcceptanceRefreshNote
        pendingIntegrationGates = @()
        acceptedLocalGates = @("repository-preflight", "full-release-build")
        acceptanceRefreshAllowedPaths = $wave197AcceptanceRefreshAllowedPaths
        sliceAccounting = "Wave 197 is three app slices, one each for FreeX, FreeW, and FreeP; cumulative accounting is 591 app slices (197 per app)."
        currentEvidence = "Wave197 evidence is recorded from committed app-specific notes, source tests, tracked FreeW raw evidence/checksums, and tracked FreeP image/hash evidence; local repository preflight and full Release build acceptance is recorded separately below."
        localGatePolicy = "Per AGENTS.md, repository preflight and the full Release build are the local branch gates. The manifest-driven integration suite and UI/render/release-only gates are delegated to GitHub after main is pushed."
        delegatedGitHubGates = @("manifest-driven-integration-suite", "ui-render-release-workflow")
        delegatedGitHubGateStatus = "not-run-locally"
        gateBoundary = "Wave197 local acceptance records only the exact tested source commit ${wave197TestedSourceCommit} and the six allowlisted acceptance/report paths; delegated GitHub gates are not claimed as locally run and full Avalonia/WPF parity is not claimed."
        repositoryPreflight = "Passed at tested source commit ${wave197TestedSourceCommit}: tools/Test-RepositoryPreflight.ps1 repository preflight passed in Mode All with exit code 0."
        fullReleaseBuildMsBuildElapsed = $wave197FullReleaseBuildMsBuildElapsed
        fullReleaseBuildWrapperElapsed = $wave197FullReleaseBuildWrapperElapsed
        fullReleaseBuild = "Passed at tested source commit ${wave197TestedSourceCommit}: dotnet build FreeX.slnx --configuration Release passed with 0 warnings and 0 errors; MSBuild 00:07:04.93; wrapper 00:07:05.2629619."
        focusedTests = "Passed at tested source commit ${wave197TestedSourceCommit}: FreeX 16/16; FreeW 20/20; FreeP 4/4."
        independentReviewStatus = "passed"
        independentReview = "Passed: final independent review found no P1/P2 findings after all remediation. This acceptance refresh preserves the exact tested-source boundary and does not claim any delegated GitHub gate or full parity."
        portabilityCorrection = "Committed portability correction remains included in the generator and focused dashboard test; it normalizes repository paths to forward slashes."
        historicalWave196Acceptance = $wave196IntegrationGateEvidence
    }
    $wave198IntegrationGateEvidence = [ordered]@{
        status = "accepted-local-gates"
        acceptanceStatus = "accepted-local-gates"
        testedSourceCommit = $wave198TestedSourceCommit
        acceptanceRefreshNote = $wave198AcceptanceRefreshNote
        pendingIntegrationGates = @()
        acceptedLocalGates = @("repository-preflight", "full-release-build")
        acceptanceRefreshAllowedPaths = $wave198AcceptanceRefreshAllowedPaths
        sliceAccounting = "Wave 198 is three app slices, one each for FreeX, FreeW, and FreeP; cumulative accounting is 594 app slices (198 per app)."
        currentEvidence = "Wave198 evidence is recorded from committed FreeX physical/source-test evidence, FreeW shared tab-pane metadata and source tests, and FreeP rejected-candidate metrics/source tests; local repository preflight and full Release build acceptance is recorded separately below."
        localGatePolicy = "Per AGENTS.md, repository preflight and the full Release build are the local branch gates. The manifest-driven integration suite and UI/render/release-only gates are delegated to GitHub after main is pushed."
        delegatedGitHubGates = @("manifest-driven-integration-suite", "ui-render-release-workflow")
        delegatedGitHubGateStatus = "not-run-locally"
        gateBoundary = "Wave198 local acceptance records only the exact tested source commit ${wave198TestedSourceCommit} and the six allowlisted acceptance/report paths; delegated GitHub gates are not claimed as locally run and full Avalonia/WPF parity is not claimed."
        repositoryPreflight = "Passed at tested source commit ${wave198TestedSourceCommit}: tools/Test-RepositoryPreflight.ps1 repository preflight passed in Mode All with exit code 0; elapsed 00:06:49.7701327."
        fullReleaseBuildMsBuildElapsed = "00:09:30.47"
        fullReleaseBuildWrapperElapsed = "00:09:30.8983681"
        fullReleaseBuild = "Passed at tested source commit ${wave198TestedSourceCommit}: dotnet build FreeX.slnx --configuration Release passed with 0 warnings and 0 errors; MSBuild 00:09:30.47; wrapper 00:09:30.8983681."
        focusedTests = "Passed at tested source commit ${wave198TestedSourceCommit}: FreeX Wave198 3/3 (combined Wave198/Wave197 command 5/5); shared DialogTabChromeParityTests 3/3; FreeW target suite 32/32 plus FontDialog/Wave198 review suite 6/6; FreeP Wave198 evidence 2/2."
        independentReviewStatus = "passed-with-remediation"
        independentReview = "Independent Wave198 review found no P1 findings. Four P2 evidence-quality findings were addressed in this acceptance record: FreeX checksums are recomputed by the focused source test, the current-source claim is narrowed, automatic combo-close focus is recorded as not measured, and a Font Dialog regression covers the shared negative-right tab-pane caller while pixel evidence remains bounded to the captured Table Properties/Borders and Shading states."
        portabilityCorrection = "Committed portability correction remains included in the generator and focused dashboard test; it normalizes repository paths to forward slashes."
        historicalWave197Acceptance = $wave197IntegrationGateEvidence
    }
    $wave199IntegrationGateEvidence = [ordered]@{
        status = "accepted-local-gates"; acceptanceStatus = "accepted-local-gates"; testedSourceCommit = $wave199TestedSourceCommit; finalPreDashboardIntegrationSourceCommit = $wave199PreDashboardIntegrationCommit; acceptanceRefreshNote = $wave199AcceptanceRefreshNote
        pendingIntegrationGates = @(); acceptedLocalGates = @("repository-preflight", "full-release-build"); acceptanceRefreshAllowedPaths = $wave199AcceptanceRefreshAllowedPaths
        sliceAccounting = "Wave 199 is three app slices, one each for FreeX, FreeW, and FreeP; cumulative accounting is 597 app slices (199 per app)."
        currentEvidence = "Wave199 evidence is recorded from committed FreeX rejected physical/source-test evidence, FreeW checksum-covered Style-dialog comparisons and capture hardening, and FreeP rejected Aptos-substitute raster evidence. The production/integration Release build passed at ${wave199TestedSourceCommit}; generated-evidence-only FreeW fingerprint refreshes then produced final pre-dashboard integration source ${wave199PreDashboardIntegrationCommit}, where repository preflight passed."
        localGatePolicy = "Per AGENTS.md, repository preflight and the full Release build are the local branch gates. The manifest-driven integration suite and UI/render/release-only gates are delegated to GitHub after main is pushed."
        delegatedGitHubGates = @("manifest-driven-integration-suite", "ui-render-release-workflow"); delegatedGitHubGateStatus = "not-run-locally"
        gateBoundary = "Wave199 local acceptance records the production/integration Release build at exact source ${wave199TestedSourceCommit}, the repository preflight and final pre-dashboard integration boundary at ${wave199PreDashboardIntegrationCommit}, and only the six allowlisted acceptance/report paths after that boundary. The intervening changes are generated-evidence-only FreeW fingerprint refreshes; delegated GitHub gates are not claimed as run and full Avalonia/WPF parity is not claimed."
        repositoryPreflight = [ordered]@{ status = "passed"; sourceCommit = $wave199PreDashboardIntegrationCommit; command = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/Test-RepositoryPreflight.ps1"; result = "Passed Mode All: portability checked 17,470 tracked paths; conflict-marker scan checked 14,175 text files; generated docs and all remaining checks passed." }
        fullReleaseBuild = [ordered]@{ status = "passed"; sourceCommit = $wave199TestedSourceCommit; command = "dotnet build FreeX.slnx --configuration Release --no-restore --disable-build-servers -p:UseSharedCompilation=false -p:NodeReuse=false /nr:false -m:1"; result = "Passed with 0 warnings and 0 errors; elapsed 00:33:04.79."; elapsed = "00:33:04.79" }
        focusedTests = "Passed evidence supplied at tested source commit ${wave199TestedSourceCommit}: FreeX 8/8; FreeW evidence 2/2 plus host guards 3/3; FreeP 10/10."
        independentReviewStatus = "passed"; independentReview = "Passed: final independent review found no P1/P2 findings. This acceptance does not claim delegated GitHub gates or full parity."
        portabilityCorrection = "The existing portability correction remains included in the generator and focused dashboard test; it normalizes repository paths to forward slashes."
        historicalWave198Acceptance = $wave198IntegrationGateEvidence
    }

    $dashboard = [ordered]@{
        schema = "freex.parity.cross-app-dashboard.v3"
        wave = 199
        cumulativeAppSlices = 597
        cumulativeAppSlicesStatus = "accepted-local-gates"
        integrationGateStatus = "accepted-local-gates"
        pendingIntegrationGates = $wave199IntegrationGateEvidence.pendingIntegrationGates
        integrationGateEvidence = $wave199IntegrationGateEvidence
        scopeBoundary = "Wave199 is three app slices, cumulative 597 (199 per app). FreeX retains a failed physical focus/save candidate, FreeW retains 141 genuine visual mismatches and 70 Avalonia extensions, and FreeP retains an unresolved text-raster residual with no accepted production candidate. These metrics do not prove complete visual parity, workflow completeness, or pixel-level equivalence, and the overall 100% parity goal remains incomplete."
        sources = @(
            "docs/parity/command-inventory.json",
            "docs/parity/functional-parity.json",
            "docs/parity/functional-parity-classification.json",
            "docs/parity/dialog-parity-inventory.json",
            "docs/parity/dialog-visual-evidence-summary.json",
            "docs/parity/dialog-visual-assets/wpf-capture/manifest.json",
            "docs/parity/dialog-visual-assets/avalonia-capture/manifest.json",
            "docs/parity/freex-excel-foreground-capture-2026-08-16.md",
            "docs/parity/freex-excel-chrome-comparison.md",
            "tools/screenshots/screenshot_manifest.json",
            "tools/screenshots_avalonia_ribbon/screenshot_manifest.json",
                    "docs/parity/freex-excel-com-baseline-2026-08-14/manifest.json",
                    "docs/parity/freex-avalonia-grid-corpus-2026-08-16/manifest.json",
                    "docs/parity/avalonia-parity-wave183-freex-namebox-overlay-20260823.md",
                    "docs/parity/avalonia-parity-wave184-freex-autofilter-20260823.md",
                    "docs/parity/avalonia-parity-wave185-freex-autofilter-sort-20260823.md",
                    "docs/parity/avalonia-parity-wave186-freex-autofilter-text-20260823.md",
                    "docs/parity/avalonia-parity-wave187-freex-autofilter-numeric-20260823.md",
                    "docs/parity/avalonia-parity-wave188-freex-autofilter-numeric-20260823.md",
                    "docs/parity/avalonia-parity-wave189-freex-autofilter-date-20260823.md",
                    "docs/parity/avalonia-parity-wave190-freex-autofilter-date-20260823.md",
                    "docs/parity/evidence/wave190-freex-autofilter-date-20260823/manifest.json",
                    "docs/parity/avalonia-parity-wave191-freex-autofilter-color-20260823.md",
                    "docs/parity/evidence/wave191-freex-autofilter-color-20260823/manifest.json",
                    "docs/parity/avalonia-parity-wave192-freex-autofilter-font-color-20260823.md",
                    "docs/parity/evidence/wave192-freex-autofilter-font-color-20260823/manifest.json",
                    "docs/parity/avalonia-parity-wave193-freex-autofilter-no-fill-20260823.md",
                    "docs/parity/evidence/wave193-freex-autofilter-no-fill-20260823/physical-result.json",
                    "docs/parity/evidence/wave193-freex-autofilter-no-fill-20260823/manifest.json",
                    "docs/parity/avalonia-parity-wave194-freex-autofilter-mixed-type-20260823.md",
                    "docs/parity/evidence/wave194-freex-autofilter-mixed-type-20260823/physical-result.json",
            "docs/parity/evidence/wave194-freex-autofilter-mixed-type-20260823/manifest.json",
            "docs/parity/evidence/wave195-freex-autofilter-criteria-workflows-20260828/manifest.json",
            "docs/parity/freew-command-inventory.json",
            "docs/parity/freew-dialog-harness/freew_dialog_route_inventory.json",
            "docs/parity/freew-dialog-harness/freew_dialog_visual_comparison.json",
            "docs/parity/freew-dialog-harness/freew_font_visual_provenance.json",
            "docs/parity/freew-word-baseline-2026-08-16/manifest.json",
            "docs/parity/freew-shell-visual-2026-08-16/freew_shell_visual_evidence.json",
            "docs/parity/freew-word-chrome-2026-08-16/manifest.json",
            "docs/parity/avalonia-parity-wave184-freew-table-properties-cell-20260823.md",
            "freew/docs/parity/avalonia-parity-wave185-freew-page-setup-text-raster-20260823.md",
            "freew/docs/parity/avalonia-parity-wave186-freew-table-properties-text-raster-20260823.md",
            "docs/parity/avalonia-parity-wave187-freew-legal-notices-20260823.md",
            "docs/parity/avalonia-parity-wave188-freew-font-20260823.md",
            "docs/parity/avalonia-parity-wave189-freew-font-antialias-20260823.md",
            "docs/parity/avalonia-parity-wave190-freew-visual-20260823.md",
            "docs/parity/avalonia-parity-wave191-freew-font-template-20260823.md",
            "docs/parity/avalonia-parity-wave192-freew-font-checkbox-20260823.md",
            "docs/parity/avalonia-parity-wave193-freew-font-checkbox-glyph-20260823.md",
            "docs/parity/avalonia-parity-wave194-freew-font-action-border-20260824.md",
            "docs/parity/freep-command-parity-inventory.json",
            "docs/parity/freep-dialog-pane-visual-evidence/summary.json",
            "docs/parity/freep-dialog-pane-visual-evidence/artifact-manifest.json",
            "docs/parity/freep-whole-window-visual-evidence/summary.json",
            "docs/parity/freep-whole-window-visual-evidence/artifact-manifest.json",
            "docs/parity/freep-render-slideshow-media-parity-20260720.json",
            "docs/parity/freep-native-picker-human-evidence.json",
            "docs/parity/freep-powerpoint-baseline-2026-08-14.json",
            "docs/parity/freep-powerpoint-recalibration-2026-08-15.json",
            "docs/parity/freep-powerpoint-chrome-2026-08-16/README.md",
            "docs/parity/freep-powerpoint-chrome-2026-08-16/manifest.json",
            "docs/parity/freep-responsive-chrome-2026-08-16/README.md",
            "docs/parity/freep-responsive-chrome-2026-08-16/manifest.json",
            "docs/parity/freep-wave184-smartart-2026-08-23.md",
            "docs/parity/freep-wave185-bullets-autofit-20260823.md",
            "docs/parity/freep-wave186-surface3d-smartart-20260823.md",
            "docs/parity/avalonia-parity-wave187-freep-surface3d-20260823.md",
            "docs/parity/avalonia-parity-wave188-freep-smartart-text-20260823.md",
            "docs/parity/avalonia-parity-wave189-freep-smartart-text-20260823.md",
            "docs/parity/avalonia-parity-wave190-freep-smartart-text-origin-20260823.md",
            "docs/parity/avalonia-parity-wave190-freep-evidence-20260823/metrics.json",
            "docs/parity/avalonia-parity-wave191-freep-smartart-color-gate-20260823.md",
            "docs/parity/avalonia-parity-wave191-freep-evidence-20260823/metrics.json",
            "docs/parity/avalonia-parity-wave192-freep-render-residual-20260823.md",
            "docs/parity/evidence/avalonia-parity-wave192-freep-evidence-20260823/metrics.json",
            "docs/parity/avalonia-parity-wave193-freep-render-residual-20260823.md",
            "docs/parity/evidence/avalonia-parity-wave193-freep-evidence-20260823/metrics.json",
            "docs/parity/evidence/avalonia-parity-wave193-freep-evidence-20260823/references.json",
            "docs/parity/evidence/avalonia-parity-wave193-freep-evidence-20260823/images.json",
            "docs/parity/freep-wave194-deck17-slide02-topology-20260823.md",
            "docs/parity/evidence/freep-wave194-deck17-slide02-topology-20260823/topology.json",
            $wave195ProgressNotePath,
            $wave196FreeXEvidenceNotePath,
            $wave196FreeXSourceTestPath,
            $wave196FreeWEvidenceNotePath,
            $wave196FreeWSourceTestPath,
            $wave196FreePEvidenceNotePath,
            $wave196FreePMetricsPath,
            $wave196FreePImagesPath,
            $wave196IntegrationNotePath,
            $wave197FreeXEvidenceNotePath,
            $wave197FreeXSourceTestPath,
            $wave197FreeWEvidenceNotePath,
            $wave197FreeWSourceTestPath,
            $wave197FreeWRawEvidencePath,
            $wave197FreeWChecksumsPath,
            $wave197FreePLeadingEvidenceNotePath,
            $wave197FreePLeadingMetricsPath,
            $wave197FreePBaselineEvidenceNotePath,
            $wave197FreePBaselineMetricsPath,
            $wave197FreePBaselineImagesPath,
            $wave197IntegrationNotePath,
            $wave198FreeXReadmePath,
            $wave198FreeXEvidenceNotePath,
            $wave198FreeXChecksumsPath,
            $wave198FreeXSourceTestPath,
            $wave198FreeWEvidenceNotePath,
            $wave198FreeWSourceTestPath,
            $wave198FreeWRawEvidencePath,
            $wave198FreePEvidenceNotePath,
            $wave198FreePMetricsPath,
            $wave198FreePSourceTestPath,
            $wave198IntegrationNotePath,
            $wave199FreeXReadmePath, $wave199FreeXEvidenceNotePath, $wave199FreeXChecksumsPath, $wave199FreeXInteractionPath, $wave199FreeXPackageProofPath, $wave199FreeWEvidenceNotePath, $wave199FreeWRecordPath, $wave199FreeWChecksumsPath, $wave199FreeWSourceTestPath, $wave199FreePEvidenceNotePath, $wave199FreePMetricsPath, $wave199FreePReferencesPath, $wave199FreePImagesPath, $wave199FreePBroaderControlsPath, $wave199FreePSourceTestPath, $wave199IntegrationNotePath
        ) + $wave196FreePSourceTestPaths + $wave196PortabilityCorrectionPaths + $wave197FreePSourceTestPaths + $wave199FreeXSourceTestPaths
        apps = @($freeX, $freeW, $freeP)
    }

    $json = ($dashboard | ConvertTo-Json -Depth 12) + "`n"
    Set-Content -LiteralPath $tempJsonPath -Value $json -NoNewline -Encoding UTF8

    $freeXVisualReviewMarkdownRows = @(
        foreach ($candidate in @($freeX.dialogVisualEvidence.visualReviewCandidates)) {
            "| $(ConvertTo-ToolMarkdownCell $candidate.id) | $($candidate.triageScore) | $($candidate.logicalDimensionMatch) | $(ConvertTo-ToolMarkdownCell $(if ([string]::IsNullOrWhiteSpace([string]$candidate.dimensionMismatchBucket)) { "none" } else { $candidate.dimensionMismatchBucket })) | $(ConvertTo-ToolMarkdownCell $candidate.reviewStatus) |"
        }
    )
    $wave198History = $dashboard.integrationGateEvidence.historicalWave198Acceptance
    $wave197History = $wave198History.historicalWave197Acceptance
    $wave196History = $wave197History.historicalWave196Acceptance
    $wave195History = $wave196History.historicalWave195Acceptance
    $wave194History = $wave195History.historicalWave194Acceptance

    $md = @(
        "# Avalonia/WPF Cross-App Parity Dashboard",
        "",
        'Generated by `tools/Generate-CrossAppParityDashboard.ps1` from existing generated parity JSON. Do not edit by hand.',
        "",
        "> Generated counts prove command/profile routing, route and artifact coverage, screenshot manifest coverage, and DPI-normalized size comparability only. They do not prove visual parity, workflow completeness, or pixel-level equivalence. High-delta paired screenshot candidates, physical/no-COM limitations, and authoritative Microsoft Office baseline availability remain explicitly separate from coverage metrics.",
        "",
        "> Wave199 records three app slices and cumulative **597** app slices (199 per app). Local integration gates are **accepted**: the full Release build passed at production/integration source ``$($dashboard.integrationGateEvidence.testedSourceCommit)``, and repository preflight passed at final pre-dashboard integration source ``$($dashboard.integrationGateEvidence.finalPreDashboardIntegrationSourceCommit)`` after generated-evidence-only FreeW fingerprint refreshes. Delegated GitHub gates remain not-run-locally and no full-parity claim is recorded. Wave198 remains historical acceptance context with its complete nested Wave197 history. Wave197 remains historical acceptance context. Wave196 remains historical acceptance context.",
        "",
        "## Summary",
        "",
        "| App | Primary evidence | Current generated state | Next slice |",
        "|---|---|---|---|",
        "| FreeX | Functional matrix, classifier, dialog inventory, dialog visual evidence, command surface | Wave199 retained no production change: automatic worksheet focus failed physical Linux and stayed A1, explicit reselect worked, Calibri/save-clean=false failed persistence; 15 canonical evidence files and 8/8 focused tests. These are coverage/triage metrics, not a visual-parity claim. | $($freeX.nextSlice) |",
        "| FreeW | Generated command inventory plus dialog rendered evidence | Wave199 rejected the Style width candidate and retained only WPF capture hardening; 32 artifacts, canonical 291/141/80/70 counts, 2/2 evidence tests, and 3/3 host guards. | $($freeW.nextSlice) |",
        "| FreeP | Generated command inventory plus dialog/whole-window rendered evidence | Wave199 retained no renderer change: Aptos substitutes rejected; 12 candidate PNGs, 30 recomputed metrics, exact index/hash binding, 18 Office references inventoried only, and 10/10 focused tests. Provenance and broader-corpus claims are not proven. | $($freeP.nextSlice) |",
        "",
        "## Rendered Evidence Summary",
        "",
        "Route inventory, rendered/comparison rows, and committed PNG/file artifacts are separate measures. Office baseline availability is an artifact-availability statement, not a visual-parity claim.",
        "",
        "FreeW canonical comparison scope: **$($freeW.renderedEvidence.canonicalComparison.kind)**. $($freeW.renderedEvidence.canonicalComparison.description) $($freeW.renderedEvidence.canonicalComparison.refreshInstruction)",
        "",
        "FreeX Wave193 No Fill transition evidence: **$($freeX.renderedEvidence.physicalEvidence.wave193PopupTransitions.summary)**. Retained evidence includes **$($freeX.renderedEvidence.physicalEvidence.wave193EvidenceArtifactCount)/$($freeX.renderedEvidence.physicalEvidence.wave193EvidenceArtifactExpectedCount) artifacts** and **$($freeX.renderedEvidence.physicalEvidence.wave193EvidenceProvenanceFileCount)/9 provenance files**.",
        "FreeX Wave194 mixed-type evidence: **$($freeX.renderedEvidence.physicalEvidence.wave194.physicalPassed)/$($freeX.renderedEvidence.physicalEvidence.wave194.physicalTotal)** physical workflow, **$($freeX.renderedEvidence.physicalEvidence.wave194.focusedAvaloniaGuardPassed)/$($freeX.renderedEvidence.physicalEvidence.wave194.focusedAvaloniaGuardTotal)** Avalonia guards, **$($freeX.renderedEvidence.physicalEvidence.wave194.focusedPresentationPassed)/$($freeX.renderedEvidence.physicalEvidence.wave194.focusedPresentationTotal)** Presentation, and **$($freeX.renderedEvidence.physicalEvidence.wave194.focusedCoreIoPassed)/$($freeX.renderedEvidence.physicalEvidence.wave194.focusedCoreIoTotal)** Core.IO (Wave194 plus five foreground-capture guards); geometry $($freeX.renderedEvidence.physicalEvidence.wave194.geometry.bounds), click $($freeX.renderedEvidence.physicalEvidence.wave194.geometry.click), package readback remains exact.",
        "FreeX Wave195 physical evidence: **$($freeX.renderedEvidence.physicalEvidence.wave195.physicalPassed)/$($freeX.renderedEvidence.physicalEvidence.wave195.physicalTotal)** production Docker/X11 sessions pass, with **$($freeX.renderedEvidence.physicalEvidence.wave195.evidenceArtifactCount)** manifest-listed artifacts, **$($freeX.renderedEvidence.physicalEvidence.wave195.screenshotCount)** screenshots, and **$($freeX.renderedEvidence.physicalEvidence.wave195.reloadWitnessPassed)/$($freeX.renderedEvidence.physicalEvidence.wave195.reloadWitnessTotal)** reload witnesses. This is bounded evidence for the named workflows and retained sessions, not exhaustive parity or WPF evidence.",
        "FreeP Wave195 rich-text selection evidence: the exact 251x74 crop improves the changed-pixel ratio from $($freeP.renderedEvidence.wave195.richTextSelection.changedPixelRatioBefore) to $($freeP.renderedEvidence.wave195.richTextSelection.changedPixelRatioAfter); this remains comparison evidence, not a full-parity claim.",
        "FreeX Wave196 evidence: **$($freeX.renderedEvidence.physicalEvidence.wave196.physicalPassed)/$($freeX.renderedEvidence.physicalEvidence.wave196.physicalTotal)** production Docker/X11 ribbon-formatting probe and **$($freeX.renderedEvidence.physicalEvidence.wave196.focusedSourceTestsPassed)/$($freeX.renderedEvidence.physicalEvidence.wave196.focusedSourceTestsTotal)** focused source tests recorded; persisted package fact $($freeX.renderedEvidence.physicalEvidence.wave196.persistedStyle), save-clean $($freeX.renderedEvidence.physicalEvidence.wave196.saveClean). This is bounded evidence, not a local integration acceptance or full-parity claim.",
        "FreeW Wave196 evidence: the committed trailing inline flow-break caret oracle and **$($freeW.renderedEvidence.wave196.focusedSourceTestsPassed)/$($freeW.renderedEvidence.wave196.focusedSourceTestsTotal)** source regressions cover post-page/post-column caret placement, including consecutive-break coverage; this remains focused source evidence, not a complete visual-parity claim.",
        "FreeP Wave196 evidence: committed deck17 slide02 light-hinting metrics move Avalonia/Office from $($freeP.renderedEvidence.wave196.avaloniaOfficeBefore)% to $($freeP.renderedEvidence.wave196.avaloniaOfficeAfter)% and WPF/Avalonia from $($freeP.renderedEvidence.wave196.wpfAvaloniaBefore)% to $($freeP.renderedEvidence.wave196.wpfAvaloniaAfter)%; $($freeP.renderedEvidence.wave196.imageHashCount) image hashes are retained. This is a scoped renderer measurement, not a complete visual-parity claim.",
        "FreeX Wave197 evidence: **$($freeX.renderedEvidence.physicalEvidence.wave197.physicalPassed)/$($freeX.renderedEvidence.physicalEvidence.wave197.physicalTotal)** production Docker/X11 number-format probe and **$($freeX.renderedEvidence.physicalEvidence.wave197.focusedSourceTestsPassed)/$($freeX.renderedEvidence.physicalEvidence.wave197.focusedSourceTestsTotal)** focused source tests recorded; report $($freeX.renderedEvidence.physicalEvidence.wave197.productionDockerX11Report), $($freeX.renderedEvidence.physicalEvidence.wave197.persistedStyle), save-clean $($freeX.renderedEvidence.physicalEvidence.wave197.saveClean). Ordinary bubble key routing remains retained and deferred combo dismissal synchronously restores worksheet focus. The later tested head differs only by the FreeW evidence/test commit; this is bounded evidence, not a full-parity claim.",
        "FreeW Wave197 evidence: **$($freeW.renderedEvidence.wave197.focusedSourceTestsPassed)/$($freeW.renderedEvidence.wave197.focusedSourceTestsTotal)** focused tests cover exactly **$($freeW.renderedEvidence.wave197.uniqueScenarioCount)** unique Legal Notices scenarios; the surface-margin candidate regressed all six rows and the 16px line-box candidate improved two long rows but regressed two. No production candidate is retained.",
        "FreeP Wave197 evidence: leading and baseline-alignment candidates are rejected with **$($freeP.renderedEvidence.wave197.focusedSourceTestsPassed)/$($freeP.renderedEvidence.wave197.focusedSourceTestsTotal)** focused tests. Leading-candidate bytes/hashes and its recorded source commit are verified; the baseline-alignment manifest explicitly records four missing untracked candidate images, and image-generation linkage remains unproven. The residual remains unresolved text-raster evidence, not a fallback-font diagnosis.",
        "FreeX Wave198 evidence: **$($freeX.renderedEvidence.physicalEvidence.wave198.physicalPassed)/$($freeX.renderedEvidence.physicalEvidence.wave198.physicalTotal)** production Docker/X11 font-family probe and **$($freeX.renderedEvidence.physicalEvidence.wave198.focusedSourceTestsPassed)/$($freeX.renderedEvidence.physicalEvidence.wave198.focusedSourceTestsTotal)** focused source tests recorded; persisted package fact $($freeX.renderedEvidence.physicalEvidence.wave198.persistedStyle), save-clean $($freeX.renderedEvidence.physicalEvidence.wave198.saveClean). Automatic combo-close focus was not measured before explicit worksheet reselect and remains unresolved.",
        "FreeW Wave198 evidence: shared tab-pane trailing-frame correction improves target changed pixels from **$($freeW.renderedEvidence.wave198.targetBeforeChangedPixels)** to **$($freeW.renderedEvidence.wave198.targetAfterChangedPixels)** (reduction $($freeW.renderedEvidence.wave198.targetChangedPixelsReduction)) and the control from **$($freeW.renderedEvidence.wave198.controlBeforeChangedPixels)** to **$($freeW.renderedEvidence.wave198.controlAfterChangedPixels)** (reduction $($freeW.renderedEvidence.wave198.controlChangedPixelsReduction)); focused review coverage is **$($freeW.renderedEvidence.wave198.focusedSourceTestsPassed)/$($freeW.renderedEvidence.wave198.focusedSourceTestsTotal)**. Raw evidence is metadata-only and PNGs are untracked.",
        "FreeP Wave198 evidence: SubpixelAntialias is rejected with **$($freeP.renderedEvidence.wave198.focusedSourceTestsPassed)/$($freeP.renderedEvidence.wave198.focusedSourceTestsTotal)** focused tests; Avalonia/Office changes from $($freeP.renderedEvidence.wave198.avaloniaOfficeBefore)% to $($freeP.renderedEvidence.wave198.avaloniaOfficeAfter)% while WPF/Avalonia changes from $($freeP.renderedEvidence.wave198.wpfAvaloniaBefore)% to $($freeP.renderedEvidence.wave198.wpfAvaloniaAfter)%. Provenance is not independently proven and no production candidate is retained.",
        "FreeX Wave199 evidence: no production change was retained; automatic worksheet focus failed physical Linux and stayed A1, explicit worksheet reselect worked, and Calibri/save-clean=false failed persistence. The canonical bundle contains **$($freeX.renderedEvidence.physicalEvidence.wave199.canonicalEvidenceFileCount)** auditable evidence files and focused source tests pass **$($freeX.renderedEvidence.physicalEvidence.wave199.focusedSourceTestsPassed)/$($freeX.renderedEvidence.physicalEvidence.wave199.focusedSourceTestsTotal)**.",
        "FreeW Wave199 evidence: only WPF visual-capture hardening is retained (50 ms polling, 15 second timeout, owned-modal close on timeout). The Style width candidate is rejected: initial **7.6030% -> 13.3183%**, populated **7.7021% -> 13.4134%**, validation **7.6030% -> 13.3183%**; all pixel/luminance/phash metrics were independently recomputed across **$($freeW.renderedEvidence.wave199.artifactCount)** artifacts. Canonical counts remain **291 surfaces / 141 mismatches / 80 pass / 70 Avalonia extensions**; focused evidence is **2/2** plus host guards **3/3**.",
        "FreeP Wave199 evidence: no production renderer change is retained and Aptos substitutes are rejected. The evidence binds **$($freeP.renderedEvidence.wave199.candidatePngCount)** candidate PNGs to exact indexes and hashes and independently recomputes **$($freeP.renderedEvidence.wave199.independentlyRecomputedPixelMetricCount)** pixel metrics; candidate/native-Aptos provenance and broader-corpus claims are explicitly not proven. The **$($freeP.renderedEvidence.wave199.officeReferencesInventoried)** Office references are inventory-only; focused tests pass **$($freeP.renderedEvidence.wave199.focusedSourceTestsPassed)/$($freeP.renderedEvidence.wave199.focusedSourceTestsTotal)**.",
        "",
        "| App | Route coverage | Artifact coverage | Paired WPF/Avalonia evidence | Physical/no-COM limitation | Authoritative Microsoft Office baseline |",
        "|---|---|---|---|---|---|",
        "| FreeX | $($freeX.renderedEvidence.routeCoverage.inventoryRouteCount) inventoried dialog routes; $($freeX.renderedEvidence.routeCoverage.pairedRouteEvidenceCount) paired route evidence rows | $($freeX.renderedEvidence.artifactCoverage.wpfManifestSurfaceCount) WPF + $($freeX.renderedEvidence.artifactCoverage.avaloniaManifestSurfaceCount) Avalonia dialog surfaces; complete $($freeX.renderedEvidence.chromeCapture.excelReferenceCount)/$($freeX.renderedEvidence.chromeCapture.wpfCaptureCount)/$($freeX.renderedEvidence.chromeCapture.avaloniaCaptureCount) Excel/WPF/Avalonia ribbon matrices; $($freeX.renderedEvidence.gridCorpus.totalAvaloniaCaptureCount) Avalonia grid-corpus captures | $($freeX.renderedEvidence.pairedEvidence.pairedSurfaceCount) paired dialog surfaces; $($freeX.renderedEvidence.chromeCapture.fixedViewportComparisonCount) fixed-width chrome triage rows per host | $($freeX.renderedEvidence.physicalEvidence.status); Linux Name Box $($freeX.renderedEvidence.physicalEvidence.linuxNameBoxParityPassed)/$($freeX.renderedEvidence.physicalEvidence.linuxNameBoxParityTotal) visual and $($freeX.renderedEvidence.physicalEvidence.linuxNameBoxInteractionPassed)/$($freeX.renderedEvidence.physicalEvidence.linuxNameBoxInteractionTotal) interaction; AutoFilter recalculation $($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterRecalculationPassed)/$($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterRecalculationTotal); sort persistence $($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterSortPersistencePassed)/$($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterSortPersistenceTotal); text criteria $($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterTextCriteriaPassed)/$($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterTextCriteriaTotal); numeric criteria $($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterNumericCriteriaPassed)/$($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterNumericCriteriaTotal) ($($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterNumericCriteriaStatus)); date criteria $($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterDateCriteriaPassed)/$($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterDateCriteriaTotal) ($($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterDateCriteriaStatus)); fill-color $($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterFillColorPassed)/$($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterFillColorTotal); font-color $($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterFontColorPassed)/$($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterFontColorTotal); No Fill $($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterNoFillPassed)/$($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterNoFillTotal); app-owned render manifests, complete foreground chrome matrices, and committed Excel range references | $($freeX.renderedEvidence.authoritativeMicrosoftOfficeBaseline.product): $($freeX.renderedEvidence.authoritativeMicrosoftOfficeBaseline.status); $($freeX.renderedEvidence.authoritativeMicrosoftOfficeBaseline.artifactCount) artifacts. $($freeX.renderedEvidence.authoritativeMicrosoftOfficeBaseline.limitation) |",
        "| FreeW | $($freeW.renderedEvidence.routeCoverage.inventoryRouteCount) inventoried route families; $($freeW.renderedEvidence.routeCoverage.comparedRouteCount) represented in comparison rows; $($freeW.renderedEvidence.routeCoverage.pairedRouteCount) paired and $($freeW.renderedEvidence.routeCoverage.avaloniaOnlyRouteCount) Avalonia-only | $($freeW.renderedEvidence.artifactCoverage.evidenceRowCount) dialog comparison rows; $($freeW.renderedEvidence.shellChrome.pairedStaticCaptureCount) paired static and $($freeW.renderedEvidence.shellChrome.pairedContextualCaptureCount) paired contextual shell captures; $($freeW.renderedEvidence.shellChrome.wordOfficeChromeReferenceCount) native Word ribbon references | $($freeW.renderedEvidence.pairedEvidence.pairedScenarioCount) paired dialog rows; $($freeW.renderedEvidence.pairedEvidence.passCount) pass classifications; $($freeW.renderedEvidence.pairedEvidence.mismatchCount) genuine visual mismatch classifications; shell captures review-required | $($freeW.renderedEvidence.physicalEvidence.status); app-owned dialog/full-window shell captures plus committed Word canvas and ribbon references | $($freeW.renderedEvidence.authoritativeMicrosoftOfficeBaseline.product): $($freeW.renderedEvidence.authoritativeMicrosoftOfficeBaseline.status); $($freeW.renderedEvidence.authoritativeMicrosoftOfficeBaseline.artifactCount) artifacts. $($freeW.renderedEvidence.authoritativeMicrosoftOfficeBaseline.limitation) |",
        "| FreeP | Dialog lane: $($freeP.renderedEvidence.routeCoverage.laneEntries[0].routeInventoryCount) routes/$($freeP.renderedEvidence.routeCoverage.laneEntries[0].renderedScenarioCount) scenarios; whole-window lane: $($freeP.renderedEvidence.routeCoverage.laneEntries[1].renderedScenarioCount) scenarios without a separate route inventory | $($freeP.renderedEvidence.artifactCoverage.wpfPngCount) WPF PNGs; $($freeP.renderedEvidence.artifactCoverage.avaloniaPngCount) Avalonia PNGs; $($freeP.renderedEvidence.artifactCoverage.diffPngCount) diff PNGs; $($freeP.renderedEvidence.nativeOfficeChrome.capturedReferenceCount) native PowerPoint ribbon refs; $($freeP.renderedEvidence.responsiveAppChrome.capturedPairCount) responsive WPF/Avalonia pairs; Wave194 topology source SHA $($freeP.renderedEvidence.wave194Topology.sourceCorpusSha256), $($freeP.renderedEvidence.wave194Topology.status), residual unresolved | $($freeP.renderedEvidence.pairedEvidence.pairedScenarioCount) paired scenarios; $($freeP.renderedEvidence.pairedEvidence.passCount) local comparison passes; $($freeP.renderedEvidence.pairedEvidence.mismatchCount) mismatches; native Office/app chrome $($freeP.renderedEvidence.nativeOfficeChrome.captureStatus)/$($freeP.renderedEvidence.responsiveAppChrome.captureStatus) | $($freeP.renderedEvidence.physicalEvidence.status); visible app-owned render targets, complete responsive app and Office ribbon lanes, and a committed PowerPoint COM corpus | $($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.product): $($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.status); $($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.artifactCount) tracked artifacts across $($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.referenceReadyDecks) decks, with $($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.missingReferenceDecks) deck missing references. Current-source WPF/Avalonia averages: $($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.wpfAverageMeanPercent)% / $($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.avaloniaAverageMeanPercent)%. $($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.limitation) |",
        "",
        "## Integration Gates",
        "",
        "Wave199 local integration gates are **accepted**. The full Release build passed at ``$($dashboard.integrationGateEvidence.testedSourceCommit)`` and repository preflight passed at final pre-dashboard source ``$($dashboard.integrationGateEvidence.finalPreDashboardIntegrationSourceCommit)``. It records three app slices and cumulative 597 app slices (199 per app). Per AGENTS.md, delegated GitHub gates are not claimed as run. $($dashboard.integrationGateEvidence.gateBoundary)",
        "",
        "- Historical Wave194 acceptance: $($wave194History.acceptanceRefreshNote) $($wave194History.fullReleaseBuild) $($wave194History.defaultNonUiTestLane)",
        "- Historical Wave195 acceptance: $($wave195History.currentEvidence) $($wave195History.gateBoundary)",
        "- Historical Wave196 acceptance: $($wave196History.currentEvidence) $($wave196History.gateBoundary)",
        "- Historical Wave197 acceptance: $($wave197History.currentEvidence) $($wave197History.gateBoundary)",
        "- Historical Wave198 acceptance: $($wave198History.currentEvidence) $($wave198History.gateBoundary)",
        "- Wave199 evidence: $($dashboard.integrationGateEvidence.currentEvidence)",
        "- Slice accounting: $($dashboard.integrationGateEvidence.sliceAccounting)",
        "- Pending local gates: $(if (@($dashboard.integrationGateEvidence.pendingIntegrationGates).Count -eq 0) { 'none; repository preflight and full Release build passed' } else { $dashboard.integrationGateEvidence.pendingIntegrationGates -join ', ' })",
        "- Delegated GitHub gates: $($dashboard.integrationGateEvidence.delegatedGitHubGates -join ', ') ($($dashboard.integrationGateEvidence.delegatedGitHubGateStatus))",
        "- Acceptance boundary: production/integration build source $($dashboard.integrationGateEvidence.testedSourceCommit); final pre-dashboard integration source $($dashboard.integrationGateEvidence.finalPreDashboardIntegrationSourceCommit); exactly six acceptance/report paths are allowlisted after the latter boundary, and the refresh does not claim delegated GitHub gates or full parity.",
        "- Repository preflight: $($dashboard.integrationGateEvidence.repositoryPreflight.status); source ``$($dashboard.integrationGateEvidence.repositoryPreflight.sourceCommit)``; $($dashboard.integrationGateEvidence.repositoryPreflight.result); command ``$($dashboard.integrationGateEvidence.repositoryPreflight.command)``.",
        "- Full Release build: $($dashboard.integrationGateEvidence.fullReleaseBuild.status); source ``$($dashboard.integrationGateEvidence.fullReleaseBuild.sourceCommit)``; $($dashboard.integrationGateEvidence.fullReleaseBuild.result); command ``$($dashboard.integrationGateEvidence.fullReleaseBuild.command)``.",
        "- Focused/evidence facts: $($dashboard.integrationGateEvidence.focusedTests)",
        "- Portability correction: $($dashboard.integrationGateEvidence.portabilityCorrection)",
        "- Historical Wave194 reintegration: $($wave194History.reintegration)",
        "- Historical Wave194 focused tests: $($wave194History.focusedTests)",
        "",
        "## FreeX Visual Review Queue",
        "",
        "This is a deterministic human-review queue, not a pass/fail result. The threshold and rationale are generated in `docs/parity/dialog-visual-evidence-summary.json`; equal dimensions or paired ids do not establish visual parity.",
        "",
        "| Surface id | Triage score | Logical dimensions match | Dimension bucket | Review status |",
        "|---|---:|---|---|---|"
    ) + $freeXVisualReviewMarkdownRows + @(
        "",
        "## Source Files",
        "",
        '- `docs/parity/command-inventory.json`',
        '- `docs/parity/functional-parity.json`',
        '- `docs/parity/functional-parity-classification.json`',
        '- `docs/parity/dialog-parity-inventory.json`',
        '- `docs/parity/dialog-visual-evidence-summary.json`',
        '- `docs/parity/dialog-visual-assets/wpf-capture/manifest.json`',
        '- `docs/parity/dialog-visual-assets/avalonia-capture/manifest.json`',
        '- `docs/parity/freex-excel-foreground-capture-2026-08-16.md`',
        '- `docs/parity/freex-excel-chrome-comparison.md`',
        '- `tools/screenshots/screenshot_manifest.json`',
        '- `tools/screenshots_avalonia_ribbon/screenshot_manifest.json`',
        '- `docs/parity/freex-excel-com-baseline-2026-08-14/manifest.json`',
        '- `docs/parity/freex-avalonia-grid-corpus-2026-08-16/manifest.json`',
        '- `docs/parity/avalonia-parity-wave183-freex-namebox-overlay-20260823.md`',
        '- `docs/parity/avalonia-parity-wave184-freex-autofilter-20260823.md`',
        '- `docs/parity/avalonia-parity-wave185-freex-autofilter-sort-20260823.md`',
        '- `docs/parity/avalonia-parity-wave186-freex-autofilter-text-20260823.md`',
        '- `docs/parity/avalonia-parity-wave187-freex-autofilter-numeric-20260823.md`',
        '- `docs/parity/avalonia-parity-wave188-freex-autofilter-numeric-20260823.md`',
        '- `docs/parity/avalonia-parity-wave189-freex-autofilter-date-20260823.md`',
        '- `docs/parity/avalonia-parity-wave190-freex-autofilter-date-20260823.md`',
        '- `docs/parity/evidence/wave190-freex-autofilter-date-20260823/manifest.json`',
        '- `docs/parity/avalonia-parity-wave191-freex-autofilter-color-20260823.md`',
        '- `docs/parity/evidence/wave191-freex-autofilter-color-20260823/manifest.json`',
        '- `docs/parity/avalonia-parity-wave192-freex-autofilter-font-color-20260823.md`',
        '- `docs/parity/evidence/wave192-freex-autofilter-font-color-20260823/manifest.json`',
        '- `docs/parity/avalonia-parity-wave193-freex-autofilter-no-fill-20260823.md`',
        '- `docs/parity/evidence/wave193-freex-autofilter-no-fill-20260823/physical-result.json`',
        '- `docs/parity/evidence/wave193-freex-autofilter-no-fill-20260823/manifest.json`',
        '- `docs/parity/avalonia-parity-wave194-freex-autofilter-mixed-type-20260823.md`',
        '- `docs/parity/evidence/wave194-freex-autofilter-mixed-type-20260823/physical-result.json`',
            '- `docs/parity/evidence/wave194-freex-autofilter-mixed-type-20260823/manifest.json`',
            '- `docs/parity/evidence/wave195-freex-autofilter-criteria-workflows-20260828/manifest.json`',
        '- `docs/parity/freew-command-inventory.json`',
        '- `docs/parity/freew-dialog-harness/freew_dialog_route_inventory.json`',
        '- `docs/parity/freew-dialog-harness/freew_dialog_visual_comparison.json`',
        '- `docs/parity/freew-dialog-harness/freew_font_visual_provenance.json`',
        '- `docs/parity/freew-word-baseline-2026-08-16/manifest.json`',
        '- `docs/parity/freew-shell-visual-2026-08-16/freew_shell_visual_evidence.json`',
        '- `docs/parity/freew-word-chrome-2026-08-16/manifest.json`',
        '- `docs/parity/avalonia-parity-wave184-freew-table-properties-cell-20260823.md`',
        '- `freew/docs/parity/avalonia-parity-wave185-freew-page-setup-text-raster-20260823.md`',
        '- `freew/docs/parity/avalonia-parity-wave186-freew-table-properties-text-raster-20260823.md`',
        '- `docs/parity/avalonia-parity-wave187-freew-legal-notices-20260823.md`',
        '- `docs/parity/avalonia-parity-wave188-freew-font-20260823.md`',
        '- `docs/parity/avalonia-parity-wave189-freew-font-antialias-20260823.md`',
        '- `docs/parity/avalonia-parity-wave190-freew-visual-20260823.md`',
        '- `docs/parity/avalonia-parity-wave191-freew-font-template-20260823.md`',
        '- `docs/parity/avalonia-parity-wave192-freew-font-checkbox-20260823.md`',
        '- `docs/parity/avalonia-parity-wave193-freew-font-checkbox-glyph-20260823.md`',
        '- `docs/parity/avalonia-parity-wave194-freew-font-action-border-20260824.md`',
        '- `docs/parity/freep-command-parity-inventory.json`',
        '- `docs/parity/freep-dialog-pane-visual-evidence/summary.json`',
        '- `docs/parity/freep-dialog-pane-visual-evidence/artifact-manifest.json`',
        '- `docs/parity/freep-whole-window-visual-evidence/summary.json`',
        '- `docs/parity/freep-whole-window-visual-evidence/artifact-manifest.json`',
        '- `docs/parity/freep-render-slideshow-media-parity-20260720.json`',
        '- `docs/parity/freep-native-picker-human-evidence.json`',
        '- `docs/parity/freep-powerpoint-baseline-2026-08-14.json`',
        '- `docs/parity/freep-powerpoint-recalibration-2026-08-15.json`',
        '- `docs/parity/freep-powerpoint-chrome-2026-08-16/README.md`',
        '- `docs/parity/freep-powerpoint-chrome-2026-08-16/manifest.json`',
        '- `docs/parity/freep-responsive-chrome-2026-08-16/README.md`',
        '- `docs/parity/freep-responsive-chrome-2026-08-16/manifest.json`',
        '- `docs/parity/freep-wave184-smartart-2026-08-23.md`',
        '- `docs/parity/freep-wave185-bullets-autofit-20260823.md`',
        '- `docs/parity/freep-wave186-surface3d-smartart-20260823.md`',
        '- `docs/parity/avalonia-parity-wave187-freep-surface3d-20260823.md`',
        '- `docs/parity/avalonia-parity-wave188-freep-smartart-text-20260823.md`',
        '- `docs/parity/avalonia-parity-wave189-freep-smartart-text-20260823.md`',
        '- `docs/parity/avalonia-parity-wave190-freep-smartart-text-origin-20260823.md`',
        '- `docs/parity/avalonia-parity-wave190-freep-evidence-20260823/metrics.json`',
        '- `docs/parity/avalonia-parity-wave191-freep-smartart-color-gate-20260823.md`',
        '- `docs/parity/avalonia-parity-wave191-freep-evidence-20260823/metrics.json`',
        '- `docs/parity/avalonia-parity-wave192-freep-render-residual-20260823.md`',
        '- `docs/parity/evidence/avalonia-parity-wave192-freep-evidence-20260823/metrics.json`',
        '- `docs/parity/avalonia-parity-wave193-freep-render-residual-20260823.md`',
        '- `docs/parity/evidence/avalonia-parity-wave193-freep-evidence-20260823/metrics.json`',
        '- `docs/parity/evidence/avalonia-parity-wave193-freep-evidence-20260823/references.json`',
        '- `docs/parity/evidence/avalonia-parity-wave193-freep-evidence-20260823/images.json`',
        '- `docs/parity/freep-wave194-deck17-slide02-topology-20260823.md`',
            '- `docs/parity/evidence/freep-wave194-deck17-slide02-topology-20260823/topology.json`',
            '- `docs/parity/avalonia-parity-wave195-cross-app-integration-20260828.md`',
            '- `docs/parity/freex-wave197-ribbon-number-format/README.md`',
            '- `tests/FreeX.App.Avalonia.Tests/Wave197RibbonNumberFormatPhysicalSourceTests.cs`',
            '- `freew/docs/parity/avalonia-parity-wave197-freew-legal-notices-template-candidates-20260829.md`',
            '- `freew/FreeW.App.Avalonia.Tests/Wave197LegalNoticesEvidenceTests.cs`',
            '- `freew/docs/parity/evidence/wave197-freew-legal-notices-raw-evidence.json`',
            '- `freew/docs/parity/evidence/SHA256SUMS.txt`',
            '- `docs/parity/freep-wave197-deck17-leading-residual-20260829.md`',
            '- `docs/parity/evidence/freep-wave197-deck17-leading-residual-20260829/metrics.json`',
            '- `docs/parity/freep-wave197-deck17-baseline-alignment-20260829.md`',
            '- `docs/parity/evidence/freep-wave197-deck17-baseline-alignment-20260829/metrics.json`',
            '- `docs/parity/evidence/freep-wave197-deck17-baseline-alignment-20260829/images.json`',
            '- `docs/parity/avalonia-parity-wave197-cross-app-integration-20260829.md`',
            '- `docs/parity/freex-wave198-ribbon-font-family/README.md`',
            '- `docs/parity/freex-wave198-ribbon-font-family/FINAL-EVIDENCE.md`',
            '- `docs/parity/freex-wave198-ribbon-font-family/evidence/SHA256SUMS.txt`',
            '- `tests/FreeX.App.Avalonia.Tests/Wave198RibbonFontFamilyPhysicalSourceTests.cs`',
            '- `docs/parity/avalonia-parity-wave198-freew-table-properties-tab-pane-20260829.md`',
            '- `freew/FreeW.App.Avalonia.Tests/Wave198TablePropertiesEvidenceTests.cs`',
            '- `freew/docs/parity/evidence/wave198-freew-table-properties-tab-pane-raw-evidence.json`',
            '- `docs/parity/freep-wave198-deck17-subpixel-antialias-20260829.md`',
            '- `docs/parity/evidence/freep-wave198-deck17-subpixel-antialias-20260829/metrics.json`',
            '- `freep/FreeP.App.Rendering.Avalonia.Tests/Wave198Deck17SubpixelAntialiasEvidenceTests.cs`',
            '- `docs/parity/avalonia-parity-wave198-cross-app-integration-20260829.md`',
            '- `docs/parity/freex-wave199-ribbon-font-family/README.md`',
            '- `docs/parity/freex-wave199-ribbon-font-family/FINAL-EVIDENCE.md`',
            '- `docs/parity/freex-wave199-ribbon-font-family/evidence/SHA256SUMS.txt`',
            '- `docs/parity/freex-wave199-ribbon-font-family/evidence/interaction-validation.json`',
            '- `docs/parity/freex-wave199-ribbon-font-family/evidence/package-proof.txt`',
            '- `tests/FreeX.App.Avalonia.Tests/Wave199RibbonFontFamilyFocusSourceTests.cs`',
            '- `tests/FreeX.App.Avalonia.Tests/Wave199RibbonFontFamilyEvidenceTests.cs`',
            '- `freew/docs/parity/avalonia-parity-wave199-freew-style-dialog.md`',
            '- `freew/docs/parity/evidence/wave199-freew-style-dialog.json`',
            '- `freew/docs/parity/evidence/wave199-freew-style-dialog-artifacts/SHA256SUMS.txt`',
            '- `freew/FreeW.App.Avalonia.Tests/Wave199StyleDialogEvidenceTests.cs`',
            '- `docs/parity/freep-wave199-deck17-aptos-resource-raster-20260829.md`',
            '- `docs/parity/evidence/freep-wave199-deck17-aptos-resource-raster-20260829/metrics.json`',
            '- `docs/parity/evidence/freep-wave199-deck17-aptos-resource-raster-20260829/references.json`',
            '- `docs/parity/evidence/freep-wave199-deck17-aptos-resource-raster-20260829/images.json`',
            '- `docs/parity/evidence/freep-wave199-deck17-aptos-resource-raster-20260829/broader-controls.json`',
            '- `freep/FreeP.App.Rendering.Avalonia.Tests/Wave199Deck17AptosResourceRasterEvidenceTests.cs`',
            '- `docs/parity/avalonia-parity-wave199-cross-app-integration-20260829.md`'
    ) -join "`n"
    Set-Content -LiteralPath $tempMarkdownPath -Value ($md + "`n") -NoNewline -Encoding UTF8

    if ($Check) {
        Test-ToolGeneratedFileContentMatches -ExpectedPath $tempJsonPath -ActualPath $resolvedJsonPath -Label "Cross-app parity dashboard JSON" -GeneratorScriptName "tools/Generate-CrossAppParityDashboard.ps1" -NormalizeNewlines
        Test-ToolGeneratedFileContentMatches -ExpectedPath $tempMarkdownPath -ActualPath $resolvedMarkdownPath -Label "Cross-app parity dashboard Markdown" -GeneratorScriptName "tools/Generate-CrossAppParityDashboard.ps1" -NormalizeNewlines
    }
    else {
        Copy-Item -LiteralPath $tempJsonPath -Destination $resolvedJsonPath -Force
        Copy-Item -LiteralPath $tempMarkdownPath -Destination $resolvedMarkdownPath -Force
        Write-Host "Wrote $JsonPath and $MarkdownPath."
    }

    if ($AcceptanceRefresh) {
        if ([string]::IsNullOrWhiteSpace($AcceptanceRefreshTestedSourceCommit)) {
            throw "-AcceptanceRefresh requires -AcceptanceRefreshTestedSourceCommit; the parent must supply the exact tested source head."
        }

        $boundaryScriptPath = Join-Path $PSScriptRoot "Test-CrossAppParityDashboard.ps1"
        $boundaryHost = (Get-Command pwsh -ErrorAction Stop).Source
        $boundaryOutput = @(& $boundaryHost -NoProfile -ExecutionPolicy Bypass -File $boundaryScriptPath `
            -AcceptanceRefresh `
            -TestedSourceCommit $AcceptanceRefreshTestedSourceCommit `
            -HeadRef $AcceptanceRefreshHeadRef 2>&1)
        $boundaryExitCode = $LASTEXITCODE
        if ($boundaryExitCode -ne 0) {
            throw "Cross-app dashboard acceptance refresh boundary failed: $($boundaryOutput -join "`n")"
        }

        $boundaryOutput | ForEach-Object { Write-Host $_ }
    }
}
finally {
    Remove-ToolTemporaryDirectory -Path $tempRoot
}
