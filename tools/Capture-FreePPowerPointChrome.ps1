param(
    [string]$OutputDirectory = "docs\parity\freep-powerpoint-chrome-2026-08-16",
    [string]$Widths = "1280,1100,900,750"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")
. (Join-Path $PSScriptRoot "ScreenshotCaptureSupport.ps1")

[ScreenshotWin32]::SetProcessDPIAware() | Out-Null
$resolvedOutputDirectory = Resolve-ToolRepoPath -Path $OutputDirectory -RepoRoot $repoRoot
New-Item -ItemType Directory -Force -Path $resolvedOutputDirectory | Out-Null

function Clear-CurrentCapture {
    Clear-NativeChromeCapture -OutputDirectory $resolvedOutputDirectory -FilePattern "powerpoint_*.png"
}

function Write-BlockerManifest {
    param([Parameter(Mandatory = $true)][string]$Reason)

    Write-NativeChromeBlockerManifest -OutputDirectory $resolvedOutputDirectory -AppLabel "PowerPoint" `
        -Tool "tools/Capture-FreePPowerPointChrome.ps1" -Reason $Reason
}

function Set-PowerPointForegroundWindow {
    param(
        [Parameter(Mandatory = $true)][IntPtr]$WindowHandle,
        [Parameter(Mandatory = $true)][int]$ProcessId,
        [Parameter(Mandatory = $true)][string]$ExpectedTitle,
        [Parameter(Mandatory = $true)][string]$Operation
    )

    # Shared AttachThreadInput-based foreground transfer (ScreenshotCaptureSupport.ps1).
    Set-NativeChromeForegroundWindow $WindowHandle $ProcessId $ExpectedTitle $Operation
}

$powerPoint = $null
$presentation = $null
$powerPointProcessId = 0
try {
    $logicalWidths = @(Resolve-NativeChromeWidthSpecs $Widths)
    $screenDpi = [ScreenshotWin32]::GetScreenDpi()
    $scale = $screenDpi / 96.0
    $captureHeight = [int][Math]::Ceiling(300 * $scale)
    $windowLogicalHeight = 760
    $mappedTabNames = @("Home", "Insert", "Design", "Transitions", "Animations", "Slide Show", "View")

    $powerPoint = New-Object -ComObject PowerPoint.Application
    # PowerPoint's COM property is MsoTriState rather than a CLR Boolean.
    $powerPoint.Visible = -1
    $presentation = $powerPoint.Presentations.Add()
    Start-Sleep -Milliseconds 1400
    $windowHandle = [IntPtr]$powerPoint.HWND
    if ($windowHandle -eq [IntPtr]::Zero) {
        throw "PowerPoint did not expose an HWND after creating the blank presentation."
    }
    [ScreenshotWin32]::GetWindowThreadProcessId($windowHandle, [ref]$powerPointProcessId) | Out-Null
    if ($powerPointProcessId -le 0) {
        throw "Could not resolve the process owning the PowerPoint window."
    }
    $expectedTitle = Get-WindowTitle $windowHandle
    if ([string]::IsNullOrWhiteSpace($expectedTitle)) {
        throw "PowerPoint did not expose a non-empty window title."
    }
    Set-PowerPointForegroundWindow $windowHandle $powerPointProcessId $expectedTitle "PowerPoint chrome capture setup"

    $desktop = [System.Windows.Automation.AutomationElement]::RootElement
    $processCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
        [int]$powerPointProcessId)
    $appElement = $desktop.FindFirst([System.Windows.Automation.TreeScope]::Children, $processCondition)
    if ($null -eq $appElement) {
        throw "Could not resolve the PowerPoint UI Automation root."
    }

    $tabs = @{}
    foreach ($tabName in $mappedTabNames) {
        $tab = Find-NativeChromeTab $appElement $tabName
        if ($null -eq $tab) {
            throw "The installed PowerPoint profile does not expose required mapped tab '$tabName'."
        }
        $tabs[$tabName] = $tab
    }

    # Preserve the last complete reference set until the native window and its
    # entire mapped tab surface have passed foreground and UIA preflight.
    # A blocked desktop session must not erase usable evidence.
    Clear-CurrentCapture

    $captures = @()
    foreach ($logicalWidth in $logicalWidths) {
        $physicalWidth = [int][Math]::Ceiling($logicalWidth * $scale)
        $physicalHeight = [int][Math]::Ceiling($windowLogicalHeight * $scale)
        [ScreenshotWin32]::ShowWindow($windowHandle, 1) | Out-Null
        [ScreenshotWin32]::SetWindowPos($windowHandle, [IntPtr]::Zero, 0, 0, $physicalWidth, $physicalHeight, 0) | Out-Null
        Start-Sleep -Milliseconds 500
        Set-PowerPointForegroundWindow $windowHandle $powerPointProcessId $expectedTitle "PowerPoint ${logicalWidth}px resize"

        foreach ($tabName in $mappedTabNames) {
            $tab = Find-NativeChromeTab $appElement $tabName
            if ($null -eq $tab) {
                throw "PowerPoint mapped tab '$tabName' disappeared after resize to ${logicalWidth}px."
            }
            Select-NativeChromeTab $tab $windowHandle $powerPointProcessId $expectedTitle $tabName "PowerPoint"
            $rect = New-Object ScreenshotWin32+RECT
            if (-not [ScreenshotWin32]::GetWindowRect($windowHandle, [ref]$rect)) {
                throw "Could not resolve PowerPoint window bounds for '$tabName' at ${logicalWidth}px."
            }
            $actualWidth = $rect.Right - $rect.Left
            if ($actualWidth -le 0 -or $captureHeight -le 0) {
                throw "Invalid PowerPoint capture bounds for '$tabName' at ${logicalWidth}px."
            }
            Assert-ForegroundWindowOwnership $powerPointProcessId $expectedTitle "PowerPoint '$tabName' screen capture"
            $tabFileName = ($tabName -replace '[^a-zA-Z0-9]+', '_').Trim('_').ToLowerInvariant()
            $fileName = "powerpoint_${logicalWidth}_${tabFileName}.png"
            $fullPath = Join-Path $resolvedOutputDirectory $fileName
            Capture-ScreenRectangle $rect.Left $rect.Top $actualWidth $captureHeight $fullPath
            $captures += [ordered]@{
                captureKey = "ribbon:${logicalWidth}:${tabFileName}"
                logicalWidth = $logicalWidth
                tab = $tabName
                tabId = $tabFileName
                fileName = $fileName
                pixelWidth = $actualWidth
                pixelHeight = $captureHeight
                captureStatus = "complete"
                captureMethod = "CopyFromScreen-window-rectangle-top-band"
                sha256 = (Get-ToolFileSha256 -Path $fullPath)
            }
        }
    }

    $expectedCaptureCount = $logicalWidths.Count * $mappedTabNames.Count
    if ($captures.Count -ne $expectedCaptureCount) {
        throw "PowerPoint chrome matrix is incomplete: captured $($captures.Count) of $expectedCaptureCount states."
    }
    [ordered]@{
        schemaVersion = 1
        tool = "tools/Capture-FreePPowerPointChrome.ps1"
        generatedAtUtc = [DateTime]::UtcNow.ToString("o")
        captureStatus = "complete"
        evidenceSubject = "Microsoft PowerPoint"
        evidenceFamily = "native-office-ribbon-chrome"
        captureMethod = "foreground-guarded CopyFromScreen window top band"
        normalizedDpi = $screenDpi
        captureLogicalHeight = 300
        mappedFreePTabs = $mappedTabNames
        widths = $logicalWidths
        expectedCaptureCount = $expectedCaptureCount
        actualCaptureCount = $captures.Count
        captures = $captures
        comparisonBoundary = "This is an authoritative native-PowerPoint visual reference capture. It is mapped to FreeP's shared ribbon tabs, but is not a raw WPF/Avalonia pixel-equivalence comparison because the hosts deliberately have different shell and ribbon implementations."
        limitations = @(
            "Only tabs shared with FreeP's current top-level ribbon profile are captured; PowerPoint-only tabs and contextual tabs are outside this mapped lane.",
            "The evidence is a 300 logical-pixel top band. Slide canvas, Backstage, dialogs, panes, and native non-client decoration are covered by separate evidence lanes.",
            "The capture is guarded by PowerPoint process and exact window-title foreground ownership before tab selection and each CopyFromScreen operation."
        )
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $resolvedOutputDirectory "manifest.json") -Encoding utf8
    Write-Host "Captured $($captures.Count)/$expectedCaptureCount PowerPoint native ribbon states in $resolvedOutputDirectory"
}
catch {
    $manifestPath = Join-Path $resolvedOutputDirectory "manifest.json"
    if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
        Write-Warning "PowerPoint chrome capture was blocked; retained the previous complete reference set. $($_.Exception.Message)"
    }
    else {
        Clear-CurrentCapture
        Write-BlockerManifest $_.Exception.Message
    }
    throw
}
finally {
    if ($null -ne $presentation) {
        try { $presentation.Close() } catch { }
    }
    if ($null -ne $powerPoint) {
        try { $powerPoint.Quit() } catch { }
    }
    if ($powerPointProcessId -gt 0) {
        Get-Process -Id $powerPointProcessId -ErrorAction SilentlyContinue |
            Stop-Process -Force -ErrorAction SilentlyContinue
    }
}
