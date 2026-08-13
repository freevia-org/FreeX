[CmdletBinding()]
param(
    [int]$Port = 6084,
    [string]$OutputDir = "artifacts/freew-table-properties-x11",
    [switch]$SkipImageBuild,
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedOutput = [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDir))
$sessionMetadata = Join-Path $resolvedOutput "session.json"
$probe = Join-Path $repoRoot "tools/LinuxInteractiveDocker/freew-table-properties-x11-probe.sh"
$schema = Join-Path $repoRoot "tools/LinuxInteractiveDocker/freew-table-properties-x11.schema.json"
New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null

$startArgs = @{
    Action = "Start"
    App = "FreeW"
    Host = "Validation"
    Port = $Port
    OutputDir = $OutputDir
    SessionMetadataPath = $sessionMetadata
    AppArgument = @(
        "--table-properties-x11-validation",
        "/work/table-properties-result.json")
}
if ($SkipImageBuild) { $startArgs.SkipImageBuild = $true }
if ($SkipPublish) { $startArgs.SkipPublish = $true }

$container = $null
try {
    & (Join-Path $repoRoot "tools/Run-LinuxInteractiveDocker.ps1") @startArgs
    $session = Get-Content -LiteralPath $sessionMetadata -Raw | ConvertFrom-Json
    $container = [string]$session.containerName

    & docker cp $probe "${container}:/work/freew-table-properties-x11-probe.sh"
    & docker exec $container chmod 0755 /work/freew-table-properties-x11-probe.sh
    & docker exec $container /work/freew-table-properties-x11-probe.sh
    if ($LASTEXITCODE -ne 0) { throw "The physical FreeW Table Properties probe failed." }

    & docker cp "${container}:/work/table-properties-result.json" (Join-Path $resolvedOutput "table-properties-result.json")
    & docker cp "${container}:/work/table-properties-x11-result.json" (Join-Path $resolvedOutput "physical-result.json")
    & docker cp "${container}:/work/table-properties-x11" (Join-Path $resolvedOutput "evidence")
    Copy-Item -LiteralPath $schema -Destination (Join-Path $resolvedOutput "schema.json") -Force

    $physical = Get-Content -LiteralPath (Join-Path $resolvedOutput "physical-result.json") -Raw | ConvertFrom-Json
    if ($physical.status -ne "passed" -or $physical.dialog -ne "real" -or $physical.tabsTraversed -ne $true -or (($physical.tabPages -join ',') -ne 'Table,Row,Column,Cell')) {
        throw "Physical result did not satisfy the standalone schema contract."
    }
    Write-Host "PASS: FreeW Table Properties physical X11 validation"
    Write-Host "Artifacts: $resolvedOutput"
}
finally {
    if ($null -ne $container) {
        & (Join-Path $repoRoot "tools/Run-LinuxInteractiveDocker.ps1") -Action Stop -App FreeW -Port $Port
    }
}
