<#
.SYNOPSIS
  Runs FreeW's production Avalonia Read Aloud pause/resume smoke in the Linux Docker harness.

.DESCRIPTION
  Starts an owned FreeW validation host in an Ubuntu desktop container with the headless smoke argument. The production
  Avalonia engine drives an owned espeak-ng child that synthesizes to a temporary WAV file, pauses
  and resumes it using the exact child PID, then stops it. The retained app log is the evidence;
  no audio device or audible output is required.
#>
[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)][int]$Port = 6094,
    [string]$OutputDir = "artifacts/freew-read-aloud-pause-linux",
    [switch]$SkipPublish,
    [switch]$SkipImageBuild,
    [switch]$KeepContainer
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedOutput = if ([IO.Path]::IsPathRooted($OutputDir)) {
    [IO.Path]::GetFullPath($OutputDir)
} else {
    [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDir))
}
New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null
$metadataPath = Join-Path $resolvedOutput "session.json"
$logPath = Join-Path $resolvedOutput "app.log"
$resultPath = Join-Path $resolvedOutput "result.json"
$runner = Join-Path $repoRoot "tools/Run-LinuxInteractiveDocker.ps1"
$cleanupRequired = $false

try {
    $cleanupRequired = $true
    $runnerArguments = @(
        "-Action", "Start", "-App", "FreeW", "-Port", "$Port",
        "-Host", "Validation",
        "-OutputDir", $resolvedOutput, "-SessionMetadataPath", $metadataPath,
        "-AppArgument", "--read-aloud-pause-smoke"
    )
    if ($SkipPublish) { $runnerArguments += "-SkipPublish" }
    if ($SkipImageBuild) { $runnerArguments += "-SkipImageBuild" }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $runner @runnerArguments
    if ($LASTEXITCODE -ne 0) { throw "Linux harness start failed with exit code $LASTEXITCODE." }

    $session = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
    $sourceLog = Join-Path ([string]$session.sessionDirectory) "logs/app.log"
    if (-not (Test-Path -LiteralPath $sourceLog -PathType Leaf)) {
        throw "Harness did not retain the FreeW app log at '$sourceLog'."
    }
    $log = ""
    $logDeadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $logDeadline) {
        try {
            $log = [IO.File]::ReadAllText($sourceLog)
        } catch {
            $log = ""
        }
        if ($log.Contains("status=passed") -or $log.Contains("status=failed")) {
            break
        }
        Start-Sleep -Milliseconds 200
    }
    if (-not $KeepContainer) {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $runner `
            -Action Stop -App FreeW -Port $Port -OutputDir $resolvedOutput
        if ($LASTEXITCODE -ne 0) { throw "Linux harness stop failed with exit code $LASTEXITCODE." }
        $cleanupRequired = $false
        $log = [IO.File]::ReadAllText($sourceLog)
    }
    [IO.File]::WriteAllText($logPath, $log, [Text.UTF8Encoding]::new($false))
    $required = @(
        "backend=espeak-ng",
        "pause=passed",
        "resume=passed",
        "output_before_pause_bytes=",
        "output_while_paused_bytes=",
        "output_after_resume_bytes=",
        "pause_signal_ms=",
        "resume_signal_ms=",
        "paused_process_state=T",
        "paused_stable=true",
        "completion_while_paused=false",
        "resumed_progress=true",
        "stop=passed",
        "status=passed"
    )
    $missing = @($required | Where-Object { -not $log.Contains($_) })
    $metrics = [ordered]@{}
    foreach ($metricName in @(
        "owned_pid",
        "output_before_pause_bytes",
        "output_while_paused_bytes",
        "output_after_resume_bytes",
        "pause_signal_ms",
        "resume_signal_ms"
    )) {
        $pattern = "(?m)^$([regex]::Escape($metricName))=(?<value>.+)$"
        if ($log -match $pattern) {
            $metrics[$metricName] = $Matches["value"]
        } else {
            $missing += "$metricName=<missing>"
        }
    }
    $passed = $missing.Count -eq 0
    $result = [ordered]@{
        schemaVersion = 1
        status = if ($passed) { "passed" } else { "failed" }
        app = "FreeW"
        platform = "linux-docker"
        process = "espeak-ng"
        evidenceLevel = "production-engine-owned-child"
        log = "app.log"
        metrics = $metrics
        missing = @($missing)
    }
    [IO.File]::WriteAllText($resultPath, ($result | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
    if (-not $passed) {
        throw "Read Aloud pause validation failed. Missing evidence: $($missing -join ', ')."
    }
    Write-Host "FreeW Read Aloud pause/resume validation passed: $resolvedOutput"
}
finally {
    if ($cleanupRequired -and -not $KeepContainer) {
        try {
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $runner `
                -Action Stop -App FreeW -Port $Port -OutputDir $resolvedOutput
        } catch {
            Write-Warning "Could not stop the harness-owned FreeW container: $($_.Exception.Message)"
        }
    }
}
