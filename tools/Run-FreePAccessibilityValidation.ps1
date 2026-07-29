<#
.SYNOPSIS
  Run FreeP live-pane accessibility evidence in the Linux Docker desktop.

.DESCRIPTION
  Starts the production Avalonia FreeP shell, reads representative pane metadata
  from live controls, and runs a companion AT-SPI query against the same X11
  desktop. AT-SPI exposure is reported as not-proven when Avalonia/X11 does not
  publish an accessibility application; that limitation is evidence, not a pass.
#>
[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)]
    [int]$Port = 6158,
    [ValidateRange(640, 7680)]
    [int]$Width = 1280,
    [ValidateRange(480, 4320)]
    [int]$Height = 820,
    [ValidateRange(72, 240)]
    [int]$Dpi = 96,
    [ValidateSet("2g", "4g", "6g", "8g")]
    [string]$MemoryLimit = "4g",
    [string]$OutputDir = "artifacts/linux-family-interactive-wave58/freep-accessibility",
    [string]$PublishDir = "",
    [switch]$SkipPublish,
    [switch]$SkipImageBuild,
    [switch]$Replace
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$genericRunner = Join-Path $PSScriptRoot "Run-LinuxInteractiveDocker.ps1"
$resolvedOutputRoot = if ([IO.Path]::IsPathRooted($OutputDir)) {
    [IO.Path]::GetFullPath($OutputDir)
} else {
    [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDir))
}

function Invoke-External {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)
    Push-Location $repoRoot
    try {
        & powershell.exe @Arguments | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "powershell.exe exited with code $LASTEXITCODE." }
    } finally { Pop-Location }
}

function Read-JsonFile {
    param([Parameter(Mandatory = $true)][string]$Path)
    $deadline = (Get-Date).AddSeconds(60)
    while ((Get-Date) -lt $deadline -and -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        Start-Sleep -Milliseconds 250
    }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Expected validation file was not written: $Path"
    }
    Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

New-Item -ItemType Directory -Path $resolvedOutputRoot -Force | Out-Null
$containerName = "freex-linux-interactive-freep-$Port"
$started = $false
try {
    $startArguments = @(
        "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $genericRunner,
        "-Action", "Start", "-App", "FreeP", "-Port", "$Port",
        "-Width", "$Width", "-Height", "$Height", "-Dpi", "$Dpi",
        "-MemoryLimit", $MemoryLimit, "-OutputDir", $resolvedOutputRoot,
        "-AppArgument", "--accessibility-validation=/work/accessibility-validation"
    )
    if (-not [string]::IsNullOrWhiteSpace($PublishDir)) { $startArguments += @("-PublishDir", $PublishDir) }
    if ($SkipPublish) { $startArguments += "-SkipPublish" }
    if ($SkipImageBuild) { $startArguments += "-SkipImageBuild" }
    if ($Replace) { $startArguments += "-Replace" }

    Invoke-External -Arguments $startArguments
    $started = $true

    $probeResult = & docker exec $containerName /usr/local/bin/freep-accessibility-probe /work/accessibility-validation 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "AT-SPI probe failed in owned container '$containerName': $($probeResult -join [Environment]::NewLine)"
    }

    $sessionPath = Join-Path $resolvedOutputRoot "freep/current-session.json"
    $session = Read-JsonFile -Path $sessionPath
    $sessionDirectory = [IO.Path]::GetFullPath([string]$session.sessionDirectory)
    $livePath = Join-Path $sessionDirectory "accessibility-validation/live-pane-accessibility.json"
    $atSpiPath = Join-Path $sessionDirectory "accessibility-validation/atspi-result.json"
    $live = Read-JsonFile -Path $livePath
    $atSpi = Read-JsonFile -Path $atSpiPath
    if ($live.schemaVersion -ne 1 -or $live.suite -ne "freep-live-pane-accessibility" -or
        $live.platform -ne "linux" -or $live.shell -ne "avalonia" -or $live.app -ne "FreeP") {
        throw "Invalid live FreeP accessibility manifest: $livePath"
    }
    if (@($live.observations).Count -lt 4) {
        throw "Live FreeP accessibility manifest has fewer than four representative panes: $livePath"
    }
    foreach ($observation in @($live.observations)) {
        foreach ($property in @("paneId", "automationId", "name", "helpText", "role", "state", "value")) {
            if ([string]::IsNullOrWhiteSpace([string]$observation.$property)) {
                throw "Live FreeP accessibility observation '$($observation.paneId)' has an empty $property."
            }
        }
    }
    if ($atSpi.schemaVersion -ne 1 -or $atSpi.suite -ne "freep-atspi-accessibility" -or
        $atSpi.platform -ne "linux" -or $atSpi.shell -ne "avalonia" -or $atSpi.app -ne "FreeP" -or
        $atSpi.status -notin @("passed", "not-proven")) {
        throw "Invalid AT-SPI result: $atSpiPath"
    }
    foreach ($target in @("slides", "notes", "comments", "selection", "animation")) {
        $observation = @($atSpi.observations | Where-Object target -eq $target)
        if ($observation.Count -ne 1) {
            throw "AT-SPI result did not contain exactly one '$target' observation."
        }
        foreach ($property in @("name", "role", "state")) {
            if ([string]::IsNullOrWhiteSpace([string]$observation[0].$property)) {
                throw "AT-SPI '$target' observation has an empty $property."
            }
        }
        if ($null -eq $observation[0].PSObject.Properties["value"]) {
            throw "AT-SPI '$target' observation did not report a value field."
        }
    }

    $report = [ordered]@{
        schemaVersion = 1
        suite = "freep-accessibility-wave58-report"
        platform = "linux"
        shell = "avalonia"
        app = "FreeP"
        parameters = [ordered]@{ width = $Width; height = $Height; dpi = $Dpi; port = $Port }
        liveControl = [ordered]@{
            status = "passed"
            observationCount = @($live.observations).Count
            manifestPath = $livePath
        }
        atSpi = [ordered]@{
            status = [string]$atSpi.status
            observationCount = @($atSpi.observations).Count
            resultPath = $atSpiPath
            limitation = [string]$atSpi.limitation
            applicationName = [string]$atSpi.applicationName
            windowName = [string]$atSpi.windowName
        }
    }
    $reportPath = Join-Path $resolvedOutputRoot "freep/accessibility-validation/report.json"
    New-Item -ItemType Directory -Path (Split-Path -Parent $reportPath) -Force | Out-Null
    $report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $reportPath -Encoding utf8
    Write-Host "FreeP accessibility validation: live controls passed ($(@($live.observations).Count)); AT-SPI $($atSpi.status) ($(@($atSpi.observations).Count) observations)."
    Write-Host "Report: $reportPath"
}
finally {
    if ($started) {
        try {
            Invoke-External -Arguments @(
                "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $genericRunner,
                "-Action", "Stop", "-App", "FreeP", "-Port", "$Port",
                "-OutputDir", $resolvedOutputRoot)
        } catch { Write-Warning "Could not stop harness-owned FreeP container: $($_.Exception.Message)" }
    }
}
