#requires -Version 5.1
<#
.SYNOPSIS
    Runs the on-demand FreeX <-> Microsoft Excel fidelity batch over the fidelity corpus.

.DESCRIPTION
    This is NOT part of the normal build/test/release flow — it is a manual, on-demand comparison that
    requires desktop Excel (COM automation). It:
      1. downloads any missing corpus files (tools/Fetch-FidelityCorpus.ps1),
      2. builds tools/FreeX.FidelityCompare in Release,
      3. opens each corpus workbook in BOTH FreeX and Excel and compares computed cell values + feature
         inventory, writing a timestamped run folder (results.csv, mismatches.txt, README.md).

.PARAMETER Filter
    Only run corpus files whose name contains this substring.

.PARAMETER Out
    Output run directory (default: fidelity-corpus/runs/<timestamp>).

.PARAMETER SkipFetch
    Do not attempt to download missing corpus files.

.PARAMETER Tolerance
    Max percentage of compared cells allowed to differ before a file FAILs (default 0.5).

.EXAMPLE
    pwsh tools/Run-FidelityBatch.ps1
    pwsh tools/Run-FidelityBatch.ps1 -Filter chart -Tolerance 1
#>
[CmdletBinding()]
param(
    [string]$Filter,
    [string]$Out,
    [switch]$SkipFetch,
    [double]$Tolerance = 0.5,
    # Compute-fidelity: recompute FreeX formulas before comparing (FreeX engine vs Excel) instead of
    # trusting the file's cached results. Noisier on workbooks that use legacy array/implicit-intersection
    # formulas (see docs/testing/fidelity-batch.md).
    [switch]$Recalc
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'tools/FreeX.FidelityCompare/FreeX.FidelityCompare.csproj'

if (-not $SkipFetch) {
    Write-Host "== Fetching corpus =="
    & (Join-Path $PSScriptRoot 'Fetch-FidelityCorpus.ps1')
}

Write-Host "== Building FreeX.FidelityCompare (Release) =="
dotnet build $project -c Release | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$exe = Join-Path $repoRoot 'tools/FreeX.FidelityCompare/bin/Release/net10.0-windows/FreeX.FidelityCompare.exe'
if (-not (Test-Path $exe)) { throw "Executable not found: $exe" }

$argList = @('--tolerance', $Tolerance)
if ($Filter) { $argList += @('--filter', $Filter) }
if ($Out) { $argList += @('--out', $Out) }
if ($Recalc) { $argList += '--recalc' }

Write-Host "== Running fidelity batch =="
& $exe @argList
exit $LASTEXITCODE
