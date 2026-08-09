<#
.SYNOPSIS
  Run authoritative FreeP physical Linux output validation in the Docker harness.

.DESCRIPTION
  Runs the published FreeP Avalonia app twice in the repository's Ubuntu/X11 harness:
  once against the container-local CUPS success shim and once against its deterministic
  failure shim. The app itself calls the real slideshow, Linux video export, ffprobe,
  and Linux CUPS adapter paths and writes model-state manifests under the mounted session.
  No screenshot is counted as behavior evidence by itself.
#>
[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)]
    [int]$Port = 6094,
    [ValidateRange(640, 7680)]
    [int]$Width = 1280,
    [ValidateRange(480, 4320)]
    [int]$Height = 820,
    [ValidateRange(72, 240)]
    [int]$Dpi = 96,
    [ValidateSet("2g", "4g", "6g", "8g")]
    [string]$MemoryLimit = "4g",
    [string]$OutputDir = "artifacts/freep-physical-linux-wave13b",
    [string]$PublishDir = "",
    [switch]$SkipPublish,
    [switch]$SkipImageBuild,
    [switch]$Replace
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "VisualEvidenceScriptSupport.ps1")
$genericRunner = Join-Path $PSScriptRoot "Run-LinuxInteractiveDocker.ps1"
$resolvedOutputRoot = Resolve-VisualEvidenceOutputDirectory -OutputDirectory $OutputDir -RepoRoot $repoRoot

function Read-RunManifest {
    param([Parameter(Mandatory = $true)][string]$RunRoot)
    $sessionPath = Join-Path $RunRoot "freep/current-session.json"
    if (-not (Test-Path -LiteralPath $sessionPath -PathType Leaf)) {
        throw "FreeP session metadata is missing: $sessionPath"
    }
    $session = Read-VisualEvidenceJson -Path $sessionPath -MissingMessage "FreeP session metadata is missing: $sessionPath"
    $sessionDirectory = [IO.Path]::GetFullPath([string]$session.sessionDirectory)
    $manifestPath = Join-Path $sessionDirectory "physical-validation/freep-physical-linux-wave13b.json"
    [pscustomobject]@{
        Session = $session
        SessionDirectory = $sessionDirectory
        ManifestPath = $manifestPath
        Manifest = Read-VisualEvidenceJson -Path $manifestPath -TimeoutMilliseconds 180000 -PollMilliseconds 500 -MissingMessage "FreeP physical validation did not write a manifest: $manifestPath"
    }
}

function Assert-Manifest {
    param(
        [Parameter(Mandatory = $true)]$Run,
        [Parameter(Mandatory = $true)][string]$ExpectedCupsMode
    )
    $manifest = $Run.Manifest
    if ($manifest.schemaVersion -ne 1 -or $manifest.suite -ne "freep-physical-linux-wave13b" -or
        $manifest.platform -ne "linux" -or $manifest.shell -ne "avalonia" -or $manifest.app -ne "FreeP") {
        throw "Invalid FreeP physical manifest contract: $($Run.ManifestPath)"
    }
    if ($manifest.cupsMode -ne $ExpectedCupsMode) {
        throw "Expected CUPS mode '$ExpectedCupsMode', got '$($manifest.cupsMode)'."
    }
    if (@($manifest.results).Count -lt 8) {
        throw "FreeP physical manifest has too few model-state rows: $($Run.ManifestPath)"
    }
    if ($manifest.summary.failed -ne 0) {
        throw "FreeP physical validation failed in $ExpectedCupsMode mode: $($Run.ManifestPath)"
    }
}

function Start-ValidationRun {
    param([Parameter(Mandatory = $true)][ValidateSet("success", "failure")][string]$Mode)
    $runRoot = Join-Path $resolvedOutputRoot $Mode
    New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
    $arguments = @(
        "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $genericRunner,
        "-Action", "Start", "-App", "FreeP", "-Port", "$Port",
        "-Width", "$Width", "-Height", "$Height", "-Dpi", "$Dpi",
        "-MemoryLimit", $MemoryLimit, "-OutputDir", $runRoot,
        "-CupsDryRun", "-CupsDryRunMode", $Mode,
        "-AppArgument", "--physical-validation=/work/physical-validation"
    )
    if (-not [string]::IsNullOrWhiteSpace($PublishDir)) { $arguments += @("-PublishDir", $PublishDir) }
    if ($SkipPublish) { $arguments += "-SkipPublish" }
    if ($SkipImageBuild) { $arguments += "-SkipImageBuild" }
    if ($Replace) { $arguments += "-Replace" }

    $started = $false
    try {
        Invoke-VisualEvidenceProcess -FilePath "powershell.exe" -Arguments $arguments -WorkingDirectory $repoRoot -OutputToHost
        $started = $true
        $run = Read-RunManifest -RunRoot $runRoot
        Assert-Manifest -Run $run -ExpectedCupsMode $Mode
        return $run
    } finally {
        if ($started) {
            try {
                Invoke-VisualEvidenceProcess -FilePath "powershell.exe" -Arguments @(
                    "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $genericRunner,
                    "-Action", "Stop", "-App", "FreeP", "-Port", "$Port",
                    "-OutputDir", $runRoot) -WorkingDirectory $repoRoot -OutputToHost
            } catch { Write-Warning "Could not stop harness-owned FreeP container: $($_.Exception.Message)" }
        }
    }
}

New-Item -ItemType Directory -Path $resolvedOutputRoot -Force | Out-Null
$runs = @(
    Start-ValidationRun -Mode success
    Start-ValidationRun -Mode failure
)

$rows = @($runs | ForEach-Object { $_.Manifest.results })
$summary = [ordered]@{
    passed = @($rows | Where-Object status -eq "passed").Count
    failed = @($rows | Where-Object status -eq "failed").Count
    notProven = @($rows | Where-Object status -eq "not-proven").Count
    total = $rows.Count
}
$report = [ordered]@{
    schemaVersion = 1
    suite = "freep-physical-linux-wave13b-report"
    platform = "linux"
    shell = "avalonia"
    app = "FreeP"
    parameters = [ordered]@{ width = $Width; height = $Height; dpi = $Dpi; port = $Port }
    summary = $summary
    runs = @($runs | ForEach-Object {
        [ordered]@{
            cupsMode = $_.Manifest.cupsMode
            manifestPath = $_.ManifestPath
            sessionDirectory = $_.SessionDirectory
            summary = $_.Manifest.summary
        }
    })
}
$reportPath = Join-Path $resolvedOutputRoot "report.json"
$report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $reportPath -Encoding utf8
Write-Host "FreeP physical Linux validation: $($summary.passed) passed, $($summary.failed) failed, $($summary.notProven) not-proven, $($summary.total) total"
Write-Host "Report: $reportPath"
Write-Host "Success manifest: $($runs[0].ManifestPath)"
Write-Host "Failure manifest: $($runs[1].ManifestPath)"
if ($summary.failed -ne 0) { throw "FreeP physical Linux validation had failed rows." }
