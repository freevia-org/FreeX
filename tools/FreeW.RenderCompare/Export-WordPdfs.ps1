# Export corpus DOCX -> PDF via MS Word COM (ground-truth rendering for the FreeW visual comparison).
#
# Idempotent: skips files already exported. Writes word-export.csv + per-file _progress.log and a
# _done.flag sentinel.
#
# IMPORTANT (this environment): Word COM only drives reliably when this script runs in a *foreground*,
# interactive session. A harness-backgrounded or detached (Start-Process / Start-Job) child runs without
# an interactive window station and Word stalls on launch. Word itself is fast (~3-5 s/file once warm);
# if it appears to hang at 0 PDFs, it is the non-interactive context, not the document.
param(
  [string]$CorpusDir = (Join-Path $PSScriptRoot '..\..\freew-fidelity-corpus\files'),
  [string]$OutDir    = (Join-Path $PSScriptRoot '..\..\freew-fidelity-corpus\runs\word'),
  [string]$PdfStagingDir
)
$ErrorActionPreference = 'Stop'
$CorpusDir = (Resolve-Path $CorpusDir).Path
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
$log  = Join-Path $OutDir '_progress.log'
$done = Join-Path $OutDir '_done.flag'
Remove-Item $done -ErrorAction SilentlyContinue
"start $(Get-Date -Format o)" | Set-Content $log

if (-not $PdfStagingDir) {
  $PdfStagingDir = Join-Path $env:SystemDrive 'Temp'
}
$PdfStagingDir = [IO.Path]::GetFullPath($PdfStagingDir)
New-Item -ItemType Directory -Force -Path $PdfStagingDir | Out-Null
$wordExportHelper = Join-Path $PSScriptRoot '..\..\freew-fidelity-corpus\tools\Export-WordPdf.ps1'
$powerShellExe = Join-Path $PSHOME 'powershell.exe'
if (-not (Test-Path -LiteralPath $powerShellExe)) { $powerShellExe = 'powershell.exe' }

$results = @()
foreach ($f in (Get-ChildItem -Path $CorpusDir -Filter *.docx | Sort-Object Name)) {
  $pdf = Join-Path $OutDir ($f.BaseName + '.pdf')
  if (Test-Path $pdf) { "skip $($f.Name)" | Add-Content $log; continue }
  $rec = [ordered]@{ file = $f.Name; pages = $null; status = 'ok'; error = '' }
  $stagedPdf = Join-Path $PdfStagingDir ("fw-{0}-{1}.pdf" -f $PID, $results.Count)
  try {
    & $powerShellExe -NoProfile -ExecutionPolicy Bypass -File $wordExportHelper -InputPath $f.FullName -OutputPath $stagedPdf
    if ($LASTEXITCODE -ne 0) { throw "Word PDF child exited with code $LASTEXITCODE." }
    if (-not (Test-Path -LiteralPath $stagedPdf)) { throw 'Word PDF child completed without creating a PDF.' }
    Move-Item -LiteralPath $stagedPdf -Destination $pdf -Force
  } catch { $rec.status = 'fail'; $rec.error = $_.Exception.Message }
  finally { if (Test-Path -LiteralPath $stagedPdf) { Remove-Item -LiteralPath $stagedPdf -Force -ErrorAction SilentlyContinue } }
  "$($rec.status) $($f.Name) pages=$($rec.pages) $($rec.error)" | Add-Content $log
  $results += (New-Object psobject -Property $rec)
}
$results | Export-Csv -Path (Join-Path $OutDir 'word-export.csv') -NoTypeInformation
"done $(Get-Date -Format o) pdfs=$((Get-ChildItem (Join-Path $OutDir '*.pdf')).Count)" | Tee-Object -FilePath $done
