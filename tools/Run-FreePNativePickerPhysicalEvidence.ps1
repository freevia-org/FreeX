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

    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    $output = @(& docker exec $ContainerName bash -lc "export DISPLAY=:99; $Command" 2>&1)
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = $previousPreference
    if ($exitCode -ne 0) {
        throw "X11 command failed: $Command`n$($output -join [Environment]::NewLine)"
    }
    return $output
}

function Get-ContainerFileSize {
    param([Parameter(Mandatory = $true)][string]$Path)

    $output = @(Invoke-X11 "stat -c '%s' -- $Path 2>/dev/null || true")
    $line = $output | Where-Object { $_ -match '^\d+$' } | Select-Object -First 1
    if ($null -eq $line) {
        return $null
    }
    return [int64]$line
}

function Get-ContainerFileHash {
    param([Parameter(Mandatory = $true)][string]$Path)

    $output = @(Invoke-X11 "sha256sum -- $Path 2>/dev/null || true")
    $line = $output | Where-Object { $_ -match '^[0-9a-fA-F]{64}\s+' } | Select-Object -First 1
    if ($null -eq $line) {
        return $null
    }
    return ([string]$line -split '\s+')[0].ToLowerInvariant()
}

function Remove-ContainerFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    Invoke-X11 "rm -f -- $Path" | Out-Null
    $size = Get-ContainerFileSize $Path
    if ($null -ne $size) {
        throw "Harness-owned file was not removed: $Path"
    }
}

function Get-VisibleWindowCount {
    $output = @(Invoke-X11 "wmctrl -l | wc -l")
    $line = $output | Where-Object { $_ -match '^\d+$' } | Select-Object -First 1
    if ($null -eq $line) {
        return 0
    }
    return [int]$line
}

function Wait-VisibleWindowCountAtLeast {
    param(
        [Parameter(Mandatory = $true)][int]$Minimum,
        [int]$TimeoutSeconds = 10
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if ((Get-VisibleWindowCount) -ge $Minimum) {
            return
        }
        Start-Sleep -Milliseconds 200
    }
    throw "Expected at least $Minimum visible X11 windows."
}

function Assert-Condition {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Find-Window {
    param([Parameter(Mandatory = $true)][string]$Title)

    $ids = @(Invoke-X11 "xdotool search --onlyvisible --name '^$Title`$' 2>/dev/null || true") |
        Where-Object { $_ -match '^\d+$' }
    if ($ids.Count -eq 0) {
        return $null
    }
    return [int64]$ids[-1]
}

function Wait-Window {
    param(
        [Parameter(Mandatory = $true)][string]$Title,
        [int]$TimeoutSeconds = 10
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $id = Find-Window $Title
        if ($null -ne $id) {
            return $id
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

try {
    $extensionPath = "/documents/physical-wave9.fxp"
    $unsupportedPath = "/documents/unsupported.txt"
    Invoke-X11 "mkdir -p /work/native-picker-physical" | Out-Null

    Invoke-X11 "xdotool key --clearmodifiers ctrl+o" | Out-Null
    Start-Sleep -Milliseconds 500
    $initialSavePrompt = Find-Window "FreeP"
    if ($null -ne $initialSavePrompt) {
        Invoke-X11 "xdotool mousemove --sync 700 430; xdotool click --clearmodifiers 1" | Out-Null
        Start-Sleep -Milliseconds 500
    }
    $openWindow = Wait-Window "Open Presentation"
    $openPickerScreenshot = Capture "open-picker"
    Invoke-X11 "xdotool key Escape" | Out-Null
    Wait-Owner
    $openCancelScreenshot = Capture "open-cancel-owner"

    Remove-ContainerFile $extensionPath
    Invoke-X11 "xdotool key --clearmodifiers ctrl+shift+s" | Out-Null
    $saveWindow = Wait-Window "Save Presentation"
    Invoke-X11 "xdotool search --onlyvisible --name '^Save Presentation$' windowactivate --sync %@; xdotool mousemove --sync 600 46; xdotool click 1; xdotool key ctrl+a; xdotool type --delay 15 '$extensionPath'" | Out-Null
    $extensionScreenshot = Capture "save-extension-entry"
    Invoke-X11 "xdotool search --onlyvisible --name '^Save Presentation$' windowactivate --sync %@; xdotool mousemove --sync 1050 792; xdotool click --clearmodifiers 1" | Out-Null
    Wait-Owner
    $extensionOwnerScreenshot = Capture "save-extension-owner"
    $extensionSize = Get-ContainerFileSize $extensionPath
    Assert-Condition ($null -ne $extensionSize -and $extensionSize -gt 0) "The selected .fxp target was not created as a non-empty file: $extensionPath"

    $overwriteHashBefore = Get-ContainerFileHash $extensionPath
    Assert-Condition (-not [string]::IsNullOrWhiteSpace($overwriteHashBefore)) "Could not hash the saved overwrite target before Save As."
    Invoke-X11 "xdotool key --clearmodifiers ctrl+shift+s" | Out-Null
    $saveWindow = Wait-Window "Save Presentation"
    Invoke-X11 "xdotool search --onlyvisible --name '^Save Presentation$' windowactivate --sync %@; xdotool mousemove --sync 600 46; xdotool click 1; xdotool key ctrl+a; xdotool type --delay 15 '$extensionPath'; xdotool mousemove --sync 1050 792; xdotool click --clearmodifiers 1" | Out-Null
    Wait-VisibleWindowCountAtLeast 3
    $overwriteScreenshot = Capture "overwrite-prompt"
    Invoke-X11 "xdotool key Escape; xdotool key Escape" | Out-Null
    Wait-Owner
    $overwriteOwnerScreenshot = Capture "overwrite-decline-owner"
    $overwriteHashAfter = Get-ContainerFileHash $extensionPath
    Assert-Condition ($overwriteHashAfter -eq $overwriteHashBefore) "Declining overwrite changed the saved target hash."

    Remove-ContainerFile $unsupportedPath
    Invoke-X11 "xdotool key --clearmodifiers ctrl+shift+s" | Out-Null
    $saveWindow = Wait-Window "Save Presentation"
    Invoke-X11 "xdotool search --onlyvisible --name '^Save Presentation$' windowactivate --sync %@; xdotool mousemove --sync 600 46; xdotool click 1; xdotool key ctrl+a; xdotool type --delay 15 '$unsupportedPath'; xdotool mousemove --sync 1050 792; xdotool click --clearmodifiers 1" | Out-Null
    Wait-Window "FreeP" | Out-Null
    $errorWindowTitle = @(Invoke-X11 "xdotool getactivewindow getwindowname") -join " "
    Assert-Condition ($errorWindowTitle -eq "FreeP") "The unsupported-extension error did not open the exact FreeP error window."
    $errorScreenshot = Capture "unsupported-extension-error"
    Invoke-X11 "xdotool key Escape" | Out-Null
    Wait-Owner
    $errorOwnerScreenshot = Capture "error-owner"
    $unsupportedSize = Get-ContainerFileSize $unsupportedPath
    Assert-Condition ($null -eq $unsupportedSize) "The unsupported extension target was created unexpectedly: $unsupportedPath"

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
        detail = "GTK Save Presentation picker accepted /documents/physical-wave9.fxp, created a non-empty target, and returned to the owner."
        artifacts = @($extensionScreenshot, $extensionOwnerScreenshot)
        target = $extensionPath
        sizeBytes = $extensionSize
    },
    [ordered]@{
        id = "native.save.overwrite-decline"
        status = "passed"
        evidenceLevel = "physical-linux-x11"
        detail = "GTK displayed its real existing-file replacement prompt; Escape declined replacement, the target SHA-256 stayed unchanged, and Save As returned to FreeP."
        artifacts = @($overwriteScreenshot, $overwriteOwnerScreenshot)
        target = $extensionPath
        hashBefore = $overwriteHashBefore
        hashAfter = $overwriteHashAfter
    },
    [ordered]@{
        id = "native.save.error"
        status = "passed"
        evidenceLevel = "physical-linux-x11"
        detail = "The real picker-selected /documents/unsupported.txt target reached the exact FreeP error window, was not created, and returned to the owner after dismissal."
        artifacts = @($errorScreenshot, $errorOwnerScreenshot)
        target = $unsupportedPath
        sizeBytes = $unsupportedSize
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
}
finally {
    try {
        Remove-ContainerFile "/documents/physical-wave9.fxp"
        Remove-ContainerFile "/documents/unsupported.txt"
    }
    catch {
        Write-Warning "Could not clean harness-owned picker targets: $($_.Exception.Message)"
    }
}
