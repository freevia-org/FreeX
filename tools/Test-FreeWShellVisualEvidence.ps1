$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
& (Join-Path $PSScriptRoot 'Generate-FreeWShellVisualEvidence.ps1') -Check
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$path = Join-Path $repo 'docs\parity\freew-shell-visual-2026-08-16\freew_shell_visual_evidence.json'
$evidence = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
if ($evidence.counts.pairedStaticChrome -ne 40) { throw 'FreeW shell matrix must retain 40 static WPF/Avalonia pairs.' }
if ($evidence.counts.wpfContextualOnly -lt 1) { throw 'FreeW shell matrix must retain its explicitly classified WPF contextual evidence.' }
if ($evidence.counts.wordOfficeChromeReferences -ne 0) { throw 'This app-host shell matrix must not imply a Word chrome baseline.' }
foreach ($row in $evidence.pairedStaticChrome) {
    if ($row.classification -ne 'paired-capture-review-required') { throw "Unexpected static shell classification: $($row.classification)" }
    foreach ($relative in @($row.wpfPath, $row.avaloniaPath)) {
        $file = Join-Path $repo ($relative -replace '/', '\\')
        if (-not (Test-Path -LiteralPath $file) -or (Get-Item -LiteralPath $file).Length -le 0) { throw "Missing or empty shell PNG: $relative" }
    }
}
Write-Output "FreeW shell evidence passed: $($evidence.counts.pairedStaticChrome) paired static captures; $($evidence.counts.wpfContextualOnly) explicit contextual coverage gaps."
