<#
.SYNOPSIS
    Render the MS Word "ground truth" side of a visual-fidelity comparison: each .docx -> PDF (Word COM)
    -> page PNGs (FreeW.PdfRasterize, Windows.Data.Pdf). No external tools (pdftoppm/magick/soffice) needed.

.DESCRIPTION
    The companion to FreeW.FidelityRender (which renders the FreeW side). Run both against the same
    corpus, then compare the freew/<doc>_pN.png and word/<doc>_pN.png page pairs (open them and eyeball,
    or pixel-diff if a differ is available). By default, pages retain the dimensions Word exported at
    96 dpi, so custom paper sizes compare against FreeW's authored page surface without distortion.

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
.PARAMETER Width      Optional fixed page width px. Supply both Width and Height to intentionally rescale pages.
.PARAMETER Height     Optional fixed page height px. Supply both Width and Height to intentionally rescale pages.
.PARAMETER PdfStagingDir
    Optional flat directory for temporary Word PDFs. The default is C:\Temp. Word writes uniquely named
    PDFs directly in that directory before FreeW.PdfRasterize writes PNGs to OutDir, avoiding the long and
    nested output paths that can stall this Word installation. Each temporary PDF is removed after rasterizing.
.PARAMETER ReuseRunningWord
    Explicitly reuse an existing Word COM instance. The default creates and owns an isolated Word process so
    a user document or dialog cannot interfere with a baseline run.
.PARAMETER HiddenWord
    Hide the isolated Word process. Visible Word is the default because it is materially more reliable for
    automated PDF export on this desktop installation.
.PARAMETER TracePath
    Optional path for immediate lifecycle diagnostics. This is useful when Word blocks a COM call before
    PowerShell can flush normal console output.
.EXAMPLE
    powershell -File freew-fidelity-corpus\tools\Render-WordBaseline.ps1 -FilesDir .\mycorpus -OutDir .\out\word
#>
[CmdletBinding()]
param(
    [string]$FilesDir,
    [string]$OutDir,
    [string[]]$Docs,
    [int]$Width = 0,
    [int]$Height = 0,
    [string]$PdfStagingDir,
    [switch]$ReuseRunningWord,
    [switch]$HiddenWord,
    [string]$TracePath
)

$ErrorActionPreference = 'Stop'

function Write-WordBaselineTrace([string]$Message) {
    $line = "$(Get-Date -Format 'o') $Message"
    Write-Host $line
    if ($TracePath) {
        Add-Content -LiteralPath $TracePath -Value $line
    }
}
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

$files =
if ($Docs) {
    # A child powershell.exe invocation preserves each argument as one token.
    # Accept a comma-delimited document list so a caller can pass several
    # selected fixtures without shifting subsequent named parameters.
    $Docs |
        ForEach-Object { $_ -split ',' } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { Join-Path $FilesDir $_ } |
        Where-Object { Test-Path $_ }
}
else { Get-ChildItem $FilesDir -Filter *.docx | Where-Object { $_.Name -notlike '~$*' } | Select-Object -ExpandProperty FullName }
if (-not $files) { throw "No .docx found under $FilesDir" }
if (($Width -gt 0) -ne ($Height -gt 0)) {
    throw "-Width and -Height must be supplied together when requesting fixed raster dimensions."
}

$wordType = [type]::GetTypeFromProgID('Word.Application', $false)
if ($null -eq $wordType) {
    throw "Word COM is not available: COM ProgID 'Word.Application' is not registered"
}
Write-Host "Word baseline mode: real-word-png-render"

$wordDir = Join-Path $OutDir 'word'; $null = New-Item -ItemType Directory -Force $wordDir
if (-not $PdfStagingDir) {
    $PdfStagingDir = Join-Path $env:SystemDrive 'Temp'
}
$pdfDir = [IO.Path]::GetFullPath($PdfStagingDir)
$null = New-Item -ItemType Directory -Force $pdfDir

Write-Host "== Word baseline: $($files.Count) doc(s) -> $wordDir ==" -ForegroundColor Cyan
$wordExportScript = Join-Path $scriptDir 'Export-WordPdf.ps1'
$powerShellExe = Join-Path $PSHOME 'powershell.exe'
if (-not (Test-Path -LiteralPath $powerShellExe)) { $powerShellExe = 'powershell.exe' }
$wordMode = if ($ReuseRunningWord) { 'reusing running Word COM instance' } else { "isolated Word COM child (visible=$(-not $HiddenWord))" }
Write-Host "Word baseline mode: $wordMode"
$ok = 0; $fail = 0; $fileIndex = 0
foreach ($docx in @($files)) {
    $currentIndex = $fileIndex++
    $name = [IO.Path]::GetFileNameWithoutExtension($docx)
    # Word can block ExportAsFixedFormat on this desktop when the output inherits an elaborate fixture name.
    # Keep the temporary COM target flat and short; the final PNG retains the fixture name in $wordDir.
    $pdf = Join-Path $pdfDir "fw-$PID-$currentIndex.pdf"
    try {
        Write-WordBaselineTrace "export $name -> $pdf"
        $exportArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $wordExportScript, '-InputPath', $docx, '-OutputPath', $pdf)
        if ($ReuseRunningWord) { $exportArgs += '-ReuseRunningWord' }
        if ($HiddenWord) { $exportArgs += '-HiddenWord' }
        & $powerShellExe @exportArgs
        if ($LASTEXITCODE -ne 0) { throw "Word PDF child exited with code $LASTEXITCODE." }
        if (-not (Test-Path -LiteralPath $pdf)) { throw 'Word PDF child completed without creating a PDF.' }
        Write-WordBaselineTrace "raster $name"
        $rasterArgs = @($rasterDll.FullName, $pdf, $wordDir)
        if ($Width -gt 0) {
            $rasterArgs += @($Width, $Height)
        }
        & $dotnet @rasterArgs | Out-Null
        $temporaryStem = [IO.Path]::GetFileNameWithoutExtension($pdf)
        foreach ($png in Get-ChildItem -LiteralPath $wordDir -Filter "$temporaryStem`_p*.png") {
            $suffix = $png.Name.Substring($temporaryStem.Length)
            Move-Item -LiteralPath $png.FullName -Destination (Join-Path $wordDir "$name$suffix") -Force
        }
        Write-Host "  ok   $name"
        $ok++
    }
    catch {
        Write-Warning "  FAIL $name : $($_.Exception.Message)"
        $fail++
    }
    finally {
        if (Test-Path -LiteralPath $pdf) {
            Remove-Item -LiteralPath $pdf -Force -EA SilentlyContinue
        }
    }
}
Write-Host "== done: $ok ok, $fail failed. PNGs in $wordDir ==" -ForegroundColor Green
if ($fail -gt 0) {
    exit 1
}
