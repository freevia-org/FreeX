$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
& (Join-Path $PSScriptRoot 'Generate-FreeWShellVisualEvidence.ps1') -Check
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$path = Join-Path $repo 'docs\parity\freew-shell-visual-2026-08-16\freew_shell_visual_evidence.json'
$evidence = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
if ($evidence.counts.pairedStaticChrome -ne 40) { throw 'FreeW shell matrix must retain 40 static WPF/Avalonia pairs.' }
if ($evidence.counts.pairedContextualChrome -ne 32) { throw 'FreeW shell matrix must retain 32 paired contextual WPF/Avalonia captures.' }
if ($evidence.counts.avaloniaContextualMissing -ne 0) { throw 'FreeW shell matrix must not report a contextual-state coverage gap after fixture capture.' }
if ($evidence.counts.wordOfficeChromeReferences -ne 36 -or
    [string]$evidence.nativeWordChrome.captureStatus -ne 'complete' -or
    [int]$evidence.nativeWordChrome.actualCaptureCount -ne 36) {
    throw 'FreeW shell matrix must retain its complete 36-state native Word chrome reference lane.'
}
foreach ($row in $evidence.pairedStaticChrome) {
    if ($row.classification -ne 'paired-capture-review-required') { throw "Unexpected static shell classification: $($row.classification)" }
    foreach ($relative in @($row.wpfPath, $row.avaloniaPath)) {
        $file = Join-Path $repo ($relative -replace '/', '\\')
        if (-not (Test-Path -LiteralPath $file) -or (Get-Item -LiteralPath $file).Length -le 0) { throw "Missing or empty shell PNG: $relative" }
    }
}
foreach ($row in $evidence.pairedContextualChrome) {
    if ($row.classification -ne 'paired-contextual-capture-review-required') { throw "Unexpected contextual shell classification: $($row.classification)" }
    if ([string]::IsNullOrWhiteSpace([string]$row.avaloniaFixture) -or $row.avaloniaFixture -eq 'static') {
        throw "Contextual row has no real Avalonia editor fixture: $($row.avaloniaTabId)"
    }
    foreach ($relative in @($row.wpfPath, $row.avaloniaPath)) {
        $file = Join-Path $repo ($relative -replace '/', '\\')
        if (-not (Test-Path -LiteralPath $file) -or (Get-Item -LiteralPath $file).Length -le 0) { throw "Missing or empty contextual shell PNG: $relative" }
    }
}
Write-Output "FreeW shell evidence passed: $($evidence.counts.pairedStaticChrome) paired static captures; $($evidence.counts.pairedContextualChrome) paired contextual captures."
