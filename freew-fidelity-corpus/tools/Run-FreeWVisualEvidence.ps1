<#
.SYNOPSIS
    Generate local FreeW WPF + Avalonia visual evidence and retain normalized summaries.

.DESCRIPTION
    This no-Word runner creates the F2 DOCX fixture set under an ignored run folder, renders WPF
    evidence with FreeW.FidelityRender in composite mode, renders Avalonia page-layout evidence
    with FreeW.PageLayoutShot, validates both raw visual evidence manifests through the shared
    FreeW.App.Presentation normalizer, and writes small stable JSON/Markdown summaries.

    Bulky DOCX/PNG/raw-manifest artifacts stay under freew-fidelity-corpus/runs/.

.EXAMPLE
    pwsh freew-fidelity-corpus/tools/Run-FreeWVisualEvidence.ps1 -OutDir freew-fidelity-corpus/runs/visual-evidence-smoke
#>
[CmdletBinding()]
param(
    [string]$OutDir,
    [string]$Configuration = 'Release',
    [int]$MaxPages = 6
)

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptDir '..\..')).Path

if (-not $OutDir) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $OutDir = Join-Path $scriptDir "..\runs\visual-evidence-$stamp"
}

if ([IO.Path]::IsPathRooted($OutDir)) {
    $runRoot = $OutDir
}
else {
    $runRoot = Join-Path $repoRoot $OutDir
}
$runRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($runRoot)

$fixtureDir = Join-Path $runRoot 'fixtures\f2'
$wpfDir = Join-Path $runRoot 'wpf'
$avaloniaDir = Join-Path $runRoot 'avalonia'
$summaryJson = Join-Path $runRoot 'freew_visual_evidence_summary.json'
$summaryMarkdown = Join-Path $runRoot 'freew_visual_evidence_summary.md'

$fidelityProject = Join-Path $repoRoot 'freew\tools\FreeW.FidelityRender\FreeW.FidelityRender.csproj'
$pageShotProject = Join-Path $repoRoot 'freew\tools\FreeW.PageLayoutShot\FreeW.PageLayoutShot.csproj'
$summaryProject = Join-Path $repoRoot 'freew\tools\FreeW.VisualEvidenceSummary\FreeW.VisualEvidenceSummary.csproj'

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

Invoke-DotNetStep 'Validate and normalize combined visual evidence' @(
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

Write-Host ""
Write-Host "Visual evidence run complete." -ForegroundColor Green
Write-Host "Run root: $runRoot"
Write-Host "Summary JSON: $summaryJson"
Write-Host "Summary Markdown: $summaryMarkdown"
