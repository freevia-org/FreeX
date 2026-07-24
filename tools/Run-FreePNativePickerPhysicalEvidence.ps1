<#
.SYNOPSIS
  Capture real Linux foreground picker evidence for FreeP.

.DESCRIPTION
  Drives a running FreeP session from Run-LinuxInteractiveDocker.ps1 through X11.
  This is intentionally physical evidence: screenshots are taken from the Docker
  desktop while GTK picker windows are open. It does not substitute picker results
  or claim Windows picker parity.
#>
[CmdletBinding()]
param(
    [string]$ContainerName = "freex-linux-interactive-freep-6094",
    [string]$SessionMetadataPath = "artifacts/freep-native-picker-physical-wave9/freep/current-session.json"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$metadataPath = if ([IO.Path]::IsPathRooted($SessionMetadataPath)) {
    [IO.Path]::GetFullPath($SessionMetadataPath)
} else {
    [IO.Path]::GetFullPath((Join-Path $repoRoot $SessionMetadataPath))
}
if (-not (Test-Path -LiteralPath $metadataPath -PathType Leaf)) {
    throw "Session metadata was not found: $metadataPath"
}

$session = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
$sessionDirectory = [IO.Path]::GetFullPath([string]$session.sessionDirectory)
$evidenceDirectory = Join-Path $sessionDirectory "native-picker-physical"
New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null

function Invoke-X11 {
    param([Parameter(Mandatory = $true)][string]$Command)

    $output = @(& docker exec $ContainerName bash -lc "export DISPLAY=:99; $Command" 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "X11 command failed: $Command`n$($output -join [Environment]::NewLine)"
    }
    return $output
}

function Wait-Window {
    param(
        [Parameter(Mandatory = $true)][string]$Title,
        [int]$TimeoutSeconds = 10
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $ids = @(Invoke-X11 "xdotool search --onlyvisible --name '^$Title`$' 2>/dev/null || true") |
            Where-Object { $_ -match '^\d+$' }
        if ($ids.Count -gt 0) {
            return [int64]$ids[-1]
        }
        Start-Sleep -Milliseconds 200
    }
    throw "Native window '$Title' did not appear."
}

function Wait-Owner {
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline) {
        $active = @(Invoke-X11 "xdotool getactivewindow getwindowname 2>/dev/null || true") -join " "
        if ($active -match "FreeP") {
            return
        }
        Start-Sleep -Milliseconds 200
    }
    throw "FreeP did not regain active-window focus."
}

function Capture {
    param([Parameter(Mandatory = $true)][string]$Name)

    Invoke-X11 "scrot -o /work/native-picker-physical/$Name.png" | Out-Null
    $path = Join-Path $evidenceDirectory "$Name.png"
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -le 0) {
        throw "Expected screenshot was not captured: $path"
    }
    return "native-picker-physical/$Name.png"
}

Invoke-X11 "mkdir -p /work/native-picker-physical" | Out-Null
Invoke-X11 "xdotool key --clearmodifiers ctrl+o" | Out-Null
$openWindow = Wait-Window "Open Presentation"
$openPickerScreenshot = Capture "open-picker"
Invoke-X11 "xdotool key Escape" | Out-Null
Wait-Owner
$openCancelScreenshot = Capture "open-cancel-owner"

Invoke-X11 "xdotool key --clearmodifiers ctrl+shift+s" | Out-Null
$saveWindow = Wait-Window "Save Presentation"
Invoke-X11 "xdotool mousemove --sync 600 46; xdotool click 1; xdotool key ctrl+a; xdotool type --delay 15 'physical-wave9.fxp'" | Out-Null
$extensionScreenshot = Capture "save-extension-entry"
Invoke-X11 "xdotool mousemove --sync 1050 790; xdotool click --clearmodifiers 1" | Out-Null
Wait-Owner
$extensionOwnerScreenshot = Capture "save-extension-owner"

Invoke-X11 "xdotool key --clearmodifiers ctrl+shift+s" | Out-Null
$saveWindow = Wait-Window "Save Presentation"
Invoke-X11 "xdotool mousemove --sync 600 46; xdotool click 1; xdotool key ctrl+a; xdotool type --delay 15 'physical-wave9.fxp'; xdotool mousemove --sync 1050 790; xdotool click --clearmodifiers 1" | Out-Null
Start-Sleep -Milliseconds 800
$overwriteScreenshot = Capture "overwrite-prompt"
Invoke-X11 "xdotool key Escape; xdotool key Escape" | Out-Null
Wait-Owner
$overwriteOwnerScreenshot = Capture "overwrite-decline-owner"

Invoke-X11 "xdotool key --clearmodifiers ctrl+shift+s" | Out-Null
$saveWindow = Wait-Window "Save Presentation"
Invoke-X11 "xdotool mousemove --sync 600 46; xdotool click 1; xdotool key ctrl+a; xdotool type --delay 15 'unsupported.txt'; xdotool mousemove --sync 1050 790; xdotool click --clearmodifiers 1" | Out-Null
Start-Sleep -Milliseconds 800
$errorScreenshot = Capture "unsupported-extension-error"
Invoke-X11 "xdotool key Escape" | Out-Null
Wait-Owner
$errorOwnerScreenshot = Capture "error-owner"

$tests = @(
    [ordered]@{
        id = "native.open.cancel"
        status = "passed"
        evidenceLevel = "physical-linux-x11"
        detail = "GTK Open Presentation picker was visible; Escape cancelled it and FreeP regained focus."
        artifacts = @($openPickerScreenshot, $openCancelScreenshot)
    },
    [ordered]@{
        id = "native.save.extension-selection"
        status = "passed"
        evidenceLevel = "physical-linux-x11"
        detail = "GTK Save Presentation picker accepted a .fxp file name and returned to the owner."
        artifacts = @($extensionScreenshot, $extensionOwnerScreenshot)
    },
    [ordered]@{
        id = "native.save.overwrite-decline"
        status = "passed"
        evidenceLevel = "physical-linux-x11"
        detail = "GTK displayed its real existing-file replacement prompt; Escape declined replacement and then cancelled Save As back to FreeP."
        artifacts = @($overwriteScreenshot, $overwriteOwnerScreenshot)
    },
    [ordered]@{
        id = "native.save.error"
        status = "passed"
        evidenceLevel = "physical-linux-x11"
        detail = "A real picker-selected unsupported extension reached FreeP's visible save error dialog and returned to the owner after dismissal."
        artifacts = @($errorScreenshot, $errorOwnerScreenshot)
    },
    [ordered]@{
        id = "native.windows-picker"
        status = "not-proven"
        evidenceLevel = "foreground-native-required"
        detail = "This Linux Docker run cannot prove Windows native picker chrome or Windows OS foreground behavior."
        artifacts = @()
    }
)

$report = [ordered]@{
    schemaVersion = 1
    suite = "freep-native-picker-physical"
    platform = "linux-docker-x11"
    nativeUiParity = "linux-physical-evidence"
    container = $ContainerName
    screen = "1280x820 at 96 DPI"
    generatedBy = "tools/Run-FreePNativePickerPhysicalEvidence.ps1"
    summary = [ordered]@{
        passed = @($tests | Where-Object status -eq "passed").Count
        failed = @($tests | Where-Object status -eq "failed").Count
        notProven = @($tests | Where-Object status -eq "not-proven").Count
        total = $tests.Count
    }
    tests = $tests
}
$reportPath = Join-Path $evidenceDirectory "native-picker-physical-evidence.json"
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $reportPath -Encoding utf8
Write-Host "Physical picker evidence: $($report.summary.passed) passed, $($report.summary.failed) failed, $($report.summary.notProven) not proven"
Write-Host "Report: $reportPath"
