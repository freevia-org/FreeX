<#
.SYNOPSIS
    Generate local FreeW WPF + Avalonia visual evidence and retain normalized summaries.

.DESCRIPTION
    This runner creates the F2 DOCX fixture set under an ignored run folder, renders WPF evidence
    with FreeW.FidelityRender in composite mode, renders Avalonia page-layout evidence with
    FreeW.PageLayoutShot, validates both raw visual evidence manifests through the shared
    FreeW.App.Presentation normalizer, and writes small stable JSON/Markdown summaries. By
    default it does not require Word; pass -WordBaselineDir to compare both renderers against a
    pre-captured MS Word PNG baseline, or pass -IncludeWordBaseline to generate that Word baseline
    from the same DOCX fixtures before normalization.

    Bulky DOCX/PNG/raw-manifest artifacts stay under freew-fidelity-corpus/runs/.

.EXAMPLE
    pwsh freew-fidelity-corpus/tools/Run-FreeWVisualEvidence.ps1 -OutDir freew-fidelity-corpus/runs/visual-evidence-smoke

.EXAMPLE
    pwsh freew-fidelity-corpus/tools/Run-FreeWVisualEvidence.ps1 -OutDir freew-fidelity-corpus/runs/visual-evidence-word -WordBaselineDir freew-fidelity-corpus/runs/word-baseline -BaselineTolerance word-png-default

.EXAMPLE
    pwsh freew-fidelity-corpus/tools/Run-FreeWVisualEvidence.ps1 -OutDir freew-fidelity-corpus/runs/visual-evidence-word -IncludeWordBaseline

.EXAMPLE
    pwsh freew-fidelity-corpus/tools/Run-FreeWVisualEvidence.ps1 -OutDir freew-fidelity-corpus/runs/core-layout-proof -ScenarioSet CoreLayoutProof -WordBaselineUnavailableReason "COM ProgID 'Word.Application' is not registered"

.EXAMPLE
    pwsh freew-fidelity-corpus/tools/Run-FreeWVisualEvidence.ps1 -OutDir freew-fidelity-corpus/runs/references-heavy-word-baseline-proof -ScenarioSet ReferencesHeavyWordBaselineProof -WordBaselineUnavailableReason "COM ProgID 'Word.Application' is not registered"

.EXAMPLE
    pwsh freew-fidelity-corpus/tools/Run-FreeWVisualEvidence.ps1 -OutDir freew-fidelity-corpus/runs/legal-reference-section-page-proof -ScenarioSet LegalReferenceSectionPageProof -WordBaselineUnavailableReason "COM ProgID 'Word.Application' is not registered"

.EXAMPLE
    pwsh freew-fidelity-corpus/tools/Run-FreeWVisualEvidence.ps1 -OutDir freew-fidelity-corpus/runs/drawing-object-proof -ScenarioSet DrawingObjectVisualProof -WordBaselineUnavailableReason "COM ProgID 'Word.Application' is not registered"

.EXAMPLE
    pwsh freew-fidelity-corpus/tools/Run-FreeWVisualEvidence.ps1 -OutDir freew-fidelity-corpus/runs/grouped-drawing-object-proof -ScenarioSet GroupedDrawingObjectVisualProof -WordBaselineUnavailableReason "COM ProgID 'Word.Application' is not registered"

.EXAMPLE
    pwsh freew-fidelity-corpus/tools/Run-FreeWVisualEvidence.ps1 -OutDir freew-fidelity-corpus/runs/shape-object-proof -ScenarioSet ShapeObjectVisualProof -WordBaselineUnavailableReason "COM ProgID 'Word.Application' is not registered"

.EXAMPLE
    pwsh freew-fidelity-corpus/tools/Run-FreeWVisualEvidence.ps1 -OutDir freew-fidelity-corpus/runs/object-format-proof -ScenarioSet ObjectFormatVisualProof -WordBaselineUnavailableReason "COM ProgID 'Word.Application' is not registered"

.EXAMPLE
    pwsh freew-fidelity-corpus/tools/Run-FreeWVisualEvidence.ps1 -OutDir freew-fidelity-corpus/runs/smartart-polygon-proof -ScenarioSet SmartArtPolygonVisualProof -WordBaselineUnavailableReason "COM ProgID 'Word.Application' is not registered"

.EXAMPLE
    pwsh freew-fidelity-corpus/tools/Run-FreeWVisualEvidence.ps1 -OutDir freew-fidelity-corpus/runs/chart-visual-proof -ScenarioSet ChartVisualProof -WordBaselineUnavailableReason "COM ProgID 'Word.Application' is not registered"

.EXAMPLE
    pwsh freew-fidelity-corpus/tools/Run-FreeWVisualEvidence.ps1 -OutDir freew-fidelity-corpus/runs/wordart-watermark-proof -ScenarioSet WordArtWatermarkVisualProof -WordBaselineUnavailableReason "COM ProgID 'Word.Application' is not registered"

.EXAMPLE
    pwsh freew-fidelity-corpus/tools/Run-FreeWVisualEvidence.ps1 -OutDir freew-fidelity-corpus/runs/floating-wrapping-proof -ScenarioSet FloatingWrappingVisualProof -WordBaselineUnavailableReason "COM ProgID 'Word.Application' is not registered"

.EXAMPLE
    pwsh freew-fidelity-corpus/tools/Run-FreeWVisualEvidence.ps1 -OutDir freew-fidelity-corpus/runs/header-footer-image-proof -ScenarioSet HeaderFooterImageVisualProof -WordBaselineUnavailableReason "COM ProgID 'Word.Application' is not registered"

.EXAMPLE
    pwsh freew-fidelity-corpus/tools/Run-FreeWVisualEvidence.ps1 -OutDir freew-fidelity-corpus/runs/table-pagination-page-composition-proof -ScenarioSet TablePaginationPageCompositionProof -WordBaselineUnavailableReason "COM ProgID 'Word.Application' is not registered"

.EXAMPLE
    pwsh freew-fidelity-corpus/tools/Run-FreeWVisualEvidence.ps1 -OutDir freew-fidelity-corpus/runs/review-markup-proof -ScenarioSet ReviewMarkupVisualProof -WordBaselineUnavailableReason "COM ProgID 'Word.Application' is not registered"

.EXAMPLE
    pwsh freew-fidelity-corpus/tools/Run-FreeWVisualEvidence.ps1 -OutDir freew-fidelity-corpus/runs/review-compare-combine-proof -ScenarioSet ReviewCompareCombineVisualProof -WordBaselineUnavailableReason "COM ProgID 'Word.Application' is not registered"

.EXAMPLE
    pwsh freew-fidelity-corpus/tools/Run-FreeWVisualEvidence.ps1 -OutDir freew-fidelity-corpus/runs/equation-structure-proof -ScenarioSet EquationStructureVisualProof -WordBaselineUnavailableReason "COM ProgID 'Word.Application' is not registered"

.EXAMPLE
    pwsh freew-fidelity-corpus/tools/Run-FreeWVisualEvidence.ps1 -OutDir freew-fidelity-corpus/runs/note-placement-proof -ScenarioSet NotePlacementVisualProof -WordBaselineUnavailableReason "COM ProgID 'Word.Application' is not registered"

.EXAMPLE
    pwsh freew-fidelity-corpus/tools/Run-FreeWVisualEvidence.ps1 -OutDir freew-fidelity-corpus/runs/section-geometry-proof -ScenarioSet SectionGeometryVisualProof -WordBaselineUnavailableReason "COM ProgID 'Word.Application' is not registered"
#>
[CmdletBinding()]
param(
    [string]$OutDir,
    [string]$Configuration = 'Release',
    [int]$MaxPages = 6,
    [string]$WordBaselineDir,
    [switch]$IncludeWordBaseline,
    [string]$BaselineTolerance = 'word-png-default',
    [string]$WordBaselineUnavailableReason,
    [string]$ScenarioSet,
    [string[]]$ScenarioId
)

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $scriptDir '..\..\tools\ToolScriptSupport.ps1')
$repoRoot = Resolve-ToolFullPath (Join-Path $scriptDir "..\..")

if (-not $OutDir) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $OutDir = Join-Path $scriptDir "..\runs\visual-evidence-$stamp"
}

$runRoot = Resolve-ToolRepoPath -Path $OutDir -RepoRoot $repoRoot
$wordBaselineRoot = $null
$wordBaselineRenderRoot = $null
if ($IncludeWordBaseline) {
    $wordBaselineRenderRoot = if ([string]::IsNullOrWhiteSpace($WordBaselineDir)) {
        Join-Path $runRoot 'word-baseline'
    }
    else {
        Resolve-ToolRepoPath -Path $WordBaselineDir -RepoRoot $repoRoot
    }
    $wordBaselineRoot = Join-Path $wordBaselineRenderRoot 'word'
}
elseif (-not [string]::IsNullOrWhiteSpace($WordBaselineDir)) {
    $wordBaselineRoot = Resolve-ToolRepoPath -Path $WordBaselineDir -RepoRoot $repoRoot
    if (-not (Test-Path -LiteralPath $wordBaselineRoot -PathType Container)) {
        throw "Word baseline directory does not exist: $wordBaselineRoot"
    }
}
elseif (-not [string]::IsNullOrWhiteSpace($WordBaselineUnavailableReason)) {
    $wordBaselineRoot = $null
}

if ($wordBaselineRoot -and -not [string]::IsNullOrWhiteSpace($WordBaselineUnavailableReason)) {
    throw "-WordBaselineUnavailableReason cannot be combined with -WordBaselineDir or -IncludeWordBaseline."
}

$allowNoWordFallbackEvidence = -not $wordBaselineRoot

$fixtureDir = Join-Path $runRoot 'fixtures\f2'
$wpfDir = Join-Path $runRoot 'wpf'
$avaloniaDir = Join-Path $runRoot 'avalonia'
$summaryJson = Join-Path $runRoot 'freew_visual_evidence_summary.json'
$summaryMarkdown = Join-Path $runRoot 'freew_visual_evidence_summary.md'

$fidelityProject = Join-Path $repoRoot 'freew\tools\FreeW.FidelityRender\FreeW.FidelityRender.csproj'
$f2ObjectsProject = Join-Path $repoRoot 'freew\tools\_corpus_f2_objects\_corpus_f2_objects.csproj'
$pageShotProject = Join-Path $repoRoot 'freew\tools\FreeW.PageLayoutShot\FreeW.PageLayoutShot.csproj'
$summaryProject = Join-Path $repoRoot 'freew\tools\FreeW.VisualEvidenceSummary\FreeW.VisualEvidenceSummary.csproj'
$wordBaselineScript = Join-Path $repoRoot 'freew-fidelity-corpus\tools\Render-WordBaseline.ps1'

$namedScenarioSets = @{
    BackstagePrintExport = @(
        'backstage-print-preview-fidelity',
        'backstage-pdf-export-fidelity'
    )
    CoreLayoutProof = @(
        'f2-hf-images',
        'field-page-number-variants',
        'references-heavy-fields',
        'legal-reference-section-page-numbers',
        'equation-structures',
        'f2-footnotes',
        'f2-endnotes',
        'f2-section-landscape',
        'f2-tracked-changes',
        'f2-comments',
        'f2-outofbody-comments',
        'review-proofing-visual-depth',
        'review-protection-proofing-comments-only'
    )
    ReferencesHeavyWordBaselineProof = @(
        'references-heavy-fields'
    )
    LegalReferenceSectionPageProof = @(
        'legal-reference-section-page-numbers'
    )
    PageCompositionProof = @(
        'f2-columns',
        'f2-border-watermark',
        'page-composition-columns',
        'page-composition-border-watermark'
    )
    FloatingWrappingVisualProof = @(
        'f2-01-float-wrap',
        'page-composition-floating-image'
    )
    HeaderFooterImageVisualProof = @(
        'f2-hf-images'
    )
    TableLayoutProof = @(
        'table-layout-complex',
        'table-pagination-repeat-header',
        'table-page-composition-stress'
    )
    TablePaginationPageCompositionProof = @(
        'table-pagination-repeat-header',
        'table-page-composition-stress'
    )
    DrawingObjectVisualProof = @(
        'drawing-objects-complex',
        'object-format-position-size-style',
        'chart-smartart-complex',
        'wordart-watermark-stress',
        'wordart-picture-watermark-layout'
    )
    GroupedDrawingObjectVisualProof = @(
        'drawing-objects-complex'
    )
    ShapeObjectVisualProof = @(
        'drawing-objects-complex',
        'object-format-position-size-style'
    )
    ObjectFormatVisualProof = @(
        'object-format-position-size-style'
    )
    SmartArtPolygonVisualProof = @(
        'chart-smartart-complex'
    )
    ChartVisualProof = @(
        'chart-smartart-complex'
    )
    WordArtWatermarkVisualProof = @(
        'wordart-watermark-stress',
        'wordart-picture-watermark-layout'
    )
    ReviewMarkupVisualProof = @(
        'f2-tracked-changes',
        'f2-comments',
        'f2-outofbody-comments'
    )
    ReviewCompareCombineVisualProof = @(
        'review-compare-visual-proof',
        'review-combine-visual-proof'
    )
    ReviewProofingVisualProof = @(
        'review-proofing-visual-depth',
        'review-protection-proofing-comments-only'
    )
    EquationStructureVisualProof = @(
        'equation-structures'
    )
    NotePlacementVisualProof = @(
        'f2-footnotes',
        'f2-endnotes'
    )
    SectionGeometryVisualProof = @(
        'f2-section-landscape'
    )
}

if (-not [string]::IsNullOrWhiteSpace($ScenarioSet) -and -not $namedScenarioSets.ContainsKey($ScenarioSet)) {
    throw "Unknown ScenarioSet '$ScenarioSet'. Valid values: $($namedScenarioSets.Keys -join ', ')"
}

$effectiveScenarioIds = New-Object System.Collections.Generic.List[string]
if (-not [string]::IsNullOrWhiteSpace($ScenarioSet)) {
    foreach ($scenario in $namedScenarioSets[$ScenarioSet]) {
        $effectiveScenarioIds.Add($scenario)
    }
}
foreach ($scenario in @($ScenarioId)) {
    if (-not [string]::IsNullOrWhiteSpace($scenario)) {
        foreach ($id in $scenario.Split(',', [System.StringSplitOptions]::RemoveEmptyEntries)) {
            $effectiveScenarioIds.Add($id.Trim())
        }
    }
}
$effectiveScenarioIds = @($effectiveScenarioIds | Select-Object -Unique)

function Assert-BackstageEvidenceReadiness {
    param(
        [Parameter(Mandatory = $true)][string]$SummaryJsonPath,
        [bool]$AllowNoWordFallbackEvidence = $false
    )

    if (-not (Test-Path -LiteralPath $SummaryJsonPath -PathType Leaf)) {
        throw "Backstage evidence readiness cannot be checked because the summary JSON is missing: $SummaryJsonPath"
    }

    $summary = Get-Content -LiteralPath $SummaryJsonPath -Raw | ConvertFrom-Json
    if ([int]$summary.schemaVersion -lt 24) {
        throw "Backstage evidence readiness requires FreeW visual evidence summary schema v24 or newer, found v$($summary.schemaVersion)"
    }

    $readinessRows = @($summary.backstagePrintEvidenceReadiness)
    $evidenceRows = @($summary.evidence)
    $requiredScenarios = @(
        'backstage-print-preview-fidelity',
        'backstage-pdf-export-fidelity'
    )
    $requiredWorkflowByScenario = @{
        'backstage-print-preview-fidelity' = 'print-preview'
        'backstage-pdf-export-fidelity' = 'pdf-export'
    }
    $requiredArtifactKindByScenario = @{
        'backstage-print-preview-fidelity' = 'print-preview-fixed-layout'
        'backstage-pdf-export-fidelity' = 'pdf-export-rasterized'
    }
    $requiredPipelineByScenario = @{
        'backstage-print-preview-fidelity' = 'print-preview-fixed-layout-artifact'
        'backstage-pdf-export-fidelity' = 'pdf-export-rasterized-artifact'
    }
    $requiredRouteByScenario = @{
        'backstage-print-preview-fidelity' = 'backstage-print-preview-fixed-layout-capture'
        'backstage-pdf-export-fidelity' = 'backstage-pdf-export-raster-capture'
    }
    $requiredCaptureSourceByHost = @{
        'wpf-fidelity-render' = 'wpf-composite-renderer'
        'avalonia-page-layout-shot' = 'avalonia-render-target'
    }
    $requiredHosts = @(
        'wpf-fidelity-render',
        'avalonia-page-layout-shot'
    )
    $requiredPages = @(1, 2)
    $failures = New-Object System.Collections.Generic.List[string]
    $trustedCount = 0
    $fallbackHygieneCount = 0

    foreach ($scenarioId in $requiredScenarios) {
        foreach ($hostId in $requiredHosts) {
            foreach ($pageNumber in $requiredPages) {
                $match = @($readinessRows | Where-Object {
                    $_.scenarioId -eq $scenarioId -and
                    $_.hostId -eq $hostId -and
                    [int]$_.pageNumber -eq $pageNumber
                })

                if ($match.Count -eq 0) {
                    $failures.Add("$scenarioId/$hostId/p${pageNumber}: missing backstage readiness row")
                    continue
                }

                $row = $match[0]
                $isFallbackHygiene = $AllowNoWordFallbackEvidence -and
                    $row.status -eq 'fallback'
                if ($row.status -ne 'trusted' -and -not $isFallbackHygiene) {
                    $output = if ([string]::IsNullOrWhiteSpace([string]$row.outputSummary)) { '-' } else { [string]$row.outputSummary }
                    $notes = if ([string]::IsNullOrWhiteSpace([string]$row.notes)) { 'no notes' } else { [string]$row.notes }
                    $failures.Add("$scenarioId/$hostId/p${pageNumber}: status '$($row.status)' output '$output' notes '$notes'")
                    continue
                }

                $expectedWorkflow = $requiredWorkflowByScenario[$scenarioId]
                $evidenceMatch = @($evidenceRows | Where-Object {
                    $_.scenarioId -eq $scenarioId -and
                    $_.hostId -eq $hostId -and
                    [int]$_.pageNumber -eq $pageNumber -and
                    $_.trust.passed -eq $true
                })
                if ($evidenceMatch.Count -eq 0) {
                    $failures.Add("$scenarioId/$hostId/p${pageNumber}: missing trusted normalized evidence row for backstage artifact metadata")
                    continue
                }

                $metadata = $evidenceMatch[0].hostMetadata
                if ($isFallbackHygiene) {
                    if ($hostId -ne 'wpf-fidelity-render') {
                        $failures.Add("$scenarioId/$hostId/p${pageNumber}: fallback hygiene is only valid for WPF software renderer rows")
                        continue
                    }

                    $captureSource = [string]$metadata.captureSource
                    if ($captureSource -ne 'software-renderer') {
                        $failures.Add("$scenarioId/$hostId/p${pageNumber}: fallback hygiene expected captureSource 'software-renderer', found '$captureSource'")
                        continue
                    }

                    $renderTargetBitmap = [string]$metadata.wpfRenderTargetBitmap
                    if ($renderTargetBitmap -ne 'unavailable') {
                        $failures.Add("$scenarioId/$hostId/p${pageNumber}: fallback hygiene expected wpfRenderTargetBitmap 'unavailable', found '$renderTargetBitmap'")
                        continue
                    }

                    if ([string]::IsNullOrWhiteSpace([string]$metadata.wpfRenderTargetBitmapReason)) {
                        $failures.Add("$scenarioId/$hostId/p${pageNumber}: fallback hygiene expected wpfRenderTargetBitmapReason")
                        continue
                    }

                    $fallbackHygieneCount++
                }
                else {
                    $expectedCaptureSource = $requiredCaptureSourceByHost[$hostId]
                    $captureSource = [string]$metadata.captureSource
                    if ($captureSource -ne $expectedCaptureSource) {
                        $failures.Add("$scenarioId/$hostId/p${pageNumber}: captureSource '$captureSource' expected '$expectedCaptureSource'")
                        continue
                    }

                    $trustedCount++
                }

                $workflow = [string]$metadata.backstageWorkflow
                if ($workflow -ne $expectedWorkflow) {
                    $failures.Add("$scenarioId/$hostId/p${pageNumber}: backstageWorkflow '$workflow' expected '$expectedWorkflow'")
                    continue
                }

                $expectedArtifactKind = $requiredArtifactKindByScenario[$scenarioId]
                $artifactKind = [string]$metadata.backstageArtifactKind
                if ($artifactKind -ne $expectedArtifactKind) {
                    $failures.Add("$scenarioId/$hostId/p${pageNumber}: backstageArtifactKind '$artifactKind' expected '$expectedArtifactKind'")
                    continue
                }

                $expectedPipeline = $requiredPipelineByScenario[$scenarioId]
                $pipeline = [string]$metadata.backstagePipeline
                if ($pipeline -ne $expectedPipeline) {
                    $failures.Add("$scenarioId/$hostId/p${pageNumber}: backstagePipeline '$pipeline' expected '$expectedPipeline'")
                    continue
                }

                $expectedRoute = $requiredRouteByScenario[$scenarioId]
                $route = [string]$metadata.backstageCaptureRoute
                if ($route -ne $expectedRoute) {
                    $failures.Add("$scenarioId/$hostId/p${pageNumber}: backstageCaptureRoute '$route' expected '$expectedRoute'")
                    continue
                }
            }
        }
    }

    if ($failures.Count -gt 0) {
        throw "Backstage evidence readiness failed:`n - $($failures -join "`n - ")"
    }

    $verifiedRows = $trustedCount + $fallbackHygieneCount
    Write-Host "Backstage evidence readiness: trusted real rows=$trustedCount fallback hygiene rows=$fallbackHygieneCount"
    Write-Host "Backstage artifact metadata: verified rows=$verifiedRows schema=v$($summary.schemaVersion)"
    Write-Host "Backstage capture routes: verified rows=$verifiedRows"
    if ($fallbackHygieneCount -gt 0) {
        Write-Host "Backstage runner evidence hygiene: WPF software fallback rows=$fallbackHygieneCount"
    }
}

function Test-ScenarioFilterIncludesBackstage {
    param(
        [string[]]$ScenarioIds
    )

    if (@($ScenarioIds).Count -eq 0) {
        return $true
    }

    return @($ScenarioIds | Where-Object {
        $_ -eq 'backstage-print-preview-fidelity' -or
        $_ -eq 'backstage-pdf-export-fidelity'
    }).Count -gt 0
}

function Assert-CoreLayoutProofReadiness {
    param(
        [Parameter(Mandatory = $true)][string]$SummaryJsonPath,
        [Parameter(Mandatory = $true)][string[]]$ScenarioIds
    )

    if (@($ScenarioIds).Count -eq 0 -or -not @($ScenarioIds | Where-Object { $_ -eq 'f2-hf-images' }).Count) {
        return
    }

    if (-not (Test-Path -LiteralPath $SummaryJsonPath -PathType Leaf)) {
        throw "Core layout proof readiness cannot be checked because the summary JSON is missing: $SummaryJsonPath"
    }

    $summary = Get-Content -LiteralPath $SummaryJsonPath -Raw | ConvertFrom-Json
    $scenarios = @($summary.scenarios)
    $requiredHosts = @(
        'wpf-fidelity-render',
        'avalonia-page-layout-shot'
    )
    $failures = New-Object System.Collections.Generic.List[string]
    $trustedScenarioRows = 0

    foreach ($scenarioId in $ScenarioIds) {
        foreach ($hostId in $requiredHosts) {
            $match = @($scenarios | Where-Object {
                $_.scenarioId -eq $scenarioId -and
                $_.hostId -eq $hostId
            })

            if ($match.Count -eq 0) {
                $failures.Add("${scenarioId}/${hostId}: missing normalized scenario row")
                continue
            }

            $row = $match[0]
            if ($row.trust.passed -ne $true) {
                $notes = @($row.trust.failures) -join '; '
                if ([string]::IsNullOrWhiteSpace($notes)) {
                    $notes = 'no notes'
                }
                $failures.Add("${scenarioId}/${hostId}: scenario trust failed ($notes)")
                continue
            }

            if ([int]$row.trustedOutputs -lt [int]$row.minimumExpectedOutputs) {
                $failures.Add("${scenarioId}/${hostId}: expected at least $($row.minimumExpectedOutputs) trusted output(s), found $($row.trustedOutputs)")
                continue
            }

            $trustedScenarioRows++
        }
    }

    if ($failures.Count -gt 0) {
        throw "Core layout proof readiness failed:`n - $($failures -join "`n - ")"
    }

    Write-Host "Core layout proof readiness: trusted scenario rows=$trustedScenarioRows"
}

function Assert-ReferencesHeavyWordBaselineProofReadiness {
    param(
        [Parameter(Mandatory = $true)][string]$SummaryJsonPath,
        [Parameter(Mandatory = $true)][string[]]$ScenarioIds
    )

    if (@($ScenarioIds).Count -eq 0 -or -not @($ScenarioIds | Where-Object { $_ -eq 'references-heavy-fields' }).Count) {
        return
    }

    if (-not (Test-Path -LiteralPath $SummaryJsonPath -PathType Leaf)) {
        throw "References-heavy Word baseline proof readiness cannot be checked because the summary JSON is missing: $SummaryJsonPath"
    }

    $summary = Get-Content -LiteralPath $SummaryJsonPath -Raw | ConvertFrom-Json
    if ([int]$summary.schemaVersion -lt 46) {
        throw "References-heavy Word baseline proof readiness requires FreeW visual evidence summary schema v46 or newer, found v$($summary.schemaVersion)"
    }

    $requiredScenarioId = 'references-heavy-fields'
    $requiredHosts = @(
        'wpf-fidelity-render',
        'avalonia-page-layout-shot'
    )
    $requiredKeywords = @(
        'CITATION',
        'BIBLIOGRAPHY',
        'TOA'
    )
    $requiredToaSignatures = @(
        'category=Cases|entry=Example v. FreeW, 123 F.4th 456 (2026)|kind=explicit-page-numbers|pages=1,2|text=1, 2',
        'category=Statutes|entry=Free Software Evidence Act, 42 U.S.C. 2026|kind=explicit-page-numbers|pages=1|text=1'
    )
    $readinessRows = @($summary.referencesHeavyProofReadiness)
    $scenarios = @($summary.scenarios)
    $evidenceRows = @($summary.evidence)
    $baselineComparisons = @($summary.baselineComparisons)
    $remainingBlockers = @($summary.remainingEvidenceBlockers)
    $failures = New-Object System.Collections.Generic.List[string]
    $verifiedReadinessRows = 0
    $trustedScenarioRows = 0
    $verifiedSemanticRows = 0
    $verifiedBaselineRows = 0

    $proofRows = @($readinessRows | Where-Object { $_.scenarioId -eq $requiredScenarioId })
    if ($proofRows.Count -eq 0) {
        $failures.Add("${requiredScenarioId}: missing references-heavy field/TOA proof readiness row")
    }
    foreach ($proofRow in $proofRows) {
        if ($proofRow.trust.passed -ne $true -or $proofRow.status -ne 'paired-renderer-proof-ready') {
            $notes = @($proofRow.trust.failures) -join '; '
            if ([string]::IsNullOrWhiteSpace($notes)) {
                $notes = [string]$proofRow.baselineReadiness
            }
            $failures.Add("${requiredScenarioId}/p$($proofRow.pageNumber): readiness status '$($proofRow.status)' failed ($notes)")
            continue
        }

        if ([string]::IsNullOrWhiteSpace([string]$proofRow.semanticEvidence) -or [string]$proofRow.semanticEvidence -eq '-') {
            $failures.Add("${requiredScenarioId}/p$($proofRow.pageNumber): missing references-heavy semantic readiness summary")
            continue
        }

        $verifiedReadinessRows++
    }

    foreach ($hostId in $requiredHosts) {
        $scenarioRows = @($scenarios | Where-Object {
            $_.hostId -eq $hostId -and
            $_.scenarioId -eq $requiredScenarioId
        })
        if ($scenarioRows.Count -eq 0) {
            $failures.Add("${hostId}/${requiredScenarioId}: missing normalized scenario row")
            continue
        }

        $scenarioRow = $scenarioRows[0]
        if ($scenarioRow.trust.passed -ne $true) {
            $notes = @($scenarioRow.trust.failures) -join '; '
            if ([string]::IsNullOrWhiteSpace($notes)) {
                $notes = 'no notes'
            }
            $failures.Add("${hostId}/${requiredScenarioId}: scenario trust failed ($notes)")
            continue
        }

        if ([int]$scenarioRow.trustedOutputs -lt [int]$scenarioRow.minimumExpectedOutputs) {
            $failures.Add("${hostId}/${requiredScenarioId}: expected at least $($scenarioRow.minimumExpectedOutputs) trusted output(s), found $($scenarioRow.trustedOutputs)")
            continue
        }

        $trustedScenarioRows++

        $hostEvidenceRows = @($evidenceRows | Where-Object {
            $_.hostId -eq $hostId -and
            $_.scenarioId -eq $requiredScenarioId -and
            $_.trust.passed -eq $true
        })
        if ($hostEvidenceRows.Count -eq 0) {
            $failures.Add("${hostId}/${requiredScenarioId}: missing trusted evidence rows")
            continue
        }

        foreach ($row in $hostEvidenceRows) {
            $keywords = @($row.fields.complexFieldKeywords)
            foreach ($keyword in $requiredKeywords) {
                if ($keywords -notcontains $keyword) {
                    $failures.Add("${hostId}/${requiredScenarioId}/p$($row.pageNumber): missing complex field keyword '$keyword'")
                }
            }

            $resultSignatures = @($row.fields.complexFieldResultSignatures)
            if ($resultSignatures -notcontains 'BIBLIOGRAPHY=References') {
                $failures.Add("${hostId}/${requiredScenarioId}/p$($row.pageNumber): missing cached bibliography result signature")
            }
            if ($resultSignatures -notcontains 'TOA=Cases\t1, 2') {
                $failures.Add("${hostId}/${requiredScenarioId}/p$($row.pageNumber): missing cached TOA page-reference sentinel")
            }

            $toa = $row.tableOfAuthorities
            if ($toa.hasGeneratedTable -ne $true -or $toa.hasPageReferences -ne $true -or $toa.hasExplicitPageNumbers -ne $true) {
                $failures.Add("${hostId}/${requiredScenarioId}/p$($row.pageNumber): missing generated TOA page-number evidence")
            }

            $toaSignatures = @($toa.pageReferenceSignatures)
            foreach ($signature in $requiredToaSignatures) {
                if ($toaSignatures -notcontains $signature) {
                    $failures.Add("${hostId}/${requiredScenarioId}/p$($row.pageNumber): missing TOA signature '$signature'")
                }
            }

            $verifiedSemanticRows++
        }

        $comparisonRows = @($baselineComparisons | Where-Object {
            $_.hostId -eq $hostId -and
            $_.scenarioId -eq $requiredScenarioId -and
            $_.baselineScenarioId -eq $requiredScenarioId
        })
        if ($comparisonRows.Count -eq 0) {
            $failures.Add("${hostId}/${requiredScenarioId}: missing Word-baseline policy row")
            continue
        }

        foreach ($comparison in $comparisonRows) {
            if ($comparison.trust.passed -ne $true) {
                $notes = @($comparison.trust.failures) -join '; '
                if ([string]::IsNullOrWhiteSpace($notes)) {
                    $notes = [string]$comparison.skipReason
                }
                $failures.Add("$hostId/$requiredScenarioId/$($comparison.outputName): baseline policy trust failed ($notes)")
                continue
            }

            $baselineId = [string]$comparison.baselineId
            if (-not $baselineId.StartsWith($requiredScenarioId + '/', [StringComparison]::OrdinalIgnoreCase)) {
                $failures.Add("$hostId/$requiredScenarioId/$($comparison.outputName): baselineId '$baselineId' expected scenario '$requiredScenarioId'")
                continue
            }

            $verifiedBaselineRows++
        }
    }

    $unavailableComparisons = @($baselineComparisons | Where-Object {
        $_.scenarioId -eq $requiredScenarioId -and
        $_.status -eq 'word-baseline-unavailable'
    })
    if ($unavailableComparisons.Count -gt 0) {
        $blocker = @($remainingBlockers | Where-Object {
            $_.blockerId -eq 'references-heavy-toa-page-number-fidelity' -and
            $_.scenarioId -eq $requiredScenarioId -and
            $_.status -eq 'word-baseline-unavailable' -and
            $_.requiresWordBaseline -eq $true
        }) | Select-Object -First 1
        if (-not $blocker) {
            $failures.Add("${requiredScenarioId}: missing honest word-baseline-unavailable TOA page-number blocker")
        }
    }

    if ($failures.Count -gt 0) {
        throw "References-heavy Word baseline proof readiness failed:`n - $($failures -join "`n - ")"
    }

    Write-Host "References-heavy field/TOA proof readiness rows: verified rows=$verifiedReadinessRows"
    Write-Host "References-heavy Word baseline proof readiness: trusted scenario rows=$trustedScenarioRows"
    Write-Host "References-heavy semantic field/TOA rows: verified rows=$verifiedSemanticRows"
    Write-Host "References-heavy Word-baseline policy rows: verified rows=$verifiedBaselineRows"
    if ($unavailableComparisons.Count -gt 0) {
        Write-Host "References-heavy Word-baseline unavailable blocker: verified"
    }
}

function Assert-LegalReferenceSectionPageProofReadiness {
    param(
        [Parameter(Mandatory = $true)][string]$SummaryJsonPath,
        [Parameter(Mandatory = $true)][string[]]$ScenarioIds
    )

    $scenarioId = 'legal-reference-section-page-numbers'
    if (-not ($ScenarioIds -contains $scenarioId)) {
        return
    }

    if (-not (Test-Path -LiteralPath $SummaryJsonPath -PathType Leaf)) {
        throw "Legal-reference section page-number proof readiness cannot be checked because the summary JSON is missing: $SummaryJsonPath"
    }

    $summary = Get-Content -LiteralPath $SummaryJsonPath -Raw | ConvertFrom-Json
    if ([int]$summary.schemaVersion -lt 39) {
        throw "Legal-reference section page-number proof readiness requires FreeW visual evidence summary schema v39 or newer, found v$($summary.schemaVersion)"
    }

    $readinessRows = @($summary.legalReferenceProofReadiness)
    $scenarios = @($summary.scenarios)
    $evidenceRows = @($summary.evidence)
    $baselineComparisons = @($summary.baselineComparisons)
    $remainingBlockers = @($summary.remainingEvidenceBlockers)
    $summaryFailures = @($summary.trust.failures)
    $requiredHosts = @(
        'wpf-fidelity-render',
        'avalonia-page-layout-shot'
    )
    $requiredSignatures = @(
        'category=Cases|entry=Matter of Sectioned Pages, 101 F. Supp. 3d 2026 (D. FreeW)|kind=section-formatted-page-numbers|pages=1,2|text=i, 1',
        'category=Statutes|entry=Restart Numbering Act, 7 FreeW Code 13|kind=explicit-page-numbers|pages=2|text=1'
    )
    $failures = New-Object System.Collections.Generic.List[string]
    $trustedScenarioRows = 0
    $verifiedSemanticRows = 0
    $verifiedBaselineRows = 0
    $verifiedUnavailableBlockers = 0

    $proofRows = @($readinessRows | Where-Object { $_.scenarioId -eq $scenarioId })
    if ($proofRows.Count -eq 0) {
        $failures.Add("${scenarioId}: missing legal-reference section page-number proof readiness row")
    }
    foreach ($proofRow in $proofRows) {
        if ($proofRow.trust.passed -ne $true -or $proofRow.status -ne 'paired-renderer-proof-ready') {
            $notes = @($proofRow.trust.failures) -join '; '
            if ([string]::IsNullOrWhiteSpace($notes)) {
                $notes = [string]$proofRow.baselineReadiness
            }
            $failures.Add("${scenarioId}/p$($proofRow.pageNumber): readiness status '$($proofRow.status)' failed ($notes)")
        }

        if ([string]::IsNullOrWhiteSpace([string]$proofRow.semanticEvidence) -or [string]$proofRow.semanticEvidence -eq '-') {
            $failures.Add("${scenarioId}/p$($proofRow.pageNumber): missing legal-reference semantic readiness summary")
        }
    }

    $semanticFailures = @($summaryFailures | Where-Object {
        ([string]$_).IndexOf("field renderer pair '$scenarioId'", [StringComparison]::Ordinal) -ge 0 -or
        ([string]$_).IndexOf('legal-reference', [StringComparison]::OrdinalIgnoreCase) -ge 0
    })
    foreach ($semanticFailure in $semanticFailures) {
        $failures.Add("${scenarioId}: WPF/Avalonia legal-reference semantic parity drift ($semanticFailure)")
    }

    foreach ($hostId in $requiredHosts) {
        $scenarioRows = @($scenarios | Where-Object {
            $_.hostId -eq $hostId -and
            $_.scenarioId -eq $scenarioId
        })
        if ($scenarioRows.Count -eq 0) {
            $failures.Add("${hostId}/${scenarioId}: missing normalized scenario row")
            continue
        }

        $scenarioRow = $scenarioRows[0]
        if ($scenarioRow.trust.passed -ne $true) {
            $notes = @($scenarioRow.trust.failures) -join '; '
            if ([string]::IsNullOrWhiteSpace($notes)) {
                $notes = 'no notes'
            }
            $failures.Add("${hostId}/${scenarioId}: scenario trust failed ($notes)")
            continue
        }

        if ([int]$scenarioRow.trustedOutputs -lt 2) {
            $failures.Add("${hostId}/${scenarioId}: expected at least 2 trusted outputs, found $($scenarioRow.trustedOutputs)")
            continue
        }

        $trustedScenarioRows++

        $hostEvidenceRows = @($evidenceRows | Where-Object {
            $_.hostId -eq $hostId -and
            $_.scenarioId -eq $scenarioId -and
            $_.trust.passed -eq $true
        })
        if ($hostEvidenceRows.Count -eq 0) {
            $failures.Add("${hostId}/${scenarioId}: missing trusted normalized evidence row for legal-reference TOA semantics")
        }
        foreach ($row in $hostEvidenceRows) {
            $fields = $row.fields
            $toa = $row.tableOfAuthorities
            if (@($fields.complexFieldKeywords | Where-Object { $_ -eq 'TOA' }).Count -eq 0) {
                $failures.Add("${hostId}/${scenarioId}/p$($row.pageNumber): missing TOA complex field keyword")
                continue
            }
            if (@($fields.complexFieldResultSignatures | Where-Object { $_ -eq 'TOA=Cases\ti, 1' }).Count -eq 0) {
                $failures.Add("${hostId}/${scenarioId}/p$($row.pageNumber): missing cached TOA displayed page-reference sentinel")
                continue
            }
            if ($toa.hasGeneratedTable -ne $true -or [int]$toa.entryWithPageReferenceCount -lt 2 -or $toa.hasExplicitPageNumbers -ne $true) {
                $failures.Add("${hostId}/${scenarioId}/p$($row.pageNumber): missing generated TOA page-reference metadata")
                continue
            }
            foreach ($signature in $requiredSignatures) {
                if (@($toa.pageReferenceSignatures | Where-Object { $_ -eq $signature }).Count -eq 0) {
                    $failures.Add("${hostId}/${scenarioId}/p$($row.pageNumber): missing generated page-reference signature '$signature'")
                    continue
                }
            }
            if (@($toa.pageReferences | Where-Object {
                $_.pageReferenceKind -eq 'section-formatted-page-numbers' -and
                @($_.pageNumbers | Where-Object { [int]$_ -eq 1 }).Count -gt 0 -and
                @($_.pageNumbers | Where-Object { [int]$_ -eq 2 }).Count -gt 0 -and
                @($_.displayedPageReferences | Where-Object { $_ -eq 'i' }).Count -gt 0 -and
                @($_.displayedPageReferences | Where-Object { $_ -eq '1' }).Count -gt 0
            }).Count -eq 0) {
                $failures.Add("${hostId}/${scenarioId}/p$($row.pageNumber): missing section-formatted page-reference evidence for displayed pages i and 1")
                continue
            }

            $verifiedSemanticRows++
        }

        $comparisonRows = @($baselineComparisons | Where-Object {
            $_.hostId -eq $hostId -and
            $_.scenarioId -eq $scenarioId -and
            $_.baselineScenarioId -eq $scenarioId
        })
        if ($baselineComparisons.Count -gt 0 -and $comparisonRows.Count -eq 0) {
            $failures.Add("${hostId}/${scenarioId}: missing Word-baseline policy row")
            continue
        }

        foreach ($comparison in $comparisonRows) {
            if ($comparison.trust.passed -ne $true) {
                $notes = @($comparison.trust.failures) -join '; '
                if ([string]::IsNullOrWhiteSpace($notes)) {
                    $notes = [string]$comparison.skipReason
                }
                $failures.Add("$hostId/$scenarioId/$($comparison.outputName): baseline policy trust failed ($notes)")
                continue
            }

            $baselineId = [string]$comparison.baselineId
            if (-not $baselineId.StartsWith($scenarioId + '/', [StringComparison]::OrdinalIgnoreCase)) {
                $failures.Add("$hostId/$scenarioId/$($comparison.outputName): baselineId '$baselineId' expected scenario '$scenarioId'")
                continue
            }

            $verifiedBaselineRows++
        }
    }

    $unavailableComparisons = @($baselineComparisons | Where-Object {
        $_.scenarioId -eq $scenarioId -and
        $_.status -eq 'word-baseline-unavailable'
    })
    if ($unavailableComparisons.Count -gt 0) {
        $blocker = @($remainingBlockers | Where-Object {
            $_.blockerId -eq 'legal-reference-section-page-number-fidelity' -and
            $_.scenarioId -eq $scenarioId -and
            $_.status -eq 'word-baseline-unavailable' -and
            $_.requiresWordBaseline -eq $true
        }) | Select-Object -First 1
        if (-not $blocker) {
            $failures.Add("${scenarioId}: missing honest word-baseline-unavailable legal-reference blocker")
        }
        else {
            $verifiedUnavailableBlockers++
        }
    }

    if ($failures.Count -gt 0) {
        throw "Legal-reference section page-number proof readiness failed:`n - $($failures -join "`n - ")"
    }

    Write-Host "Legal-reference section page-number proof readiness: trusted scenario rows=$trustedScenarioRows"
    Write-Host "Legal-reference section page-number semantic rows: verified rows=$verifiedSemanticRows"
    if ($baselineComparisons.Count -gt 0) {
        Write-Host "Legal-reference section page-number Word-baseline policy rows: verified rows=$verifiedBaselineRows"
    }
    else {
        Write-Host "Legal-reference section page-number Word-baseline policy rows: no Word baseline mode requested"
    }
    if ($verifiedUnavailableBlockers -gt 0) {
        Write-Host "Legal-reference section page-number Word-baseline unavailable blocker: verified"
    }
}

function Assert-PageCompositionProofReadiness {
    param(
        [Parameter(Mandatory = $true)][string]$SummaryJsonPath,
        [Parameter(Mandatory = $true)][string[]]$ScenarioIds
    )

    $requiredScenarioIds = @(
        'f2-columns',
        'f2-border-watermark',
        'page-composition-columns',
        'page-composition-border-watermark'
    )
    if (@($ScenarioIds).Count -eq 0 -or @($requiredScenarioIds | Where-Object { $ScenarioIds -notcontains $_ }).Count -gt 0) {
        return
    }

    if (-not (Test-Path -LiteralPath $SummaryJsonPath -PathType Leaf)) {
        throw "Page composition proof readiness cannot be checked because the summary JSON is missing: $SummaryJsonPath"
    }

    $summary = Get-Content -LiteralPath $SummaryJsonPath -Raw | ConvertFrom-Json
    if ([int]$summary.schemaVersion -lt 25) {
        throw "Page composition proof readiness requires FreeW visual evidence summary schema v25 or newer, found v$($summary.schemaVersion)"
    }

    $scenarios = @($summary.scenarios)
    $baselineComparisons = @($summary.baselineComparisons)
    $requirements = @(
        [pscustomobject]@{
            HostId = 'wpf-fidelity-render'
            ScenarioId = 'f2-columns'
            BaselineScenarioId = 'f2-columns'
            MinimumTrustedOutputs = 2
        },
        [pscustomobject]@{
            HostId = 'wpf-fidelity-render'
            ScenarioId = 'f2-border-watermark'
            BaselineScenarioId = 'f2-border-watermark'
            MinimumTrustedOutputs = 1
        },
        [pscustomobject]@{
            HostId = 'avalonia-page-layout-shot'
            ScenarioId = 'page-composition-columns'
            BaselineScenarioId = 'f2-columns'
            MinimumTrustedOutputs = 1
        },
        [pscustomobject]@{
            HostId = 'avalonia-page-layout-shot'
            ScenarioId = 'page-composition-border-watermark'
            BaselineScenarioId = 'f2-border-watermark'
            MinimumTrustedOutputs = 1
        }
    )
    $failures = New-Object System.Collections.Generic.List[string]
    $trustedScenarioRows = 0
    $verifiedBaselineRows = 0

    foreach ($requirement in $requirements) {
        $match = @($scenarios | Where-Object {
            $_.hostId -eq $requirement.HostId -and
            $_.scenarioId -eq $requirement.ScenarioId
        })

        if ($match.Count -eq 0) {
            $failures.Add("$($requirement.HostId)/$($requirement.ScenarioId): missing normalized scenario row")
            continue
        }

        $row = $match[0]
        if ($row.trust.passed -ne $true) {
            $notes = @($row.trust.failures) -join '; '
            if ([string]::IsNullOrWhiteSpace($notes)) {
                $notes = 'no notes'
            }
            $failures.Add("$($requirement.HostId)/$($requirement.ScenarioId): scenario trust failed ($notes)")
            continue
        }

        if ([int]$row.trustedOutputs -lt [int]$requirement.MinimumTrustedOutputs) {
            $failures.Add("$($requirement.HostId)/$($requirement.ScenarioId): expected at least $($requirement.MinimumTrustedOutputs) trusted output(s), found $($row.trustedOutputs)")
            continue
        }

        $trustedScenarioRows++

        $comparisonRows = @($baselineComparisons | Where-Object {
            $_.hostId -eq $requirement.HostId -and
            $_.scenarioId -eq $requirement.ScenarioId -and
            $_.baselineScenarioId -eq $requirement.BaselineScenarioId
        })
        if ($comparisonRows.Count -eq 0) {
            $failures.Add("$($requirement.HostId)/$($requirement.ScenarioId): missing Word-baseline policy row for $($requirement.BaselineScenarioId)")
            continue
        }

        foreach ($comparison in $comparisonRows) {
            if ($comparison.trust.passed -ne $true) {
                $notes = @($comparison.trust.failures) -join '; '
                if ([string]::IsNullOrWhiteSpace($notes)) {
                    $notes = [string]$comparison.skipReason
                }
                $failures.Add("$($requirement.HostId)/$($requirement.ScenarioId)/$($comparison.outputName): baseline policy trust failed ($notes)")
                continue
            }

            $baselineId = [string]$comparison.baselineId
            if (-not $baselineId.StartsWith($requirement.BaselineScenarioId + '/', [StringComparison]::OrdinalIgnoreCase)) {
                $failures.Add("$($requirement.HostId)/$($requirement.ScenarioId)/$($comparison.outputName): baselineId '$baselineId' expected scenario '$($requirement.BaselineScenarioId)'")
                continue
            }

            $verifiedBaselineRows++
        }
    }

    if ($failures.Count -gt 0) {
        throw "Page composition proof readiness failed:`n - $($failures -join "`n - ")"
    }

    Write-Host "Page composition proof readiness: trusted scenario rows=$trustedScenarioRows"
    Write-Host "Page composition Word-baseline policy rows: verified rows=$verifiedBaselineRows"
}

function Assert-FloatingWrappingProofReadiness {
    param(
        [Parameter(Mandatory = $true)][string]$SummaryJsonPath,
        [Parameter(Mandatory = $true)][string[]]$ScenarioIds
    )

    $requiredScenarioIds = @(
        'f2-01-float-wrap',
        'page-composition-floating-image'
    )
    if (@($ScenarioIds).Count -gt 0 -and @($requiredScenarioIds | Where-Object { $ScenarioIds -contains $_ }).Count -eq 0) {
        return
    }

    if (@($ScenarioIds).Count -gt 0 -and @($requiredScenarioIds | Where-Object { $ScenarioIds -notcontains $_ }).Count -gt 0) {
        return
    }

    if (-not (Test-Path -LiteralPath $SummaryJsonPath -PathType Leaf)) {
        throw "Floating/wrapping proof readiness cannot be checked because the summary JSON is missing: $SummaryJsonPath"
    }

    $summary = Get-Content -LiteralPath $SummaryJsonPath -Raw | ConvertFrom-Json
    if ([int]$summary.schemaVersion -lt 29) {
        throw "Floating/wrapping proof readiness requires FreeW visual evidence summary schema v29 or newer, found v$($summary.schemaVersion)"
    }

    $readinessRows = @($summary.floatingWrappingProofReadiness)
    $row = @($readinessRows | Where-Object {
        $_.scenarioId -eq 'floating-wrapping-visual-proof' -and
        [int]$_.pageNumber -eq 1
    }) | Select-Object -First 1
    if (-not $row) {
        throw "Floating/wrapping proof readiness failed: missing readiness row"
    }

    $failures = New-Object System.Collections.Generic.List[string]
    if ($row.trust.passed -ne $true -or $row.status -ne 'paired-renderer-proof-ready') {
        $notes = @($row.trust.failures) -join '; '
        if ([string]::IsNullOrWhiteSpace($notes)) {
            $notes = [string]$row.baselineReadiness
        }
        $failures.Add("readiness status '$($row.status)' failed ($notes)")
    }

    if ([string]$row.wpfScenarioId -ne 'f2-01-float-wrap') {
        $failures.Add("wpfScenarioId '$($row.wpfScenarioId)' expected 'f2-01-float-wrap'")
    }

    if ([string]$row.avaloniaScenarioId -ne 'page-composition-floating-image') {
        $failures.Add("avaloniaScenarioId '$($row.avaloniaScenarioId)' expected 'page-composition-floating-image'")
    }

    $semanticEvidence = [string]$row.semanticEvidence
    if ($semanticEvidence.IndexOf('WPF', [StringComparison]::Ordinal) -lt 0 -or
        $semanticEvidence.IndexOf('Avalonia', [StringComparison]::Ordinal) -lt 0 -or
        $semanticEvidence.IndexOf('wraps=', [StringComparison]::Ordinal) -lt 0) {
        $failures.Add("missing WPF/Avalonia floating wrap semantic evidence")
    }

    $baselineRows = @($summary.baselineComparisons | Where-Object {
        [int]$_.pageNumber -eq 1 -and
        ($_.scenarioId -eq 'f2-01-float-wrap' -or $_.scenarioId -eq 'page-composition-floating-image')
    })
    if ($baselineRows.Count -gt 0) {
        foreach ($comparison in $baselineRows) {
            if ($comparison.trust.passed -ne $true) {
                $failures.Add("$($comparison.hostId)/$($comparison.scenarioId)/$($comparison.outputName): baseline policy trust failed")
            }
            if ([string]$comparison.baselineScenarioId -ne 'f2-01-float-wrap') {
                $failures.Add("$($comparison.hostId)/$($comparison.scenarioId)/$($comparison.outputName): baselineScenarioId '$($comparison.baselineScenarioId)' expected 'f2-01-float-wrap'")
            }
        }
    }

    if ($failures.Count -gt 0) {
        throw "Floating/wrapping proof readiness failed:`n - $($failures -join "`n - ")"
    }

    Write-Host "Floating/wrapping proof readiness: trusted paired row=1"
    if ($baselineRows.Count -gt 0) {
        Write-Host "Floating/wrapping Word-baseline policy rows: verified rows=$($baselineRows.Count)"
    }
    else {
        Write-Host "Floating/wrapping Word-baseline policy rows: no Word baseline mode requested"
    }
}

function Assert-HeaderFooterImageVisualProofReadiness {
    param(
        [Parameter(Mandatory = $true)][string]$SummaryJsonPath,
        [Parameter(Mandatory = $true)][string[]]$ScenarioIds
    )

    $scenarioId = 'f2-hf-images'
    if ($ScenarioIds -notcontains $scenarioId) {
        return
    }

    if (-not (Test-Path -LiteralPath $SummaryJsonPath -PathType Leaf)) {
        throw "Header/footer image visual proof readiness cannot be checked because the summary JSON is missing: $SummaryJsonPath"
    }

    $summary = Get-Content -LiteralPath $SummaryJsonPath -Raw | ConvertFrom-Json
    if ([int]$summary.schemaVersion -lt 43) {
        throw "Header/footer image visual proof readiness requires FreeW visual evidence summary schema v43 or newer, found v$($summary.schemaVersion)"
    }

    $readinessRows = @($summary.headerFooterImageProofReadiness | Where-Object { $_.scenarioId -eq $scenarioId })
    $scenarios = @($summary.scenarios)
    $baselineComparisons = @($summary.baselineComparisons)
    $blockers = @($summary.remainingEvidenceBlockers)
    $summaryFailures = @($summary.trust.failures)
    $requiredHosts = @(
        'wpf-fidelity-render',
        'avalonia-page-layout-shot'
    )
    $failures = New-Object System.Collections.Generic.List[string]
    $trustedScenarioRows = 0
    $verifiedSemanticRows = 0
    $verifiedBaselineRows = 0

    if ($readinessRows.Count -lt 2) {
        $failures.Add("${scenarioId}: expected at least two header/footer image proof readiness rows")
    }

    foreach ($proofRow in $readinessRows) {
        if ($proofRow.trust.passed -ne $true -or $proofRow.status -ne 'paired-renderer-proof-ready') {
            $notes = @($proofRow.trust.failures) -join '; '
            if ([string]::IsNullOrWhiteSpace($notes)) {
                $notes = [string]$proofRow.baselineReadiness
            }
            $failures.Add("${scenarioId}/p$($proofRow.pageNumber): readiness status '$($proofRow.status)' failed ($notes)")
        }

        $semantic = [string]$proofRow.semanticEvidence
        if ([string]::IsNullOrWhiteSpace($semantic) -or $semantic -eq '-') {
            $failures.Add("${scenarioId}/p$($proofRow.pageNumber): missing header/footer image semantic readiness summary")
        }
        elseif ($semantic -notlike '*header/footer image(s)*' -or $semantic -notlike '*image slots=*') {
            $failures.Add("${scenarioId}/p$($proofRow.pageNumber): missing header/footer image slot semantic evidence")
        }
        else {
            $verifiedSemanticRows++
        }
    }

    $semanticFailures = @($summaryFailures | Where-Object {
        ([string]$_).IndexOf("header/footer renderer pair '$scenarioId'", [StringComparison]::Ordinal) -ge 0
    })
    foreach ($semanticFailure in $semanticFailures) {
        $failures.Add("${scenarioId}: WPF/Avalonia semantic parity drift ($semanticFailure)")
    }

    foreach ($hostId in $requiredHosts) {
        $match = @($scenarios | Where-Object {
            $_.hostId -eq $hostId -and
            $_.scenarioId -eq $scenarioId
        })

        if ($match.Count -eq 0) {
            $failures.Add("${hostId}/${scenarioId}: missing normalized scenario row")
            continue
        }

        $row = $match[0]
        if ($row.trust.passed -ne $true) {
            $notes = @($row.trust.failures) -join '; '
            if ([string]::IsNullOrWhiteSpace($notes)) {
                $notes = 'no notes'
            }
            $failures.Add("${hostId}/${scenarioId}: scenario trust failed ($notes)")
            continue
        }

        if ([int]$row.trustedOutputs -lt 2) {
            $failures.Add("${hostId}/${scenarioId}: expected at least two trusted header/footer image outputs, found $($row.trustedOutputs)")
            continue
        }

        $trustedScenarioRows++
    }

    if ($baselineComparisons.Count -gt 0) {
        foreach ($hostId in $requiredHosts) {
            $matches = @($baselineComparisons | Where-Object {
                $_.hostId -eq $hostId -and
                $_.scenarioId -eq $scenarioId
            })
            if ($matches.Count -eq 0) {
                $failures.Add("${hostId}/${scenarioId}: missing Word-baseline policy row")
                continue
            }

            foreach ($comparison in $matches) {
                if ([string]$comparison.baselineScenarioId -ne $scenarioId) {
                    $failures.Add("${hostId}/${scenarioId}/p$($comparison.pageNumber): unexpected baseline scenario '$($comparison.baselineScenarioId)'")
                    continue
                }

                $verifiedBaselineRows++
            }
        }

        $hasWordBaselineUnavailable = @($baselineComparisons | Where-Object {
            $_.scenarioId -eq $scenarioId -and
            $_.status -eq 'word-baseline-unavailable'
        }).Count -gt 0
        if ($hasWordBaselineUnavailable) {
            $blockerRows = @($blockers | Where-Object {
                $_.blockerId -eq 'f2-hf-images-word-baseline-fidelity' -and
                $_.scenarioId -eq $scenarioId -and
                $_.status -eq 'word-baseline-unavailable' -and
                $_.requiresWordBaseline -eq $true
            })
            if ($blockerRows.Count -eq 0) {
                $failures.Add("${scenarioId}: missing honest word-baseline-unavailable header/footer image blocker")
            }
        }
    }

    if ($failures.Count -gt 0) {
        throw "Header/footer image visual proof readiness failed:`n - $($failures -join "`n - ")"
    }

    Write-Host "Header/footer image visual proof readiness: trusted scenario rows=$trustedScenarioRows"
    Write-Host "Header/footer image semantic rows: verified rows=$verifiedSemanticRows"
    if ($baselineComparisons.Count -gt 0) {
        Write-Host "Header/footer image Word-baseline policy rows: verified rows=$verifiedBaselineRows"
    }
    else {
        Write-Host "Header/footer image Word-baseline policy rows: no Word baseline mode requested"
    }
}

function Assert-TableLayoutProofReadiness {
    param(
        [Parameter(Mandatory = $true)][string]$SummaryJsonPath,
        [Parameter(Mandatory = $true)][string[]]$ScenarioIds
    )

    $requiredScenarioIds = @(
        'table-layout-complex',
        'table-pagination-repeat-header',
        'table-page-composition-stress'
    )
    if (@($ScenarioIds).Count -eq 0 -or @($requiredScenarioIds | Where-Object { $ScenarioIds -notcontains $_ }).Count -gt 0) {
        return
    }

    if (-not (Test-Path -LiteralPath $SummaryJsonPath -PathType Leaf)) {
        throw "Table layout proof readiness cannot be checked because the summary JSON is missing: $SummaryJsonPath"
    }

    $summary = Get-Content -LiteralPath $SummaryJsonPath -Raw | ConvertFrom-Json
    if ([int]$summary.schemaVersion -lt 25) {
        throw "Table layout proof readiness requires FreeW visual evidence summary schema v25 or newer, found v$($summary.schemaVersion)"
    }

    $scenarios = @($summary.scenarios)
    $baselineComparisons = @($summary.baselineComparisons)
    $requiredHosts = @(
        'wpf-fidelity-render',
        'avalonia-page-layout-shot'
    )
    $minimumTrustedOutputsByScenario = @{
        'table-layout-complex' = 1
        'table-pagination-repeat-header' = 2
        'table-page-composition-stress' = 3
    }
    $failures = New-Object System.Collections.Generic.List[string]
    $trustedScenarioRows = 0
    $verifiedBaselineRows = 0

    foreach ($scenarioId in $requiredScenarioIds) {
        foreach ($hostId in $requiredHosts) {
            $match = @($scenarios | Where-Object {
                $_.hostId -eq $hostId -and
                $_.scenarioId -eq $scenarioId
            })

            if ($match.Count -eq 0) {
                $failures.Add("${hostId}/${scenarioId}: missing normalized scenario row")
                continue
            }

            $row = $match[0]
            if ($row.trust.passed -ne $true) {
                $notes = @($row.trust.failures) -join '; '
                if ([string]::IsNullOrWhiteSpace($notes)) {
                    $notes = 'no notes'
                }
                $failures.Add("${hostId}/${scenarioId}: scenario trust failed ($notes)")
                continue
            }

            $minimumTrustedOutputs = [int]$minimumTrustedOutputsByScenario[$scenarioId]
            if ([int]$row.trustedOutputs -lt $minimumTrustedOutputs) {
                $failures.Add("${hostId}/${scenarioId}: expected at least $minimumTrustedOutputs trusted output(s), found $($row.trustedOutputs)")
                continue
            }

            $trustedScenarioRows++

            $comparisonRows = @($baselineComparisons | Where-Object {
                $_.hostId -eq $hostId -and
                $_.scenarioId -eq $scenarioId -and
                $_.baselineScenarioId -eq $scenarioId
            })
            if ($comparisonRows.Count -eq 0) {
                $failures.Add("${hostId}/${scenarioId}: missing Word-baseline policy row")
                continue
            }

            foreach ($comparison in $comparisonRows) {
                if ($comparison.trust.passed -ne $true) {
                    $notes = @($comparison.trust.failures) -join '; '
                    if ([string]::IsNullOrWhiteSpace($notes)) {
                        $notes = [string]$comparison.skipReason
                    }
                    $failures.Add("$hostId/$scenarioId/$($comparison.outputName): baseline policy trust failed ($notes)")
                    continue
                }

                $baselineId = [string]$comparison.baselineId
                if (-not $baselineId.StartsWith($scenarioId + '/', [StringComparison]::OrdinalIgnoreCase)) {
                    $failures.Add("$hostId/$scenarioId/$($comparison.outputName): baselineId '$baselineId' expected scenario '$scenarioId'")
                    continue
                }

                $verifiedBaselineRows++
            }
        }
    }

    if ($failures.Count -gt 0) {
        throw "Table layout proof readiness failed:`n - $($failures -join "`n - ")"
    }

    Write-Host "Table layout proof readiness: trusted scenario rows=$trustedScenarioRows"
    Write-Host "Table layout Word-baseline policy rows: verified rows=$verifiedBaselineRows"
}

function Assert-TablePaginationPageCompositionProofReadiness {
    param(
        [Parameter(Mandatory = $true)][string]$SummaryJsonPath,
        [Parameter(Mandatory = $true)][string[]]$ScenarioIds
    )

    $requiredScenarioIds = @(
        'table-pagination-repeat-header',
        'table-page-composition-stress'
    )
    $selectedScenarioIds = @($requiredScenarioIds | Where-Object { $ScenarioIds -contains $_ })
    if ($selectedScenarioIds.Count -eq 0) {
        return
    }

    if (-not (Test-Path -LiteralPath $SummaryJsonPath -PathType Leaf)) {
        throw "Table pagination/page composition proof readiness cannot be checked because the summary JSON is missing: $SummaryJsonPath"
    }

    $summary = Get-Content -LiteralPath $SummaryJsonPath -Raw | ConvertFrom-Json
    if ([int]$summary.schemaVersion -lt 49) {
        throw "Table pagination/page composition proof readiness requires FreeW visual evidence summary schema v49 or newer, found v$($summary.schemaVersion)"
    }

    $readinessRows = @($summary.tablePaginationProofReadiness)
    $scenarios = @($summary.scenarios)
    $baselineComparisons = @($summary.baselineComparisons)
    $blockers = @($summary.remainingEvidenceBlockers)
    $requiredHosts = @(
        'wpf-fidelity-render',
        'avalonia-page-layout-shot'
    )
    $minimumTrustedOutputsByScenario = @{
        'table-pagination-repeat-header' = 2
        'table-page-composition-stress' = 3
    }
    $failures = New-Object System.Collections.Generic.List[string]
    $trustedScenarioRows = 0
    $verifiedReadinessRows = 0
    $verifiedBaselineRows = 0

    foreach ($scenarioId in $selectedScenarioIds) {
        $requiredPages = 1..([int]$minimumTrustedOutputsByScenario[$scenarioId])

        foreach ($proofRow in @($readinessRows | Where-Object { $_.scenarioId -eq $scenarioId })) {
            if ($proofRow.status -ne 'paired-renderer-proof-ready') {
                $notes = @($proofRow.trust.failures) -join '; '
                if ([string]::IsNullOrWhiteSpace($notes)) {
                    $notes = [string]$proofRow.baselineReadiness
                }
                $failures.Add("${scenarioId}/p$($proofRow.pageNumber): readiness status '$($proofRow.status)' failed ($notes)")
                continue
            }

            if ([string]$proofRow.semanticEvidence -notmatch 'repeatedHeaderPages=') {
                $failures.Add("${scenarioId}/p$($proofRow.pageNumber): missing repeated-header pagination semantic evidence")
            }

            if ([string]$proofRow.semanticEvidence -notmatch 'keepRows=1') {
                $failures.Add("${scenarioId}/p$($proofRow.pageNumber): missing keep-together row semantic evidence")
            }

            if ([string]$proofRow.semanticEvidence -notmatch 'rowCells=' -or
                [string]$proofRow.semanticEvidence -notmatch 'tableSig=' -or
                [string]$proofRow.semanticEvidence -notmatch 'paginationSig=') {
                $failures.Add("${scenarioId}/p$($proofRow.pageNumber): missing deterministic table semantic evidence fingerprints")
            }

            if ($scenarioId -eq 'table-page-composition-stress') {
                if ([string]$proofRow.semanticEvidence -notmatch 'page-border' -or
                    [string]$proofRow.semanticEvidence -notmatch 'watermark' -or
                    [string]$proofRow.semanticEvidence -notmatch 'header-footer' -or
                    [string]$proofRow.semanticEvidence -notmatch 'NUMPAGES') {
                    $failures.Add("${scenarioId}/p$($proofRow.pageNumber): missing page composition semantic evidence")
                }
            }

            if ($proofRow.trust.passed -ne $true) {
                $notes = @($proofRow.trust.failures) -join '; '
                $failures.Add("${scenarioId}/p$($proofRow.pageNumber): readiness trust failed ($notes)")
            }

            $verifiedReadinessRows++
        }

        foreach ($pageNumber in $requiredPages) {
            $proofRows = @($readinessRows | Where-Object {
                $_.scenarioId -eq $scenarioId -and
                [int]$_.pageNumber -eq [int]$pageNumber
            })
            if ($proofRows.Count -eq 0) {
                $failures.Add("${scenarioId}/p${pageNumber}: missing table pagination/page composition readiness row")
            }
        }

        foreach ($hostId in $requiredHosts) {
            $scenarioRows = @($scenarios | Where-Object {
                $_.hostId -eq $hostId -and
                $_.scenarioId -eq $scenarioId
            })
            if ($scenarioRows.Count -eq 0) {
                $failures.Add("${hostId}/${scenarioId}: missing normalized scenario row")
                continue
            }

            $scenarioRow = $scenarioRows[0]
            if ($scenarioRow.trust.passed -ne $true) {
                $notes = @($scenarioRow.trust.failures) -join '; '
                $failures.Add("${hostId}/${scenarioId}: scenario trust failed ($notes)")
                continue
            }

            $minimumTrustedOutputs = [int]$minimumTrustedOutputsByScenario[$scenarioId]
            if ([int]$scenarioRow.trustedOutputs -lt $minimumTrustedOutputs) {
                $failures.Add("${hostId}/${scenarioId}: expected at least $minimumTrustedOutputs trusted output(s), found $($scenarioRow.trustedOutputs)")
                continue
            }

            $trustedScenarioRows++

            $comparisonRows = @($baselineComparisons | Where-Object {
                $_.hostId -eq $hostId -and
                $_.scenarioId -eq $scenarioId -and
                $_.baselineScenarioId -eq $scenarioId
            })
            if ($comparisonRows.Count -eq 0) {
                $failures.Add("${hostId}/${scenarioId}: missing Word-baseline policy row")
                continue
            }

            foreach ($comparison in $comparisonRows) {
                if ($comparison.trust.passed -ne $true) {
                    $notes = @($comparison.trust.failures) -join '; '
                    if ([string]::IsNullOrWhiteSpace($notes)) {
                        $notes = [string]$comparison.skipReason
                    }
                    $failures.Add("$hostId/$scenarioId/$($comparison.outputName): baseline policy trust failed ($notes)")
                    continue
                }

                $baselineId = [string]$comparison.baselineId
                if (-not $baselineId.StartsWith($scenarioId + '/', [StringComparison]::OrdinalIgnoreCase)) {
                    $failures.Add("$hostId/$scenarioId/$($comparison.outputName): baselineId '$baselineId' expected scenario '$scenarioId'")
                    continue
                }

                $verifiedBaselineRows++
            }
        }

        $hasWordBaselineUnavailable = @($baselineComparisons | Where-Object {
            $_.scenarioId -eq $scenarioId -and
            $_.status -eq 'word-baseline-unavailable'
        }).Count -gt 0
        if ($hasWordBaselineUnavailable) {
            $blockerRows = @($blockers | Where-Object {
                $_.blockerId -eq "${scenarioId}-word-baseline-fidelity" -and
                $_.scenarioId -eq $scenarioId -and
                $_.status -eq 'word-baseline-unavailable' -and
                $_.requiresWordBaseline -eq $true
            })
            if ($blockerRows.Count -eq 0) {
                $failures.Add("${scenarioId}: missing honest word-baseline-unavailable table pagination blocker")
            }
        }
    }

    if ($failures.Count -gt 0) {
        throw "Table pagination/page composition proof readiness failed:`n - $($failures -join "`n - ")"
    }

    Write-Host "Table pagination/page composition proof readiness: trusted scenario rows=$trustedScenarioRows"
    Write-Host "Table pagination/page composition semantic rows: verified rows=$verifiedReadinessRows"
    Write-Host "Table pagination/page composition Word-baseline policy rows: verified rows=$verifiedBaselineRows"
}

function Assert-DrawingObjectVisualProofReadiness {
    param(
        [Parameter(Mandatory = $true)][string]$SummaryJsonPath,
        [Parameter(Mandatory = $true)][string[]]$ScenarioIds
    )

    $requiredScenarioIds = @(
        'drawing-objects-complex',
        'object-format-position-size-style',
        'chart-smartart-complex',
        'wordart-watermark-stress',
        'wordart-picture-watermark-layout'
    )
    $selectedScenarioIds = @($requiredScenarioIds | Where-Object { $ScenarioIds -contains $_ })
    if ($selectedScenarioIds.Count -eq 0) {
        return
    }

    if (-not (Test-Path -LiteralPath $SummaryJsonPath -PathType Leaf)) {
        throw "Drawing object visual proof readiness cannot be checked because the summary JSON is missing: $SummaryJsonPath"
    }

    $summary = Get-Content -LiteralPath $SummaryJsonPath -Raw | ConvertFrom-Json
    if ([int]$summary.schemaVersion -lt 28) {
        throw "Drawing object visual proof readiness requires FreeW visual evidence summary schema v28 or newer, found v$($summary.schemaVersion)"
    }

    $readinessRows = @($summary.drawingObjectProofReadiness)
    $scenarios = @($summary.scenarios)
    $baselineComparisons = @($summary.baselineComparisons)
    $summaryFailures = @($summary.trust.failures)
    $requiredHosts = @(
        'wpf-fidelity-render',
        'avalonia-page-layout-shot'
    )
    $failures = New-Object System.Collections.Generic.List[string]
    $trustedScenarioRows = 0
    $verifiedSemanticRows = 0
    $verifiedBaselineRows = 0

    foreach ($scenarioId in $selectedScenarioIds) {
        $proofRows = @($readinessRows | Where-Object { $_.scenarioId -eq $scenarioId })
        if ($proofRows.Count -eq 0) {
            $failures.Add("${scenarioId}: missing drawing/object proof readiness row")
        }
        foreach ($proofRow in $proofRows) {
            if ($proofRow.trust.passed -ne $true -or $proofRow.status -ne 'paired-renderer-proof-ready') {
                $notes = @($proofRow.trust.failures) -join '; '
                if ([string]::IsNullOrWhiteSpace($notes)) {
                    $notes = [string]$proofRow.baselineReadiness
                }
                $failures.Add("${scenarioId}/p$($proofRow.pageNumber): readiness status '$($proofRow.status)' failed ($notes)")
            }

            if ([string]::IsNullOrWhiteSpace([string]$proofRow.semanticEvidence) -or [string]$proofRow.semanticEvidence -eq '-') {
                $failures.Add("${scenarioId}/p$($proofRow.pageNumber): missing drawing/object semantic readiness summary")
            }
        }

        $semanticFailureFragments = @(
            "drawing-object renderer pair '$scenarioId'",
            "chart/SmartArt renderer pair '$scenarioId'",
            "WordArt watermark renderer pair '$scenarioId'"
        )
        $semanticFailures = @($summaryFailures | Where-Object {
            $failure = [string]$_
            @($semanticFailureFragments | Where-Object { $failure.IndexOf($_, [StringComparison]::Ordinal) -ge 0 }).Count -gt 0
        })
        foreach ($semanticFailure in $semanticFailures) {
            $failures.Add("${scenarioId}: WPF/Avalonia semantic parity drift ($semanticFailure)")
        }

        foreach ($hostId in $requiredHosts) {
            $match = @($scenarios | Where-Object {
                $_.hostId -eq $hostId -and
                $_.scenarioId -eq $scenarioId
            })

            if ($match.Count -eq 0) {
                $failures.Add("${hostId}/${scenarioId}: missing normalized scenario row")
                continue
            }

            $row = $match[0]
            if ($row.trust.passed -ne $true) {
                $notes = @($row.trust.failures) -join '; '
                if ([string]::IsNullOrWhiteSpace($notes)) {
                    $notes = 'no notes'
                }
                $failures.Add("${hostId}/${scenarioId}: scenario trust failed ($notes)")
                continue
            }

            if ([int]$row.trustedOutputs -lt 1) {
                $failures.Add("${hostId}/${scenarioId}: expected at least 1 trusted output, found $($row.trustedOutputs)")
                continue
            }

            $trustedScenarioRows++

            $evidenceRows = @($summary.evidence | Where-Object {
                $_.hostId -eq $hostId -and
                $_.scenarioId -eq $scenarioId -and
                $_.trust.passed -eq $true
            })
            if ($evidenceRows.Count -eq 0) {
                $failures.Add("${hostId}/${scenarioId}: missing trusted normalized evidence row for drawing-object visual semantics")
            }
            foreach ($evidenceRow in $evidenceRows) {
                switch ($scenarioId) {
                    'drawing-objects-complex' {
                        if ([int]$evidenceRow.drawingObjects.floatingObjectCount -lt 1 -or
                            $evidenceRow.drawingObjects.hasCharts -ne $true -or
                            $evidenceRow.drawingObjects.hasSmartArt -ne $true -or
                            $evidenceRow.drawingObjects.hasWordArt -ne $true -or
                            @($evidenceRow.drawingObjects.groupChildren.childVisualSignatures).Count -eq 0 -or
                            [int]$evidenceRow.drawingObjects.effects.renderedGroupChildEffectObjectCount -lt 1) {
                            $failures.Add("${hostId}/${scenarioId}/p$($evidenceRow.pageNumber): missing grouped drawing/chart/SmartArt/WordArt semantic evidence")
                            continue
                        }
                    }
                    'object-format-position-size-style' {
                        if ([int]$evidenceRow.drawingObjects.altTextObjectCount -lt 1 -or
                            [int]$evidenceRow.drawingObjects.effects.effectObjectCount -lt 1 -or
                            $evidenceRow.drawingObjects.hasZOrder -ne $true) {
                            $failures.Add("${hostId}/${scenarioId}/p$($evidenceRow.pageNumber): missing object-format alt text, effects, or z-order semantic evidence")
                            continue
                        }
                        if ([int]$evidenceRow.drawingObjects.floatingObjectCount -ne 3 -or
                            [int]$evidenceRow.drawingObjects.behindTextCount -ne 1 -or
                            [int]$evidenceRow.drawingObjects.inFrontCount -ne 2 -or
                            $evidenceRow.drawingObjects.hasImages -ne $true -or
                            $evidenceRow.drawingObjects.hasShapes -ne $true -or
                            $evidenceRow.drawingObjects.hasWordArt -ne $true -or
                            $evidenceRow.drawingObjects.hasSquareWrap -ne $true -or
                            $evidenceRow.drawingObjects.hasTopAndBottomWrap -ne $true -or
                            [int]$evidenceRow.drawingObjects.altTextObjectCount -ne 3) {
                            $failures.Add("${hostId}/${scenarioId}/p$($evidenceRow.pageNumber): missing object-format position/size/style object semantic evidence")
                            continue
                        }
                        if ([int]$evidenceRow.drawingObjects.effects.effectObjectCount -ne 3 -or
                            [int]$evidenceRow.drawingObjects.effects.shapeEffectObjectCount -lt 1 -or
                            [int]$evidenceRow.drawingObjects.effects.imageEffectObjectCount -lt 1 -or
                            [int]$evidenceRow.drawingObjects.effects.wordArtEffectObjectCount -lt 1 -or
                            $evidenceRow.drawingObjects.effects.hasShadow -ne $true -or
                            $evidenceRow.drawingObjects.effects.hasGlow -ne $true -or
                            $evidenceRow.drawingObjects.effects.hasReflection -ne $true -or
                            $evidenceRow.drawingObjects.effects.hasSoftEdge -ne $true -or
                            $evidenceRow.drawingObjects.effects.hasBevel -ne $true -or
                            $evidenceRow.drawingObjects.effects.hasArtisticEffect -ne $true) {
                            $failures.Add("${hostId}/${scenarioId}/p$($evidenceRow.pageNumber): missing object-format shared effect semantic evidence")
                            continue
                        }
                        $effectSummaries = @($evidenceRow.drawingObjects.effects.effectSummaries)
                        foreach ($requiredEffect in @('Shape:shadow+bevel', 'Image:shadow+glow+reflection+soft-edge+bevel+artistic:GlowDiffused', 'WordArt:glow')) {
                            if (($effectSummaries | Where-Object { $_ -eq $requiredEffect }).Count -eq 0) {
                                $failures.Add("${hostId}/${scenarioId}/p$($evidenceRow.pageNumber): missing object-format effect summary '$requiredEffect'")
                                continue
                            }
                        }
                    }
                    'chart-smartart-complex' {
                        if ([int]$evidenceRow.chartSmartArt.chartCount -lt 1 -or
                            [int]$evidenceRow.chartSmartArt.smartArtCount -lt 1 -or
                            @($evidenceRow.chartSmartArt.chartVisualSignatures).Count -eq 0 -or
                            @($evidenceRow.chartSmartArt.smartArtVisualSignatures).Count -eq 0) {
                            $failures.Add("${hostId}/${scenarioId}/p$($evidenceRow.pageNumber): missing chart/SmartArt semantic visual signatures")
                            continue
                        }
                    }
                    'wordart-watermark-stress' {
                        if ($evidenceRow.drawingObjects.hasWordArt -ne $true -or
                            $evidenceRow.pageFeatures.watermark.present -ne $true -or
                            $evidenceRow.pageFeatures.pageBorder.present -ne $true -or
                            [int]$evidenceRow.drawingObjects.effects.effectObjectCount -lt 1) {
                            $failures.Add("${hostId}/${scenarioId}/p$($evidenceRow.pageNumber): missing WordArt, watermark, page-border, or effect semantic evidence")
                            continue
                        }
                    }
                    'wordart-picture-watermark-layout' {
                        if ($evidenceRow.drawingObjects.hasWordArt -ne $true -or
                            $evidenceRow.pageFeatures.watermark.present -ne $true -or
                            $evidenceRow.pageFeatures.watermark.isPicture -ne $true -or
                            $evidenceRow.pageFeatures.pageBorder.present -ne $true -or
                            [int]$evidenceRow.pageFeatures.columns.count -lt 2) {
                            $failures.Add("${hostId}/${scenarioId}/p$($evidenceRow.pageNumber): missing WordArt picture-watermark layout semantic evidence")
                            continue
                        }
                    }
                }

                $verifiedSemanticRows++
            }

            $comparisonRows = @($baselineComparisons | Where-Object {
                $_.hostId -eq $hostId -and
                $_.scenarioId -eq $scenarioId -and
                $_.baselineScenarioId -eq $scenarioId
            })
            if ($baselineComparisons.Count -gt 0 -and $comparisonRows.Count -eq 0) {
                $failures.Add("${hostId}/${scenarioId}: missing Word-baseline policy row")
                continue
            }

            foreach ($comparison in $comparisonRows) {
                if ($comparison.trust.passed -ne $true) {
                    $notes = @($comparison.trust.failures) -join '; '
                    if ([string]::IsNullOrWhiteSpace($notes)) {
                        $notes = [string]$comparison.skipReason
                    }
                    $failures.Add("$hostId/$scenarioId/$($comparison.outputName): baseline policy trust failed ($notes)")
                    continue
                }

                $baselineId = [string]$comparison.baselineId
                if (-not $baselineId.StartsWith($scenarioId + '/', [StringComparison]::OrdinalIgnoreCase)) {
                    $failures.Add("$hostId/$scenarioId/$($comparison.outputName): baselineId '$baselineId' expected scenario '$scenarioId'")
                    continue
                }

                $verifiedBaselineRows++
            }
        }
    }

    if ($failures.Count -gt 0) {
        throw "Drawing object visual proof readiness failed:`n - $($failures -join "`n - ")"
    }

    Write-Host "Drawing object visual proof readiness: trusted scenario rows=$trustedScenarioRows"
    Write-Host "Drawing object visual semantic rows: verified rows=$verifiedSemanticRows"
    if ($baselineComparisons.Count -gt 0) {
        Write-Host "Drawing object Word-baseline policy rows: verified rows=$verifiedBaselineRows"
    }
    else {
        Write-Host "Drawing object Word-baseline policy rows: no Word baseline mode requested"
    }
}

function Assert-GroupedDrawingObjectVisualProofReadiness {
    param(
        [Parameter(Mandatory = $true)][string]$SummaryJsonPath,
        [Parameter(Mandatory = $true)][string[]]$ScenarioIds
    )

    $scenarioId = 'drawing-objects-complex'
    if (-not ($ScenarioIds -contains $scenarioId)) {
        return
    }

    if (-not (Test-Path -LiteralPath $SummaryJsonPath -PathType Leaf)) {
        throw "Grouped drawing object visual proof readiness cannot be checked because the summary JSON is missing: $SummaryJsonPath"
    }

    $summary = Get-Content -LiteralPath $SummaryJsonPath -Raw | ConvertFrom-Json
    if ([int]$summary.schemaVersion -lt 28) {
        throw "Grouped drawing object visual proof readiness requires FreeW visual evidence summary schema v28 or newer, found v$($summary.schemaVersion)"
    }

    $failures = New-Object System.Collections.Generic.List[string]
    $requiredHosts = @(
        'wpf-fidelity-render',
        'avalonia-page-layout-shot'
    )
    $forbiddenScenarioIds = @(
        'chart-smartart-complex',
        'wordart-watermark-stress',
        'wordart-picture-watermark-layout',
        'table-layout-complex',
        'table-pagination-repeat-header',
        'table-page-composition-stress'
    )
    $readinessRows = @($summary.drawingObjectProofReadiness | Where-Object { $_.scenarioId -eq $scenarioId })
    $baselineComparisons = @($summary.baselineComparisons)
    $remainingBlockers = @($summary.remainingEvidenceBlockers)
    $verifiedSemanticRows = 0
    $verifiedBaselineRows = 0

    if ($ScenarioIds.Count -eq 1) {
        $unexpectedEvidence = @($summary.evidence | Where-Object { $forbiddenScenarioIds -contains $_.scenarioId })
        if ($unexpectedEvidence.Count -gt 0) {
            $failures.Add("Grouped drawing object proof included unrelated evidence scenario(s): $((@($unexpectedEvidence | ForEach-Object { $_.scenarioId }) | Select-Object -Unique) -join ', ')")
        }

        $unexpectedReadiness = @($summary.drawingObjectProofReadiness | Where-Object {
            $_.scenarioId -ne $scenarioId
        })
        if ($unexpectedReadiness.Count -gt 0) {
            $failures.Add("Grouped drawing object proof included unrelated drawing readiness row(s): $((@($unexpectedReadiness | ForEach-Object { $_.scenarioId }) | Select-Object -Unique) -join ', ')")
        }
    }

    if ($readinessRows.Count -eq 0) {
        $failures.Add("${scenarioId}: missing grouped drawing/object readiness row")
    }

    foreach ($readinessRow in $readinessRows) {
        if ($readinessRow.status -ne 'paired-renderer-proof-ready' -or $readinessRow.trust.passed -ne $true) {
            $notes = @($readinessRow.trust.failures) -join '; '
            if ([string]::IsNullOrWhiteSpace($notes)) {
                $notes = [string]$readinessRow.baselineReadiness
            }
            $failures.Add("${scenarioId}/p$($readinessRow.pageNumber): grouped drawing readiness failed ($notes)")
            continue
        }

        $semanticEvidence = [string]$readinessRow.semanticEvidence
        if ($semanticEvidence -notmatch '5 grouped child object\(s\)' -or
            $semanticEvidence -notmatch 'grouped child visual signatures=5' -or
            $semanticEvidence -notmatch '2 rendered grouped child effect object\(s\)') {
            $failures.Add("${scenarioId}/p$($readinessRow.pageNumber): missing grouped child semantic summary in readiness row")
        }

        if ($readinessRow.wordBaselineStatus -notmatch 'word-baseline-unavailable|passed|not-run') {
            $failures.Add("${scenarioId}/p$($readinessRow.pageNumber): unexpected Word-baseline status '$($readinessRow.wordBaselineStatus)'")
        }
    }

    foreach ($hostId in $requiredHosts) {
        $scenarioRows = @($summary.scenarios | Where-Object {
            $_.hostId -eq $hostId -and
            $_.scenarioId -eq $scenarioId
        })
        if ($scenarioRows.Count -eq 0) {
            $failures.Add("${hostId}/${scenarioId}: missing normalized scenario row")
            continue
        }

        if ($scenarioRows[0].trust.passed -ne $true) {
            $notes = @($scenarioRows[0].trust.failures) -join '; '
            if ([string]::IsNullOrWhiteSpace($notes)) {
                $notes = 'no notes'
            }
            $failures.Add("${hostId}/${scenarioId}: scenario trust failed ($notes)")
            continue
        }

        $evidenceRows = @($summary.evidence | Where-Object {
            $_.hostId -eq $hostId -and
            $_.scenarioId -eq $scenarioId -and
            $_.trust.passed -eq $true
        })
        if ($evidenceRows.Count -eq 0) {
            $failures.Add("${hostId}/${scenarioId}: missing trusted grouped drawing evidence row")
            continue
        }

        foreach ($evidenceRow in $evidenceRows) {
            $groupChildren = $evidenceRow.drawingObjects.groupChildren
            $effects = $evidenceRow.drawingObjects.effects
            $kindSummaries = @($groupChildren.childKindSummaries)
            $visualSignatures = @($groupChildren.childVisualSignatures)
            $renderedEffectSummaries = @($effects.renderedGroupChildEffectSummaries)

            if ([int]$groupChildren.childCount -lt 5 -or
                [int]$groupChildren.imageChildCount -lt 1 -or
                [int]$groupChildren.shapeChildCount -lt 1 -or
                [int]$groupChildren.chartChildCount -lt 1 -or
                [int]$groupChildren.smartArtChildCount -lt 1 -or
                [int]$groupChildren.wordArtChildCount -lt 1 -or
                $groupChildren.hasMixedTypedChildren -ne $true) {
                $failures.Add("${hostId}/${scenarioId}/p$($evidenceRow.pageNumber): missing mixed grouped child counts")
                continue
            }

            foreach ($requiredKind in @(
                'Group0Child0:Image',
                'Group0Child1:Shape',
                'Group0Child2:Chart',
                'Group0Child3:WordArt',
                'Group0Child4:SmartArt'
            )) {
                if ($kindSummaries -notcontains $requiredKind) {
                    $failures.Add("${hostId}/${scenarioId}/p$($evidenceRow.pageNumber): missing grouped child kind '$requiredKind'")
                }
            }

            foreach ($requiredSignaturePrefix in @(
                'Group0Child0:Image:',
                'Group0Child1:Shape:',
                'Group0Child2:Chart:',
                'Group0Child3:WordArt:',
                'Group0Child4:SmartArt:'
            )) {
                if (@($visualSignatures | Where-Object { ([string]$_).StartsWith($requiredSignaturePrefix, [StringComparison]::Ordinal) }).Count -eq 0) {
                    $failures.Add("${hostId}/${scenarioId}/p$($evidenceRow.pageNumber): missing grouped child visual signature '$requiredSignaturePrefix'")
                }
            }

            if ([int]$effects.renderedGroupChildEffectObjectCount -lt 2 -or
                [int]$effects.renderedGroupChildShapeEffectObjectCount -lt 1 -or
                [int]$effects.renderedGroupChildWordArtEffectObjectCount -lt 1 -or
                $renderedEffectSummaries -notcontains 'GroupChild1:Shape:glow' -or
                $renderedEffectSummaries -notcontains 'GroupChild3:WordArt:glow') {
                $failures.Add("${hostId}/${scenarioId}/p$($evidenceRow.pageNumber): missing rendered grouped child effect evidence")
                continue
            }

            $verifiedSemanticRows++
        }

        $comparisonRows = @($baselineComparisons | Where-Object {
            $_.hostId -eq $hostId -and
            $_.scenarioId -eq $scenarioId -and
            $_.baselineScenarioId -eq $scenarioId
        })
        if ($baselineComparisons.Count -gt 0 -and $comparisonRows.Count -eq 0) {
            $failures.Add("${hostId}/${scenarioId}: missing Word-baseline policy row")
            continue
        }

        foreach ($comparison in $comparisonRows) {
            if ($comparison.trust.passed -ne $true) {
                $notes = @($comparison.trust.failures) -join '; '
                if ([string]::IsNullOrWhiteSpace($notes)) {
                    $notes = [string]$comparison.skipReason
                }
                $failures.Add("$hostId/$scenarioId/$($comparison.outputName): baseline policy trust failed ($notes)")
                continue
            }

            $verifiedBaselineRows++
        }
    }

    $unavailableComparisons = @($baselineComparisons | Where-Object {
        $_.scenarioId -eq $scenarioId -and
        $_.status -eq 'word-baseline-unavailable'
    })
    if ($unavailableComparisons.Count -gt 0) {
        $blocker = @($remainingBlockers | Where-Object {
            $_.blockerId -eq 'drawing-objects-complex-word-baseline-fidelity' -and
            $_.scenarioId -eq $scenarioId -and
            $_.status -eq 'word-baseline-unavailable' -and
            $_.requiresWordBaseline -eq $true
        }) | Select-Object -First 1
        if (-not $blocker) {
            $failures.Add("${scenarioId}: missing honest word-baseline-unavailable grouped drawing blocker")
        }
    }

    if ($failures.Count -gt 0) {
        throw "Grouped drawing object visual proof readiness failed:`n - $($failures -join "`n - ")"
    }

    Write-Host "Grouped drawing object visual proof readiness: readiness rows=$($readinessRows.Count)"
    Write-Host "Grouped drawing object semantic rows: verified rows=$verifiedSemanticRows"
    if ($baselineComparisons.Count -gt 0) {
        Write-Host "Grouped drawing object Word-baseline policy rows: verified rows=$verifiedBaselineRows"
    }
    else {
        Write-Host "Grouped drawing object Word-baseline policy rows: no Word baseline mode requested"
    }
    if ($unavailableComparisons.Count -gt 0) {
        Write-Host "Grouped drawing object Word-baseline unavailable blocker: verified"
    }
}

function Assert-SmartArtPolygonVisualProofReadiness {
    param(
        [Parameter(Mandatory = $true)][string]$SummaryJsonPath,
        [Parameter(Mandatory = $true)][string[]]$ScenarioIds
    )

    $scenarioId = 'chart-smartart-complex'
    if (-not ($ScenarioIds -contains $scenarioId)) {
        return
    }

    if (-not (Test-Path -LiteralPath $SummaryJsonPath -PathType Leaf)) {
        throw "SmartArt polygon visual proof readiness cannot be checked because the summary JSON is missing: $SummaryJsonPath"
    }

    $summary = Get-Content -LiteralPath $SummaryJsonPath -Raw | ConvertFrom-Json
    if ([int]$summary.schemaVersion -lt 38) {
        throw "SmartArt polygon visual proof readiness requires FreeW visual evidence summary schema v38 or newer, found v$($summary.schemaVersion)"
    }

    $requiredHosts = @(
        'wpf-fidelity-render',
        'avalonia-page-layout-shot'
    )
    $readinessRows = @($summary.drawingObjectProofReadiness | Where-Object { $_.scenarioId -eq $scenarioId })
    $evidenceRows = @($summary.evidence)
    $baselineComparisons = @($summary.baselineComparisons)
    $remainingBlockers = @($summary.remainingEvidenceBlockers)
    $failures = New-Object System.Collections.Generic.List[string]
    $verifiedSemanticRows = 0
    $verifiedBaselineRows = 0

    if ($readinessRows.Count -eq 0) {
        $failures.Add("${scenarioId}: missing SmartArt drawing/object proof readiness row")
    }
    foreach ($proofRow in $readinessRows) {
        if ($proofRow.trust.passed -ne $true -or $proofRow.status -ne 'paired-renderer-proof-ready') {
            $notes = @($proofRow.trust.failures) -join '; '
            if ([string]::IsNullOrWhiteSpace($notes)) {
                $notes = [string]$proofRow.baselineReadiness
            }
            $failures.Add("${scenarioId}/p$($proofRow.pageNumber): readiness status '$($proofRow.status)' failed ($notes)")
        }
        if ([string]$proofRow.semanticEvidence -notmatch 'SmartArt polygon nodes=') {
            $failures.Add("${scenarioId}/p$($proofRow.pageNumber): semantic readiness does not report SmartArt polygon nodes")
        }
        if ([string]$proofRow.semanticEvidence -notmatch 'SmartArt layouts=.*pyramid1') {
            $failures.Add("${scenarioId}/p$($proofRow.pageNumber): semantic readiness does not report pyramid1 SmartArt layout")
        }
    }

    foreach ($hostId in $requiredHosts) {
        $hostRows = @($evidenceRows | Where-Object {
            $_.hostId -eq $hostId -and
            $_.scenarioId -eq $scenarioId -and
            $_.trust.passed -eq $true
        })
        if ($hostRows.Count -eq 0) {
            $failures.Add("${hostId}/${scenarioId}: missing trusted normalized evidence row for SmartArt polygon proof")
            continue
        }

        foreach ($row in $hostRows) {
            $smartArt = $row.chartSmartArt
            if ([int]$smartArt.smartArtCount -lt 2 -or
                [int]$smartArt.smartArtNodeCount -lt 7 -or
                @($smartArt.smartArtVisualSignatures).Count -lt 2) {
                $failures.Add("${hostId}/${scenarioId}/p$($row.pageNumber): missing multiple SmartArt plans or signatures")
                continue
            }

            $signatures = @($smartArt.smartArtVisualSignatures)
            if (@($signatures | Where-Object {
                ([string]$_).IndexOf('layout=pyramid1', [StringComparison]::Ordinal) -ge 0 -and
                ([string]$_).IndexOf('geometry=kind=Pyramid', [StringComparison]::Ordinal) -ge 0 -and
                ([string]$_).IndexOf('polygons=0=', [StringComparison]::Ordinal) -ge 0
            }).Count -eq 0) {
                $failures.Add("${hostId}/${scenarioId}/p$($row.pageNumber): missing Basic Pyramid polygon geometry signature")
                continue
            }

            $verifiedSemanticRows++
        }

        $comparisonRows = @($baselineComparisons | Where-Object {
            $_.hostId -eq $hostId -and
            $_.scenarioId -eq $scenarioId -and
            $_.baselineScenarioId -eq $scenarioId
        })
        if ($baselineComparisons.Count -gt 0 -and $comparisonRows.Count -eq 0) {
            $failures.Add("${hostId}/${scenarioId}: missing Word-baseline policy row")
            continue
        }
        foreach ($comparison in $comparisonRows) {
            if ($comparison.trust.passed -ne $true) {
                $notes = @($comparison.trust.failures) -join '; '
                if ([string]::IsNullOrWhiteSpace($notes)) {
                    $notes = [string]$comparison.skipReason
                }
                $failures.Add("$hostId/$scenarioId/$($comparison.outputName): baseline policy trust failed ($notes)")
                continue
            }
            $verifiedBaselineRows++
        }
    }

    $unavailableComparisons = @($baselineComparisons | Where-Object {
        $_.scenarioId -eq $scenarioId -and
        $_.status -eq 'word-baseline-unavailable'
    })
    if ($unavailableComparisons.Count -gt 0) {
        $blocker = @($remainingBlockers | Where-Object {
            $_.blockerId -eq 'chart-smartart-complex-word-baseline-fidelity' -and
            $_.scenarioId -eq $scenarioId -and
            $_.status -eq 'word-baseline-unavailable' -and
            $_.requiresWordBaseline -eq $true
        }) | Select-Object -First 1
        if (-not $blocker) {
            $failures.Add("${scenarioId}: missing honest word-baseline-unavailable SmartArt polygon blocker")
        }
    }

    if ($failures.Count -gt 0) {
        throw "SmartArt polygon visual proof readiness failed:`n - $($failures -join "`n - ")"
    }

    Write-Host "SmartArt polygon visual proof readiness: trusted semantic rows=$verifiedSemanticRows"
    if ($baselineComparisons.Count -gt 0) {
        Write-Host "SmartArt polygon Word-baseline policy rows: verified rows=$verifiedBaselineRows"
    }
    else {
        Write-Host "SmartArt polygon Word-baseline policy rows: no Word baseline mode requested"
    }
    if ($unavailableComparisons.Count -gt 0) {
        Write-Host "SmartArt polygon Word-baseline unavailable blocker: verified"
    }
}

function Assert-ChartVisualProofReadiness {
    param(
        [Parameter(Mandatory = $true)][string]$SummaryJsonPath,
        [Parameter(Mandatory = $true)][string[]]$ScenarioIds
    )

    $scenarioId = 'chart-smartart-complex'
    if (-not ($ScenarioIds -contains $scenarioId)) {
        return
    }

    if (-not (Test-Path -LiteralPath $SummaryJsonPath -PathType Leaf)) {
        throw "Chart visual proof readiness cannot be checked because the summary JSON is missing: $SummaryJsonPath"
    }

    $summary = Get-Content -LiteralPath $SummaryJsonPath -Raw | ConvertFrom-Json
    if ([int]$summary.schemaVersion -lt 48) {
        throw "Chart visual proof readiness requires FreeW visual evidence summary schema v48 or newer, found v$($summary.schemaVersion)"
    }

    $requiredHosts = @(
        'wpf-fidelity-render',
        'avalonia-page-layout-shot'
    )
    $readinessRows = @($summary.drawingObjectProofReadiness | Where-Object { $_.scenarioId -eq $scenarioId })
    $evidenceRows = @($summary.evidence)
    $baselineComparisons = @($summary.baselineComparisons)
    $remainingBlockers = @($summary.remainingEvidenceBlockers)
    $failures = New-Object System.Collections.Generic.List[string]
    $verifiedSemanticRows = 0
    $verifiedBaselineRows = 0

    if ($readinessRows.Count -eq 0) {
        $failures.Add("${scenarioId}: missing chart drawing/object proof readiness row")
    }
    foreach ($proofRow in $readinessRows) {
        if ($proofRow.trust.passed -ne $true -or $proofRow.status -ne 'paired-renderer-proof-ready') {
            $notes = @($proofRow.trust.failures) -join '; '
            if ([string]::IsNullOrWhiteSpace($notes)) {
                $notes = [string]$proofRow.baselineReadiness
            }
            $failures.Add("${scenarioId}/p$($proofRow.pageNumber): readiness status '$($proofRow.status)' failed ($notes)")
        }
        if ([string]$proofRow.semanticEvidence -notmatch 'chart signatures=') {
            $failures.Add("${scenarioId}/p$($proofRow.pageNumber): semantic readiness does not report chart signatures")
        }
    }

    foreach ($hostId in $requiredHosts) {
        $hostRows = @($evidenceRows | Where-Object {
            $_.hostId -eq $hostId -and
            $_.scenarioId -eq $scenarioId -and
            $_.trust.passed -eq $true
        })
        if ($hostRows.Count -eq 0) {
            $failures.Add("${hostId}/${scenarioId}: missing trusted normalized evidence row for chart visual proof")
            continue
        }

        foreach ($row in $hostRows) {
            $chartSmartArt = $row.chartSmartArt
            if ([int]$chartSmartArt.chartCount -lt 2 -or
                @($chartSmartArt.chartVisualSignatures).Count -lt 2) {
                $failures.Add("${hostId}/${scenarioId}/p$($row.pageNumber): missing multiple chart plans or signatures")
                continue
            }
            if (@($chartSmartArt.chartDataSignatures).Count -lt 2) {
                $failures.Add("${hostId}/${scenarioId}/p$($row.pageNumber): missing chart data signatures")
                continue
            }

            $signatures = @($chartSmartArt.chartVisualSignatures)
            if (@($signatures | Where-Object {
                ([string]$_).IndexOf('kind=Column', [StringComparison]::Ordinal) -ge 0 -and
                ([string]$_).IndexOf('geometry=Bars', [StringComparison]::Ordinal) -ge 0 -and
                ([string]$_).IndexOf('legend=1', [StringComparison]::Ordinal) -ge 0 -and
                ([string]$_).IndexOf('dataLabels=1', [StringComparison]::Ordinal) -ge 0
            }).Count -eq 0) {
                $failures.Add("${hostId}/${scenarioId}/p$($row.pageNumber): missing styled column chart signature")
                continue
            }

            if (@($signatures | Where-Object {
                ([string]$_).IndexOf('kind=Scatter', [StringComparison]::Ordinal) -ge 0 -and
                ([string]$_).IndexOf('geometry=MarkerOnly', [StringComparison]::Ordinal) -ge 0 -and
                ([string]$_).IndexOf('markers=1', [StringComparison]::Ordinal) -ge 0
            }).Count -eq 0) {
                $failures.Add("${hostId}/${scenarioId}/p$($row.pageNumber): missing marker scatter chart signature")
                continue
            }

            $dataSignatures = @($chartSmartArt.chartDataSignatures)
            if (@($dataSignatures | Where-Object {
                ([string]$_).IndexOf('kind=Column', [StringComparison]::Ordinal) -ge 0 -and
                ([string]$_).IndexOf('categoryLabels=Q1,Q2,Q3,Q4', [StringComparison]::Ordinal) -ge 0 -and
                ([string]$_).IndexOf('seriesData=0:Revenue=1.4,1.8,1.6,2.2', [StringComparison]::Ordinal) -ge 0
            }).Count -eq 0) {
                $failures.Add("${hostId}/${scenarioId}/p$($row.pageNumber): missing column chart data signature")
                continue
            }

            if (@($dataSignatures | Where-Object {
                ([string]$_).IndexOf('kind=Scatter', [StringComparison]::Ordinal) -ge 0 -and
                ([string]$_).IndexOf('categoryLabels=155,160,165,170', [StringComparison]::Ordinal) -ge 0 -and
                ([string]$_).IndexOf('seriesData=0:Sample=52,58,62,66', [StringComparison]::Ordinal) -ge 0
            }).Count -eq 0) {
                $failures.Add("${hostId}/${scenarioId}/p$($row.pageNumber): missing scatter chart data signature")
                continue
            }

            $verifiedSemanticRows++
        }

        $comparisonRows = @($baselineComparisons | Where-Object {
            $_.hostId -eq $hostId -and
            $_.scenarioId -eq $scenarioId -and
            $_.baselineScenarioId -eq $scenarioId
        })
        if ($baselineComparisons.Count -gt 0 -and $comparisonRows.Count -eq 0) {
            $failures.Add("${hostId}/${scenarioId}: missing Word-baseline policy row")
            continue
        }
        foreach ($comparison in $comparisonRows) {
            if ($comparison.trust.passed -ne $true) {
                $notes = @($comparison.trust.failures) -join '; '
                if ([string]::IsNullOrWhiteSpace($notes)) {
                    $notes = [string]$comparison.skipReason
                }
                $failures.Add("$hostId/$scenarioId/$($comparison.outputName): baseline policy trust failed ($notes)")
                continue
            }
            $verifiedBaselineRows++
        }
    }

    $unavailableComparisons = @($baselineComparisons | Where-Object {
        $_.scenarioId -eq $scenarioId -and
        $_.status -eq 'word-baseline-unavailable'
    })
    if ($unavailableComparisons.Count -gt 0) {
        $blocker = @($remainingBlockers | Where-Object {
            $_.blockerId -eq 'chart-smartart-complex-word-baseline-fidelity' -and
            $_.scenarioId -eq $scenarioId -and
            $_.status -eq 'word-baseline-unavailable' -and
            $_.requiresWordBaseline -eq $true
        }) | Select-Object -First 1
        if (-not $blocker) {
            $failures.Add("${scenarioId}: missing honest word-baseline-unavailable chart visual blocker")
        }
    }

    if ($failures.Count -gt 0) {
        throw "Chart visual proof readiness failed:`n - $($failures -join "`n - ")"
    }

    Write-Host "Chart visual proof readiness: trusted semantic rows=$verifiedSemanticRows"
    if ($baselineComparisons.Count -gt 0) {
        Write-Host "Chart visual Word-baseline policy rows: verified rows=$verifiedBaselineRows"
    }
    else {
        Write-Host "Chart visual Word-baseline policy rows: no Word baseline mode requested"
    }
    if ($unavailableComparisons.Count -gt 0) {
        Write-Host "Chart visual Word-baseline unavailable blocker: verified"
    }
}

function Assert-WordArtWatermarkVisualProofReadiness {
    param(
        [Parameter(Mandatory = $true)][string]$SummaryJsonPath,
        [Parameter(Mandatory = $true)][string[]]$ScenarioIds
    )

    $requiredScenarioIds = @(
        'wordart-watermark-stress',
        'wordart-picture-watermark-layout'
    )
    $selectedScenarioIds = @($requiredScenarioIds | Where-Object { $ScenarioIds -contains $_ })
    if ($selectedScenarioIds.Count -eq 0) {
        return
    }

    if (-not (Test-Path -LiteralPath $SummaryJsonPath -PathType Leaf)) {
        throw "WordArt/watermark visual proof readiness cannot be checked because the summary JSON is missing: $SummaryJsonPath"
    }

    $summary = Get-Content -LiteralPath $SummaryJsonPath -Raw | ConvertFrom-Json
    if ([int]$summary.schemaVersion -lt 47) {
        throw "WordArt/watermark visual proof readiness requires FreeW visual evidence summary schema v47 or newer, found v$($summary.schemaVersion)"
    }

    $readinessRows = @($summary.wordArtWatermarkProofReadiness)
    $evidenceRows = @($summary.evidence)
    $baselineComparisons = @($summary.baselineComparisons)
    $remainingBlockers = @($summary.remainingEvidenceBlockers)
    $requiredHosts = @(
        'wpf-fidelity-render',
        'avalonia-page-layout-shot'
    )
    $failures = New-Object System.Collections.Generic.List[string]
    $trustedScenarioRows = 0
    $verifiedSemanticRows = 0
    $verifiedBaselineRows = 0
    $verifiedUnavailableBlockers = 0

    foreach ($scenarioId in $selectedScenarioIds) {
        $proofRows = @($readinessRows | Where-Object { $_.scenarioId -eq $scenarioId })
        if ($proofRows.Count -eq 0) {
            $failures.Add("${scenarioId}: missing WordArt/watermark proof readiness row")
        }
        foreach ($proofRow in $proofRows) {
            if ($proofRow.trust.passed -ne $true -or $proofRow.status -ne 'paired-renderer-proof-ready') {
                $notes = @($proofRow.trust.failures) -join '; '
                if ([string]::IsNullOrWhiteSpace($notes)) {
                    $notes = [string]$proofRow.baselineReadiness
                }
                $failures.Add("${scenarioId}/p$($proofRow.pageNumber): readiness status '$($proofRow.status)' failed ($notes)")
            }

            $semanticEvidence = [string]$proofRow.semanticEvidence
            if ([string]::IsNullOrWhiteSpace($semanticEvidence) -or $semanticEvidence -eq '-') {
                $failures.Add("${scenarioId}/p$($proofRow.pageNumber): missing WordArt/watermark semantic readiness summary")
            }
            elseif ($scenarioId -eq 'wordart-watermark-stress' -and
                ($semanticEvidence.IndexOf('watermark', [StringComparison]::OrdinalIgnoreCase) -lt 0 -or
                 $semanticEvidence.IndexOf('wordart', [StringComparison]::OrdinalIgnoreCase) -lt 0)) {
                $failures.Add("${scenarioId}/p$($proofRow.pageNumber): semantic readiness does not report text watermark and WordArt evidence")
            }
            elseif ($scenarioId -eq 'wordart-picture-watermark-layout' -and
                ($semanticEvidence.IndexOf('picture watermark', [StringComparison]::OrdinalIgnoreCase) -lt 0 -or
                 $semanticEvidence.IndexOf('wordart', [StringComparison]::OrdinalIgnoreCase) -lt 0)) {
                $failures.Add("${scenarioId}/p$($proofRow.pageNumber): semantic readiness does not report picture watermark and WordArt evidence")
            }
        }

        foreach ($hostId in $requiredHosts) {
            $hostRows = @($evidenceRows | Where-Object {
                $_.hostId -eq $hostId -and
                $_.scenarioId -eq $scenarioId -and
                $_.trust.passed -eq $true
            })
            if ($hostRows.Count -eq 0) {
                $failures.Add("${hostId}/${scenarioId}: missing trusted normalized evidence row for WordArt/watermark visual proof")
                continue
            }

            foreach ($row in $hostRows) {
                if ($row.drawingObjects.hasWordArt -ne $true -or
                    $row.pageFeatures.watermark.present -ne $true -or
                    $row.pageFeatures.pageBorder.present -ne $true) {
                    $failures.Add("${hostId}/${scenarioId}/p$($row.pageNumber): missing WordArt, watermark, or page-border semantic evidence")
                    continue
                }

                if ($scenarioId -eq 'wordart-watermark-stress' -and
                    [int]$row.drawingObjects.effects.wordArtEffectObjectCount -lt 1) {
                    $failures.Add("${hostId}/${scenarioId}/p$($row.pageNumber): missing WordArt effect semantic evidence")
                    continue
                }

                if ($scenarioId -eq 'wordart-picture-watermark-layout' -and
                    ($row.pageFeatures.watermark.isPicture -ne $true -or
                     [int]$row.pageFeatures.columns.count -lt 2)) {
                    $failures.Add("${hostId}/${scenarioId}/p$($row.pageNumber): missing picture-watermark column layout evidence")
                    continue
                }

                $verifiedSemanticRows++
            }

            $trustedScenarioRows++
            $comparisonRows = @($baselineComparisons | Where-Object {
                $_.hostId -eq $hostId -and
                $_.scenarioId -eq $scenarioId -and
                $_.baselineScenarioId -eq $scenarioId
            })
            if ($baselineComparisons.Count -gt 0 -and $comparisonRows.Count -eq 0) {
                $failures.Add("${hostId}/${scenarioId}: missing Word-baseline policy row")
                continue
            }

            foreach ($comparison in $comparisonRows) {
                if ($comparison.trust.passed -ne $true) {
                    $notes = @($comparison.trust.failures) -join '; '
                    if ([string]::IsNullOrWhiteSpace($notes)) {
                        $notes = [string]$comparison.skipReason
                    }
                    $failures.Add("$hostId/$scenarioId/$($comparison.outputName): baseline policy trust failed ($notes)")
                    continue
                }
                $verifiedBaselineRows++
            }
        }

        $unavailableComparisons = @($baselineComparisons | Where-Object {
            $_.scenarioId -eq $scenarioId -and
            $_.status -eq 'word-baseline-unavailable'
        })
        if ($unavailableComparisons.Count -gt 0) {
            $blocker = @($remainingBlockers | Where-Object {
                $_.blockerId -eq ($scenarioId + '-word-baseline-fidelity') -and
                $_.scenarioId -eq $scenarioId -and
                $_.status -eq 'word-baseline-unavailable' -and
                $_.requiresWordBaseline -eq $true
            }) | Select-Object -First 1
            if (-not $blocker) {
                $failures.Add("${scenarioId}: missing honest word-baseline-unavailable WordArt/watermark blocker")
            }
            else {
                $verifiedUnavailableBlockers++
            }
        }
    }

    if ($failures.Count -gt 0) {
        throw "WordArt/watermark visual proof readiness failed:`n - $($failures -join "`n - ")"
    }

    Write-Host "WordArt/watermark visual proof readiness: trusted scenario rows=$trustedScenarioRows"
    Write-Host "WordArt/watermark visual semantic rows: verified rows=$verifiedSemanticRows"
    if ($baselineComparisons.Count -gt 0) {
        Write-Host "WordArt/watermark Word-baseline policy rows: verified rows=$verifiedBaselineRows"
    }
    else {
        Write-Host "WordArt/watermark Word-baseline policy rows: no Word baseline mode requested"
    }
    if ($verifiedUnavailableBlockers -gt 0) {
        Write-Host "WordArt/watermark Word-baseline unavailable blockers: verified rows=$verifiedUnavailableBlockers"
    }
}

function Assert-ReviewCompareCombineVisualProofReadiness {
    param(
        [Parameter(Mandatory = $true)][string]$SummaryJsonPath,
        [Parameter(Mandatory = $true)][string[]]$ScenarioIds
    )

    $requiredScenarioIds = @(
        'review-compare-visual-proof',
        'review-combine-visual-proof'
    )
    $selectedScenarioIds = @($requiredScenarioIds | Where-Object { $ScenarioIds -contains $_ })
    if ($selectedScenarioIds.Count -eq 0) {
        return
    }

    if (-not (Test-Path -LiteralPath $SummaryJsonPath -PathType Leaf)) {
        throw "Review compare/combine visual proof readiness cannot be checked because the summary JSON is missing: $SummaryJsonPath"
    }

    $summary = Get-Content -LiteralPath $SummaryJsonPath -Raw | ConvertFrom-Json
    if ([int]$summary.schemaVersion -lt 41) {
        throw "Review compare/combine visual proof readiness requires FreeW visual evidence summary schema v41 or newer, found v$($summary.schemaVersion)"
    }

    $readinessRows = @($summary.reviewCompareCombineProofReadiness)
    $scenarios = @($summary.scenarios)
    $baselineComparisons = @($summary.baselineComparisons)
    $remainingBlockers = @($summary.remainingEvidenceBlockers)
    $summaryFailures = @($summary.trust.failures)
    $requiredHosts = @(
        'wpf-fidelity-render',
        'avalonia-page-layout-shot'
    )
    $failures = New-Object System.Collections.Generic.List[string]
    $trustedScenarioRows = 0
    $verifiedSemanticRows = 0
    $verifiedBaselineRows = 0
    $verifiedUnavailableBlockers = 0

    foreach ($scenarioId in $selectedScenarioIds) {
        $proofRows = @($readinessRows | Where-Object { $_.scenarioId -eq $scenarioId })
        if ($proofRows.Count -eq 0) {
            $failures.Add("${scenarioId}: missing review compare/combine proof readiness row")
        }
        foreach ($proofRow in $proofRows) {
            if ($proofRow.trust.passed -ne $true -or $proofRow.status -ne 'paired-renderer-proof-ready') {
                $notes = @($proofRow.trust.failures) -join '; '
                if ([string]::IsNullOrWhiteSpace($notes)) {
                    $notes = [string]$proofRow.baselineReadiness
                }
                $failures.Add("${scenarioId}/p$($proofRow.pageNumber): readiness status '$($proofRow.status)' failed ($notes)")
            }

            if ([string]::IsNullOrWhiteSpace([string]$proofRow.semanticEvidence) -or [string]$proofRow.semanticEvidence -eq '-') {
                $failures.Add("${scenarioId}/p$($proofRow.pageNumber): missing compare/combine semantic readiness summary")
            }
        }

        $semanticFailures = @($summaryFailures | Where-Object {
            ([string]$_).IndexOf("review renderer pair '$scenarioId'", [StringComparison]::Ordinal) -ge 0 -or
            ([string]$_).IndexOf("review compare/combine proof '$scenarioId'", [StringComparison]::Ordinal) -ge 0
        })
        foreach ($semanticFailure in $semanticFailures) {
            $failures.Add("${scenarioId}: WPF/Avalonia compare-combine semantic parity drift ($semanticFailure)")
        }

        foreach ($hostId in $requiredHosts) {
            $match = @($scenarios | Where-Object {
                $_.hostId -eq $hostId -and
                $_.scenarioId -eq $scenarioId
            })

            if ($match.Count -eq 0) {
                $failures.Add("${hostId}/${scenarioId}: missing normalized scenario row")
                continue
            }

            $row = $match[0]
            if ($row.trust.passed -ne $true) {
                $notes = @($row.trust.failures) -join '; '
                if ([string]::IsNullOrWhiteSpace($notes)) {
                    $notes = 'no notes'
                }
                $failures.Add("${hostId}/${scenarioId}: scenario trust failed ($notes)")
                continue
            }

            if ([int]$row.trustedOutputs -lt 1) {
                $failures.Add("${hostId}/${scenarioId}: expected at least 1 trusted output, found $($row.trustedOutputs)")
                continue
            }

            $trustedScenarioRows++

            $evidenceRows = @($summary.evidence | Where-Object {
                $_.hostId -eq $hostId -and
                $_.scenarioId -eq $scenarioId -and
                $_.trust.passed -eq $true
            })
            if ($evidenceRows.Count -eq 0) {
                $failures.Add("${hostId}/${scenarioId}: missing trusted normalized evidence row for compare/combine semantics")
            }
            foreach ($evidenceRow in $evidenceRows) {
                $semantics = $evidenceRow.reviewCompareCombine
                if ($scenarioId -eq 'review-compare-visual-proof') {
                    if ($semantics.operation -ne 'compare' -or
                        $semantics.hasCompareSemantics -ne $true -or
                        [int]$semantics.revisionCount -lt 1 -or
                        [int]$semantics.insertionCount -lt 1 -or
                        [int]$semantics.deletionCount -lt 1 -or
                        [int]$semantics.authorCount -ne 1 -or
                        @($semantics.authors | Where-Object { $_ -eq 'Riley' }).Count -ne 1) {
                        $failures.Add("${hostId}/${scenarioId}/p$($evidenceRow.pageNumber): missing compare revision/authorship semantic evidence")
                        continue
                    }
                }
                elseif ($scenarioId -eq 'review-combine-visual-proof') {
                    if ($semantics.operation -ne 'combine' -or
                        $semantics.hasCombineSemantics -ne $true -or
                        [int]$semantics.revisionCount -lt 1 -or
                        [int]$semantics.insertionCount -lt 1 -or
                        [int]$semantics.deletionCount -lt 1 -or
                        [int]$semantics.authorCount -lt 2 -or
                        @($semantics.authors | Where-Object { $_ -eq 'Alice' }).Count -ne 1 -or
                        @($semantics.authors | Where-Object { $_ -eq 'Bob' }).Count -ne 1) {
                        $failures.Add("${hostId}/${scenarioId}/p$($evidenceRow.pageNumber): missing combine multi-author semantic evidence")
                        continue
                    }
                }

                if (@($semantics.stableSignatures).Count -ne [int]$semantics.revisionCount) {
                    $failures.Add("${hostId}/${scenarioId}/p$($evidenceRow.pageNumber): compare/combine stable signatures do not cover every revision")
                    continue
                }

                $verifiedSemanticRows++
            }

            $comparisonRows = @($baselineComparisons | Where-Object {
                $_.hostId -eq $hostId -and
                $_.scenarioId -eq $scenarioId -and
                $_.baselineScenarioId -eq $scenarioId
            })
            if ($baselineComparisons.Count -gt 0 -and $comparisonRows.Count -eq 0) {
                $failures.Add("${hostId}/${scenarioId}: missing Word-baseline policy row")
                continue
            }

            foreach ($comparison in $comparisonRows) {
                if ($comparison.trust.passed -ne $true) {
                    $notes = @($comparison.trust.failures) -join '; '
                    if ([string]::IsNullOrWhiteSpace($notes)) {
                        $notes = [string]$comparison.skipReason
                    }
                    $failures.Add("$hostId/$scenarioId/$($comparison.outputName): baseline policy trust failed ($notes)")
                    continue
                }

                $verifiedBaselineRows++
            }
        }

        $unavailableComparisons = @($baselineComparisons | Where-Object {
            $_.scenarioId -eq $scenarioId -and
            $_.status -eq 'word-baseline-unavailable'
        })
        if ($unavailableComparisons.Count -gt 0) {
            $blocker = @($remainingBlockers | Where-Object {
                $_.blockerId -eq "${scenarioId}-word-baseline-fidelity" -and
                $_.scenarioId -eq $scenarioId -and
                $_.status -eq 'word-baseline-unavailable' -and
                $_.requiresWordBaseline -eq $true
            }) | Select-Object -First 1
            if (-not $blocker) {
                $failures.Add("${scenarioId}: missing honest word-baseline-unavailable review compare/combine blocker")
            }
            else {
                $verifiedUnavailableBlockers++
            }
        }
    }

    if ($failures.Count -gt 0) {
        throw "Review compare/combine visual proof readiness failed:`n - $($failures -join "`n - ")"
    }

    Write-Host "Review compare/combine visual proof readiness: trusted scenario rows=$trustedScenarioRows"
    Write-Host "Review compare/combine visual semantic rows: verified rows=$verifiedSemanticRows"
    if ($baselineComparisons.Count -gt 0) {
        Write-Host "Review compare/combine Word-baseline policy rows: verified rows=$verifiedBaselineRows"
    }
    else {
        Write-Host "Review compare/combine Word-baseline policy rows: no Word baseline mode requested"
    }
    if ($verifiedUnavailableBlockers -gt 0) {
        Write-Host "Review compare/combine Word-baseline unavailable blockers: verified rows=$verifiedUnavailableBlockers"
    }
}

function Assert-ReviewMarkupVisualProofReadiness {
    param(
        [Parameter(Mandatory = $true)][string]$SummaryJsonPath,
        [Parameter(Mandatory = $true)][string[]]$ScenarioIds
    )

    $requiredScenarioIds = @(
        'f2-tracked-changes',
        'f2-comments'
    )
    $selectedScenarioIds = @($requiredScenarioIds | Where-Object { $ScenarioIds -contains $_ })
    if ($selectedScenarioIds.Count -eq 0) {
        return
    }

    if (-not (Test-Path -LiteralPath $SummaryJsonPath -PathType Leaf)) {
        throw "Review markup visual proof readiness cannot be checked because the summary JSON is missing: $SummaryJsonPath"
    }

    $summary = Get-Content -LiteralPath $SummaryJsonPath -Raw | ConvertFrom-Json
    if ([int]$summary.schemaVersion -lt 42) {
        throw "Review markup visual proof readiness requires FreeW visual evidence summary schema v42 or newer, found v$($summary.schemaVersion)"
    }

    $readinessRows = @($summary.reviewMarkupProofReadiness)
    $scenarios = @($summary.scenarios)
    $baselineComparisons = @($summary.baselineComparisons)
    $remainingBlockers = @($summary.remainingEvidenceBlockers)
    $summaryFailures = @($summary.trust.failures)
    $requiredHosts = @(
        'wpf-fidelity-render',
        'avalonia-page-layout-shot'
    )
    $failures = New-Object System.Collections.Generic.List[string]
    $trustedScenarioRows = 0
    $verifiedSemanticRows = 0
    $verifiedBaselineRows = 0
    $verifiedUnavailableBlockers = 0

    foreach ($scenarioId in $selectedScenarioIds) {
        $proofRows = @($readinessRows | Where-Object { $_.scenarioId -eq $scenarioId })
        if ($proofRows.Count -eq 0) {
            $failures.Add("${scenarioId}: missing review markup proof readiness row")
        }
        foreach ($proofRow in $proofRows) {
            if ($proofRow.trust.passed -ne $true -or $proofRow.status -ne 'paired-renderer-proof-ready') {
                $notes = @($proofRow.trust.failures) -join '; '
                if ([string]::IsNullOrWhiteSpace($notes)) {
                    $notes = [string]$proofRow.baselineReadiness
                }
                $failures.Add("${scenarioId}/p$($proofRow.pageNumber): readiness status '$($proofRow.status)' failed ($notes)")
            }

            if ([string]::IsNullOrWhiteSpace([string]$proofRow.semanticEvidence) -or [string]$proofRow.semanticEvidence -eq '-') {
                $failures.Add("${scenarioId}/p$($proofRow.pageNumber): missing review markup semantic readiness summary")
            }
        }

        $semanticFailures = @($summaryFailures | Where-Object {
            ([string]$_).IndexOf("review renderer pair '$scenarioId'", [StringComparison]::Ordinal) -ge 0 -or
            ([string]$_).IndexOf("review markup visual proof '$scenarioId'", [StringComparison]::Ordinal) -ge 0
        })
        foreach ($semanticFailure in $semanticFailures) {
            $failures.Add("${scenarioId}: WPF/Avalonia review markup semantic parity drift ($semanticFailure)")
        }

        foreach ($hostId in $requiredHosts) {
            $match = @($scenarios | Where-Object {
                $_.hostId -eq $hostId -and
                $_.scenarioId -eq $scenarioId
            })

            if ($match.Count -eq 0) {
                $failures.Add("${hostId}/${scenarioId}: missing normalized scenario row")
                continue
            }

            $row = $match[0]
            if ($row.trust.passed -ne $true) {
                $notes = @($row.trust.failures) -join '; '
                if ([string]::IsNullOrWhiteSpace($notes)) {
                    $notes = 'no notes'
                }
                $failures.Add("${hostId}/${scenarioId}: scenario trust failed ($notes)")
                continue
            }

            if ([int]$row.trustedOutputs -lt 1) {
                $failures.Add("${hostId}/${scenarioId}: expected at least 1 trusted output, found $($row.trustedOutputs)")
                continue
            }

            $trustedScenarioRows++

            $evidenceRows = @($summary.evidence | Where-Object {
                $_.hostId -eq $hostId -and
                $_.scenarioId -eq $scenarioId -and
                $_.trust.passed -eq $true
            })
            if ($evidenceRows.Count -eq 0) {
                $failures.Add("${hostId}/${scenarioId}: missing trusted normalized evidence row for review markup semantics")
            }
            foreach ($evidenceRow in $evidenceRows) {
                $markup = $evidenceRow.reviewMarkup
                if ($scenarioId -eq 'f2-tracked-changes') {
                    if ([int]$markup.revisionCount -lt 1 -or
                        [int]$markup.insertionCount -lt 1 -or
                        [int]$markup.deletionCount -lt 1 -or
                        [int]$markup.authorCount -lt 3 -or
                        @($markup.authors | Where-Object { $_ -eq 'Alice' }).Count -ne 1 -or
                        @($markup.authors | Where-Object { $_ -eq 'Bob' }).Count -ne 1 -or
                        @($markup.authors | Where-Object { $_ -eq 'Carol' }).Count -ne 1) {
                        $failures.Add("${hostId}/${scenarioId}/p$($evidenceRow.pageNumber): missing tracked-change authorship semantic evidence")
                        continue
                    }

                    if (@($markup.revisionStableSignatures).Count -lt [int]$markup.revisionCount) {
                        $failures.Add("${hostId}/${scenarioId}/p$($evidenceRow.pageNumber): tracked-change signatures do not cover every revision")
                        continue
                    }
                }
                elseif ($scenarioId -eq 'f2-comments') {
                    if ([int]$markup.commentCount -lt 1 -or
                        [int]$markup.commentAnchorCount -lt 1 -or
                        [int]$markup.commentReferenceCount -lt 1) {
                        $failures.Add("${hostId}/${scenarioId}/p$($evidenceRow.pageNumber): missing comment anchor/reference semantic evidence")
                        continue
                    }

                    if (@($markup.commentStableSignatures).Count -lt [int]$markup.commentCount) {
                        $failures.Add("${hostId}/${scenarioId}/p$($evidenceRow.pageNumber): comment signatures do not cover every top-level comment")
                        continue
                    }
                }

                $verifiedSemanticRows++
            }

            $comparisonRows = @($baselineComparisons | Where-Object {
                $_.hostId -eq $hostId -and
                $_.scenarioId -eq $scenarioId -and
                $_.baselineScenarioId -eq $scenarioId
            })
            if ($baselineComparisons.Count -gt 0 -and $comparisonRows.Count -eq 0) {
                $failures.Add("${hostId}/${scenarioId}: missing Word-baseline policy row")
                continue
            }

            foreach ($comparison in $comparisonRows) {
                if ($comparison.trust.passed -ne $true) {
                    $notes = @($comparison.trust.failures) -join '; '
                    if ([string]::IsNullOrWhiteSpace($notes)) {
                        $notes = [string]$comparison.skipReason
                    }
                    $failures.Add("$hostId/$scenarioId/$($comparison.outputName): baseline policy trust failed ($notes)")
                    continue
                }

                $verifiedBaselineRows++
            }
        }

        $unavailableComparisons = @($baselineComparisons | Where-Object {
            $_.scenarioId -eq $scenarioId -and
            $_.status -eq 'word-baseline-unavailable'
        })
        if ($unavailableComparisons.Count -gt 0) {
            $blocker = @($remainingBlockers | Where-Object {
                $_.blockerId -eq "${scenarioId}-word-baseline-fidelity" -and
                $_.scenarioId -eq $scenarioId -and
                $_.status -eq 'word-baseline-unavailable' -and
                $_.requiresWordBaseline -eq $true
            }) | Select-Object -First 1
            if (-not $blocker) {
                $failures.Add("${scenarioId}: missing honest word-baseline-unavailable review markup blocker")
            }
            else {
                $verifiedUnavailableBlockers++
            }
        }
    }

    if ($failures.Count -gt 0) {
        throw "Review markup visual proof readiness failed:`n - $($failures -join "`n - ")"
    }

    Write-Host "Review markup visual proof readiness: trusted scenario rows=$trustedScenarioRows"
    Write-Host "Review markup visual semantic rows: verified rows=$verifiedSemanticRows"
    if ($baselineComparisons.Count -gt 0) {
        Write-Host "Review markup Word-baseline policy rows: verified rows=$verifiedBaselineRows"
    }
    else {
        Write-Host "Review markup Word-baseline policy rows: no Word baseline mode requested"
    }
    if ($verifiedUnavailableBlockers -gt 0) {
        Write-Host "Review markup Word-baseline unavailable blockers: verified rows=$verifiedUnavailableBlockers"
    }
}

function Assert-ReviewProofingVisualProofReadiness {
    param(
        [Parameter(Mandatory = $true)][string]$SummaryJsonPath,
        [Parameter(Mandatory = $true)][string[]]$ScenarioIds
    )

    $requiredScenarioIds = @(
        'review-proofing-visual-depth',
        'review-protection-proofing-comments-only'
    )
    $selectedScenarioIds = @($requiredScenarioIds | Where-Object { $ScenarioIds -contains $_ })
    if ($selectedScenarioIds.Count -eq 0) {
        return
    }

    if (-not (Test-Path -LiteralPath $SummaryJsonPath -PathType Leaf)) {
        throw "Review proofing visual proof readiness cannot be checked because the summary JSON is missing: $SummaryJsonPath"
    }

    $summary = Get-Content -LiteralPath $SummaryJsonPath -Raw | ConvertFrom-Json
    if ([int]$summary.schemaVersion -lt 34) {
        throw "Review proofing visual proof readiness requires FreeW visual evidence summary schema v34 or newer, found v$($summary.schemaVersion)"
    }

    $readinessRows = @($summary.reviewProofingProofReadiness)
    $scenarios = @($summary.scenarios)
    $evidenceRows = @($summary.evidence)
    $baselineComparisons = @($summary.baselineComparisons)
    $remainingBlockers = @($summary.remainingEvidenceBlockers)
    $requiredHosts = @(
        'wpf-fidelity-render',
        'avalonia-page-layout-shot'
    )
    $failures = New-Object System.Collections.Generic.List[string]
    $trustedScenarioRows = 0
    $verifiedSemanticRows = 0
    $verifiedBaselineRows = 0
    $verifiedUnavailableBlockers = 0

    foreach ($scenarioId in $selectedScenarioIds) {
        $proofRows = @($readinessRows | Where-Object { $_.scenarioId -eq $scenarioId })
        if ($proofRows.Count -eq 0) {
            $failures.Add("${scenarioId}: missing review proofing visual proof readiness row")
        }
        foreach ($proofRow in $proofRows) {
            if ($proofRow.trust.passed -ne $true -or $proofRow.status -ne 'paired-renderer-proof-ready') {
                $notes = @($proofRow.trust.failures) -join '; '
                if ([string]::IsNullOrWhiteSpace($notes)) {
                    $notes = [string]$proofRow.baselineReadiness
                }
                $failures.Add("${scenarioId}/p$($proofRow.pageNumber): readiness status '$($proofRow.status)' failed ($notes)")
            }

            if ([string]::IsNullOrWhiteSpace([string]$proofRow.semanticEvidence) -or [string]$proofRow.semanticEvidence -eq '-') {
                $failures.Add("${scenarioId}/p$($proofRow.pageNumber): missing proofing visual semantic readiness summary")
            }
        }

        foreach ($hostId in $requiredHosts) {
            $scenarioRows = @($scenarios | Where-Object {
                $_.hostId -eq $hostId -and
                $_.scenarioId -eq $scenarioId
            })
            if ($scenarioRows.Count -eq 0) {
                $failures.Add("${hostId}/${scenarioId}: missing normalized scenario row")
                continue
            }

            $scenarioRow = $scenarioRows[0]
            if ($scenarioRow.trust.passed -ne $true) {
                $notes = @($scenarioRow.trust.failures) -join '; '
                if ([string]::IsNullOrWhiteSpace($notes)) {
                    $notes = 'no notes'
                }
                $failures.Add("${hostId}/${scenarioId}: scenario trust failed ($notes)")
                continue
            }

            if ([int]$scenarioRow.trustedOutputs -lt 1) {
                $failures.Add("${hostId}/${scenarioId}: expected at least 1 trusted output, found $($scenarioRow.trustedOutputs)")
                continue
            }

            $trustedScenarioRows++

            $hostEvidenceRows = @($evidenceRows | Where-Object {
                $_.hostId -eq $hostId -and
                $_.scenarioId -eq $scenarioId -and
                $_.trust.passed -eq $true
            })
            if ($hostEvidenceRows.Count -eq 0) {
                $failures.Add("${hostId}/${scenarioId}: missing trusted normalized evidence row for proofing adornment semantics")
            }
            foreach ($row in $hostEvidenceRows) {
                $proofing = $row.proofingDiagnostics
                if ([int]$proofing.diagnosticCount -lt 1 -or
                    [int]$proofing.spellingCount -lt 1 -or
                    [int]$proofing.grammarCount -lt 1 -or
                    [int]$proofing.adornmentCount -lt 1 -or
                    [int]$proofing.adornmentCount -ne [int]$proofing.diagnosticCount -or
                    $proofing.hasSpellingUnderline -ne $true -or
                    $proofing.hasGrammarUnderline -ne $true) {
                    $failures.Add("${hostId}/${scenarioId}/p$($row.pageNumber): missing proofing visual adornment metadata")
                    continue
                }

                $adornmentSignatures = @($proofing.adornmentStableSignatures)
                if (@($adornmentSignatures | Where-Object { ([string]$_).IndexOf('adornment=spelling-squiggle', [StringComparison]::Ordinal) -ge 0 }).Count -eq 0) {
                    $failures.Add("${hostId}/${scenarioId}/p$($row.pageNumber): missing stable spelling-squiggle signature")
                    continue
                }
                if (@($adornmentSignatures | Where-Object { ([string]$_).IndexOf('adornment=grammar-squiggle', [StringComparison]::Ordinal) -ge 0 }).Count -eq 0) {
                    $failures.Add("${hostId}/${scenarioId}/p$($row.pageNumber): missing stable grammar-squiggle signature")
                    continue
                }

                $verifiedSemanticRows++
            }

            $comparisonRows = @($baselineComparisons | Where-Object {
                $_.hostId -eq $hostId -and
                $_.scenarioId -eq $scenarioId -and
                $_.baselineScenarioId -eq $scenarioId
            })
            if ($baselineComparisons.Count -gt 0 -and $comparisonRows.Count -eq 0) {
                $failures.Add("${hostId}/${scenarioId}: missing Word-baseline policy row")
                continue
            }

            foreach ($comparison in $comparisonRows) {
                if ($comparison.trust.passed -ne $true) {
                    $notes = @($comparison.trust.failures) -join '; '
                    if ([string]::IsNullOrWhiteSpace($notes)) {
                        $notes = [string]$comparison.skipReason
                    }
                    $failures.Add("$hostId/$scenarioId/$($comparison.outputName): baseline policy trust failed ($notes)")
                    continue
                }

                $baselineId = [string]$comparison.baselineId
                if (-not $baselineId.StartsWith($scenarioId + '/', [StringComparison]::OrdinalIgnoreCase)) {
                    $failures.Add("$hostId/$scenarioId/$($comparison.outputName): baselineId '$baselineId' expected scenario '$scenarioId'")
                    continue
                }

                $verifiedBaselineRows++
            }
        }

        $unavailableComparisons = @($baselineComparisons | Where-Object {
            $_.scenarioId -eq $scenarioId -and
            $_.status -eq 'word-baseline-unavailable'
        })
        if ($unavailableComparisons.Count -gt 0) {
            $blocker = @($remainingBlockers | Where-Object {
                $_.blockerId -eq "${scenarioId}-word-baseline-fidelity" -and
                $_.scenarioId -eq $scenarioId -and
                $_.status -eq 'word-baseline-unavailable' -and
                $_.requiresWordBaseline -eq $true
            }) | Select-Object -First 1
            if (-not $blocker) {
                $failures.Add("${scenarioId}: missing honest word-baseline-unavailable proofing visual blocker")
            }
            else {
                $verifiedUnavailableBlockers++
            }
        }
    }

    if ($failures.Count -gt 0) {
        throw "Review proofing visual proof readiness failed:`n - $($failures -join "`n - ")"
    }

    Write-Host "Review proofing visual proof readiness: trusted scenario rows=$trustedScenarioRows"
    Write-Host "Review proofing visual semantic rows: verified rows=$verifiedSemanticRows"
    if ($baselineComparisons.Count -gt 0) {
        Write-Host "Review proofing Word-baseline policy rows: verified rows=$verifiedBaselineRows"
    }
    else {
        Write-Host "Review proofing Word-baseline policy rows: no Word baseline mode requested"
    }
    if ($verifiedUnavailableBlockers -gt 0) {
        Write-Host "Review proofing Word-baseline unavailable blockers: verified rows=$verifiedUnavailableBlockers"
    }
}

function Assert-EquationStructureVisualProofReadiness {
    param(
        [Parameter(Mandatory = $true)][string]$SummaryJsonPath,
        [Parameter(Mandatory = $true)][string[]]$ScenarioIds
    )

    $scenarioId = 'equation-structures'
    if (-not ($ScenarioIds -contains $scenarioId)) {
        return
    }

    if (-not (Test-Path -LiteralPath $SummaryJsonPath -PathType Leaf)) {
        throw "Equation structure visual proof readiness cannot be checked because the summary JSON is missing: $SummaryJsonPath"
    }

    $summary = Get-Content -LiteralPath $SummaryJsonPath -Raw | ConvertFrom-Json
    if ([int]$summary.schemaVersion -lt 37) {
        throw "Equation structure visual proof readiness requires FreeW visual evidence summary schema v37 or newer, found v$($summary.schemaVersion)"
    }

    $scenarios = @($summary.scenarios)
    $evidenceRows = @($summary.evidence)
    $baselineComparisons = @($summary.baselineComparisons)
    $remainingBlockers = @($summary.remainingEvidenceBlockers)
    $summaryFailures = @($summary.trust.failures)
    $requiredHosts = @(
        'wpf-fidelity-render',
        'avalonia-page-layout-shot'
    )
    $requiredElementKindMinimums = @{
        Fraction = 1
        Radical = 1
        NAry = 2
        Matrix = 1
        EquationArray = 1
        Accent = 1
        Bar = 2
        Delimiter = 1
        GroupChar = 2
        FunctionApply = 2
    }
    $requiredElementGeometryTokens = @(
        'geometry=script',
        'geometry=fraction',
        'geometry=radical',
        'geometry=nary',
        'geometry=matrix',
        'geometry=equationarray',
        'geometry=accent',
        'geometry=bar',
        'geometry=delimiter',
        'geometry=groupchar',
        'geometry=function-apply'
    )
    $requiredSpacingGeometryTokens = @(
        'spacing=script',
        'spacing=fraction',
        'spacing=radical',
        'spacing=nary',
        'spacing=matrix',
        'spacing=equationarray'
    )
    $requiredSegmentRoleMinimums = @{
        Superscript = 2
        Subscript = 2
        FractionNumerator = 1
        FractionDenominator = 1
        RadicalDegree = 1
        RadicalRadicand = 1
        NAryLowerLimit = 2
        NAryUpperLimit = 2
        NAryOperand = 2
        MatrixCell = 6
        AccentMark = 1
        BarMark = 2
        DelimiterContent = 1
        GroupCharMark = 2
        FunctionArgument = 2
    }
    $failures = New-Object System.Collections.Generic.List[string]
    $trustedScenarioRows = 0
    $verifiedSemanticRows = 0
    $verifiedBaselineRows = 0
    $verifiedUnavailableBlockers = 0

    $semanticFailures = @($summaryFailures | Where-Object {
        ([string]$_).IndexOf("equation renderer pair '$scenarioId'", [StringComparison]::Ordinal) -ge 0
    })
    foreach ($semanticFailure in $semanticFailures) {
        $failures.Add("${scenarioId}: WPF/Avalonia equation semantic parity drift ($semanticFailure)")
    }

    foreach ($hostId in $requiredHosts) {
        $scenarioRows = @($scenarios | Where-Object {
            $_.hostId -eq $hostId -and
            $_.scenarioId -eq $scenarioId
        })
        if ($scenarioRows.Count -eq 0) {
            $failures.Add("${hostId}/${scenarioId}: missing normalized scenario row")
            continue
        }

        $scenarioRow = $scenarioRows[0]
        if ($scenarioRow.trust.passed -ne $true) {
            $notes = @($scenarioRow.trust.failures) -join '; '
            if ([string]::IsNullOrWhiteSpace($notes)) {
                $notes = 'no notes'
            }
            $failures.Add("${hostId}/${scenarioId}: scenario trust failed ($notes)")
            continue
        }

        if ([int]$scenarioRow.trustedOutputs -lt 1) {
            $failures.Add("${hostId}/${scenarioId}: expected at least 1 trusted output, found $($scenarioRow.trustedOutputs)")
            continue
        }

        $trustedScenarioRows++

        $hostEvidenceRows = @($evidenceRows | Where-Object {
            $_.hostId -eq $hostId -and
            $_.scenarioId -eq $scenarioId -and
            $_.trust.passed -eq $true
        })
        if ($hostEvidenceRows.Count -eq 0) {
            $failures.Add("${hostId}/${scenarioId}: missing trusted normalized evidence row for equation geometry semantics")
        }
        foreach ($row in $hostEvidenceRows) {
            $equations = $row.equations
            if ([int]$equations.equationCount -lt 8 -or
                [int]$equations.elementCount -lt 8 -or
                [int]$equations.segmentCount -lt 8) {
                $failures.Add("${hostId}/${scenarioId}/p$($row.pageNumber): missing modeled equation geometry depth")
                continue
            }

            $elementKindCounts = @($equations.elementKindCounts)
            foreach ($kind in $requiredElementKindMinimums.Keys) {
                $prefix = $kind + '='
                $match = @($elementKindCounts | Where-Object {
                    ([string]$_).StartsWith($prefix, [StringComparison]::Ordinal)
                }) | Select-Object -First 1
                $count = 0
                if ($match) {
                    [int]::TryParse(([string]$match).Substring($prefix.Length), [ref]$count) | Out-Null
                }
                $minimum = [int]$requiredElementKindMinimums[$kind]
                if ($count -lt $minimum) {
                    $failures.Add("${hostId}/${scenarioId}/p$($row.pageNumber): missing $kind equation element evidence, expected at least $minimum and found $count")
                    continue
                }
            }

            $segmentRoleCounts = @($equations.segmentRoleCounts)
            foreach ($role in $requiredSegmentRoleMinimums.Keys) {
                $prefix = $role + '='
                $match = @($segmentRoleCounts | Where-Object {
                    ([string]$_).StartsWith($prefix, [StringComparison]::Ordinal)
                }) | Select-Object -First 1
                $count = 0
                if ($match) {
                    [int]::TryParse(([string]$match).Substring($prefix.Length), [ref]$count) | Out-Null
                }
                $minimum = [int]$requiredSegmentRoleMinimums[$role]
                if ($count -lt $minimum) {
                    $failures.Add("${hostId}/${scenarioId}/p$($row.pageNumber): missing $role equation segment role evidence, expected at least $minimum and found $count")
                    continue
                }
            }

            $elementGeometrySignatures = @($equations.elementGeometrySignatures)
            if ($elementGeometrySignatures.Count -lt [int]$equations.elementCount) {
                $failures.Add("${hostId}/${scenarioId}/p$($row.pageNumber): element geometry signatures do not cover every equation element")
                continue
            }
            foreach ($token in $requiredElementGeometryTokens) {
                if (@($elementGeometrySignatures | Where-Object { ([string]$_).IndexOf($token, [StringComparison]::Ordinal) -ge 0 }).Count -eq 0) {
                    $failures.Add("${hostId}/${scenarioId}/p$($row.pageNumber): missing equation element geometry signature token '$token'")
                    continue
                }
            }

            $spacingGeometrySignatures = @($equations.spacingGeometrySignatures)
            foreach ($token in $requiredSpacingGeometryTokens) {
                if (@($spacingGeometrySignatures | Where-Object { ([string]$_).IndexOf($token, [StringComparison]::Ordinal) -ge 0 }).Count -eq 0) {
                    $failures.Add("${hostId}/${scenarioId}/p$($row.pageNumber): missing equation spacing geometry signature token '$token'")
                    continue
                }
            }

            $verifiedSemanticRows++
        }

        $comparisonRows = @($baselineComparisons | Where-Object {
            $_.hostId -eq $hostId -and
            $_.scenarioId -eq $scenarioId -and
            $_.baselineScenarioId -eq $scenarioId
        })
        if ($baselineComparisons.Count -gt 0 -and $comparisonRows.Count -eq 0) {
            $failures.Add("${hostId}/${scenarioId}: missing Word-baseline policy row")
            continue
        }

        foreach ($comparison in $comparisonRows) {
            if ($comparison.trust.passed -ne $true) {
                $notes = @($comparison.trust.failures) -join '; '
                if ([string]::IsNullOrWhiteSpace($notes)) {
                    $notes = [string]$comparison.skipReason
                }
                $failures.Add("$hostId/$scenarioId/$($comparison.outputName): baseline policy trust failed ($notes)")
                continue
            }

            $baselineId = [string]$comparison.baselineId
            if (-not $baselineId.StartsWith($scenarioId + '/', [StringComparison]::OrdinalIgnoreCase)) {
                $failures.Add("$hostId/$scenarioId/$($comparison.outputName): baselineId '$baselineId' expected scenario '$scenarioId'")
                continue
            }

            $verifiedBaselineRows++
        }
    }

    $unavailableComparisons = @($baselineComparisons | Where-Object {
        $_.scenarioId -eq $scenarioId -and
        $_.status -eq 'word-baseline-unavailable'
    })
    if ($unavailableComparisons.Count -gt 0) {
        $blocker = @($remainingBlockers | Where-Object {
            $_.blockerId -eq 'equation-structures-word-baseline-fidelity' -and
            $_.scenarioId -eq $scenarioId -and
            $_.status -eq 'word-baseline-unavailable' -and
            $_.requiresWordBaseline -eq $true
        }) | Select-Object -First 1
        if (-not $blocker) {
            $failures.Add("${scenarioId}: missing honest word-baseline-unavailable equation visual blocker")
        }
        else {
            $blockerSemanticEvidence = @($blocker.semanticEvidence)
            foreach ($token in @('structureFamilies=', 'roleFamilies=', 'geometryFamilies=', 'spacingFamilies=')) {
                if (@($blockerSemanticEvidence | Where-Object { ([string]$_).IndexOf($token, [StringComparison]::Ordinal) -ge 0 }).Count -eq 0) {
                    $failures.Add("${scenarioId}: word-baseline-unavailable blocker missing semantic evidence token '$token'")
                }
            }
            $verifiedUnavailableBlockers++
        }
    }

    if ($failures.Count -gt 0) {
        throw "Equation structure visual proof readiness failed:`n - $($failures -join "`n - ")"
    }

    Write-Host "Equation structure visual proof readiness: trusted scenario rows=$trustedScenarioRows"
    Write-Host "Equation structure visual semantic rows: verified rows=$verifiedSemanticRows"
    if ($baselineComparisons.Count -gt 0) {
        Write-Host "Equation structure Word-baseline policy rows: verified rows=$verifiedBaselineRows"
    }
    else {
        Write-Host "Equation structure Word-baseline policy rows: no Word baseline mode requested"
    }
    if ($verifiedUnavailableBlockers -gt 0) {
        Write-Host "Equation structure Word-baseline unavailable blocker: verified"
    }
}

function Assert-NotePlacementVisualProofReadiness {
    param(
        [Parameter(Mandatory = $true)][string]$SummaryJsonPath,
        [Parameter(Mandatory = $true)][string[]]$ScenarioIds
    )

    $selectedScenarioIds = @($ScenarioIds | Where-Object {
        $_ -eq 'f2-footnotes' -or $_ -eq 'f2-endnotes'
    })
    if ($selectedScenarioIds.Count -eq 0) {
        return
    }

    if (-not (Test-Path -LiteralPath $SummaryJsonPath -PathType Leaf)) {
        throw "Note placement visual proof readiness cannot be checked because the summary JSON is missing: $SummaryJsonPath"
    }

    $summary = Get-Content -LiteralPath $SummaryJsonPath -Raw | ConvertFrom-Json
    if ([int]$summary.schemaVersion -lt 44) {
        throw "Note placement visual proof readiness requires FreeW visual evidence summary schema v44 or newer, found v$($summary.schemaVersion)"
    }

    $scenarios = @($summary.scenarios)
    $readinessRows = @($summary.notePlacementProofReadiness)
    $baselineComparisons = @($summary.baselineComparisons)
    $remainingBlockers = @($summary.remainingEvidenceBlockers)
    $requiredHosts = @(
        'wpf-fidelity-render',
        'avalonia-page-layout-shot'
    )
    $failures = New-Object System.Collections.Generic.List[string]
    $trustedScenarioRows = 0
    $verifiedSemanticRows = 0
    $verifiedBaselineRows = 0
    $verifiedUnavailableBlockers = 0

    foreach ($scenarioId in $selectedScenarioIds) {
        $proofRows = @($readinessRows | Where-Object { $_.scenarioId -eq $scenarioId })
        if ($proofRows.Count -eq 0) {
            $failures.Add("${scenarioId}: missing note placement visual proof readiness row")
        }
        foreach ($proofRow in $proofRows) {
            if ($proofRow.trust.passed -ne $true -or $proofRow.status -ne 'paired-renderer-proof-ready') {
                $notes = @($proofRow.trust.failures) -join '; '
                if ([string]::IsNullOrWhiteSpace($notes)) {
                    $notes = [string]$proofRow.baselineReadiness
                }
                $failures.Add("${scenarioId}/p$($proofRow.pageNumber): readiness status '$($proofRow.status)' failed ($notes)")
            }

            if ([string]::IsNullOrWhiteSpace([string]$proofRow.semanticEvidence) -or [string]$proofRow.semanticEvidence -eq '-') {
                $failures.Add("${scenarioId}/p$($proofRow.pageNumber): missing note placement semantic readiness summary")
            }
        }

        foreach ($hostId in $requiredHosts) {
            $scenarioRows = @($scenarios | Where-Object {
                $_.hostId -eq $hostId -and
                $_.scenarioId -eq $scenarioId
            })
            if ($scenarioRows.Count -eq 0) {
                $failures.Add("${hostId}/${scenarioId}: missing normalized scenario row")
                continue
            }

            $scenarioRow = $scenarioRows[0]
            if ($scenarioRow.trust.passed -ne $true) {
                $notes = @($scenarioRow.trust.failures) -join '; '
                if ([string]::IsNullOrWhiteSpace($notes)) {
                    $notes = 'no notes'
                }
                $failures.Add("${hostId}/${scenarioId}: scenario trust failed ($notes)")
                continue
            }

            if ([int]$scenarioRow.trustedOutputs -lt [int]$scenarioRow.minimumExpectedOutputs) {
                $failures.Add("${hostId}/${scenarioId}: expected at least $($scenarioRow.minimumExpectedOutputs) trusted output(s), found $($scenarioRow.trustedOutputs)")
                continue
            }

            $trustedScenarioRows++

            $comparisonRows = @($baselineComparisons | Where-Object {
                $_.hostId -eq $hostId -and
                $_.scenarioId -eq $scenarioId -and
                $_.baselineScenarioId -eq $scenarioId
            })
            if ($baselineComparisons.Count -gt 0 -and $comparisonRows.Count -eq 0) {
                $failures.Add("${hostId}/${scenarioId}: missing Word-baseline policy row")
                continue
            }

            foreach ($comparison in $comparisonRows) {
                if ($comparison.trust.passed -ne $true) {
                    $notes = @($comparison.trust.failures) -join '; '
                    if ([string]::IsNullOrWhiteSpace($notes)) {
                        $notes = [string]$comparison.skipReason
                    }
                    $failures.Add("$hostId/$scenarioId/$($comparison.outputName): baseline policy trust failed ($notes)")
                    continue
                }

                $baselineId = [string]$comparison.baselineId
                if (-not $baselineId.StartsWith($scenarioId + '/', [StringComparison]::OrdinalIgnoreCase)) {
                    $failures.Add("$hostId/$scenarioId/$($comparison.outputName): baselineId '$baselineId' expected scenario '$scenarioId'")
                    continue
                }

                $verifiedBaselineRows++
            }
        }

        if ($scenarioId -eq 'f2-footnotes') {
            if (@($proofRows | Where-Object { [string]$_.semanticEvidence -match 'WPF footnotes' }).Count -eq 0 -or
                @($proofRows | Where-Object { [string]$_.semanticEvidence -match 'Avalonia footnotes' }).Count -eq 0) {
                $failures.Add("${scenarioId}: missing WPF/Avalonia footnote placement semantic evidence")
            }
        }
        elseif ($scenarioId -eq 'f2-endnotes') {
            $finalBodyRows = @($proofRows | Where-Object {
                [int]$_.pageNumber -eq 2 -and
                [string]$_.semanticEvidence -match 'endnotes' -and
                [string]$_.semanticEvidence -match 'body page' -and
                [string]$_.semanticEvidence -notmatch 'synthetic page'
            })
            if ($finalBodyRows.Count -eq 0) {
                $failures.Add("${scenarioId}: missing final body-page endnote semantic evidence")
            }
        }

        $verifiedSemanticRows += $proofRows.Count

        $unavailableComparisons = @($baselineComparisons | Where-Object {
            $_.scenarioId -eq $scenarioId -and
            $_.status -eq 'word-baseline-unavailable'
        })
        if ($unavailableComparisons.Count -gt 0) {
            $blocker = @($remainingBlockers | Where-Object {
                $_.blockerId -eq "${scenarioId}-word-baseline-fidelity" -and
                $_.scenarioId -eq $scenarioId -and
                $_.status -eq 'word-baseline-unavailable' -and
                $_.requiresWordBaseline -eq $true
            }) | Select-Object -First 1
            if (-not $blocker) {
                $failures.Add("${scenarioId}: missing honest word-baseline-unavailable note placement blocker")
            }
            else {
                $verifiedUnavailableBlockers++
            }
        }
    }

    if ($failures.Count -gt 0) {
        throw "Note placement visual proof readiness failed:`n - $($failures -join "`n - ")"
    }

    Write-Host "Note placement visual proof readiness: trusted scenario rows=$trustedScenarioRows"
    Write-Host "Note placement semantic rows: verified rows=$verifiedSemanticRows"
    if ($baselineComparisons.Count -gt 0) {
        Write-Host "Note placement Word-baseline policy rows: verified rows=$verifiedBaselineRows"
    }
    else {
        Write-Host "Note placement Word-baseline policy rows: no Word baseline mode requested"
    }
    if ($verifiedUnavailableBlockers -gt 0) {
        Write-Host "Note placement Word-baseline unavailable blockers: verified rows=$verifiedUnavailableBlockers"
    }
}

function Assert-SectionGeometryVisualProofReadiness {
    param(
        [Parameter(Mandatory = $true)][string]$SummaryJsonPath,
        [Parameter(Mandatory = $true)][string[]]$ScenarioIds
    )

    $scenarioId = 'f2-section-landscape'
    if (-not @($ScenarioIds | Where-Object { $_ -eq $scenarioId }).Count) {
        return
    }

    if (-not (Test-Path -LiteralPath $SummaryJsonPath -PathType Leaf)) {
        throw "Section geometry visual proof readiness cannot be checked because the summary JSON is missing: $SummaryJsonPath"
    }

    $summary = Get-Content -LiteralPath $SummaryJsonPath -Raw | ConvertFrom-Json
    if ([int]$summary.schemaVersion -lt 45) {
        throw "Section geometry visual proof readiness requires FreeW visual evidence summary schema v45 or newer, found v$($summary.schemaVersion)"
    }

    $scenarios = @($summary.scenarios)
    $readinessRows = @($summary.sectionGeometryProofReadiness | Where-Object { $_.scenarioId -eq $scenarioId })
    $baselineComparisons = @($summary.baselineComparisons)
    $remainingBlockers = @($summary.remainingEvidenceBlockers)
    $requiredHosts = @(
        'wpf-fidelity-render',
        'avalonia-page-layout-shot'
    )
    $requiredPages = @(1, 2)
    $failures = New-Object System.Collections.Generic.List[string]
    $trustedScenarioRows = 0
    $verifiedReadinessRows = 0
    $verifiedBaselineRows = 0
    $verifiedUnavailableBlockers = 0

    foreach ($pageNumber in $requiredPages) {
        $proofRow = @($readinessRows | Where-Object { [int]$_.pageNumber -eq $pageNumber }) | Select-Object -First 1
        if (-not $proofRow) {
            $failures.Add("${scenarioId}/p${pageNumber}: missing section geometry proof readiness row")
            continue
        }

        if ($proofRow.trust.passed -ne $true -or $proofRow.status -ne 'paired-renderer-proof-ready') {
            $notes = @($proofRow.trust.failures) -join '; '
            if ([string]::IsNullOrWhiteSpace($notes)) {
                $notes = [string]$proofRow.baselineReadiness
            }
            $failures.Add("${scenarioId}/p${pageNumber}: readiness status '$($proofRow.status)' failed ($notes)")
        }

        $semanticEvidence = [string]$proofRow.semanticEvidence
        if ([string]::IsNullOrWhiteSpace($semanticEvidence) -or $semanticEvidence -eq '-') {
            $failures.Add("${scenarioId}/p${pageNumber}: missing section geometry semantic readiness summary")
        }
        if ($pageNumber -eq 1 -and $semanticEvidence -notmatch 'section=1') {
            $failures.Add("${scenarioId}/p1: missing section=1 semantic evidence")
        }
        if ($pageNumber -eq 2 -and ($semanticEvidence -notmatch 'section=2' -or $semanticEvidence -notmatch 'landscape')) {
            $failures.Add("${scenarioId}/p2: missing landscape section=2 semantic evidence")
        }

        $verifiedReadinessRows++
    }

    foreach ($hostId in $requiredHosts) {
        $scenarioRows = @($scenarios | Where-Object {
            $_.hostId -eq $hostId -and
            $_.scenarioId -eq $scenarioId
        })
        if ($scenarioRows.Count -eq 0) {
            $failures.Add("${hostId}/${scenarioId}: missing normalized scenario row")
            continue
        }

        $scenarioRow = $scenarioRows[0]
        if ($scenarioRow.trust.passed -ne $true) {
            $notes = @($scenarioRow.trust.failures) -join '; '
            if ([string]::IsNullOrWhiteSpace($notes)) {
                $notes = 'no notes'
            }
            $failures.Add("${hostId}/${scenarioId}: scenario trust failed ($notes)")
            continue
        }

        if ([int]$scenarioRow.trustedOutputs -lt 2) {
            $failures.Add("${hostId}/${scenarioId}: expected at least 2 trusted outputs, found $($scenarioRow.trustedOutputs)")
            continue
        }

        $trustedScenarioRows++

        $comparisonRows = @($baselineComparisons | Where-Object {
            $_.hostId -eq $hostId -and
            $_.scenarioId -eq $scenarioId -and
            $_.baselineScenarioId -eq $scenarioId
        })
        if ($baselineComparisons.Count -gt 0 -and $comparisonRows.Count -eq 0) {
            $failures.Add("${hostId}/${scenarioId}: missing Word-baseline policy row")
            continue
        }

        foreach ($comparison in $comparisonRows) {
            if ($comparison.trust.passed -ne $true) {
                $notes = @($comparison.trust.failures) -join '; '
                if ([string]::IsNullOrWhiteSpace($notes)) {
                    $notes = [string]$comparison.skipReason
                }
                $failures.Add("$hostId/$scenarioId/$($comparison.outputName): baseline policy trust failed ($notes)")
                continue
            }

            $baselineId = [string]$comparison.baselineId
            if (-not $baselineId.StartsWith($scenarioId + '/', [StringComparison]::OrdinalIgnoreCase)) {
                $failures.Add("$hostId/$scenarioId/$($comparison.outputName): baselineId '$baselineId' expected scenario '$scenarioId'")
                continue
            }

            $verifiedBaselineRows++
        }
    }

    $unavailableComparisons = @($baselineComparisons | Where-Object {
        $_.scenarioId -eq $scenarioId -and
        $_.status -eq 'word-baseline-unavailable'
    })
    if ($unavailableComparisons.Count -gt 0) {
        $blocker = @($remainingBlockers | Where-Object {
            $_.blockerId -eq 'f2-section-landscape-word-baseline-fidelity' -and
            $_.scenarioId -eq $scenarioId -and
            $_.status -eq 'word-baseline-unavailable' -and
            $_.requiresWordBaseline -eq $true
        }) | Select-Object -First 1
        if (-not $blocker) {
            $failures.Add("${scenarioId}: missing honest word-baseline-unavailable section geometry blocker")
        }
        else {
            $verifiedUnavailableBlockers++
        }
    }

    if ($failures.Count -gt 0) {
        throw "Section geometry visual proof readiness failed:`n - $($failures -join "`n - ")"
    }

    Write-Host "Section geometry visual proof readiness: trusted scenario rows=$trustedScenarioRows"
    Write-Host "Section geometry semantic rows: verified rows=$verifiedReadinessRows"
    if ($baselineComparisons.Count -gt 0) {
        Write-Host "Section geometry Word-baseline policy rows: verified rows=$verifiedBaselineRows"
    }
    else {
        Write-Host "Section geometry Word-baseline policy rows: no Word baseline mode requested"
    }
    if ($verifiedUnavailableBlockers -gt 0) {
        Write-Host "Section geometry Word-baseline unavailable blocker: verified"
    }
}

New-Item -ItemType Directory -Force $fixtureDir | Out-Null
New-Item -ItemType Directory -Force $wpfDir | Out-Null
New-Item -ItemType Directory -Force $avaloniaDir | Out-Null

Invoke-DotNetStep 'Generate F2 DOCX fixtures' @(
    'run',
    '--project', $fidelityProject,
    '-c', $Configuration,
    '--',
    '--generate-f2-corpus', $fixtureDir
)

if (@($effectiveScenarioIds).Count -eq 0 -or $effectiveScenarioIds -contains 'f2-01-float-wrap') {
    Invoke-DotNetStep 'Generate floating/wrapping DOCX fixtures' @(
        'run',
        '--project', $f2ObjectsProject,
        '-c', $Configuration,
        '--',
        $fixtureDir
    )
}

if ($IncludeWordBaseline) {
    $wordBaselineArgs = @(
        '-FilesDir', $fixtureDir,
        '-OutDir', $wordBaselineRenderRoot
    )
    if ($effectiveScenarioIds.Count -gt 0) {
        $wordBaselineDocs = @($effectiveScenarioIds | ForEach-Object { "$_.docx" })
        $missingWordBaselineDocs = @($wordBaselineDocs | Where-Object {
            -not (Test-Path -LiteralPath (Join-Path $fixtureDir $_) -PathType Leaf)
        })
        if ($missingWordBaselineDocs.Count -gt 0) {
            throw "Selected Word baseline fixture(s) are missing: $($missingWordBaselineDocs -join ', ')"
        }

        $wordBaselineArgs += '-Docs'
        # powershell.exe -File binds only the next command-line token to a
        # string[] parameter. Pass one comma-delimited token so the baseline
        # script can expand every selected fixture without shifting arguments.
        $wordBaselineArgs += ($wordBaselineDocs -join ',')
    }

    Invoke-PowerShellStep 'Render MS Word baseline PNGs' $wordBaselineScript $wordBaselineArgs

    if (-not (Test-Path -LiteralPath $wordBaselineRoot -PathType Container)) {
        throw "Word baseline renderer did not produce the expected PNG directory: $wordBaselineRoot"
    }
}

$wpfFixtureDirForRender = $fixtureDir
if ($effectiveScenarioIds.Count -gt 0) {
    $selectedWpfFixtureDir = Join-Path $runRoot 'wpf-fixtures'
    New-Item -ItemType Directory -Force $selectedWpfFixtureDir | Out-Null
    foreach ($scenarioId in $effectiveScenarioIds) {
        Copy-Item -LiteralPath (Join-Path $fixtureDir "$scenarioId.docx") -Destination $selectedWpfFixtureDir -Force
    }
    $wpfFixtureDirForRender = $selectedWpfFixtureDir
}

Invoke-DotNetStep 'Render WPF visual evidence (composite)' @(
    'run',
    '--project', $fidelityProject,
    '-c', $Configuration,
    '--',
    $wpfFixtureDirForRender,
    $wpfDir,
    ([Math]::Max(1, $MaxPages).ToString([Globalization.CultureInfo]::InvariantCulture)),
    '--composite'
)

$avaloniaRenderArgs = @(
    'run',
    '--project', $pageShotProject,
    '-c', $Configuration,
    '--',
    $avaloniaDir,
    '--fixtures-dir', $fixtureDir
)
foreach ($scenarioId in $effectiveScenarioIds) {
    $avaloniaRenderArgs += @('--scenario', $scenarioId)
}
Invoke-DotNetStep 'Render Avalonia page-layout evidence' $avaloniaRenderArgs

$wpfManifest = Join-Path $wpfDir 'freew_visual_evidence_manifest.json'
$avaloniaManifest = Join-Path $avaloniaDir 'freew_visual_evidence_manifest.json'
if (-not (Test-Path $wpfManifest)) {
    throw "WPF visual evidence manifest was not produced: $wpfManifest"
}
if (-not (Test-Path $avaloniaManifest)) {
    throw "Avalonia visual evidence manifest was not produced: $avaloniaManifest"
}

$summaryArgs = @(
    'run',
    '--project', $summaryProject,
    '-c', $Configuration,
    '--',
    '--run-root', $runRoot,
    '--manifest', $wpfManifest,
    '--manifest', $avaloniaManifest,
    '--output-json', $summaryJson,
    '--output-md', $summaryMarkdown
)

if ($wordBaselineRoot) {
    $summaryArgs += @(
        '--word-baseline-dir', $wordBaselineRoot,
        '--baseline-tolerance', $BaselineTolerance
    )
}
elseif (-not [string]::IsNullOrWhiteSpace($WordBaselineUnavailableReason)) {
    $summaryArgs += @(
        '--word-baseline-unavailable-reason', $WordBaselineUnavailableReason,
        '--baseline-tolerance', $BaselineTolerance
    )
}

if ($allowNoWordFallbackEvidence) {
    $summaryArgs += '--allow-no-word-fallback-evidence'
}

foreach ($scenario in @($effectiveScenarioIds)) {
    $summaryArgs += @('--include-scenario', $scenario)
}

Invoke-DotNetStep 'Validate and normalize combined visual evidence' $summaryArgs
if (Test-ScenarioFilterIncludesBackstage $effectiveScenarioIds) {
    Assert-BackstageEvidenceReadiness $summaryJson $allowNoWordFallbackEvidence
}
else {
    Write-Host "Backstage evidence readiness: skipped by scenario filter"
}
Assert-CoreLayoutProofReadiness $summaryJson $effectiveScenarioIds
Assert-ReferencesHeavyWordBaselineProofReadiness $summaryJson $effectiveScenarioIds
Assert-LegalReferenceSectionPageProofReadiness $summaryJson $effectiveScenarioIds
Assert-PageCompositionProofReadiness $summaryJson $effectiveScenarioIds
Assert-FloatingWrappingProofReadiness $summaryJson $effectiveScenarioIds
Assert-HeaderFooterImageVisualProofReadiness $summaryJson $effectiveScenarioIds
Assert-TableLayoutProofReadiness $summaryJson $effectiveScenarioIds
Assert-TablePaginationPageCompositionProofReadiness $summaryJson $effectiveScenarioIds
Assert-DrawingObjectVisualProofReadiness $summaryJson $effectiveScenarioIds
Assert-GroupedDrawingObjectVisualProofReadiness $summaryJson $effectiveScenarioIds
Assert-SmartArtPolygonVisualProofReadiness $summaryJson $effectiveScenarioIds
Assert-ChartVisualProofReadiness $summaryJson $effectiveScenarioIds
Assert-WordArtWatermarkVisualProofReadiness $summaryJson $effectiveScenarioIds
Assert-ReviewMarkupVisualProofReadiness $summaryJson $effectiveScenarioIds
Assert-ReviewCompareCombineVisualProofReadiness $summaryJson $effectiveScenarioIds
Assert-ReviewProofingVisualProofReadiness $summaryJson $effectiveScenarioIds
Assert-EquationStructureVisualProofReadiness $summaryJson $effectiveScenarioIds
Assert-NotePlacementVisualProofReadiness $summaryJson $effectiveScenarioIds
Assert-SectionGeometryVisualProofReadiness $summaryJson $effectiveScenarioIds

Write-Host ""
Write-Host "Visual evidence run complete." -ForegroundColor Green
Write-Host "Run root: $runRoot"
if ($wordBaselineRoot) {
    Write-Host "Word baseline mode: word-png-comparison"
    Write-Host "Word baseline directory: $wordBaselineRoot"
    Write-Host "Baseline tolerance: $BaselineTolerance"
}
elseif (-not [string]::IsNullOrWhiteSpace($WordBaselineUnavailableReason)) {
    Write-Host "Word baseline mode: word-baseline-unavailable"
    Write-Host "Word baseline unavailable reason: $WordBaselineUnavailableReason"
    Write-Host "Baseline tolerance: $BaselineTolerance"
}
else {
    Write-Host "Word baseline mode: visual-evidence-only"
}
if (-not [string]::IsNullOrWhiteSpace($ScenarioSet)) {
    Write-Host "Scenario set: $ScenarioSet"
}
if (@($effectiveScenarioIds).Count -gt 0) {
    Write-Host "Scenario filter: $($effectiveScenarioIds -join ', ')"
}
Write-Host "Summary JSON: $summaryJson"
Write-Host "Summary Markdown: $summaryMarkdown"
