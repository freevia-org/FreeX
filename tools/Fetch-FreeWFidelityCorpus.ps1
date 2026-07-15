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
. (Join-Path $PSScriptRoot 'ToolScriptSupport.ps1')

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $ManifestPath) {
    $ManifestPath = Join-Path $repoRoot 'freew-fidelity-corpus/manifest.csv'
}
$filesDir = Join-Path $repoRoot 'freew-fidelity-corpus/files'

$result = Invoke-FidelityCorpusDownload `
    -ManifestPath $ManifestPath `
    -FilesDirectory $filesDir `
    -CorpusLabel 'FreeW fidelity corpus' `
    -LocalDirectoryLabel 'freew-fidelity-corpus/files/' `
    -Force:$Force

exit $result.ExitCode
