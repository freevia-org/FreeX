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
    Get-ChildItem -LiteralPath $resolvedOutputDirectory -Filter "word_*.png" -File -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction Stop
    foreach ($name in @("manifest.json", "blocker-manifest.json")) {
        Remove-Item -LiteralPath (Join-Path $resolvedOutputDirectory $name) -Force -ErrorAction SilentlyContinue
    }
}

function Write-BlockerManifest {
    param([Parameter(Mandatory = $true)][string]$Reason)

    [ordered]@{
        schemaVersion = 1
        tool = "tools/Capture-FreeWWordChrome.ps1"
        captureStatus = "blocked"
        reason = $Reason
        generatedAtUtc = [DateTime]::UtcNow.ToString("o")
        policy = "No partial evidence is retained when the expected Word-owned foreground window cannot be validated."
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $resolvedOutputDirectory "blocker-manifest.json") -Encoding utf8
}

function Set-WordForegroundWindow {
    param(
        [Parameter(Mandatory = $true)][IntPtr]$WindowHandle,
        [Parameter(Mandatory = $true)][int]$ProcessId,
        [Parameter(Mandatory = $true)][string]$ExpectedTitle,
        [Parameter(Mandatory = $true)][string]$Operation
    )

    [ScreenshotWin32]::ShowWindow($WindowHandle, 1) | Out-Null
    [ScreenshotWin32]::BringWindowToTop($WindowHandle) | Out-Null
    [ScreenshotWin32]::SetForegroundWindow($WindowHandle) | Out-Null
    Start-Sleep -Milliseconds 350
    Assert-ForegroundWindowOwnership $ProcessId $ExpectedTitle $Operation
}

function Resolve-WidthSpecs {
    param([Parameter(Mandatory = $true)][string]$Value)

    $result = @()
    foreach ($entry in $Value.Split(',')) {
        $trimmed = $entry.Trim()
        $width = 0
        if (-not [int]::TryParse($trimmed, [ref]$width) -or $width -lt 750) {
            throw "Widths must be comma-separated integral logical widths of at least 750. Invalid value: '$trimmed'."
        }
        $result += $width
    }
    if ($result.Count -eq 0 -or @($result | Select-Object -Unique).Count -ne $result.Count) {
        throw "Widths must contain at least one distinct value."
    }
    return $result
}

function Find-WordTab {
    param(
        [Parameter(Mandatory = $true)]$AppElement,
        [Parameter(Mandatory = $true)][string]$TabName
    )

    $condition = New-Object System.Windows.Automation.AndCondition @(
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            $TabName)),
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::TabItem)))
    return $AppElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Select-WordTab {
    param(
        [Parameter(Mandatory = $true)]$Tab,
        [Parameter(Mandatory = $true)][IntPtr]$WindowHandle,
        [Parameter(Mandatory = $true)][int]$ProcessId,
        [Parameter(Mandatory = $true)][string]$ExpectedTitle,
        [Parameter(Mandatory = $true)][string]$TabName
    )

    Set-WordForegroundWindow $WindowHandle $ProcessId $ExpectedTitle "Word '$TabName' tab selection"
    try {
        $selection = [System.Windows.Automation.SelectionItemPattern]$Tab.GetCurrentPattern(
            [System.Windows.Automation.SelectionItemPattern]::Pattern)
        $selection.Select()
    }
    catch {
        $bounds = $Tab.Current.BoundingRectangle
        if ($bounds.Width -le 0 -or $bounds.Height -le 0) {
            throw "Word tab '$TabName' has no actionable bounds."
        }
        [System.Windows.Forms.Cursor]::Position = [System.Drawing.Point]::new(
            [int]($bounds.Left + ($bounds.Width / 2)),
            [int]($bounds.Top + ($bounds.Height / 2)))
        Assert-ForegroundWindowOwnership $ProcessId $ExpectedTitle "Word '$TabName' fallback click"
        [ScreenshotWin32]::mouse_event(2, 0, 0, 0, 0)
        [ScreenshotWin32]::mouse_event(4, 0, 0, 0, 0)
    }
    Start-Sleep -Milliseconds 650
}

$word = $null
$document = $null
$wordProcessId = 0
try {
    Clear-CurrentCapture
    $logicalWidths = @(Resolve-WidthSpecs $Widths)
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
        if ($null -eq (Find-WordTab $appElement $tabName)) {
            throw "The installed Word profile does not expose required mapped tab '$tabName'."
        }
    }

    $captures = @()
    foreach ($logicalWidth in $logicalWidths) {
        $physicalWidth = [int][Math]::Ceiling($logicalWidth * $scale)
        $physicalHeight = [int][Math]::Ceiling($windowLogicalHeight * $scale)
        [ScreenshotWin32]::ShowWindow($windowHandle, 1) | Out-Null
        [ScreenshotWin32]::SetWindowPos($windowHandle, [IntPtr]::Zero, 0, 0, $physicalWidth, $physicalHeight, 0) | Out-Null
        Start-Sleep -Milliseconds 500
        Set-WordForegroundWindow $windowHandle $wordProcessId $expectedTitle "Word ${logicalWidth}px resize"

        foreach ($tabName in $mappedTabNames) {
            $tab = Find-WordTab $appElement $tabName
            if ($null -eq $tab) {
                throw "Word mapped tab '$tabName' disappeared after resize to ${logicalWidth}px."
            }
            Select-WordTab $tab $windowHandle $wordProcessId $expectedTitle $tabName
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
                sha256 = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash.ToLowerInvariant()
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
    Clear-CurrentCapture
    Write-BlockerManifest $_.Exception.Message
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
