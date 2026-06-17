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
  [string]$OutDir    = (Join-Path $PSScriptRoot '..\..\freew-fidelity-corpus\runs\word')
)
$ErrorActionPreference = 'Stop'
$CorpusDir = (Resolve-Path $CorpusDir).Path
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
$log  = Join-Path $OutDir '_progress.log'
$done = Join-Path $OutDir '_done.flag'
Remove-Item $done -ErrorAction SilentlyContinue
"start $(Get-Date -Format o)" | Set-Content $log

$wdExportFormatPDF = 17
$wdStatisticPages  = 2

$word = New-Object -ComObject Word.Application
$word.Visible = $false; $word.DisplayAlerts = 0
try { $word.AutomationSecurity = 3 } catch {}
$results = @()
try {
  foreach ($f in (Get-ChildItem -Path $CorpusDir -Filter *.docx | Sort-Object Name)) {
    $pdf = Join-Path $OutDir ($f.BaseName + '.pdf')
    if (Test-Path $pdf) { "skip $($f.Name)" | Add-Content $log; continue }
    $rec = [ordered]@{ file = $f.Name; pages = $null; status = 'ok'; error = '' }
    $doc = $null
    try {
      $doc = $word.Documents.Open($f.FullName, $false, $true)   # ConfirmConversions=false, ReadOnly=true
      $rec.pages = $doc.ComputeStatistics($wdStatisticPages)
      $doc.ExportAsFixedFormat($pdf, $wdExportFormatPDF)
    } catch { $rec.status = 'fail'; $rec.error = $_.Exception.Message }
    finally { if ($doc) { $doc.Close($false) | Out-Null } }
    "$($rec.status) $($f.Name) pages=$($rec.pages) $($rec.error)" | Add-Content $log
    $results += (New-Object psobject -Property $rec)
  }
} finally {
  $word.Quit()
  [System.Runtime.InteropServices.Marshal]::ReleaseComObject($word) | Out-Null
}
$results | Export-Csv -Path (Join-Path $OutDir 'word-export.csv') -NoTypeInformation
"done $(Get-Date -Format o) pdfs=$((Get-ChildItem (Join-Path $OutDir '*.pdf')).Count)" | Tee-Object -FilePath $done
