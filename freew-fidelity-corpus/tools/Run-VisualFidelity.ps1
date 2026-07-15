<#
.SYNOPSIS
    Visual fidelity comparison: FreeW's rendering vs a ground-truth renderer (MS Word or LibreOffice).

.DESCRIPTION
    For each corpus .docx this script:
      1. Renders FreeW's view to PNG via the FreeW.FidelityRender tool (one PNG per page).
      2. Produces a ground-truth PDF using MS Word (COM) or LibreOffice (soffice), then rasterizes
         the PDF pages to PNG (pdftoppm / ImageMagick / soffice, whichever is available).
      3. Diffs each page pair (mean absolute pixel difference + % pixels changed beyond a threshold),
         writes a CSV summary, and saves side-by-side montages for eyeballing.

    Designed to run on a machine WITH MS Word installed (the box that produced this corpus had none).
    Word is preferred for the baseline; LibreOffice is the fallback. PDF rasterization needs one of
    pdftoppm (poppler), magick (ImageMagick), or LibreOffice.

.PARAMETER FilesDir
    Folder of input .docx (default: the corpus files/ folder next to this script).

.PARAMETER OutDir
    Output root for renders + report (default: freew-fidelity-corpus/runs/visual-<timestamp>).

.PARAMETER Docs
    Optional explicit list of file names to compare (default: a representative subset).

.PARAMETER Baseline
    auto | word | libreoffice  (default auto: Word if available, else LibreOffice).

.EXAMPLE
    pwsh freew-fidelity-corpus/tools/Run-VisualFidelity.ps1
.EXAMPLE
    pwsh freew-fidelity-corpus/tools/Run-VisualFidelity.ps1 -Baseline word -Docs bookmarks.docx,delins.docx
#>
[CmdletBinding()]
param(
    [string]$FilesDir,
    [string]$OutDir,
    [string[]]$Docs,
    [ValidateSet('auto', 'word', 'libreoffice')]
    [string]$Baseline = 'auto',
    [int]$DiffThreshold = 32   # per-channel delta above which a pixel counts as "changed"
)

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptDir '..\..')).Path

if (-not $FilesDir) { $FilesDir = Join-Path $scriptDir '..\files' }
$FilesDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($FilesDir)
if (-not (Test-Path $FilesDir)) { throw "Files dir not found: $FilesDir (run tools/Fetch-FreeWFidelityCorpus.ps1 or supply -FilesDir)" }

if (-not $OutDir) {
    $stamp = (Get-Date -Format 'yyyyMMdd-HHmmss')
    $OutDir = Join-Path $scriptDir "..\runs\visual-$stamp"
}
$null = New-Item -ItemType Directory -Force $OutDir
$freewDir = Join-Path $OutDir 'freew'; $null = New-Item -ItemType Directory -Force $freewDir
$baseDir = Join-Path $OutDir 'baseline'; $null = New-Item -ItemType Directory -Force $baseDir
$diffDir = Join-Path $OutDir 'diff'; $null = New-Item -ItemType Directory -Force $diffDir

if (-not $Docs -or $Docs.Count -eq 0) {
    $Docs = @('bookmarks.docx', 'delins.docx', 'ComplexNumberedLists.docx', 'checkboxes.docx',
        'table_footnotes.docx', 'footnotes.docx', 'VariousPictures.docx', 'PageSpecificHeadFoot.docx')
}
$inputs = $Docs | ForEach-Object { Join-Path $FilesDir $_ } | Where-Object { Test-Path $_ }
if ($inputs.Count -eq 0) { throw "None of the requested docs exist under $FilesDir" }

function Have($exe) { [bool](Get-Command $exe -ErrorAction SilentlyContinue) }

# ---- 1. FreeW renders -------------------------------------------------------
Write-Host "== Rendering FreeW side ==" -ForegroundColor Cyan
$renderProj = Join-Path $repoRoot 'freew\tools\FreeW.FidelityRender\FreeW.FidelityRender.csproj'
dotnet build $renderProj -c Release --nologo | Out-Null
foreach ($f in $inputs) {
    dotnet run --project $renderProj -c Release --no-build -- $f $freewDir 6 | Out-Null
    Write-Host "  freew: $([IO.Path]::GetFileNameWithoutExtension($f))"
}

# ---- 2. Ground-truth baseline ----------------------------------------------
$useWord = $false; $useLo = $false
if ($Baseline -in @('auto', 'word')) {
    try { $w = New-Object -ComObject Word.Application; $w.Quit(); $useWord = $true } catch { $useWord = $false }
}
if (-not $useWord -and $Baseline -in @('auto', 'libreoffice')) { $useLo = Have 'soffice' }
if (-not $useWord -and -not $useLo) {
    throw "No baseline renderer: install MS Word, or LibreOffice (soffice on PATH). Re-run on a Word-equipped machine."
}
Write-Host "== Baseline renderer: $(if($useWord){'MS Word (COM)'}else{'LibreOffice'}) ==" -ForegroundColor Cyan

$pdfDir = Join-Path $OutDir 'baseline-pdf'; $null = New-Item -ItemType Directory -Force $pdfDir
function Export-Pdf($docx, $pdf) {
    if ($useWord) {
        $word = New-Object -ComObject Word.Application
        $word.Visible = $false
        # Automation hardening: without these, opening a doc that Word wants to write-lock
        # (or any prompt-triggering content) pops a MODAL dialog that hangs headless COM.
        try { $word.DisplayAlerts = 0 } catch {}              # wdAlertsNone
        try { $word.AutomationSecurity = 3 } catch {}          # msoAutomationSecurityForceDisable (no macros)
        try {
            # Open(FileName, ConfirmConversions=$false, ReadOnly=$true): ReadOnly avoids the
            # write-lock / "~$" owner file and the "file in use" prompt that caused the hang.
            $doc = $word.Documents.Open($docx, $false, $true)
            # ExportAsFixedFormat: OutputFileName, ExportFormat=17 (wdExportFormatPDF)
            $doc.ExportAsFixedFormat($pdf, 17)
            $doc.Close($false)
        } finally {
            $word.Quit()
            [System.Runtime.InteropServices.Marshal]::ReleaseComObject($word) | Out-Null
            [System.GC]::Collect()
            [System.GC]::WaitForPendingFinalizers()
        }
    }
    else {
        & soffice --headless --convert-to pdf --outdir (Split-Path $pdf) $docx | Out-Null
    }
}

# PDF -> PNG rasterizer (pick what's available)
# FreeW.PdfRasterize is the preferred path on Windows when external tools are absent.
$rasterizeProj = Join-Path $repoRoot 'freew\tools\FreeW.PdfRasterize\FreeW.PdfRasterize.csproj'
$rasterizeDll  = Join-Path $repoRoot 'freew\tools\FreeW.PdfRasterize\bin\Release\net10.0-windows10.0.19041.0\FreeW.PdfRasterize.dll'

function Ensure-PdfRasterizer {
    if (-not (Test-Path $rasterizeDll)) {
        Write-Host "  [build] FreeW.PdfRasterize ..." -ForegroundColor DarkGray
        dotnet build $rasterizeProj -c Release --nologo | Out-Null
    }
}

function Rasterize-Pdf($pdf, $outPrefix) {
    if (Have 'pdftoppm') { & pdftoppm -png -r 96 $pdf $outPrefix | Out-Null; return }
    if (Have 'magick') { & magick -density 96 $pdf "$outPrefix-%d.png" | Out-Null; return }
    if (Have 'soffice') {
        # soffice renders only the first page to png; acceptable fallback for page 1.
        & soffice --headless --convert-to png --outdir (Split-Path $outPrefix) $pdf | Out-Null; return
    }
    # Windows-native fallback: FreeW.PdfRasterize (Windows.Data.Pdf WinRT API).
    # Outputs to a temp dir then renames files to match the <outPrefix>-N.png convention
    # used by pdftoppm so the rest of the script sees consistent names.
    if (Test-Path $rasterizeProj) {
        Ensure-PdfRasterizer
        $tmpOut = Join-Path ([IO.Path]::GetTempPath()) ([IO.Path]::GetRandomFileName())
        $null = New-Item -ItemType Directory -Force $tmpOut
        $stem = [IO.Path]::GetFileNameWithoutExtension($pdf)
        dotnet $rasterizeDll $pdf $tmpOut | Out-Null
        # FreeW.PdfRasterize emits <stem>_pN.png (1-based).
        # Copy them to <outPrefix>-N.png (pdftoppm-style, 1-based) so pairing logic
        # finds them via the $baseDir wildcard filter "*.png".
        # We also keep the _pN.png suffix because the diff loop sorts by Name and that
        # already aligns with FidelityRender's <docname>_pN.png naming.
        Get-ChildItem $tmpOut -Filter "${stem}_p*.png" | Sort-Object Name | ForEach-Object {
            $dest = Join-Path (Split-Path $outPrefix) $_.Name
            Copy-Item $_.FullName $dest -Force
        }
        Remove-Item $tmpOut -Recurse -Force
        return
    }
    throw "No PDF rasterizer found. Install pdftoppm (Poppler), ImageMagick (magick), or LibreOffice (soffice) on PATH; or ensure freew/tools/FreeW.PdfRasterize exists in the repo. Baseline PDFs are in $pdfDir."
}

foreach ($f in $inputs) {
    $name = [IO.Path]::GetFileNameWithoutExtension($f)
    $pdf = Join-Path $pdfDir "$name.pdf"
    Export-Pdf $f $pdf
    if (Test-Path $pdf) {
        Rasterize-Pdf $pdf (Join-Path $baseDir $name)
        Write-Host "  baseline: $name"
    }
    else { Write-Host "  baseline FAILED: $name" -ForegroundColor Yellow }
}

# ---- 3. Diff ----------------------------------------------------------------
Add-Type -AssemblyName System.Drawing
function Load-Bmp($path) { New-Object System.Drawing.Bitmap (([System.Drawing.Image]::FromFile($path))) }

function Compare-Pages($aPath, $bPath, $diffPath, $threshold) {
    $a = Load-Bmp $aPath; $b = Load-Bmp $bPath
    try {
        $w = [Math]::Min($a.Width, $b.Width); $h = [Math]::Min($a.Height, $b.Height)
        $step = [Math]::Max(1, [int]($w / 400))   # sample to keep it fast
        [long]$sum = 0; [long]$changed = 0; [long]$n = 0
        for ($y = 0; $y -lt $h; $y += $step) {
            for ($x = 0; $x -lt $w; $x += $step) {
                $pa = $a.GetPixel($x, $y); $pb = $b.GetPixel($x, $y)
                $d = [Math]::Abs($pa.R - $pb.R) + [Math]::Abs($pa.G - $pb.G) + [Math]::Abs($pa.B - $pb.B)
                $sum += $d; $n++
                if (($d / 3) -gt $threshold) { $changed++ }
            }
        }
        $meanAbs = if ($n) { [Math]::Round($sum / (3.0 * $n), 2) } else { 0 }
        $pctChanged = if ($n) { [Math]::Round(100.0 * $changed / $n, 2) } else { 0 }
        [pscustomobject]@{ MeanAbsDelta = $meanAbs; PctChanged = $pctChanged }
    }
    finally { $a.Dispose(); $b.Dispose() }
}

$rows = @()
foreach ($f in $inputs) {
    $name = [IO.Path]::GetFileNameWithoutExtension($f)
    $freewPages = Get-ChildItem $freewDir -Filter "$name`_p*.png" | Sort-Object Name
    $basePages = Get-ChildItem $baseDir -Filter "$name*.png" | Sort-Object Name
    $pairs = [Math]::Min($freewPages.Count, $basePages.Count)
    if ($pairs -eq 0) {
        $rows += [pscustomobject]@{ doc = $name; page = '-'; meanAbsDelta = '-'; pctChanged = '-'; note = 'missing renders' }
        continue
    }
    for ($i = 0; $i -lt $pairs; $i++) {
        $cmp = Compare-Pages $freewPages[$i].FullName $basePages[$i].FullName (Join-Path $diffDir "$name`_p$($i+1).png") $DiffThreshold
        $rows += [pscustomobject]@{ doc = $name; page = ($i + 1); meanAbsDelta = $cmp.MeanAbsDelta; pctChanged = $cmp.PctChanged; note = '' }
    }
}

$csv = Join-Path $OutDir 'visual-fidelity.csv'
$rows | Export-Csv -NoTypeInformation -Encoding utf8 $csv
Write-Host ""
Write-Host "== Visual fidelity (lower = closer to baseline) ==" -ForegroundColor Cyan
$rows | Format-Table -AutoSize
Write-Host "CSV:     $csv"
Write-Host "FreeW:   $freewDir"
Write-Host "Baseline:$baseDir"
Write-Host "Note: meanAbsDelta is 0-255 per-channel; pctChanged = % sampled pixels differing > $DiffThreshold."
