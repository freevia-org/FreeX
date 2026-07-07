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
#>
[CmdletBinding()]
param(
    [string]$OutDir,
    [string]$Configuration = 'Release',
    [int]$MaxPages = 6,
    [string]$WordBaselineDir,
    [switch]$IncludeWordBaseline,
    [string]$BaselineTolerance = 'word-png-default'
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

$fixtureDir = Join-Path $runRoot 'fixtures\f2'
$wpfDir = Join-Path $runRoot 'wpf'
$avaloniaDir = Join-Path $runRoot 'avalonia'
$summaryJson = Join-Path $runRoot 'freew_visual_evidence_summary.json'
$summaryMarkdown = Join-Path $runRoot 'freew_visual_evidence_summary.md'

$fidelityProject = Join-Path $repoRoot 'freew\tools\FreeW.FidelityRender\FreeW.FidelityRender.csproj'
$pageShotProject = Join-Path $repoRoot 'freew\tools\FreeW.PageLayoutShot\FreeW.PageLayoutShot.csproj'
$summaryProject = Join-Path $repoRoot 'freew\tools\FreeW.VisualEvidenceSummary\FreeW.VisualEvidenceSummary.csproj'
$wordBaselineScript = Join-Path $repoRoot 'freew-fidelity-corpus\tools\Render-WordBaseline.ps1'

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
    if ([int]$summary.schemaVersion -ne 24) {
        throw "Backstage evidence readiness requires FreeW visual evidence summary schema v24, found v$($summary.schemaVersion)"
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
            }
        }
    }

    if ($failures.Count -gt 0) {
        throw "Backstage evidence readiness failed:`n - $($failures -join "`n - ")"
    }

    Write-Host "Backstage evidence readiness: trusted required rows=$trustedCount"
    Write-Host "Backstage artifact metadata: verified rows=$trustedCount schema=v$($summary.schemaVersion)"
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

Invoke-DotNetStep 'Validate and normalize combined visual evidence' $summaryArgs
Assert-BackstageEvidenceReadiness $summaryJson

Write-Host ""
Write-Host "Visual evidence run complete." -ForegroundColor Green
Write-Host "Run root: $runRoot"
if ($wordBaselineRoot) {
    Write-Host "Word baseline mode: word-png-comparison"
    Write-Host "Word baseline directory: $wordBaselineRoot"
    Write-Host "Baseline tolerance: $BaselineTolerance"
}
else {
    Write-Host "Word baseline mode: visual-evidence-only"
}
Write-Host "Summary JSON: $summaryJson"
Write-Host "Summary Markdown: $summaryMarkdown"
