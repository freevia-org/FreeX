#requires -Version 5.1
<#
.SYNOPSIS
    Downloads the on-demand real-world DOCX fidelity corpus declared in freew-fidelity-corpus/manifest.csv.

.DESCRIPTION
    The FreeW fidelity corpus is NOT part of the normal build, test, or release flow. Only the
    manifest and this downloader are committed; third-party DOCX binaries land in the git-ignored
    freew-fidelity-corpus/files/ folder so the repo stays small and does not redistribute them.

    Every committed manifest row must point at a permissively-licensed or public-domain source
    and record its license. Add private documents with source=local and local:// URLs; this
    downloader skips those rows.

.PARAMETER Force
    Re-download files that already exist locally.

.PARAMETER ManifestPath
    Override the manifest path (defaults to freew-fidelity-corpus/manifest.csv next to the repo root).
#>
[CmdletBinding()]
param(
    [switch]$Force,
    [string]$ManifestPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $ManifestPath) {
    $ManifestPath = Join-Path $repoRoot 'freew-fidelity-corpus/manifest.csv'
}
$filesDir = Join-Path $repoRoot 'freew-fidelity-corpus/files'

if (-not (Test-Path $ManifestPath)) {
    throw "Manifest not found: $ManifestPath"
}
if (-not (Test-Path $filesDir)) {
    New-Item -ItemType Directory -Path $filesDir -Force | Out-Null
}

$rows = Import-Csv -Path $ManifestPath
$downloaded = 0
$skipped = 0
$failed = 0
$localSkipped = 0

foreach ($row in $rows) {
    if ([string]::IsNullOrWhiteSpace($row.license)) {
        throw "Manifest row '$($row.id)' is missing a license."
    }

    $target = Join-Path $filesDir $row.file
    if ($row.url -like 'local://*' -or $row.source -eq 'local') {
        if (Test-Path $target) {
            Write-Host "[local ] $($row.file) (present)"
        } else {
            Write-Warning "[local ] $($row.file) declared local but not found in freew-fidelity-corpus/files/"
        }
        $localSkipped++
        continue
    }

    if ((Test-Path $target) -and -not $Force) {
        $skipped++
        Write-Host "[skip  ] $($row.file) (already downloaded)"
        continue
    }

    try {
        Invoke-WebRequest -Uri $row.url -OutFile $target -UseBasicParsing -TimeoutSec 120
        $size = (Get-Item $target).Length
        if ($size -le 0) { throw "downloaded 0 bytes" }
        $downloaded++
        Write-Host ("[ok    ] {0} ({1:N0} bytes, {2})" -f $row.file, $size, $row.license)
    } catch {
        $failed++
        if (Test-Path $target) { Remove-Item $target -Force -ErrorAction SilentlyContinue }
        Write-Warning "[fail  ] $($row.file) <- $($row.url): $($_.Exception.Message)"
    }
}

Write-Host ""
Write-Host ("FreeW fidelity corpus: {0} downloaded, {1} already present, {2} local, {3} failed (of {4} rows)." -f `
    $downloaded, $skipped, $localSkipped, $failed, $rows.Count)
Write-Host "Files: $filesDir"

if ($failed -gt 0) { exit 1 }
exit 0
