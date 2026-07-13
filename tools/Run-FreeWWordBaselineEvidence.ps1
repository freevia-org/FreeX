param(
    [string]$RunRoot = (Join-Path $PSScriptRoot "..\freew-fidelity-corpus\runs\visual-evidence"),
    [string]$Configuration = "Release",
    [int]$MaxPagesPerDocument = 3,
    [string]$WordApplicationProgId = "Word.Application",
    [switch]$AllowMissingWord,
    [switch]$SkipEvidenceRender
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath([string]$Path) {
    $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}

function Test-ComProgIdAvailable([string]$ProgId) {
    if ([string]::IsNullOrWhiteSpace($ProgId)) {
        return $false
    }

    $type = [type]::GetTypeFromProgID($ProgId, $false)
    return $null -ne $type
}

function Invoke-DotNetRun([string]$ProjectPath, [string[]]$ToolArgs) {
    & dotnet run --project $ProjectPath --configuration $Configuration -- @ToolArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet run failed for $ProjectPath with exit code $LASTEXITCODE"
    }
}

function Invoke-DotNetBuild([string]$ProjectPath) {
    & dotnet build $ProjectPath --configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed for $ProjectPath with exit code $LASTEXITCODE"
    }
}

function Invoke-DotNetRunNoBuild([string]$ProjectPath, [string[]]$ToolArgs) {
    & dotnet run --no-restore --no-build --project $ProjectPath --configuration $Configuration -- @ToolArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet run --no-build failed for $ProjectPath with exit code $LASTEXITCODE"
    }
}

function Write-WordBaselineUnavailableSummary([string]$Reason) {
    $skipPath = Join-Path $runRootFull "_word_baseline_unavailable.json"
    [ordered]@{
        status = "word-baseline-unavailable"
        evidenceMode = "no-word-fallback"
        baselineEvidenceClass = "word-baseline-unavailable"
        authoritativeWordPngParity = $false
        reason = $Reason
        allowMissingWord = [bool]$AllowMissingWord
        trust = [ordered]@{
            passed = $true
            failures = @()
        }
        summaryRowStatus = "word-baseline-unavailable"
        createdUtc = [DateTimeOffset]::UtcNow.ToString("O")
    } | ConvertTo-Json | Set-Content -Encoding UTF8 $skipPath

    if (-not $AllowMissingWord) {
        Write-Error "Word baseline is unavailable: $Reason. Re-run with -AllowMissingWord to verify the no-Word summary path."
        exit 3
    }

    Write-Host "Word baseline unavailable: $skipPath"
    Write-Host "Word baseline mode: no-word-fallback"

    Invoke-DotNetRun $summaryProject @(
        "--run-root", $runRootFull,
        "--manifest", $wpfManifest,
        "--manifest", $avaloniaManifest,
        "--word-baseline-scope", "generated-corpus",
        "--baseline-tolerance", "word-png-default",
        "--allow-no-word-fallback-evidence",
        "--word-baseline-unavailable-reason", $Reason,
        "--output-json", $summaryJson,
        "--output-md", $summaryMd)

    Write-Host "summary json: $summaryJson"
    Write-Host "summary markdown: $summaryMd"
    exit 0
}

$repoRoot = Resolve-FullPath (Join-Path $PSScriptRoot "..")
$runRootFull = Resolve-FullPath $RunRoot
$fixtureDir = Join-Path $runRootFull "fixtures"
$wpfDir = Join-Path $runRootFull "wpf"
$avaloniaDir = Join-Path $runRootFull "avalonia"
$wordPdfDir = Join-Path $runRootFull "word-pdf"
$wordBaselineDir = Join-Path $runRootFull "word-baseline"
$summaryJson = Join-Path $runRootFull "freew_visual_evidence_summary.json"
$summaryMd = Join-Path $runRootFull "freew_visual_evidence_summary.md"
$fidelityRenderProject = Join-Path $repoRoot "freew\tools\FreeW.FidelityRender\FreeW.FidelityRender.csproj"
$pageLayoutShotProject = Join-Path $repoRoot "freew\tools\FreeW.PageLayoutShot\FreeW.PageLayoutShot.csproj"
$pdfRasterizeProject = Join-Path $repoRoot "freew\tools\FreeW.PdfRasterize\FreeW.PdfRasterize.csproj"
$summaryProject = Join-Path $repoRoot "freew\tools\FreeW.VisualEvidenceSummary\FreeW.VisualEvidenceSummary.csproj"
$wordExportScript = Join-Path $repoRoot "tools\FreeW.RenderCompare\Export-WordPdfs.ps1"

New-Item -ItemType Directory -Force -Path $runRootFull, $fixtureDir, $wpfDir, $avaloniaDir, $wordPdfDir, $wordBaselineDir | Out-Null

if (-not $SkipEvidenceRender) {
    Invoke-DotNetBuild $fidelityRenderProject
    Invoke-DotNetRunNoBuild $fidelityRenderProject @("--generate-f2-corpus", $fixtureDir)
    Invoke-DotNetRunNoBuild $fidelityRenderProject @($fixtureDir, $wpfDir, $MaxPagesPerDocument.ToString([Globalization.CultureInfo]::InvariantCulture), "--composite", "--software-fallback")
    Invoke-DotNetRun $pageLayoutShotProject @($avaloniaDir)
}

$wpfManifest = Join-Path $wpfDir "freew_visual_evidence_manifest.json"
$avaloniaManifest = Join-Path $avaloniaDir "freew_visual_evidence_manifest.json"
if (-not (Test-Path $wpfManifest) -or -not (Test-Path $avaloniaManifest)) {
    throw "expected visual evidence manifests were not found under '$wpfDir' and '$avaloniaDir'"
}

$wordAvailable = Test-ComProgIdAvailable $WordApplicationProgId
if (-not $wordAvailable) {
    Write-WordBaselineUnavailableSummary "COM ProgID '$WordApplicationProgId' is not registered"
}

try {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $wordExportScript -CorpusDir $fixtureDir -OutDir $wordPdfDir
    if ($LASTEXITCODE -ne 0) {
        throw "Word PDF export failed with exit code $LASTEXITCODE"
    }

    foreach ($pdf in Get-ChildItem -Path $wordPdfDir -Filter *.pdf | Sort-Object Name) {
        Invoke-DotNetRun $pdfRasterizeProject @($pdf.FullName, $wordBaselineDir)
    }

    foreach ($page in 1..[Math]::Max(1, $MaxPagesPerDocument)) {
        $printPreviewSource = Join-Path $wordBaselineDir ("backstage-print-preview-fidelity_p{0}.png" -f $page)
        $printPreviewTarget = Join-Path $wordBaselineDir ("backstage-print-preview_p{0}.png" -f $page)
        if ((Test-Path $printPreviewSource) -and -not (Test-Path $printPreviewTarget)) {
            Copy-Item -LiteralPath $printPreviewSource -Destination $printPreviewTarget
        }

        $pdfExportSource = Join-Path $wordBaselineDir ("backstage-pdf-export-fidelity_p{0}.png" -f $page)
        $pdfExportTarget = Join-Path $wordBaselineDir ("backstage-pdf-export_p{0}.png" -f $page)
        if ((Test-Path $pdfExportSource) -and -not (Test-Path $pdfExportTarget)) {
            Copy-Item -LiteralPath $pdfExportSource -Destination $pdfExportTarget
        }
    }
}
catch {
    Write-WordBaselineUnavailableSummary "MS Word baseline PNG generation failed: $($_.Exception.Message)"
}

Invoke-DotNetRun $summaryProject @(
    "--run-root", $runRootFull,
    "--manifest", $wpfManifest,
    "--manifest", $avaloniaManifest,
    "--word-baseline-dir", $wordBaselineDir,
    "--word-baseline-scope", "generated-corpus",
    "--baseline-tolerance", "word-png-default",
    "--output-json", $summaryJson,
    "--output-md", $summaryMd)

Write-Host "fixtures: $((Get-ChildItem -Path $fixtureDir -Filter *.docx).Count)"
Write-Host "word baseline PNGs: $((Get-ChildItem -Path $wordBaselineDir -Filter *.png).Count)"
Write-Host "Word baseline mode: real-word-png-comparison"
Write-Host "summary json: $summaryJson"
Write-Host "summary markdown: $summaryMd"
