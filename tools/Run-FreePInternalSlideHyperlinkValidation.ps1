[CmdletBinding()]
param(
    [int]$Port = 6097,
    [int]$Width = 1280,
    [int]$Height = 820,
    [string]$OutputDir = "artifacts/freep-internal-slide-hyperlink-x11-wave60",
    [string]$PublishDir = "",
    [switch]$SkipPublish,
    [switch]$SkipImageBuild,
    [switch]$Replace
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$runner = Join-Path $PSScriptRoot "Run-LinuxInteractiveDocker.ps1"
$probe = Join-Path $PSScriptRoot "LinuxInteractiveDocker/run-freep-internal-slide-hyperlink-probe.sh"
$out = [IO.Path]::GetFullPath((Join-Path $root $OutputDir))
New-Item -ItemType Directory -Force -Path $out | Out-Null
$startParameters = @{
    Action = "Start"; App = "FreeP"; Host = "Validation"; Port = $Port; Width = $Width; Height = $Height; OutputDir = $out
    AppArgument = @(
        "--physical-internal-slide-hyperlink-fixture=/work/freep-internal-slide-hyperlink")
}
if ($PublishDir) { $startParameters.PublishDir = $PublishDir }
if ($SkipPublish) { $startParameters.SkipPublish = $true }; if ($SkipImageBuild) { $startParameters.SkipImageBuild = $true }; if ($Replace) { $startParameters.Replace = $true }
& $runner @startParameters
$session = Get-Content (Join-Path $out "freep/current-session.json") -Raw | ConvertFrom-Json
$sessionDir = [IO.Path]::GetFullPath([string]$session.sessionDirectory)
$probeInWork = Join-Path $sessionDir "freep-internal-slide-hyperlink-probe.sh"
Copy-Item $probe $probeInWork -Force
try {
    & docker exec --env DISPLAY=:99 $session.containerName bash /work/freep-internal-slide-hyperlink-probe.sh /work/freep-internal-slide-hyperlink
    if ($LASTEXITCODE -ne 0) { throw "FreeP internal-slide hyperlink probe failed." }
    $evidenceDestination = Join-Path $out "evidence"
    New-Item -ItemType Directory -Force -Path $evidenceDestination | Out-Null
    & docker cp "$($session.containerName):/work/freep-internal-slide-hyperlink/." $evidenceDestination
    if ($LASTEXITCODE -ne 0) { throw "Could not copy physical hyperlink evidence from the owned container." }
} finally {
    & $runner -Action Stop -App FreeP -Port $Port -OutputDir $out | Out-Host
}
Write-Host "FreeP internal-slide hyperlink validation: 1 passed, 0 failed"
