<#
.SYNOPSIS
    Render the MS Word "ground truth" side of a visual-fidelity comparison: each .docx -> PDF (Word COM)
    -> page PNGs (FreeW.PdfRasterize, Windows.Data.Pdf). No external tools (pdftoppm/magick/soffice) needed.

.DESCRIPTION
    The companion to FreeW.FidelityRender (which renders the FreeW side). Run both against the same
    corpus, then compare the freew/<doc>_pN.png and word/<doc>_pN.png page pairs (open them and eyeball,
    or pixel-diff if a differ is available). Pages are rendered at 816x1056 (8.5x11 @ 96dpi) to match
    FreeW.FidelityRender 1:1.

    Robustness (learned the hard way): Word COM hangs headless if a modal dialog appears. This script
      * clears HKCU\...\Word\Resiliency\StartupItems first, so a prior crash's Document-Recovery pane
        cannot pop (the #1 cause of silent hangs after a force-killed WINWORD),
      * opens every doc READ-ONLY (Documents.Open($path,$false,$true)) so Word never takes a write lock
        or shows a "file in use" prompt,
      * sets DisplayAlerts=wdAlertsNone and AutomationSecurity=ForceDisable,
      * reuses ONE Word.Application across all docs (cold start is ~20s; warm exports are ~1-2s),
      * always Quit()s cleanly. NEVER force-kill WINWORD mid-export — that recreates the recovery state.
        If you must kill it, re-run: the resiliency clear at the top will recover the next run.

.PARAMETER FilesDir   Folder of input .docx (default: ../files next to this script).
.PARAMETER OutDir     Output folder for word/ PNGs (default: ../runs/word-<n>). Created if absent.
.PARAMETER Docs       Optional explicit file names; default: all *.docx in FilesDir.
.PARAMETER Width      Page width px  (default 816).
.PARAMETER Height     Page height px (default 1056).
.EXAMPLE
    powershell -File freew-fidelity-corpus\tools\Render-WordBaseline.ps1 -FilesDir .\mycorpus -OutDir .\out\word
#>
[CmdletBinding()]
param(
    [string]$FilesDir,
    [string]$OutDir,
    [string[]]$Docs,
    [int]$Width = 816,
    [int]$Height = 1056
)

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptDir '..\..')).Path

if (-not $FilesDir) { $FilesDir = Join-Path $scriptDir '..\files' }
$FilesDir = (Resolve-Path $FilesDir).Path
if (-not $OutDir) { $OutDir = Join-Path $scriptDir '..\runs\word-out' }
$null = New-Item -ItemType Directory -Force $OutDir

# Locate the FreeW.PdfRasterize tool (build it if needed).
$dotnet = Join-Path $env:LocalAppData 'Microsoft\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }
$env:DOTNET_ROOT = Split-Path $dotnet
$rasterProj = Join-Path $repoRoot 'freew\tools\FreeW.PdfRasterize\FreeW.PdfRasterize.csproj'
$rasterDll = Get-ChildItem -Recurse -Filter FreeW.PdfRasterize.dll (Join-Path $repoRoot 'freew\tools\FreeW.PdfRasterize\bin') -EA SilentlyContinue | Select-Object -First 1
if (-not $rasterDll) {
    Write-Host "Building FreeW.PdfRasterize..." -ForegroundColor Cyan
    & $dotnet build $rasterProj -c Release | Out-Null
    $rasterDll = Get-ChildItem -Recurse -Filter FreeW.PdfRasterize.dll (Join-Path $repoRoot 'freew\tools\FreeW.PdfRasterize\bin') | Select-Object -First 1
}

# --- Robustness: clear the crash-recovery state that hangs headless Word automation. ---
foreach ($k in 'StartupItems', 'DisabledItems') {
    Remove-Item "HKCU:\Software\Microsoft\Office\16.0\Word\Resiliency\$k" -Recurse -Force -EA SilentlyContinue
}

$files =
if ($Docs) { $Docs | ForEach-Object { Join-Path $FilesDir $_ } | Where-Object { Test-Path $_ } }
else { Get-ChildItem $FilesDir -Filter *.docx | Where-Object { $_.Name -notlike '~$*' } | Select-Object -ExpandProperty FullName }
if (-not $files) { throw "No .docx found under $FilesDir" }

$wordType = [type]::GetTypeFromProgID('Word.Application', $false)
if ($null -eq $wordType) {
    throw "Word COM is not available: COM ProgID 'Word.Application' is not registered"
}
Write-Host "Word baseline mode: real-word-png-render"

$pdfDir = Join-Path $OutDir '_pdf'; $null = New-Item -ItemType Directory -Force $pdfDir
$wordDir = Join-Path $OutDir 'word'; $null = New-Item -ItemType Directory -Force $wordDir

Write-Host "== Word baseline: $($files.Count) doc(s) -> $wordDir ==" -ForegroundColor Cyan
$word = New-Object -ComObject Word.Application
$word.Visible = $false
try { $word.DisplayAlerts = 0 } catch {}
try { $word.AutomationSecurity = 3 } catch {}   # msoAutomationSecurityForceDisable
$ok = 0; $fail = 0
try {
    foreach ($docx in $files) {
        $name = [IO.Path]::GetFileNameWithoutExtension($docx)
        $pdf = Join-Path $pdfDir "$name.pdf"
        try {
            $d = $word.Documents.Open($docx, $false, $true)   # ConfirmConversions=$false, ReadOnly=$true
            $d.ExportAsFixedFormat($pdf, 17)                  # wdExportFormatPDF
            $d.Close($false)
            & $dotnet $rasterDll.FullName $pdf $wordDir $Width $Height | Out-Null
            Write-Host "  ok   $name"
            $ok++
        }
        catch {
            Write-Warning "  FAIL $name : $($_.Exception.Message)"
            $fail++
        }
    }
}
finally {
    $word.Quit()
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($word) | Out-Null
    [System.GC]::Collect(); [System.GC]::WaitForPendingFinalizers()
}
Write-Host "== done: $ok ok, $fail failed. PNGs in $wordDir ==" -ForegroundColor Green
