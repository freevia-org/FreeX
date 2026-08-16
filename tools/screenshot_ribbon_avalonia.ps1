param(
    [string]$Widths = $env:FREEX_AVALONIA_SS_TOUR_WIDTHS,
    [string]$DllPath = $env:FREEX_AVALONIA_RIBBON_DLL_PATH
)

# Captures the visible Windows Avalonia host rather than the deterministic
# RenderTargetBitmap dialog corpus.  This is intentionally a separate output
# contract: its top-band rectangle, logical viewport, pair keys, and foreground
# guard match screenshot_excel.ps1 and screenshot_ribbon.ps1.
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$repoRoot = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $PSScriptRoot "screenshots_avalonia_ribbon"
. (Join-Path $PSScriptRoot "ScreenshotCaptureSupport.ps1")
[ScreenshotWin32]::SetProcessDPIAware() | Out-Null

$tabNames = @("Home", "Insert", "Draw", "Page Layout", "Formulas", "Data", "Review", "View", "Help")
$captureWidths = @(Resolve-CaptureWidths $Widths)
$windowLogicalHeight = 768
$captureLimitations = @(
    "Ribbon tab captures cover the visible top window band only.",
    "This lane is a foreground Windows capture. It does not replace the deterministic Avalonia dialog corpus.",
    "Transient popups, dropdowns, native dialogs, and context menus require separately guarded capture contracts."
)
$interactiveCapturePlan = @(
    [pscustomobject]@{
        ScenarioId = "ribbon:foreground-top-band"
        EvidenceFamily = "ribbon"
        EvidenceSubject = "freex-avalonia"
        CaptureStatus = "complete-by-this-run"
        PairKeyPattern = "ribbon:<WidthLabel>:<TabFileName>"
        Trigger = "Launch the FreeX Avalonia Windows host, select each static ribbon tab through its visible UI-Automation text node, then capture the owned foreground window top band."
        CaptureRequirement = "Retain a full width matrix only when the expected Avalonia process owns foreground immediately before selection and capture."
        CounterpartSubject = "excel"
    }
)

function Resolve-AvaloniaDllPath($requestedDllPath) {
    if (-not [string]::IsNullOrWhiteSpace($requestedDllPath)) {
        $candidate = if ([System.IO.Path]::IsPathRooted($requestedDllPath)) { $requestedDllPath } else { Join-Path (Get-Location) $requestedDllPath }
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return [System.IO.Path]::GetFullPath($candidate) }
        throw "Avalonia capture DLL was not found at $candidate. Pass -DllPath with a built FreeX.ParityCapture.Avalonia.dll."
    }

    $releaseDll = Join-Path $repoRoot "tools\FreeX.ParityCapture.Avalonia\bin\Release\net10.0\FreeX.ParityCapture.Avalonia.dll"
    if (Test-Path -LiteralPath $releaseDll -PathType Leaf) { return $releaseDll }
    throw "FreeX.ParityCapture.Avalonia.dll was not found. Build tools/FreeX.ParityCapture.Avalonia in Release before invoking this capture."
}

function Clear-AvaloniaRibbonEvidenceArtifacts {
    if (-not (Test-Path -LiteralPath $outDir -PathType Container)) { return }
    Get-ChildItem -LiteralPath $outDir -Filter "*.png" -File -ErrorAction SilentlyContinue | Remove-Item -Force
    Remove-Item -LiteralPath (Join-Path $outDir "screenshot_manifest.json") -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $outDir "screenshot_blocker_manifest.json") -Force -ErrorAction SilentlyContinue
}

function Write-AvaloniaBlockerManifest([string]$operation, [int]$expectedPid, [string]$expectedTitle, [string]$reason) {
    [pscustomobject]@{
        Tool = "screenshot_ribbon_avalonia.ps1"
        EvidenceFamily = "ribbon"
        EvidenceSubject = "freex-avalonia"
        EvidenceApp = "FreeX Avalonia (Windows)"
        CaptureStatus = "blocked"
        BlockedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        Operation = $operation
        Reason = $reason
        ExpectedForeground = [pscustomobject]@{ ProcessId = $expectedPid; WindowTitle = $expectedTitle }
        ActualForeground = Get-ForegroundWindowInfo
        RequestedWidths = @($captureWidths | ForEach-Object { $_.Label })
        RequestedTabs = $tabNames
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outDir "screenshot_blocker_manifest.json") -Encoding UTF8
}

function Assert-AvaloniaForeground([int]$expectedPid, [string]$expectedTitle, [string]$operation) {
    try {
        Assert-ForegroundWindowOwnership $expectedPid $expectedTitle $operation $null
    }
    catch {
        Clear-AvaloniaRibbonEvidenceArtifacts
        Write-AvaloniaBlockerManifest $operation $expectedPid $expectedTitle $_.Exception.Message
        throw
    }
}

function Set-AvaloniaForegroundWindow([IntPtr]$windowHandle, [int]$expectedPid, [string]$expectedTitle, [string]$operation) {
    $shell = New-Object -ComObject WScript.Shell
    for ($attempt = 0; $attempt -lt 16; $attempt++) {
        [ScreenshotWin32]::ShowWindow($windowHandle, 9) | Out-Null
        [ScreenshotWin32]::SetForegroundWindow($windowHandle) | Out-Null
        $shell.AppActivate($expectedPid) | Out-Null
        Start-Sleep -Milliseconds 250
        $foreground = Get-ForegroundWindowInfo
        if ($foreground.ProcessId -eq $expectedPid -and $foreground.Title -eq $expectedTitle) { return }
    }

    Assert-AvaloniaForeground $expectedPid $expectedTitle $operation
}

function Find-AvaloniaTabText([System.Windows.Automation.AutomationElement]$application, [string]$tabName, [int]$windowTop) {
    $nameCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $tabName)
    $matches = $application.FindAll([System.Windows.Automation.TreeScope]::Descendants, $nameCondition)
    foreach ($match in $matches) {
        $rect = $match.Current.BoundingRectangle
        if ($match.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and $rect.Width -gt 0 -and $rect.Height -gt 0 -and $rect.Top -ge $windowTop -and $rect.Top -lt ($windowTop + 260)) {
            return $match
        }
    }
    return $null
}

$proc = $null
try {
    Clear-AvaloniaRibbonEvidenceArtifacts
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
    $dll = Resolve-AvaloniaDllPath $DllPath
    $proc = Start-Process -FilePath "dotnet" -ArgumentList @($dll) -WorkingDirectory (Split-Path -Parent $dll) -PassThru
    $hwnd = [IntPtr]::Zero
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        Start-Sleep -Milliseconds 500
        $processNow = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
        if ($null -eq $processNow) { throw "FreeX Avalonia exited before it exposed a desktop window." }
        if ($processNow.MainWindowHandle -ne 0) { $hwnd = [IntPtr]$processNow.MainWindowHandle; break }
    }
    if ($hwnd -eq [IntPtr]::Zero) { throw "FreeX Avalonia did not expose a desktop window." }

    $expectedTitle = (Get-Process -Id $proc.Id).MainWindowTitle
    if ([string]::IsNullOrWhiteSpace($expectedTitle)) { throw "FreeX Avalonia desktop window has no title." }
    Set-AvaloniaForegroundWindow $hwnd $proc.Id $expectedTitle "initial capture setup"

    $dpi = [ScreenshotWin32]::GetScreenDpi()
    $scale = $dpi / 96.0
    $captureHeight = [int]([Math]::Ceiling(300 * $scale))
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $pidCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$proc.Id)
    $application = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $pidCondition)
    if ($null -eq $application) { throw "UI Automation did not expose the launched FreeX Avalonia desktop window." }

    $capturedFiles = @()
    foreach ($widthSpec in $captureWidths) {
        Set-ScreenshotCaptureWindowWidth $hwnd $widthSpec $scale $windowLogicalHeight {
            param($windowHandle)
            Set-AvaloniaForegroundWindow $windowHandle $proc.Id $expectedTitle "window resize capture setup"
        }
        Start-Sleep -Milliseconds 350
        $windowRect = New-Object ScreenshotWin32+RECT
        [ScreenshotWin32]::GetWindowRect($hwnd, [ref]$windowRect) | Out-Null

        foreach ($tabName in $tabNames) {
            $tab = Find-AvaloniaTabText $application $tabName $windowRect.Top
            if ($null -eq $tab) { throw "FreeX Avalonia did not expose the '$tabName' ribbon tab in the visible top band." }
            $rect = $tab.Current.BoundingRectangle
            [System.Windows.Forms.Cursor]::Position = [System.Drawing.Point]::new([int]($rect.Left + $rect.Width / 2), [int]($rect.Top + $rect.Height / 2))
            Assert-AvaloniaForeground $proc.Id $expectedTitle "ribbon tab mouse input"
            [ScreenshotWin32]::mouse_event(2, 0, 0, 0, 0)
            [ScreenshotWin32]::mouse_event(4, 0, 0, 0, 0)
            Start-Sleep -Milliseconds 650

            [ScreenshotWin32]::GetWindowRect($hwnd, [ref]$windowRect) | Out-Null
            $safe = $tabName -replace '[^a-zA-Z0-9_]', '_'
            $fileName = "avalonia_ribbon_$($widthSpec.Label)_$safe.png"
            Assert-AvaloniaForeground $proc.Id $expectedTitle "screen capture"
            Capture-ScreenRectangle $windowRect.Left $windowRect.Top ($windowRect.Right - $windowRect.Left) $captureHeight (Join-Path $outDir $fileName)
            $capturedFiles += [pscustomobject]@{
                CaptureSequence = $capturedFiles.Count + 1
                CaptureKey = "ribbon:$($widthSpec.Label):$safe"
                PairKey = "ribbon:$($widthSpec.Label):$safe"
                EvidenceSubject = "freex-avalonia"
                CounterpartSubject = "excel"
                CounterpartFileName = "excel_$($widthSpec.Label)_$safe.png"
                Tab = $tabName
                TabFileName = $safe
                WidthLabel = $widthSpec.Label
                WindowLogicalWidth = $widthSpec.WindowLogicalWidth
                EvidencePurpose = $widthSpec.EvidencePurpose
                CaptureMethod = "CopyFromScreen-window-rectangle-top-band"
                CaptureStatus = "complete"
                FileName = $fileName
                Path = (Join-Path $outDir $fileName)
                Width = $windowRect.Right - $windowRect.Left
                Height = $captureHeight
                WindowBounds = [pscustomobject]@{ Left = $windowRect.Left; Top = $windowRect.Top; Right = $windowRect.Right; Bottom = $windowRect.Bottom; Width = $windowRect.Right - $windowRect.Left; Height = $windowRect.Bottom - $windowRect.Top }
            }
        }
    }

    $finalRect = New-Object ScreenshotWin32+RECT
    [ScreenshotWin32]::GetWindowRect($hwnd, [ref]$finalRect) | Out-Null
    Write-RibbonScreenshotEvidenceManifest -ToolName "screenshot_ribbon_avalonia.ps1" -OutputDirectory $outDir -WindowRect $finalRect -CaptureLogicalHeight 300 -CapturePhysicalHeight $captureHeight -Widths $captureWidths -Captures $capturedFiles -ExpectedProcessId $proc.Id -ExpectedWindowTitle $expectedTitle -EvidenceSubject "freex-avalonia" -EvidenceApp "FreeX Avalonia (Windows)" -OutputNaming "avalonia_ribbon_<WidthLabel>_<RibbonTab>.png" -CounterpartSubject "excel" -CounterpartTool "screenshot_excel.ps1" -CounterpartOutputNaming "excel_<WidthLabel>_<RibbonTab>.png" -Tabs $tabNames -Limitations $captureLimitations -InteractiveCapturePlan $interactiveCapturePlan
    Write-Host "Captured $($capturedFiles.Count) Avalonia ribbon states."
}
finally {
    if ($null -ne $proc -and -not $proc.HasExited) {
        Stop-Process -Id $proc.Id -Force
    }
}
