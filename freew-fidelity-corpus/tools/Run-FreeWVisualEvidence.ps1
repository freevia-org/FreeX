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
    pwsh freew-fidelity-corpus/tools/Run-FreeWVisualEvidence.ps1 -OutDir freew-fidelity-corpus/runs/drawing-object-proof -ScenarioSet DrawingObjectVisualProof -WordBaselineUnavailableReason "COM ProgID 'Word.Application' is not registered"

.EXAMPLE
    pwsh freew-fidelity-corpus/tools/Run-FreeWVisualEvidence.ps1 -OutDir freew-fidelity-corpus/runs/floating-wrapping-proof -ScenarioSet FloatingWrappingVisualProof -WordBaselineUnavailableReason "COM ProgID 'Word.Application' is not registered"
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
$repoRoot = (Resolve-Path (Join-Path $scriptDir '..\..')).Path

function Resolve-RepositoryPath {
    param(
        [Parameter(Mandatory = $true)][string]$Path
    )

    if ([IO.Path]::IsPathRooted($Path)) {
        $candidate = $Path
    }
    else {
        $candidate = Join-Path $repoRoot $Path
    }

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($candidate)
}

if (-not $OutDir) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $OutDir = Join-Path $scriptDir "..\runs\visual-evidence-$stamp"
}

$runRoot = Resolve-RepositoryPath $OutDir
$wordBaselineRoot = $null
$wordBaselineRenderRoot = $null
if ($IncludeWordBaseline) {
    $wordBaselineRenderRoot = if ([string]::IsNullOrWhiteSpace($WordBaselineDir)) {
        Join-Path $runRoot 'word-baseline'
    }
    else {
        Resolve-RepositoryPath $WordBaselineDir
    }
    $wordBaselineRoot = Join-Path $wordBaselineRenderRoot 'word'
}
elseif (-not [string]::IsNullOrWhiteSpace($WordBaselineDir)) {
    $wordBaselineRoot = Resolve-RepositoryPath $WordBaselineDir
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
        'equation-structures',
        'f2-footnotes',
        'f2-endnotes',
        'f2-section-landscape',
        'f2-tracked-changes',
        'f2-comments',
        'review-proofing-visual-depth',
        'review-protection-proofing-comments-only'
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
    TableLayoutProof = @(
        'table-layout-complex',
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

function Invoke-DotNetStep {
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    Write-Host ""
    Write-Host "== $Label ==" -ForegroundColor Cyan
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Label failed with exit code $LASTEXITCODE"
    }
}

function Invoke-PowerShellStep {
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$ScriptPath,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    $powerShell = Get-Command powershell.exe -ErrorAction SilentlyContinue
    if (-not $powerShell) {
        throw "$Label requires powershell.exe because MS Word COM automation is Windows-only."
    }

    Write-Host ""
    Write-Host "== $Label ==" -ForegroundColor Cyan
    & $powerShell.Path -NoProfile -ExecutionPolicy Bypass -File $ScriptPath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Label failed with exit code $LASTEXITCODE"
    }
}

function Assert-BackstageEvidenceReadiness {
    param(
        [Parameter(Mandatory = $true)][string]$SummaryJsonPath
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
    $requiredHosts = @(
        'wpf-fidelity-render',
        'avalonia-page-layout-shot'
    )
    $requiredPages = @(1, 2)
    $failures = New-Object System.Collections.Generic.List[string]
    $trustedCount = 0

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
                if ($row.status -ne 'trusted') {
                    $output = if ([string]::IsNullOrWhiteSpace([string]$row.outputSummary)) { '-' } else { [string]$row.outputSummary }
                    $notes = if ([string]::IsNullOrWhiteSpace([string]$row.notes)) { 'no notes' } else { [string]$row.notes }
                    $failures.Add("$scenarioId/$hostId/p${pageNumber}: status '$($row.status)' output '$output' notes '$notes'")
                    continue
                }

                $trustedCount++

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

    Write-Host "Backstage evidence readiness: trusted required rows=$trustedCount"
    Write-Host "Backstage artifact metadata: verified rows=$trustedCount schema=v$($summary.schemaVersion)"
    Write-Host "Backstage capture routes: verified rows=$trustedCount"
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
        'table-page-composition-stress' = 2
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
    Invoke-PowerShellStep 'Render MS Word baseline PNGs' $wordBaselineScript @(
        '-FilesDir', $fixtureDir,
        '-OutDir', $wordBaselineRenderRoot
    )

    if (-not (Test-Path -LiteralPath $wordBaselineRoot -PathType Container)) {
        throw "Word baseline renderer did not produce the expected PNG directory: $wordBaselineRoot"
    }
}

Invoke-DotNetStep 'Render WPF visual evidence (composite)' @(
    'run',
    '--project', $fidelityProject,
    '-c', $Configuration,
    '--',
    $fixtureDir,
    $wpfDir,
    ([Math]::Max(1, $MaxPages).ToString([Globalization.CultureInfo]::InvariantCulture)),
    '--composite'
)

Invoke-DotNetStep 'Render Avalonia page-layout evidence' @(
    'run',
    '--project', $pageShotProject,
    '-c', $Configuration,
    '--',
    $avaloniaDir
)

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

foreach ($scenario in @($effectiveScenarioIds)) {
    $summaryArgs += @('--include-scenario', $scenario)
}

Invoke-DotNetStep 'Validate and normalize combined visual evidence' $summaryArgs
if (Test-ScenarioFilterIncludesBackstage $effectiveScenarioIds) {
    Assert-BackstageEvidenceReadiness $summaryJson
}
else {
    Write-Host "Backstage evidence readiness: skipped by scenario filter"
}
Assert-CoreLayoutProofReadiness $summaryJson $effectiveScenarioIds
Assert-PageCompositionProofReadiness $summaryJson $effectiveScenarioIds
Assert-FloatingWrappingProofReadiness $summaryJson $effectiveScenarioIds
Assert-TableLayoutProofReadiness $summaryJson $effectiveScenarioIds
Assert-DrawingObjectVisualProofReadiness $summaryJson $effectiveScenarioIds

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
