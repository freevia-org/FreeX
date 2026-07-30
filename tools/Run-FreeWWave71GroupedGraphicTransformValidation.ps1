[CmdletBinding()]
param([string]$OutputDir = "freew/artifacts/wave71-grouped-graphic-transform", [switch]$LinuxProbe)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedOutput = [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDir))
New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null

if ($LinuxProbe) {
    if ($IsWindows) { throw "Linux probe requested on Windows; fail-closed." }
    & bash (Join-Path $repoRoot "tools/LinuxInteractiveDocker/run-freew-wave71-grouped-graphic-transform-probe.sh") $resolvedOutput
    exit $LASTEXITCODE
}

& dotnet test (Join-Path $repoRoot "freew/FreeW.Core.Model.Tests/FreeW.Core.Model.Tests.csproj") --configuration Release --no-restore --filter "FullyQualifiedName~SetDrawingGroupChildRotationCommand_RotatesNestedChartAndSmartArt"
if ($LASTEXITCODE -ne 0) { throw "Managed grouped graphic transform test failed." }
& dotnet test (Join-Path $repoRoot "freew/FreeW.Core.IO.Tests/FreeW.Core.IO.Tests.csproj") --configuration Release --no-restore --filter "FullyQualifiedName~DrawingGroup_ChartAndSmartArtChildTransforms_RoundTripThroughDocx"
if ($LASTEXITCODE -ne 0) { throw "DOCX grouped graphic transform test failed." }
