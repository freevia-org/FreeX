<#
.SYNOPSIS
  Foreground X11 validation for FreeW Avalonia's app-owned CUPS print route.

.DESCRIPTION
  Starts the shared Linux interactive Docker harness with a container-local CUPS dry-run
  queue, drives the production FreeW Backstage Print route with X11 input, validates dialog
  ownership/focus, cancellation focus restoration, and PDF submission, then stops only the
  harness-owned container. Native GTK/system print-picker chrome is explicitly not claimed.
#>
[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)]
    [int]$Port = 6091,
    [ValidateRange(640, 7680)]
    [int]$Width = 1280,
    [ValidateRange(480, 4320)]
    [int]$Height = 820,
    [ValidateRange(72, 240)]
    [int]$Dpi = 96,
    [ValidateSet("2g", "4g", "6g", "8g")]
    [string]$MemoryLimit = "4g",
    [string]$OutputDir = "artifacts/linux-foreground-print",
    [string]$PublishDir = "",
    [switch]$SkipPublish,
    [switch]$SkipImageBuild,
    [switch]$Replace
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")
$genericRunner = Join-Path $PSScriptRoot "Run-LinuxInteractiveDocker.ps1"
$probeSource = Join-Path $PSScriptRoot "LinuxInteractiveDocker/run-freew-foreground-print-probe.sh"
$resolvedOutputRoot = if ([IO.Path]::IsPathRooted($OutputDir)) {
    [IO.Path]::GetFullPath($OutputDir)
} else {
    [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDir))
}

$sessionDirectory = $null
$started = $false
try {
    $startArguments = @(
        "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $genericRunner,
        "-Action", "Start", "-App", "FreeW", "-Port", "$Port",
        "-Width", "$Width", "-Height", "$Height", "-Dpi", "$Dpi",
        "-MemoryLimit", $MemoryLimit, "-OutputDir", $resolvedOutputRoot,
        "-CupsDryRun"
    )
    if (-not [string]::IsNullOrWhiteSpace($PublishDir)) { $startArguments += @("-PublishDir", $PublishDir) }
    if ($SkipPublish) { $startArguments += "-SkipPublish" }
    if ($SkipImageBuild) { $startArguments += "-SkipImageBuild" }
    if ($Replace) { $startArguments += "-Replace" }
    Invoke-ToolProcess -FilePath "powershell.exe" -Arguments $startArguments -WorkingDirectory $repoRoot
    $started = $true

    $currentSessionPath = Join-Path $resolvedOutputRoot "freew/current-session.json"
    if (-not (Test-Path -LiteralPath $currentSessionPath -PathType Leaf)) { throw "Missing session metadata: $currentSessionPath" }
    $session = Get-Content -LiteralPath $currentSessionPath -Raw | ConvertFrom-Json
    $sessionDirectory = [IO.Path]::GetFullPath([string]$session.sessionDirectory)
    $probeInWork = Join-Path $sessionDirectory "freew-foreground-print-probe.sh"
    Copy-Item -LiteralPath $probeSource -Destination $probeInWork -Force
    $probeOutput = @(& docker exec --env FREEW_PRINT_INPUT_DELAY_MS=180 --env FREEW_PRINT_SETTLE_SECONDS=0.55 `
        ([string]$session.containerName) bash /work/freew-foreground-print-probe.sh /work/freew-foreground-print 2>&1)
    $probeExitCode = $LASTEXITCODE
    $probeLog = Join-Path $sessionDirectory "freew-foreground-print-probe.log"
    $probeOutput | Set-Content -LiteralPath $probeLog -Encoding utf8
    $manifestPath = Join-Path $sessionDirectory "freew-foreground-print/freew-foreground-print-wave9.json"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Probe did not write manifest: $manifestPath" }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    Write-Host "Foreground print evidence: $($manifest.summary.passed) passed, $($manifest.summary.failed) failed, $($manifest.summary.notProven) not-proven, $($manifest.summary.total) total"
    Write-Host "Manifest: $manifestPath"
    Write-Host "Probe log: $probeLog"
    if ($probeExitCode -ne 0 -or $manifest.summary.failed -ne 0) { throw "FreeW foreground print validation failed; evidence retained at $manifestPath" }
} finally {
    if ($started) {
        try {
            Invoke-ToolProcess -FilePath "powershell.exe" -Arguments @(
                "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $genericRunner,
                "-Action", "Stop", "-App", "FreeW", "-Port", "$Port",
                "-OutputDir", $resolvedOutputRoot
            ) -WorkingDirectory $repoRoot
        } catch { Write-Warning "Could not stop harness-owned FreeW container: $($_.Exception.Message)" }
    }
}
