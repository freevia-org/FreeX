#requires -Version 5.1
<#
.SYNOPSIS
    Downloads the on-demand real-world XLSX/XLS fidelity corpus declared in fidelity-corpus/manifest.csv.

.DESCRIPTION
    The fidelity corpus is NOT part of the normal build/test/release flow. It backs the on-demand
    FreeX <-> Excel visual+functional fidelity batch (tools/FreeX.FidelityCompare). Only the manifest and
    this downloader are committed; the binary workbooks land in the git-ignored fidelity-corpus/files/
    folder so the repo stays clean and no third-party files are redistributed here.

    Every committed manifest row must point at a permissively-licensed or public-domain source (the
    'license' column is required and recorded). Add your own complex local workbooks by dropping them into
    fidelity-corpus/files/ and adding a row with source=local and a local:// url (those are skipped here).

.PARAMETER Force
    Re-download files that already exist locally.

.PARAMETER ManifestPath
    Override the manifest path (defaults to fidelity-corpus/manifest.csv next to the repo root).

.PARAMETER Source
    Download only rows whose source column matches this value.
#>
[CmdletBinding()]
param(
    [switch]$Force,
    [string]$ManifestPath,
    [string]$Source
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ToolScriptSupport.ps1')

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $ManifestPath) {
    $ManifestPath = Join-Path $repoRoot 'fidelity-corpus/manifest.csv'
}
$filesDir = Join-Path $repoRoot 'fidelity-corpus/files'

$result = Invoke-FidelityCorpusDownload `
    -ManifestPath $ManifestPath `
    -FilesDirectory $filesDir `
    -CorpusLabel 'Fidelity corpus' `
    -LocalDirectoryLabel 'fidelity-corpus/files/' `
    -Source $Source `
    -Force:$Force

exit $result.ExitCode
