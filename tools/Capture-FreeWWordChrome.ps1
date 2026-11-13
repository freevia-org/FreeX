param(
    [string]$OutputDirectory = "docs\parity\freew-word-chrome-2026-08-16",
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
    Clear-NativeChromeCapture -OutputDirectory $resolvedOutputDirectory -FilePattern "word_*.png"
}

function Write-BlockerManifest {
    param([Parameter(Mandatory = $true)][string]$Reason)

    Write-NativeChromeBlockerManifest -OutputDirectory $resolvedOutputDirectory -AppLabel "Word" `
        -Tool "tools/Capture-FreeWWordChrome.ps1" -Reason $Reason
}

function Set-WordForegroundWindow {
    param(
        [Parameter(Mandatory = $true)][IntPtr]$WindowHandle,
        [Parameter(Mandatory = $true)][int]$ProcessId,
        [Parameter(Mandatory = $true)][string]$ExpectedTitle,
        [Parameter(Mandatory = $true)][string]$Operation
    )

    # Shared AttachThreadInput-based foreground transfer (ScreenshotCaptureSupport.ps1).
    Set-NativeChromeForegroundWindow $WindowHandle $ProcessId $ExpectedTitle $Operation
}

$word = $null
$document = $null
$wordProcessId = 0
try {
    $logicalWidths = @(Resolve-NativeChromeWidthSpecs $Widths)
    $screenDpi = [ScreenshotWin32]::GetScreenDpi()
    $scale = $screenDpi / 96.0
    $captureHeight = [int][Math]::Ceiling(300 * $scale)
    $windowLogicalHeight = 760
    # This is the standard Word ribbon profile: FreeW's Developer tab remains a separately
    # configurable Office-ribbon extension and is not claimed by this native default-profile lane.
    $mappedTabNames = @("Home", "Insert", "Design", "Layout", "References", "Mailings", "Review", "View", "Help")

    $word = New-Object -ComObject Word.Application
    $word.Visible = $true
    $document = $word.Documents.Add()
    Start-Sleep -Milliseconds 1400
    # Word exposes the HWND on its active Document window, unlike PowerPoint which exposes it
    # directly on Application. Resolve this only after the blank document has been created.
    $windowHandle = [IntPtr]$word.ActiveWindow.Hwnd
    if ($windowHandle -eq [IntPtr]::Zero) {
        throw "Word did not expose an HWND after creating the blank document."
    }
    [ScreenshotWin32]::GetWindowThreadProcessId($windowHandle, [ref]$wordProcessId) | Out-Null
    if ($wordProcessId -le 0) {
        throw "Could not resolve the process owning the Word window."
    }
    $expectedTitle = Get-WindowTitle $windowHandle
    if ([string]::IsNullOrWhiteSpace($expectedTitle)) {
        throw "Word did not expose a non-empty window title."
    }
    Set-WordForegroundWindow $windowHandle $wordProcessId $expectedTitle "Word chrome capture setup"

    $desktop = [System.Windows.Automation.AutomationElement]::RootElement
    $processCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
        [int]$wordProcessId)
    $appElement = $desktop.FindFirst([System.Windows.Automation.TreeScope]::Children, $processCondition)
    if ($null -eq $appElement) {
        throw "Could not resolve the Word UI Automation root."
    }

    foreach ($tabName in $mappedTabNames) {
        if ($null -eq (Find-NativeChromeTab $appElement $tabName)) {
            throw "The installed Word profile does not expose required mapped tab '$tabName'."
        }
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
        Set-WordForegroundWindow $windowHandle $wordProcessId $expectedTitle "Word ${logicalWidth}px resize"

        foreach ($tabName in $mappedTabNames) {
            $tab = Find-NativeChromeTab $appElement $tabName
            if ($null -eq $tab) {
                throw "Word mapped tab '$tabName' disappeared after resize to ${logicalWidth}px."
            }
            Select-NativeChromeTab $tab $windowHandle $wordProcessId $expectedTitle $tabName "Word"
            $rect = New-Object ScreenshotWin32+RECT
            if (-not [ScreenshotWin32]::GetWindowRect($windowHandle, [ref]$rect)) {
                throw "Could not resolve Word window bounds for '$tabName' at ${logicalWidth}px."
            }
            $actualWidth = $rect.Right - $rect.Left
            if ($actualWidth -le 0 -or $captureHeight -le 0) {
                throw "Invalid Word capture bounds for '$tabName' at ${logicalWidth}px."
            }
            Assert-ForegroundWindowOwnership $wordProcessId $expectedTitle "Word '$tabName' screen capture"
            $tabFileName = ($tabName -replace '[^a-zA-Z0-9]+', '_').Trim('_').ToLowerInvariant()
            $fileName = "word_${logicalWidth}_${tabFileName}.png"
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
        throw "Word chrome matrix is incomplete: captured $($captures.Count) of $expectedCaptureCount states."
    }
    [ordered]@{
        schemaVersion = 1
        tool = "tools/Capture-FreeWWordChrome.ps1"
        generatedAtUtc = [DateTime]::UtcNow.ToString("o")
        captureStatus = "complete"
        evidenceSubject = "Microsoft Word"
        evidenceFamily = "native-office-ribbon-chrome"
        captureMethod = "foreground-guarded CopyFromScreen window top band"
        normalizedDpi = $screenDpi
        captureLogicalHeight = 300
        mappedFreeWTabs = $mappedTabNames
        widths = $logicalWidths
        expectedCaptureCount = $expectedCaptureCount
        actualCaptureCount = $captures.Count
        captures = $captures
        comparisonBoundary = "This is an authoritative native-Word visual reference capture. It is mapped to FreeW's standard ribbon tabs, but is not a raw WPF/Avalonia pixel-equivalence comparison because the hosts deliberately have different shell and ribbon implementations."
        limitations = @(
            "The standard Word profile does not include FreeW's configurable Developer tab; it is outside this native default-profile lane.",
            "The evidence is a 300 logical-pixel top band. Document canvas, Backstage, dialogs, panes, contextual tabs, and native non-client decoration are covered by separate evidence lanes.",
            "The capture is guarded by Word process and exact window-title foreground ownership before tab selection and each CopyFromScreen operation."
        )
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $resolvedOutputDirectory "manifest.json") -Encoding utf8
    Write-Host "Captured $($captures.Count)/$expectedCaptureCount Word native ribbon states in $resolvedOutputDirectory"
}
catch {
    $manifestPath = Join-Path $resolvedOutputDirectory "manifest.json"
    if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
        Write-Warning "Word chrome capture was blocked; retained the previous complete reference set. $($_.Exception.Message)"
    }
    else {
        Clear-CurrentCapture
        Write-BlockerManifest $_.Exception.Message
    }
    throw
}
finally {
    if ($null -ne $document) {
        try { $document.Close(0) } catch { }
    }
    if ($null -ne $word) {
        try { $word.Quit() } catch { }
    }
    if ($wordProcessId -gt 0) {
        Get-Process -Id $wordProcessId -ErrorAction SilentlyContinue |
            Stop-Process -Force -ErrorAction SilentlyContinue
    }
}
