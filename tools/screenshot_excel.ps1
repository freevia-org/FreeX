param(
    [string]$Widths = $env:FREEX_SS_TOUR_WIDTHS,
    [string]$AutoFilterFlyoutTour = $env:FREEX_EXCEL_AUTOFILTER_FLYOUT_TOUR,
    [string]$NumberFormatDropdownTour = $env:FREEX_EXCEL_NUMBER_FORMAT_DROPDOWN_TOUR,
    [string]$HomeBordersDropdownTour = $env:FREEX_EXCEL_HOME_BORDERS_DROPDOWN_TOUR,
    [string]$WorksheetContextMenuTour = $env:FREEX_EXCEL_WORKSHEET_CONTEXT_MENU_TOUR,
    [string]$OpenWorkbookDialogTour = $env:FREEX_EXCEL_OPEN_WORKBOOK_DIALOG_TOUR,
    [string]$SaveAsWorkbookDialogTour = $env:FREEX_EXCEL_SAVE_AS_WORKBOOK_DIALOG_TOUR
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
trap {
    if ($wpid -is [int] -and $wpid -gt 0) {
        Get-Process -Id $wpid -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    }

    throw $_
}
$outDir = Join-Path $PSScriptRoot "screenshots_excel"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
. (Join-Path $PSScriptRoot "ScreenshotCaptureSupport.ps1")
[ScreenshotWin32]::SetProcessDPIAware() | Out-Null
$autoFilterFlyoutOutDir = Join-Path $outDir "autofilter-flyout-tour"
$numberFormatDropdownOutDir = Join-Path $outDir "home-number-format-dropdown-tour"
$homeBordersDropdownOutDir = Join-Path $outDir "home-borders-dropdown-tour"
$worksheetContextMenuOutDir = Join-Path $outDir "worksheet-context-menu-tour"
$openWorkbookDialogOutDir = Join-Path $outDir "open-workbook-dialog-tour"
$saveAsWorkbookDialogOutDir = Join-Path $outDir "save-as-workbook-dialog-tour"

function Clear-ExcelTourArtifacts {
    param(
        [Parameter(Mandatory = $true)][string]$OutputDirectory,
        [Parameter(Mandatory = $true)][string]$ManifestFileName
    )

    Clear-ScreenshotTourEvidenceArtifacts -OutputDirectory $OutputDirectory -ManifestFileName $ManifestFileName
}
$tabNames = @("Home", "Insert", "Draw", "Page Layout", "Formulas", "Data", "Review", "View", "Help")
$script:requestedTabNames = $tabNames
$script:availableTabNames = @()
$script:skippedTabNames = @()
$script:capturedFiles = @()
$captureLimitations = @(
    "Ribbon tab captures cover the top window band only.",
    "Transient popups, dropdowns, native dialogs, and context menus require separate guarded captures.",
    "Global input and screen capture are blocked unless the expected process and window title own the foreground window."
)
$interactiveCapturePlan = @(
    [pscustomobject]@{
        ScenarioId = "popup:table-autofilter-dropdown"
        ScenarioFileName = "table_autofilter_dropdown"
        Priority = 1
        EvidenceFamily = "popup"
        EvidenceSubject = "excel"
        CaptureStatus = "planned-separate-foreground-guarded-capture"
        CaptureOutputNaming = "interactive_<ScenarioFileName>_<State>.png"
        PairKeyPattern = "interactive:table-autofilter-dropdown:<State>"
        Trigger = "Create or open a sample table/range with values and blanks, enable AutoFilter, then open the active header dropdown with Alt+Down or the header arrow."
        CaptureRequirement = "Capture the active popup/dialog/menu bounds, not the owner-window ribbon band."
        ForegroundGuard = "Re-check Excel foreground ownership before each setup input and before the dropdown-opening input; discard captures when the expected Excel window or popup is not foreground-owned."
        CounterpartSubject = "freex"
    },
    [pscustomobject]@{
        ScenarioId = "dropdown:home-number-format"
        ScenarioFileName = "home_number_format"
        Priority = 2
        EvidenceFamily = "dropdown"
        EvidenceSubject = "excel"
        CaptureStatus = "planned-separate-foreground-guarded-capture"
        CaptureOutputNaming = "interactive_<ScenarioFileName>_<State>.png"
        PairKeyPattern = "interactive:home-number-format:<State>"
        Trigger = "Select Home, open the Number Format combo box, and capture the opened dropdown with the selected format visible."
        CaptureRequirement = "Capture the active popup/dialog/menu bounds, not the owner-window ribbon band."
        ForegroundGuard = "Re-check Excel foreground ownership before opening the dropdown and before the screenshot."
        CounterpartSubject = "freex"
    },
    [pscustomobject]@{
        ScenarioId = "dropdown:home-borders"
        ScenarioFileName = "home_borders"
        Priority = 3
        EvidenceFamily = "dropdown"
        EvidenceSubject = "excel"
        CaptureStatus = "planned-separate-foreground-guarded-capture"
        CaptureOutputNaming = "interactive_<ScenarioFileName>_<State>.png"
        PairKeyPattern = "interactive:home-borders:<State>"
        Trigger = "Select Home, open the Borders dropdown with Alt,H,B, and capture the opened menu."
        CaptureRequirement = "Capture the active popup/dialog/menu bounds, not the owner-window ribbon band."
        ForegroundGuard = "Re-check Excel foreground ownership before opening the dropdown and before the screenshot."
        CounterpartSubject = "freex"
    },
    [pscustomobject]@{
        ScenarioId = "context-menu:worksheet-cell"
        ScenarioFileName = "worksheet_cell_context_menu"
        Priority = 4
        EvidenceFamily = "context-menu"
        EvidenceSubject = "excel"
        CaptureStatus = "planned-separate-foreground-guarded-capture"
        CaptureOutputNaming = "interactive_<ScenarioFileName>_<State>.png"
        PairKeyPattern = "interactive:worksheet-cell-context-menu:<State>"
        Trigger = "Select a representative cell and open the worksheet context menu with Shift+F10, the Menu key, or a guarded right-click."
        CaptureRequirement = "Capture the active popup/dialog/menu bounds, not the owner-window ribbon band."
        ForegroundGuard = "Re-check Excel foreground ownership before the context-menu input and validate the menu belongs to the expected workbook window."
        CounterpartSubject = "freex"
    },
    [pscustomobject]@{
        ScenarioId = "native-dialog:open-workbook"
        ScenarioFileName = "open_workbook_dialog"
        Priority = 5
        EvidenceFamily = "native-dialog"
        EvidenceSubject = "excel"
        CaptureStatus = "planned-separate-foreground-guarded-capture"
        CaptureOutputNaming = "interactive_<ScenarioFileName>_<State>.png"
        PairKeyPattern = "interactive:open-workbook-dialog:<State>"
        Trigger = "Open File > Open > Browse or the equivalent guarded keyboard path that reaches Excel's native Open dialog."
        CaptureRequirement = "Capture the active popup/dialog/menu bounds, not the owner-window ribbon band."
        ForegroundGuard = "Treat the native dialog as the expected foreground target after the final launch input; abort if another process or unrelated dialog owns foreground focus."
        CounterpartSubject = "freex"
    },
    [pscustomobject]@{
        ScenarioId = "native-dialog:save-as-workbook"
        ScenarioFileName = "save_as_workbook_dialog"
        Priority = 6
        EvidenceFamily = "native-dialog"
        EvidenceSubject = "excel"
        CaptureStatus = "planned-separate-foreground-guarded-capture"
        CaptureOutputNaming = "interactive_<ScenarioFileName>_<State>.png"
        PairKeyPattern = "interactive:save-as-workbook-dialog:<State>"
        Trigger = "Open File > Save As > Browse or the equivalent guarded keyboard path that reaches Excel's native Save As dialog."
        CaptureRequirement = "Capture the active popup/dialog/menu bounds, not the owner-window ribbon band."
        ForegroundGuard = "Treat the native dialog as the expected foreground target after the final launch input; abort if another process or unrelated dialog owns foreground focus."
        CounterpartSubject = "freex"
    }
)
$windowLogicalHeight = 768

$captureWidths = @(Resolve-CaptureWidths $Widths)

$dpi   = [ScreenshotWin32]::GetScreenDpi()
$scale = $dpi / 96.0
Write-Host "Screen DPI: $dpi  Scale: $scale"

function Write-RootCaptureBlockerManifest($operation, $expectedPid, $expectedTitle, $reason) {
    $manifestPath = Join-Path $outDir "screenshot_blocker_manifest.json"
    [pscustomobject]@{
        Tool = "screenshot_excel.ps1"
        EvidenceFamily = "ribbon"
        EvidenceSubject = "excel"
        EvidenceApp = "Microsoft Excel"
        CaptureStatus = "blocked"
        BlockedAt = (Get-Date).ToString("o")
        Operation = $operation
        Reason = $reason
        OutputDirectory = $outDir
        ValidEvidenceManifest = "screenshot_manifest.json"
        ExpectedForeground = [pscustomobject]@{
            ProcessId = $expectedPid
            WindowTitle = $expectedTitle
        }
        ActualForeground = Get-ForegroundWindowInfo
        RequestedWidths = @($captureWidths | ForEach-Object { $_.Label })
        RequestedTabs = $script:requestedTabNames
        AvailableTabs = $script:availableTabNames
        SkippedTabs = $script:skippedTabNames
        Policy = "Root ribbon screenshots and screenshot_manifest.json are discarded unless the expected Excel process and window title own foreground immediately before global input and screen capture."
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    Write-Warning "Saved blocker manifest $manifestPath"
}

$script:ForegroundWindowOwnershipFailureAction = {
    param($operation, $expectedPid, $expectedTitle, $reason)
    Clear-ScreenshotEvidenceArtifacts
    Write-RootCaptureBlockerManifest $operation $expectedPid $expectedTitle $reason
}

function Set-ExcelForegroundWindow($excelHwnd, $excelPid, $expectedTitle, $operation) {
    $shell = $null
    try {
        $shell = New-Object -ComObject WScript.Shell
    }
    catch {
        $shell = $null
    }

    for ($attempt = 0; $attempt -lt 16; $attempt++) {
        [ScreenshotWin32]::ShowWindow($excelHwnd, 9) | Out-Null
        [ScreenshotWin32]::SetWindowPos($excelHwnd, [IntPtr](-1), 0, 0, 0, 0, 0x0043) | Out-Null
        Start-Sleep -Milliseconds 40
        [ScreenshotWin32]::SetWindowPos($excelHwnd, [IntPtr](-2), 0, 0, 0, 0, 0x0043) | Out-Null

        $foreground = [ScreenshotWin32]::GetForegroundWindow()
        $foregroundPid = 0
        $targetPid = 0
        $foregroundThread = if ($foreground -ne [IntPtr]::Zero) { [ScreenshotWin32]::GetWindowThreadProcessId($foreground, [ref]$foregroundPid) } else { 0 }
        $targetThread = [ScreenshotWin32]::GetWindowThreadProcessId($excelHwnd, [ref]$targetPid)
        $currentThread = [ScreenshotWin32]::GetCurrentThreadId()
        $attachedTarget = $false
        $attachedForeground = $false
        try {
            if ($targetThread -ne 0 -and $targetThread -ne $currentThread) {
                $attachedTarget = [ScreenshotWin32]::AttachThreadInput($currentThread, $targetThread, $true)
            }
            if ($foregroundThread -ne 0 -and $foregroundThread -ne $currentThread -and $foregroundThread -ne $targetThread) {
                $attachedForeground = [ScreenshotWin32]::AttachThreadInput($currentThread, $foregroundThread, $true)
            }

            [ScreenshotWin32]::BringWindowToTop($excelHwnd) | Out-Null
            [ScreenshotWin32]::SetActiveWindow($excelHwnd) | Out-Null
            [ScreenshotWin32]::SetFocus($excelHwnd) | Out-Null
            [ScreenshotWin32]::SetForegroundWindow($excelHwnd) | Out-Null
        }
        finally {
            if ($attachedForeground) {
                [ScreenshotWin32]::AttachThreadInput($currentThread, $foregroundThread, $false) | Out-Null
            }
            if ($attachedTarget) {
                [ScreenshotWin32]::AttachThreadInput($currentThread, $targetThread, $false) | Out-Null
            }
        }

        if (($attempt % 4) -eq 3) {
            [ScreenshotWin32]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
            [ScreenshotWin32]::SetForegroundWindow($excelHwnd) | Out-Null
            [ScreenshotWin32]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
        }

        if ($null -ne $shell) {
            $shell.AppActivate([int]$excelPid) | Out-Null
        }

        Start-Sleep -Milliseconds 300

        $foreground = [ScreenshotWin32]::GetForegroundWindow()
        if ($foreground -eq [IntPtr]::Zero) {
            continue
        }

        $actualPid = 0
        [ScreenshotWin32]::GetWindowThreadProcessId($foreground, [ref]$actualPid) | Out-Null
        $title = New-Object System.Text.StringBuilder 512
        [ScreenshotWin32]::GetWindowText($foreground, $title, $title.Capacity) | Out-Null
        if ($actualPid -eq $excelPid -and $title.ToString() -eq $expectedTitle) {
            return
        }
    }

    Assert-ForegroundWindowOwnership $excelPid $expectedTitle $operation $script:ForegroundWindowOwnershipFailureAction
}

function Find-ExcelPopupWindow($expectedPid, $ownerWindowHandle, $minimumWidth, $minimumHeight) {
    $windows = [ScreenshotWin32]::GetVisibleWindowsByProcess($expectedPid) |
        Where-Object {
            $_.Handle -ne $ownerWindowHandle -and
            $_.ClassName -ne "XLMAIN" -and
            ($_.Right - $_.Left) -gt $minimumWidth -and
            ($_.Bottom - $_.Top) -gt $minimumHeight
        } |
        Sort-Object @{ Expression = { ($_.Right - $_.Left) * ($_.Bottom - $_.Top) }; Descending = $true }

    return $windows | Select-Object -First 1
}

function Find-ExcelAutoFilterPopupWindow($expectedPid, $ownerWindowHandle) {
    return Find-ExcelPopupWindow $expectedPid $ownerWindowHandle 120 80
}

function Find-ExcelOpenWorkbookDialogWindow($expectedPid, $ownerWindowHandle) {
    $windows = [ScreenshotWin32]::GetVisibleWindowsByProcess($expectedPid) |
        Where-Object {
            $_.Handle -ne $ownerWindowHandle -and
            $_.ClassName -eq "#32770" -and
            $_.Title -eq "Open" -and
            ($_.Right - $_.Left) -gt 400 -and
            ($_.Bottom - $_.Top) -gt 300
        } |
        Sort-Object @{ Expression = { ($_.Right - $_.Left) * ($_.Bottom - $_.Top) }; Descending = $true }

    return $windows | Select-Object -First 1
}

function Find-ExcelSaveAsWorkbookDialogWindow($expectedPid, $ownerWindowHandle) {
    $windows = [ScreenshotWin32]::GetVisibleWindowsByProcess($expectedPid) |
        Where-Object {
            $_.Handle -ne $ownerWindowHandle -and
            ($_.ClassName -eq "NUIDialog" -or ($_.ClassName -eq "#32770" -and $_.Title -eq "Save As")) -and
            ($_.Right - $_.Left) -gt 400 -and
            ($_.Bottom - $_.Top) -gt 250
        } |
        Sort-Object @{ Expression = { ($_.Right - $_.Left) * ($_.Bottom - $_.Top) }; Descending = $true }

    return $windows | Select-Object -First 1
}

function New-ExcelAutoFilterSampleWorkbook($excelApp) {
    $workbook = $excelApp.Workbooks.Add()
    $worksheet = $workbook.Worksheets.Item(1)
    $worksheet.Name = "Filter"
    $worksheet.Range("A1").Value2 = "score"
    $worksheet.Range("B1").Value2 = "region"
    $worksheet.Range("C1").Value2 = "item"
    $worksheet.Range("D1").Value2 = "amount"
    $worksheet.Range("A2").Value2 = 1
    $worksheet.Range("B2").Value2 = "East"
    $worksheet.Range("C2").Value2 = "Alpha"
    $worksheet.Range("D2").Value2 = 10
    $worksheet.Range("A3").Value2 = 2
    $worksheet.Range("B3").Value2 = "West"
    $worksheet.Range("C3").Value2 = "Beta"
    $worksheet.Range("D3").Value2 = 20
    $worksheet.Range("A4").Value2 = 3
    $worksheet.Range("B4").Value2 = "East"
    $worksheet.Range("C4").Value2 = "Gamma"
    $worksheet.Range("D4").Value2 = 30
    $worksheet.Range("A5").Value2 = 4
    $worksheet.Range("B5").Value2 = "West"
    $worksheet.Range("C5").Value2 = "Delta"
    $worksheet.Range("D5").Value2 = 40
    $worksheet.Range("A6").Value2 = $null
    $worksheet.Range("B6").Value2 = "North"
    $worksheet.Range("C6").Value2 = "Blank score"
    $worksheet.Range("D6").Value2 = 50
    $worksheet.Range("A1:D6").AutoFilter() | Out-Null
    $worksheet.Range("A:D").EntireColumn.AutoFit() | Out-Null
    $worksheet.Range("A1").Select() | Out-Null

    return $workbook
}

function New-ExcelNumberFormatSampleWorkbook($excelApp) {
    $workbook = $excelApp.Workbooks.Add()
    $worksheet = $workbook.Worksheets.Item(1)
    $worksheet.Name = "Number Format"
    $worksheet.Range("A1").Value2 = 1234.56
    $worksheet.Range("B1").Value2 = "Home Number Format dropdown sample"
    $worksheet.Range("A:B").EntireColumn.AutoFit() | Out-Null
    $worksheet.Range("A1").Select() | Out-Null

    return $workbook
}

function New-ExcelWorksheetContextMenuSampleWorkbook($excelApp) {
    $workbook = $excelApp.Workbooks.Add()
    $worksheet = $workbook.Worksheets.Item(1)
    $worksheet.Name = "Context Menu"
    $worksheet.Range("A1").Value2 = "Region"
    $worksheet.Range("B1").Value2 = "Score"
    $worksheet.Range("C1").Value2 = "Note"
    $worksheet.Range("A2").Value2 = "North"
    $worksheet.Range("B2").Value2 = 1234.56
    $worksheet.Range("C2").Value2 = "Worksheet context menu"
    $worksheet.Range("A:C").EntireColumn.AutoFit() | Out-Null
    $worksheet.Range("B2").Select() | Out-Null

    return $workbook
}

function Click-ExcelAutoFilterHeaderDropdown($excelApp, $worksheet, $headerAddress, $expectedPid, $expectedTitle) {
    $header = $worksheet.Range($headerAddress)
    $window = $excelApp.ActiveWindow
    $left = $window.PointsToScreenPixelsX($header.Left)
    $top = $window.PointsToScreenPixelsY($header.Top)
    $pointToScreenScale = 2.0
    $clickX = [int]($left + ($header.Width * $pointToScreenScale) - 12)
    $clickY = [int]($top + ($header.Height * $pointToScreenScale / 2.0))

    [ScreenshotWin32]::SetCursorPos($clickX, $clickY) | Out-Null
    Start-Sleep -Milliseconds 100
    Assert-ForegroundWindowOwnership $expectedPid $expectedTitle "Excel AutoFilter dropdown mouse down" $script:ForegroundWindowOwnershipFailureAction
    [ScreenshotWin32]::mouse_event(2, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 60
    Assert-ForegroundWindowOwnership $expectedPid $expectedTitle "Excel AutoFilter dropdown mouse up" $script:ForegroundWindowOwnershipFailureAction
    [ScreenshotWin32]::mouse_event(4, 0, 0, 0, 0)
}

function Expand-ExcelNumberFormatDropdown($expectedPid, $excelElement, $expectedTitle) {
    $comboCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        "NumberFormatGallery")
    $combo = $excelElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $comboCondition)
    if ($null -eq $combo) {
        Clear-ExcelTourArtifacts $numberFormatDropdownOutDir "excel_home_number_format_dropdown_tour_manifest.json"
        throw "Excel Home number-format dropdown tour could not find the NumberFormatGallery ComboBox."
    }

    $pattern = $null
    if (-not $combo.TryGetCurrentPattern(
            [System.Windows.Automation.ExpandCollapsePattern]::Pattern,
            [ref]$pattern)) {
        Clear-ExcelTourArtifacts $numberFormatDropdownOutDir "excel_home_number_format_dropdown_tour_manifest.json"
        throw "Excel Home number-format dropdown tour could not expand NumberFormatGallery through UI Automation."
    }

    Assert-ForegroundWindowOwnership $expectedPid $expectedTitle "Excel Number Format dropdown expand" $script:ForegroundWindowOwnershipFailureAction
    $pattern.Expand()
}

function Open-ExcelWorksheetContextMenu($expectedPid, $expectedTitle) {
    Assert-ForegroundWindowOwnership $expectedPid $expectedTitle "Excel worksheet context menu keyboard input" $script:ForegroundWindowOwnershipFailureAction
    [System.Windows.Forms.SendKeys]::SendWait("+{F10}")
}

function Open-ExcelHomeBordersDropdown($expectedPid, $expectedTitle) {
    Assert-ForegroundWindowOwnership $expectedPid $expectedTitle "Excel Home Borders dropdown keyboard input" $script:ForegroundWindowOwnershipFailureAction
    [System.Windows.Forms.SendKeys]::SendWait("%h")
    Start-Sleep -Milliseconds 350
    Assert-ForegroundWindowOwnership $expectedPid $expectedTitle "Excel Home Borders dropdown keytip continuation" $script:ForegroundWindowOwnershipFailureAction
    [System.Windows.Forms.SendKeys]::SendWait("b")
}

function Open-ExcelNativeOpenDialog($expectedPid, $expectedTitle) {
    Assert-ForegroundWindowOwnership $expectedPid $expectedTitle "Excel native Open dialog keyboard input" $script:ForegroundWindowOwnershipFailureAction
    [System.Windows.Forms.SendKeys]::SendWait("^{F12}")
}

function Open-ExcelNativeSaveAsDialog($expectedPid, $expectedTitle) {
    Assert-ForegroundWindowOwnership $expectedPid $expectedTitle "Excel native Save As dialog keyboard input" $script:ForegroundWindowOwnershipFailureAction
    [System.Windows.Forms.SendKeys]::SendWait("{F12}")
}

function Invoke-ExcelAutoFilterFlyoutTour {
    New-Item -ItemType Directory -Force -Path $autoFilterFlyoutOutDir | Out-Null
    Clear-ScreenshotTourEvidenceArtifacts -OutputDirectory $autoFilterFlyoutOutDir -ManifestFileName "excel_autofilter_flyout_tour_manifest.json"

    $excelApp = $null
    $workbook = $null
    try {
        $excelApp = New-Object -ComObject Excel.Application
        $excelApp.Visible = $true
        $excelApp.DisplayAlerts = $false
        $excelApp.WindowState = -4143
        $excelApp.Top = 0
        $excelApp.Left = 0
        $excelApp.Width = 900
        $excelApp.Height = 720
        $workbook = New-ExcelAutoFilterSampleWorkbook $excelApp
        $worksheet = $excelApp.ActiveSheet
        Start-Sleep -Milliseconds 700

        $excelHwnd = [IntPtr]$excelApp.Hwnd
        if ($excelHwnd -eq [IntPtr]::Zero) {
            Clear-ExcelTourArtifacts $autoFilterFlyoutOutDir "excel_autofilter_flyout_tour_manifest.json"
            throw "Excel AutoFilter flyout tour could not resolve the Excel window handle."
        }

        $excelPid = 0
        [ScreenshotWin32]::GetWindowThreadProcessId($excelHwnd, [ref]$excelPid) | Out-Null
        $excelTitle = Get-WindowTitle $excelHwnd
        Set-ExcelForegroundWindow $excelHwnd $excelPid $excelTitle "Excel AutoFilter flyout setup"
        Assert-ForegroundWindowOwnership $excelPid $excelTitle "Excel AutoFilter flyout setup" $script:ForegroundWindowOwnershipFailureAction

        Click-ExcelAutoFilterHeaderDropdown $excelApp $worksheet "A1" $excelPid $excelTitle
        Start-Sleep -Milliseconds 900
        Assert-ForegroundProcessOwnership $excelPid "Excel AutoFilter flyout capture" "Excel"

        $popup = Find-ExcelAutoFilterPopupWindow $excelPid $excelHwnd
        if ($null -eq $popup) {
            Clear-ExcelTourArtifacts $autoFilterFlyoutOutDir "excel_autofilter_flyout_tour_manifest.json"
            throw "Excel AutoFilter flyout tour did not detect a foreground Excel popup window after opening the header dropdown."
        }

        $windowRect = New-Object ScreenshotWin32+RECT
        [ScreenshotWin32]::GetWindowRect($excelHwnd, [ref]$windowRect) | Out-Null
        $captureSource = "popup-window-rectangle"
        $captureBounds = [pscustomobject]@{
            Left = $popup.Left
            Top = $popup.Top
            Right = $popup.Right
            Bottom = $popup.Bottom
            Width = $popup.Right - $popup.Left
            Height = $popup.Bottom - $popup.Top
        }
        $popupBounds = [pscustomobject]@{
            Handle = $popup.Handle.ToString()
            ClassName = $popup.ClassName
            Title = $popup.Title
            Left = $popup.Left
            Top = $popup.Top
            Right = $popup.Right
            Bottom = $popup.Bottom
            Width = $popup.Right - $popup.Left
            Height = $popup.Bottom - $popup.Top
        }

        $fileName = "interactive_table_autofilter_dropdown_opened.png"
        $path = Join-Path $autoFilterFlyoutOutDir $fileName
        Assert-ForegroundProcessOwnership $excelPid "Excel AutoFilter flyout screen capture" "Excel"
        Capture-ScreenRectangle $captureBounds.Left $captureBounds.Top $captureBounds.Width $captureBounds.Height $path

        $manifestPath = Join-Path $autoFilterFlyoutOutDir "excel_autofilter_flyout_tour_manifest.json"
        [pscustomobject]@{
            Tool = "FREEX_EXCEL_AUTOFILTER_FLYOUT_TOUR"
            EvidenceFamily = "popup"
            EvidenceSubject = "excel"
            EvidenceApp = "Microsoft Excel"
            OutputDirectory = $autoFilterFlyoutOutDir
            OutputNaming = "interactive_table_autofilter_dropdown_opened.png"
            CatalogEvidenceTarget = "docs/testing/ui-test-catalog.md"
            HeaderCell = "A1"
            HeaderText = "score"
            AutoFilterRange = "A1:D6"
            FilterColumnOffset = 0
            CaptureStatus = "complete"
            CaptureMethod = $captureSource
            ForegroundGuard = [pscustomobject]@{
                Required = $true
                ExpectedProcessId = $excelPid
                ExpectedWindowTitle = $excelTitle
                Policy = "Seed through Excel automation, then abort and clear AutoFilter flyout evidence unless Excel owns foreground immediately before the header-arrow click and screen capture."
            }
            Pairing = [pscustomobject]@{
                PairKeyPattern = "interactive:table-autofilter-dropdown:<State>"
                PairKey = "interactive:table-autofilter-dropdown:opened"
                CounterpartSubject = "freex"
                CounterpartTool = "FREEX_AUTOFILTER_FLYOUT_TOUR"
                CounterpartFileName = "freex_table_autofilter_dropdown.png"
            }
            Scenario = [pscustomobject]@{
                ScenarioId = "popup:table-autofilter-dropdown"
                ScenarioFileName = "table_autofilter_dropdown"
                State = "opened"
                HeaderCell = "A1"
                HeaderText = "score"
                SampleRange = "A1:D6"
                SampleValues = @("1", "2", "3", "4", "(Blanks)")
                Trigger = "Excel COM seeds the sample range, selects A1, and a foreground-guarded header-arrow click opens the AutoFilter dropdown."
            }
            WindowBounds = $captureBounds
            PopupBounds = $popupBounds
            Captures = @(
                [pscustomobject]@{
                    CaptureSequence = 1
                    CaptureKey = "interactive:table-autofilter-dropdown:opened"
                    PairKey = "interactive:table-autofilter-dropdown:opened"
                    EvidenceSubject = "excel"
                    CounterpartSubject = "freex"
                    CounterpartFileName = "freex_table_autofilter_dropdown.png"
                    FileName = $fileName
                    Path = $path
                    Width = $captureBounds.Width
                    Height = $captureBounds.Height
                    CaptureMethod = $captureSource
                    CaptureStatus = "complete"
                }
            )
        } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

        Write-Host "Saved $path"
        Write-Host "Saved $manifestPath"
    }
    finally {
        if ($null -ne $workbook) {
            $workbook.Close($false) | Out-Null
        }
        if ($null -ne $excelApp) {
            $excelApp.Quit() | Out-Null
        }
    }
}

function Invoke-ExcelNumberFormatDropdownTour {
    New-Item -ItemType Directory -Force -Path $numberFormatDropdownOutDir | Out-Null
    Clear-ScreenshotTourEvidenceArtifacts -OutputDirectory $numberFormatDropdownOutDir -ManifestFileName "excel_home_number_format_dropdown_tour_manifest.json"

    $excelApp = $null
    $workbook = $null
    try {
        $excelApp = New-Object -ComObject Excel.Application
        $excelApp.Visible = $true
        $excelApp.DisplayAlerts = $false
        $excelApp.WindowState = -4143
        $excelApp.Top = 0
        $excelApp.Left = 0
        $excelApp.Width = 900
        $excelApp.Height = 720
        $workbook = New-ExcelNumberFormatSampleWorkbook $excelApp
        Start-Sleep -Milliseconds 700

        $excelHwnd = [IntPtr]$excelApp.Hwnd
        if ($excelHwnd -eq [IntPtr]::Zero) {
            Clear-ExcelTourArtifacts $numberFormatDropdownOutDir "excel_home_number_format_dropdown_tour_manifest.json"
            throw "Excel Home number-format dropdown tour could not resolve the Excel window handle."
        }

        $excelPid = 0
        [ScreenshotWin32]::GetWindowThreadProcessId($excelHwnd, [ref]$excelPid) | Out-Null
        $excelTitle = Get-WindowTitle $excelHwnd
        Set-ExcelForegroundWindow $excelHwnd $excelPid $excelTitle "Excel Number Format dropdown setup"
        Assert-ForegroundWindowOwnership $excelPid $excelTitle "Excel Number Format dropdown setup" $script:ForegroundWindowOwnershipFailureAction

        $desktop = [System.Windows.Automation.AutomationElement]::RootElement
        $processCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
            [int]$excelPid)
        $excelElement = $desktop.FindFirst(
            [System.Windows.Automation.TreeScope]::Children,
            $processCondition)
        if ($null -eq $excelElement) {
            Clear-ExcelTourArtifacts $numberFormatDropdownOutDir "excel_home_number_format_dropdown_tour_manifest.json"
            throw "Excel Home number-format dropdown tour could not find the Excel UI Automation root."
        }

        Expand-ExcelNumberFormatDropdown $excelPid $excelElement $excelTitle
        Start-Sleep -Milliseconds 900
        Assert-ForegroundProcessOwnership $excelPid "Excel Number Format dropdown capture" "Excel"

        $popup = Find-ExcelPopupWindow $excelPid $excelHwnd 120 120
        if ($null -eq $popup) {
            Clear-ExcelTourArtifacts $numberFormatDropdownOutDir "excel_home_number_format_dropdown_tour_manifest.json"
            throw "Excel Home number-format dropdown tour did not detect a foreground Excel popup window after expanding NumberFormatGallery."
        }

        $captureSource = "popup-window-rectangle"
        $captureBounds = [pscustomobject]@{
            Left = $popup.Left
            Top = $popup.Top
            Right = $popup.Right
            Bottom = $popup.Bottom
            Width = $popup.Right - $popup.Left
            Height = $popup.Bottom - $popup.Top
        }
        $popupBounds = [pscustomobject]@{
            Handle = $popup.Handle.ToString()
            ClassName = $popup.ClassName
            Title = $popup.Title
            Left = $popup.Left
            Top = $popup.Top
            Right = $popup.Right
            Bottom = $popup.Bottom
            Width = $popup.Right - $popup.Left
            Height = $popup.Bottom - $popup.Top
        }

        $fileName = "interactive_home_number_format_opened.png"
        $path = Join-Path $numberFormatDropdownOutDir $fileName
        Assert-ForegroundProcessOwnership $excelPid "Excel Number Format dropdown screen capture" "Excel"
        Capture-ScreenRectangle $captureBounds.Left $captureBounds.Top $captureBounds.Width $captureBounds.Height $path

        $manifestPath = Join-Path $numberFormatDropdownOutDir "excel_home_number_format_dropdown_tour_manifest.json"
        [pscustomobject]@{
            Tool = "FREEX_EXCEL_NUMBER_FORMAT_DROPDOWN_TOUR"
            EvidenceFamily = "dropdown"
            EvidenceSubject = "excel"
            EvidenceApp = "Microsoft Excel"
            OutputDirectory = $numberFormatDropdownOutDir
            OutputNaming = "interactive_home_number_format_opened.png"
            CatalogEvidenceTarget = "docs/testing/ui-test-catalog.md"
            SelectedCell = "A1"
            SelectedFormat = "General"
            CaptureStatus = "complete"
            CaptureMethod = $captureSource
            ForegroundGuard = [pscustomobject]@{
                Required = $true
                ExpectedProcessId = $excelPid
                ExpectedWindowTitle = $excelTitle
                Policy = "Seed through Excel automation, then abort and clear number-format dropdown evidence unless Excel owns foreground immediately before expanding NumberFormatGallery and before screen capture."
            }
            Pairing = [pscustomobject]@{
                PairKeyPattern = "interactive:home-number-format:<State>"
                PairKey = "interactive:home-number-format:opened"
                CounterpartSubject = "freex"
                CounterpartTool = "FREEX_HOME_NUMBER_FORMAT_DROPDOWN_TOUR"
                CounterpartFileName = "freex_dropdown_home_number_format_opened.png"
            }
            Scenario = [pscustomobject]@{
                ScenarioId = "dropdown:home-number-format"
                ScenarioFileName = "home_number_format"
                State = "opened"
                SelectedCell = "A1"
                SampleValue = "1234.56"
                Trigger = "Excel COM seeds A1, selects Home's NumberFormatGallery ComboBox through UI Automation, and expands it with a foreground guard."
            }
            WindowBounds = $captureBounds
            PopupBounds = $popupBounds
            Captures = @(
                [pscustomobject]@{
                    CaptureSequence = 1
                    CaptureKey = "interactive:home-number-format:opened"
                    PairKey = "interactive:home-number-format:opened"
                    EvidenceSubject = "excel"
                    CounterpartSubject = "freex"
                    CounterpartFileName = "freex_dropdown_home_number_format_opened.png"
                    FileName = $fileName
                    Path = $path
                    Width = $captureBounds.Width
                    Height = $captureBounds.Height
                    CaptureMethod = $captureSource
                    CaptureStatus = "complete"
                }
            )
        } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

        Write-Host "Saved $path"
        Write-Host "Saved $manifestPath"
    }
    finally {
        if ($null -ne $workbook) {
            $workbook.Close($false) | Out-Null
        }
        if ($null -ne $excelApp) {
            $excelApp.Quit() | Out-Null
        }
    }
}

function Invoke-ExcelHomeBordersDropdownTour {
    New-Item -ItemType Directory -Force -Path $homeBordersDropdownOutDir | Out-Null
    Clear-ScreenshotTourEvidenceArtifacts -OutputDirectory $homeBordersDropdownOutDir -ManifestFileName "excel_home_borders_dropdown_tour_manifest.json"

    $excelApp = $null
    $workbook = $null
    try {
        $excelApp = New-Object -ComObject Excel.Application
        $excelApp.Visible = $true
        $excelApp.DisplayAlerts = $false
        $excelApp.WindowState = -4143
        $excelApp.Top = 0
        $excelApp.Left = 0
        $excelApp.Width = 900
        $excelApp.Height = 720
        $workbook = $excelApp.Workbooks.Add()
        Start-Sleep -Milliseconds 700

        $excelHwnd = [IntPtr]$excelApp.Hwnd
        if ($excelHwnd -eq [IntPtr]::Zero) {
            Clear-ExcelTourArtifacts $homeBordersDropdownOutDir "excel_home_borders_dropdown_tour_manifest.json"
            throw "Excel Home Borders dropdown tour could not resolve the Excel window handle."
        }

        $excelPid = 0
        [ScreenshotWin32]::GetWindowThreadProcessId($excelHwnd, [ref]$excelPid) | Out-Null
        $excelTitle = Get-WindowTitle $excelHwnd
        Set-ExcelForegroundWindow $excelHwnd $excelPid $excelTitle "Excel Home Borders dropdown setup"
        Assert-ForegroundWindowOwnership $excelPid $excelTitle "Excel Home Borders dropdown setup" $script:ForegroundWindowOwnershipFailureAction

        Open-ExcelHomeBordersDropdown $excelPid $excelTitle
        Start-Sleep -Milliseconds 1800
        Assert-ForegroundProcessOwnership $excelPid "Excel Home Borders dropdown capture" "Excel"

        $popup = Find-ExcelPopupWindow $excelPid $excelHwnd 120 160
        if ($null -eq $popup) {
            Clear-ExcelTourArtifacts $homeBordersDropdownOutDir "excel_home_borders_dropdown_tour_manifest.json"
            throw "Excel Home Borders dropdown tour did not detect a foreground Excel popup window after Alt,H,B."
        }

        if (($popup.Right - $popup.Left) -gt 560 -or ($popup.Bottom - $popup.Top) -gt 1040) {
            Clear-ExcelTourArtifacts $homeBordersDropdownOutDir "excel_home_borders_dropdown_tour_manifest.json"
            throw "Excel Home Borders dropdown tour detected an oversized candidate window ($($popup.Right - $popup.Left)x$($popup.Bottom - $popup.Top)) instead of the Borders menu."
        }

        $captureSource = "popup-window-rectangle"
        $captureBounds = [pscustomobject]@{
            Left = $popup.Left
            Top = $popup.Top
            Right = $popup.Right
            Bottom = $popup.Bottom
            Width = $popup.Right - $popup.Left
            Height = $popup.Bottom - $popup.Top
        }

        $fileName = "interactive_home_borders_opened.png"
        $path = Join-Path $homeBordersDropdownOutDir $fileName
        Assert-ForegroundProcessOwnership $excelPid "Excel Home Borders dropdown screen capture" "Excel"
        Capture-ScreenRectangle $captureBounds.Left $captureBounds.Top $captureBounds.Width $captureBounds.Height $path

        $manifestPath = Join-Path $homeBordersDropdownOutDir "excel_home_borders_dropdown_tour_manifest.json"
        [pscustomobject]@{
            Tool = "FREEX_EXCEL_HOME_BORDERS_DROPDOWN_TOUR"
            EvidenceFamily = "dropdown"
            EvidenceSubject = "excel"
            EvidenceApp = "Microsoft Excel"
            OutputDirectory = $homeBordersDropdownOutDir
            OutputNaming = "interactive_home_borders_opened.png"
            CatalogEvidenceTarget = "docs/testing/ui-test-catalog.md"
            ScenarioId = "dropdown:home-borders"
            EntryPath = "Alt,H,B"
            CaptureStatus = "complete"
            CaptureMethod = $captureSource
            ForegroundGuard = [pscustomobject]@{
                Required = $true
                ExpectedProcessId = $excelPid
                ExpectedWindowTitle = $excelTitle
                Policy = "Seed a blank Excel workbook, then abort and clear Home Borders dropdown evidence unless Excel owns foreground immediately before Alt,H,B and before screen capture."
            }
            Pairing = [pscustomobject]@{
                PairKeyPattern = "interactive:home-borders:<State>"
                PairKey = "interactive:home-borders:opened"
                CounterpartSubject = "freex"
                CounterpartTool = "FREEX_HOME_BORDERS_DROPDOWN_TOUR"
                CounterpartFileName = "freex_dropdown_home_borders_opened.png"
            }
            Scenario = [pscustomobject]@{
                ScenarioId = "dropdown:home-borders"
                ScenarioFileName = "home_borders"
                State = "opened"
                Trigger = "Excel COM starts a blank workbook and foreground-guarded Alt,H,B opens the Home Borders dropdown."
            }
            WindowBounds = $captureBounds
            Captures = @(
                [pscustomobject]@{
                    CaptureSequence = 1
                    CaptureKey = "interactive:home-borders:opened"
                    PairKey = "interactive:home-borders:opened"
                    EvidenceSubject = "excel"
                    CounterpartSubject = "freex"
                    CounterpartFileName = "freex_dropdown_home_borders_opened.png"
                    FileName = $fileName
                    Path = $path
                    Width = $captureBounds.Width
                    Height = $captureBounds.Height
                    CaptureMethod = $captureSource
                    CaptureStatus = "complete"
                }
            )
        } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

        Write-Host "Saved $path"
        Write-Host "Saved $manifestPath"
    }
    finally {
        if ($null -ne $workbook) {
            $workbook.Close($false) | Out-Null
        }
        if ($null -ne $excelApp) {
            $excelApp.Quit() | Out-Null
        }
    }
}

function Invoke-ExcelWorksheetContextMenuTour {
    New-Item -ItemType Directory -Force -Path $worksheetContextMenuOutDir | Out-Null
    Clear-ScreenshotTourEvidenceArtifacts -OutputDirectory $worksheetContextMenuOutDir -ManifestFileName "excel_worksheet_context_menu_tour_manifest.json"

    $excelApp = $null
    $workbook = $null
    try {
        $excelApp = New-Object -ComObject Excel.Application
        $excelApp.Visible = $true
        $excelApp.DisplayAlerts = $false
        $excelApp.WindowState = -4143
        $excelApp.Top = 0
        $excelApp.Left = 0
        $excelApp.Width = 900
        $excelApp.Height = 720
        $workbook = New-ExcelWorksheetContextMenuSampleWorkbook $excelApp
        Start-Sleep -Milliseconds 700

        $excelHwnd = [IntPtr]$excelApp.Hwnd
        if ($excelHwnd -eq [IntPtr]::Zero) {
            Clear-ExcelTourArtifacts $worksheetContextMenuOutDir "excel_worksheet_context_menu_tour_manifest.json"
            throw "Excel worksheet context menu tour could not resolve the Excel window handle."
        }

        $excelPid = 0
        [ScreenshotWin32]::GetWindowThreadProcessId($excelHwnd, [ref]$excelPid) | Out-Null
        $excelTitle = Get-WindowTitle $excelHwnd
        Set-ExcelForegroundWindow $excelHwnd $excelPid $excelTitle "Excel worksheet context menu setup"
        Assert-ForegroundWindowOwnership $excelPid $excelTitle "Excel worksheet context menu setup" $script:ForegroundWindowOwnershipFailureAction

        Open-ExcelWorksheetContextMenu $excelPid $excelTitle
        Start-Sleep -Milliseconds 900
        Assert-ForegroundProcessOwnership $excelPid "Excel worksheet context menu capture" "Excel"

        $popup = Find-ExcelPopupWindow $excelPid $excelHwnd 120 120
        if ($null -eq $popup) {
            Clear-ExcelTourArtifacts $worksheetContextMenuOutDir "excel_worksheet_context_menu_tour_manifest.json"
            throw "Excel worksheet context menu tour did not detect a foreground Excel popup window after Shift+F10."
        }

        $captureSource = "popup-window-rectangle"
        $captureBounds = [pscustomobject]@{
            Left = $popup.Left
            Top = $popup.Top
            Right = $popup.Right
            Bottom = $popup.Bottom
            Width = $popup.Right - $popup.Left
            Height = $popup.Bottom - $popup.Top
        }
        $popupBounds = [pscustomobject]@{
            Handle = $popup.Handle.ToString()
            ClassName = $popup.ClassName
            Title = $popup.Title
            Left = $popup.Left
            Top = $popup.Top
            Right = $popup.Right
            Bottom = $popup.Bottom
            Width = $popup.Right - $popup.Left
            Height = $popup.Bottom - $popup.Top
        }

        $fileName = "interactive_worksheet_cell_context_menu_opened.png"
        $path = Join-Path $worksheetContextMenuOutDir $fileName
        Assert-ForegroundProcessOwnership $excelPid "Excel worksheet context menu screen capture" "Excel"
        Capture-ScreenRectangle $captureBounds.Left $captureBounds.Top $captureBounds.Width $captureBounds.Height $path

        $manifestPath = Join-Path $worksheetContextMenuOutDir "excel_worksheet_context_menu_tour_manifest.json"
        [pscustomobject]@{
            Tool = "FREEX_EXCEL_WORKSHEET_CONTEXT_MENU_TOUR"
            EvidenceFamily = "context-menu"
            EvidenceSubject = "excel"
            EvidenceApp = "Microsoft Excel"
            OutputDirectory = $worksheetContextMenuOutDir
            OutputNaming = "interactive_worksheet_cell_context_menu_opened.png"
            CatalogEvidenceTarget = "docs/testing/ui-test-catalog.md"
            SelectedCell = "B2"
            EntryPath = "Shift+F10"
            CaptureStatus = "complete"
            CaptureMethod = $captureSource
            ForegroundGuard = [pscustomobject]@{
                Required = $true
                ExpectedProcessId = $excelPid
                ExpectedWindowTitle = $excelTitle
                Policy = "Seed through Excel automation, then abort and clear worksheet context-menu evidence unless Excel owns foreground immediately before Shift+F10 and before screen capture."
            }
            Pairing = [pscustomobject]@{
                PairKeyPattern = "interactive:worksheet-cell-context-menu:<State>"
                PairKey = "interactive:worksheet-cell-context-menu:opened"
                CounterpartSubject = "freex"
                CounterpartTool = "FREEX_WORKSHEET_CONTEXT_MENU_TOUR"
                CounterpartFileName = "freex_context_menu_worksheet_cell_opened.png"
            }
            Scenario = [pscustomobject]@{
                ScenarioId = "context-menu:worksheet-cell"
                ScenarioFileName = "worksheet_cell_context_menu"
                State = "opened"
                SelectedCell = "B2"
                SampleValue = "1234.56"
                Trigger = "Excel COM seeds B2 and a foreground-guarded Shift+F10 opens the worksheet-cell context menu."
            }
            WindowBounds = $captureBounds
            PopupBounds = $popupBounds
            Captures = @(
                [pscustomobject]@{
                    CaptureSequence = 1
                    CaptureKey = "interactive:worksheet-cell-context-menu:opened"
                    PairKey = "interactive:worksheet-cell-context-menu:opened"
                    EvidenceSubject = "excel"
                    CounterpartSubject = "freex"
                    CounterpartFileName = "freex_context_menu_worksheet_cell_opened.png"
                    FileName = $fileName
                    Path = $path
                    Width = $captureBounds.Width
                    Height = $captureBounds.Height
                    CaptureMethod = $captureSource
                    CaptureStatus = "complete"
                }
            )
        } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

        Write-Host "Saved $path"
        Write-Host "Saved $manifestPath"
    }
    finally {
        if ($null -ne $workbook) {
            $workbook.Close($false) | Out-Null
        }
        if ($null -ne $excelApp) {
            $excelApp.Quit() | Out-Null
        }
    }
}

function Invoke-ExcelOpenWorkbookDialogTour {
    New-Item -ItemType Directory -Force -Path $openWorkbookDialogOutDir | Out-Null
    Clear-ScreenshotTourEvidenceArtifacts -OutputDirectory $openWorkbookDialogOutDir -ManifestFileName "excel_open_workbook_dialog_tour_manifest.json"

    $excelApp = $null
    $workbook = $null
    $excelPid = 0
    try {
        $excelApp = New-Object -ComObject Excel.Application
        $excelApp.Visible = $true
        $excelApp.DisplayAlerts = $false
        $excelApp.WindowState = -4143
        $excelApp.Top = 0
        $excelApp.Left = 0
        $excelApp.Width = 900
        $excelApp.Height = 720
        $workbook = $excelApp.Workbooks.Add()
        Start-Sleep -Milliseconds 700

        $excelHwnd = [IntPtr]$excelApp.Hwnd
        if ($excelHwnd -eq [IntPtr]::Zero) {
            Clear-ExcelTourArtifacts $openWorkbookDialogOutDir "excel_open_workbook_dialog_tour_manifest.json"
            throw "Excel native Open dialog tour could not resolve the Excel window handle."
        }

        [ScreenshotWin32]::GetWindowThreadProcessId($excelHwnd, [ref]$excelPid) | Out-Null
        $excelTitle = Get-WindowTitle $excelHwnd
        Set-ExcelForegroundWindow $excelHwnd $excelPid $excelTitle "Excel native Open dialog setup"
        Assert-ForegroundWindowOwnership $excelPid $excelTitle "Excel native Open dialog setup" $script:ForegroundWindowOwnershipFailureAction

        Open-ExcelNativeOpenDialog $excelPid $excelTitle
        Start-Sleep -Milliseconds 1200
        Assert-ForegroundProcessOwnership $excelPid "Excel native Open dialog capture" "Excel"

        $dialog = Find-ExcelOpenWorkbookDialogWindow $excelPid $excelHwnd
        if ($null -eq $dialog) {
            Clear-ExcelTourArtifacts $openWorkbookDialogOutDir "excel_open_workbook_dialog_tour_manifest.json"
            throw "Excel native Open dialog tour did not detect an Excel-owned '#32770' Open dialog after Ctrl+F12."
        }

        $captureSource = "native-dialog-window-rectangle"
        $captureBounds = [pscustomobject]@{
            Left = $dialog.Left
            Top = $dialog.Top
            Right = $dialog.Right
            Bottom = $dialog.Bottom
            Width = $dialog.Right - $dialog.Left
            Height = $dialog.Bottom - $dialog.Top
        }
        $dialogBounds = [pscustomobject]@{
            Handle = $dialog.Handle.ToString()
            ClassName = $dialog.ClassName
            Title = $dialog.Title
            Left = $dialog.Left
            Top = $dialog.Top
            Right = $dialog.Right
            Bottom = $dialog.Bottom
            Width = $dialog.Right - $dialog.Left
            Height = $dialog.Bottom - $dialog.Top
        }

        $fileName = "interactive_open_workbook_dialog_opened.png"
        $path = Join-Path $openWorkbookDialogOutDir $fileName
        Assert-ForegroundProcessOwnership $excelPid "Excel native Open dialog screen capture" "Excel"
        Capture-ScreenRectangle $captureBounds.Left $captureBounds.Top $captureBounds.Width $captureBounds.Height $path

        $manifestPath = Join-Path $openWorkbookDialogOutDir "excel_open_workbook_dialog_tour_manifest.json"
        [pscustomobject]@{
            Tool = "FREEX_EXCEL_OPEN_WORKBOOK_DIALOG_TOUR"
            EvidenceFamily = "native-dialog"
            EvidenceSubject = "excel"
            EvidenceApp = "Microsoft Excel"
            OutputDirectory = $openWorkbookDialogOutDir
            OutputNaming = "interactive_open_workbook_dialog_opened.png"
            CatalogEvidenceTarget = "docs/testing/ui-test-catalog.md"
            DialogTitle = "Open"
            DialogClassName = "#32770"
            EntryPath = "Ctrl+F12"
            CaptureStatus = "complete"
            CaptureMethod = $captureSource
            ForegroundGuard = [pscustomobject]@{
                Required = $true
                ExpectedProcessId = $excelPid
                ExpectedWindowTitle = $excelTitle
                Policy = "Seed a blank Excel workbook, then abort and clear native Open dialog evidence unless Excel owns foreground immediately before Ctrl+F12 and before screen capture."
            }
            Pairing = [pscustomobject]@{
                PairKeyPattern = "interactive:open-workbook-dialog:<State>"
                PairKey = "interactive:open-workbook-dialog:opened"
                CounterpartSubject = "freex"
                CounterpartTool = "FREEX_OPEN_WORKBOOK_DIALOG_TOUR"
                CounterpartFileName = "freex_open_workbook_dialog_opened.png"
            }
            Scenario = [pscustomobject]@{
                ScenarioId = "native-dialog:open-workbook"
                ScenarioFileName = "open_workbook_dialog"
                State = "opened"
                Trigger = "Excel COM starts a blank workbook and a foreground-guarded Ctrl+F12 opens the native Open dialog."
            }
            WindowBounds = $captureBounds
            DialogBounds = $dialogBounds
            Captures = @(
                [pscustomobject]@{
                    CaptureSequence = 1
                    CaptureKey = "interactive:open-workbook-dialog:opened"
                    PairKey = "interactive:open-workbook-dialog:opened"
                    EvidenceSubject = "excel"
                    CounterpartSubject = "freex"
                    CounterpartFileName = "freex_open_workbook_dialog_opened.png"
                    FileName = $fileName
                    Path = $path
                    Width = $captureBounds.Width
                    Height = $captureBounds.Height
                    CaptureMethod = $captureSource
                    CaptureStatus = "complete"
                }
            )
        } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

        Write-Host "Saved $path"
        Write-Host "Saved $manifestPath"
    }
    finally {
        if ($excelPid -gt 0) {
            Get-Process -Id $excelPid -ErrorAction SilentlyContinue |
                Stop-Process -Force -ErrorAction SilentlyContinue
        }
    }
}

function Invoke-ExcelSaveAsWorkbookDialogTour {
    New-Item -ItemType Directory -Force -Path $saveAsWorkbookDialogOutDir | Out-Null
    Clear-ScreenshotTourEvidenceArtifacts -OutputDirectory $saveAsWorkbookDialogOutDir -ManifestFileName "excel_save_as_workbook_dialog_tour_manifest.json"

    $excelApp = $null
    $workbook = $null
    $excelPid = 0
    try {
        $excelApp = New-Object -ComObject Excel.Application
        $excelApp.Visible = $true
        $excelApp.DisplayAlerts = $false
        $excelApp.WindowState = -4143
        $excelApp.Top = 0
        $excelApp.Left = 0
        $excelApp.Width = 900
        $excelApp.Height = 720
        $workbook = $excelApp.Workbooks.Add()
        Start-Sleep -Milliseconds 700

        $excelHwnd = [IntPtr]$excelApp.Hwnd
        if ($excelHwnd -eq [IntPtr]::Zero) {
            Clear-ExcelTourArtifacts $saveAsWorkbookDialogOutDir "excel_save_as_workbook_dialog_tour_manifest.json"
            throw "Excel native Save As dialog tour could not resolve the Excel window handle."
        }

        [ScreenshotWin32]::GetWindowThreadProcessId($excelHwnd, [ref]$excelPid) | Out-Null
        $excelTitle = Get-WindowTitle $excelHwnd
        Set-ExcelForegroundWindow $excelHwnd $excelPid $excelTitle "Excel native Save As dialog setup"
        Assert-ForegroundWindowOwnership $excelPid $excelTitle "Excel native Save As dialog setup" $script:ForegroundWindowOwnershipFailureAction

        Open-ExcelNativeSaveAsDialog $excelPid $excelTitle
        Start-Sleep -Milliseconds 1200
        Assert-ForegroundProcessOwnership $excelPid "Excel native Save As dialog capture" "Excel"

        $dialog = Find-ExcelSaveAsWorkbookDialogWindow $excelPid $excelHwnd
        if ($null -eq $dialog) {
            Clear-ExcelTourArtifacts $saveAsWorkbookDialogOutDir "excel_save_as_workbook_dialog_tour_manifest.json"
            throw "Excel Save As dialog tour did not detect an Excel-owned NUIDialog or '#32770' Save As dialog after F12."
        }

        $captureSource = "native-dialog-window-rectangle"
        $captureBounds = [pscustomobject]@{
            Left = $dialog.Left
            Top = $dialog.Top
            Right = $dialog.Right
            Bottom = $dialog.Bottom
            Width = $dialog.Right - $dialog.Left
            Height = $dialog.Bottom - $dialog.Top
        }
        $dialogBounds = [pscustomobject]@{
            Handle = $dialog.Handle.ToString()
            ClassName = $dialog.ClassName
            Title = $dialog.Title
            Left = $dialog.Left
            Top = $dialog.Top
            Right = $dialog.Right
            Bottom = $dialog.Bottom
            Width = $dialog.Right - $dialog.Left
            Height = $dialog.Bottom - $dialog.Top
        }

        $fileName = "interactive_save_as_workbook_dialog_opened.png"
        $path = Join-Path $saveAsWorkbookDialogOutDir $fileName
        Assert-ForegroundProcessOwnership $excelPid "Excel native Save As dialog screen capture" "Excel"
        Capture-ScreenRectangle $captureBounds.Left $captureBounds.Top $captureBounds.Width $captureBounds.Height $path

        $manifestPath = Join-Path $saveAsWorkbookDialogOutDir "excel_save_as_workbook_dialog_tour_manifest.json"
        [pscustomobject]@{
            Tool = "FREEX_EXCEL_SAVE_AS_WORKBOOK_DIALOG_TOUR"
            EvidenceFamily = "native-dialog"
            EvidenceSubject = "excel"
            EvidenceApp = "Microsoft Excel"
            OutputDirectory = $saveAsWorkbookDialogOutDir
            OutputNaming = "interactive_save_as_workbook_dialog_opened.png"
            CatalogEvidenceTarget = "docs/testing/ui-test-catalog.md"
            DialogTitle = $dialog.Title
            DialogClassName = $dialog.ClassName
            EntryPath = "F12"
            CaptureStatus = "complete"
            CaptureMethod = $captureSource
            ForegroundGuard = [pscustomobject]@{
                Required = $true
                ExpectedProcessId = $excelPid
                ExpectedWindowTitle = $excelTitle
                Policy = "Seed a blank Excel workbook, then abort and clear Save As dialog evidence unless Excel owns foreground immediately before F12 and before screen capture."
            }
            Pairing = [pscustomobject]@{
                PairKeyPattern = "interactive:save-as-workbook-dialog:<State>"
                PairKey = "interactive:save-as-workbook-dialog:opened"
                CounterpartSubject = "freex"
                CounterpartTool = "FREEX_SAVE_AS_WORKBOOK_DIALOG_TOUR"
                CounterpartFileName = "freex_save_as_workbook_dialog_opened.png"
            }
            Scenario = [pscustomobject]@{
                ScenarioId = "native-dialog:save-as-workbook"
                ScenarioFileName = "save_as_workbook_dialog"
                State = "opened"
                Trigger = "Excel COM starts a blank workbook and a foreground-guarded F12 opens the native Save As dialog."
            }
            WindowBounds = $captureBounds
            DialogBounds = $dialogBounds
            Captures = @(
                [pscustomobject]@{
                    CaptureSequence = 1
                    CaptureKey = "interactive:save-as-workbook-dialog:opened"
                    PairKey = "interactive:save-as-workbook-dialog:opened"
                    EvidenceSubject = "excel"
                    CounterpartSubject = "freex"
                    CounterpartFileName = "freex_save_as_workbook_dialog_opened.png"
                    FileName = $fileName
                    Path = $path
                    Width = $captureBounds.Width
                    Height = $captureBounds.Height
                    CaptureMethod = $captureSource
                    CaptureStatus = "complete"
                }
            )
        } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

        Write-Host "Saved $path"
        Write-Host "Saved $manifestPath"
    }
    finally {
        if ($excelPid -gt 0) {
            Get-Process -Id $excelPid -ErrorAction SilentlyContinue |
                Stop-Process -Force -ErrorAction SilentlyContinue
        }
        elseif ($null -ne $excelApp) {
            $excelApp.Quit() | Out-Null
        }
    }
}

if ($AutoFilterFlyoutTour -eq "1") {
    Invoke-ExcelAutoFilterFlyoutTour
    Write-Host "Done."
    exit 0
}

if ($NumberFormatDropdownTour -eq "1") {
    Invoke-ExcelNumberFormatDropdownTour
    Write-Host "Done."
    exit 0
}

if ($HomeBordersDropdownTour -eq "1") {
    Invoke-ExcelHomeBordersDropdownTour
    Write-Host "Done."
    exit 0
}

if ($WorksheetContextMenuTour -eq "1") {
    Invoke-ExcelWorksheetContextMenuTour
    Write-Host "Done."
    exit 0
}

if ($OpenWorkbookDialogTour -eq "1") {
    Invoke-ExcelOpenWorkbookDialogTour
    Write-Host "Done."
    exit 0
}

if ($SaveAsWorkbookDialogTour -eq "1") {
    Invoke-ExcelSaveAsWorkbookDialogTour
    Write-Host "Done."
    exit 0
}

Clear-ScreenshotEvidenceArtifacts

# Launch Excel with a blank workbook to skip start screen
$exe = "C:\Program Files\Microsoft Office\root\Office16\EXCEL.EXE"
if (-not (Test-Path -LiteralPath $exe)) {
    throw "Excel executable was not found at $exe. Install Microsoft Excel or update tools\screenshot_excel.ps1 before running this capture."
}

$excelLaunchStarted = Get-Date
$excelProcess = Start-Process -FilePath $exe -ArgumentList @("/x") -PassThru
Write-Host "Launched Excel PID $($excelProcess.Id) (searching for matching class XLMAIN)"

function Resolve-LaunchedExcelMainWindow($preferredProcessId, $launchStarted) {
    $launchThreshold = $launchStarted.AddSeconds(-2)
    $candidateProcesses = Get-Process EXCEL -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Id -eq $preferredProcessId -or
            ($null -ne $_.StartTime -and $_.StartTime -ge $launchThreshold)
        } |
        Sort-Object @{ Expression = { if ($_.Id -eq $preferredProcessId) { 0 } else { 1 } } }, StartTime -Descending

    foreach ($candidateProcess in $candidateProcesses) {
        $windows = [ScreenshotWin32]::GetVisibleWindowsByProcess($candidateProcess.Id) |
            Where-Object { $_.ClassName -eq "XLMAIN" } |
            Sort-Object @{ Expression = { if ([string]::IsNullOrWhiteSpace($_.Title)) { 1 } else { 0 } } }
        foreach ($window in $windows) {
            return [pscustomobject]@{
                Handle = $window.Handle
                ProcessId = $candidateProcess.Id
            }
        }
    }

    return $null
}

$hwnd = [IntPtr]::Zero
$launchedExcelWindow = $null
for ($i = 0; $i -lt 30; $i++) {
    $launchedExcelWindow = Resolve-LaunchedExcelMainWindow $excelProcess.Id $excelLaunchStarted
    if ($null -ne $launchedExcelWindow) {
        $hwnd = [IntPtr]$launchedExcelWindow.Handle
        break
    }
    Start-Sleep -Milliseconds 500
}
if ($hwnd -eq [IntPtr]::Zero) {
    Clear-ScreenshotEvidenceArtifacts
    Get-Process -Id $excelProcess.Id -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    throw "No launched Excel window found; refusing to bind to an existing Excel workbook window."
}

Write-Host "HWND: $hwnd"
# Get PID and expected title from the launched window before foreground activation.
$wpid = [int]$launchedExcelWindow.ProcessId
Write-Host "Excel PID: $wpid"
$expectedTitle = Get-WindowTitle $hwnd

# Restore (not maximized), then move to primary monitor top-left. The width matrix loop
# controls maximized and fixed-width states.
[ScreenshotWin32]::ShowWindow($hwnd, 1) | Out-Null   # SW_RESTORE
Start-Sleep -Milliseconds 300
# SWP_NOSIZE=0x0001 - move to primary monitor origin without resizing
[ScreenshotWin32]::SetWindowPos($hwnd, [IntPtr]::Zero, 0, 0, 0, 0, 0x0001) | Out-Null
Start-Sleep -Milliseconds 300
Set-ExcelForegroundWindow $hwnd $wpid $expectedTitle "initial capture setup"

$desktop = [System.Windows.Automation.AutomationElement]::RootElement
$cond    = New-Object System.Windows.Automation.PropertyCondition(
               [System.Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$wpid)
$appEl   = $desktop.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if ($appEl -eq $null) { Write-Error "UIA element not found"; exit 1 }

function Open-ExcelBlankWorkbook {
    $blankWorkbookCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        "Blank workbook")
    $blankWorkbook = $appEl.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $blankWorkbookCondition)
    if ($null -eq $blankWorkbook) {
        Clear-ScreenshotEvidenceArtifacts
        throw "The launched Excel start surface did not expose the required 'Blank workbook' template; refusing to capture a ribbon without an active workbook."
    }

    Assert-ForegroundWindowOwnership $wpid $expectedTitle "blank workbook setup" $script:ForegroundWindowOwnershipFailureAction
    try {
        $invokePattern = [System.Windows.Automation.InvokePattern]$blankWorkbook.GetCurrentPattern(
            [System.Windows.Automation.InvokePattern]::Pattern)
        $invokePattern.Invoke()
    }
    catch {
        Clear-ScreenshotEvidenceArtifacts
        throw "Failed to create the blank Excel workbook required for ribbon capture: $($_.Exception.Message)"
    }

    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        Start-Sleep -Milliseconds 250
        $currentTitle = Get-WindowTitle $hwnd
        if (-not [string]::IsNullOrWhiteSpace($currentTitle) -and
            $currentTitle -ne $expectedTitle -and
            $currentTitle -match '.+ - Excel$') {
            return $currentTitle
        }
    }

    Clear-ScreenshotEvidenceArtifacts
    throw "Excel did not open the requested blank workbook; refusing to capture a workbook-less ribbon."
}

$expectedTitle = Open-ExcelBlankWorkbook
Set-ExcelForegroundWindow $hwnd $wpid $expectedTitle "blank workbook capture setup"

$captureH = [int]([Math]::Ceiling(300 * $scale))
Write-Host "Capture height: $captureH physical px (300 logical)"

function Find-ExcelRibbonTab($tabName) {
    $tabCond = New-Object System.Windows.Automation.PropertyCondition(
                   [System.Windows.Automation.AutomationElement]::NameProperty, $tabName)
    $matches = $appEl.FindAll([System.Windows.Automation.TreeScope]::Descendants, $tabCond)
    $windowRect = New-Object ScreenshotWin32+RECT
    [ScreenshotWin32]::GetWindowRect($hwnd, [ref]$windowRect) | Out-Null
    foreach ($match in $matches) {
        $rect = $match.Current.BoundingRectangle
        if ($match.Current.ControlType -eq [System.Windows.Automation.ControlType]::TabItem -and
            $rect.Width -gt 0 -and $rect.Height -gt 0 -and
            $rect.Top -ge $windowRect.Top -and $rect.Top -lt ($windowRect.Top + 180)) {
            return $match
        }
    }

    return $null
}

function Assert-ExcelRibbonTabSelected($tabEl, $tabName) {
    try {
        $selectionPattern = [System.Windows.Automation.SelectionItemPattern]$tabEl.GetCurrentPattern(
            [System.Windows.Automation.SelectionItemPattern]::Pattern)
        if ($null -eq $selectionPattern -or -not $selectionPattern.Current.IsSelected) {
            throw "Excel ribbon tab '$tabName' did not become selected; refusing to retain a mislabeled capture."
        }
    }
    catch {
        Clear-ScreenshotEvidenceArtifacts
        throw "Unable to verify that Excel ribbon tab '$tabName' is selected; refusing to retain a mislabeled capture. $($_.Exception.Message)"
    }
}

function Resolve-ExcelAvailableRibbonTabs {
    $available = @()
    $skipped = @()

    foreach ($tabName in $script:requestedTabNames) {
        if ($null -ne (Find-ExcelRibbonTab $tabName)) {
            $available += $tabName
        }
        else {
            $skipped += $tabName
        }
    }

    if ($available.Count -eq 0) {
        Clear-ScreenshotEvidenceArtifacts
        throw "Blocked: none of the requested Excel ribbon tabs were found. Requested tabs: $($script:requestedTabNames -join ', ')."
    }

    $script:availableTabNames = $available
    $script:skippedTabNames = $skipped
    if ($skipped.Count -gt 0) {
        Write-Warning "Skipping unavailable Excel ribbon tab(s): $($skipped -join ', ')"
    }
}

Resolve-ExcelAvailableRibbonTabs

function Screenshot-Tab($tabName, $widthSpec) {
    $tabEl = Find-ExcelRibbonTab $tabName
    if ($tabEl -eq $null) {
        Clear-ScreenshotEvidenceArtifacts
        throw "Ribbon screenshot tab '$tabName' was discovered during preflight but was not found during capture; aborting instead of writing an incomplete evidence matrix."
    }

    # Click the visible TabItem via its bounding rectangle center. Do not send Enter first: that
    # would activate the previously focused tab and can retain a mislabeled screenshot.
    $rect = $tabEl.Current.BoundingRectangle
    $cx   = [int]($rect.Left + $rect.Width  / 2)
    $cy   = [int]($rect.Top  + $rect.Height / 2)
    [System.Windows.Forms.Cursor]::Position = [System.Drawing.Point]::new($cx, $cy)
    Start-Sleep -Milliseconds 100
    Assert-ForegroundWindowOwnership $wpid $expectedTitle "ribbon tab mouse down" $script:ForegroundWindowOwnershipFailureAction
    [ScreenshotWin32]::mouse_event(2,0,0,0,0)
    Start-Sleep -Milliseconds 50
    Assert-ForegroundWindowOwnership $wpid $expectedTitle "ribbon tab mouse up" $script:ForegroundWindowOwnershipFailureAction
    [ScreenshotWin32]::mouse_event(4,0,0,0,0)
    Start-Sleep -Milliseconds 800
    Assert-ExcelRibbonTabSelected $tabEl $tabName

    $wrect = New-Object ScreenshotWin32+RECT
    [ScreenshotWin32]::GetWindowRect($hwnd, [ref]$wrect) | Out-Null
    $w = $wrect.Right - $wrect.Left
    Assert-ForegroundWindowOwnership $wpid $expectedTitle "screen capture" $script:ForegroundWindowOwnershipFailureAction

    $safe = $tabName -replace '[^a-zA-Z0-9_]','_'
    $fileName = "excel_$($widthSpec.Label)_$safe.png"
    $path = Join-Path $outDir $fileName
    Capture-ScreenRectangle $wrect.Left $wrect.Top $w $captureH $path
    $script:capturedFiles += [pscustomobject]@{
        CaptureSequence = $script:capturedFiles.Count + 1
        CaptureKey = "ribbon:$($widthSpec.Label):$safe"
        PairKey = "ribbon:$($widthSpec.Label):$safe"
        EvidenceSubject = "excel"
        CounterpartSubject = "freex"
        CounterpartFileName = "ribbon_$($widthSpec.Label)_$safe.png"
        Tab = $tabName
        TabFileName = $safe
        WidthLabel = $widthSpec.Label
        WindowLogicalWidth = $widthSpec.WindowLogicalWidth
        EvidencePurpose = $widthSpec.EvidencePurpose
        CaptureMethod = "CopyFromScreen-window-rectangle-top-band"
        CaptureStatus = "complete"
        FileName = $fileName
        Path = $path
        Width = $w
        Height = $captureH
        WindowBounds = [pscustomobject]@{
            Left = $wrect.Left
            Top = $wrect.Top
            Right = $wrect.Right
            Bottom = $wrect.Bottom
            Width = $w
            Height = $wrect.Bottom - $wrect.Top
        }
    }
    Write-Host "Saved $path ($w x $captureH)"
}

foreach ($widthSpec in $captureWidths) {
    Write-Host "Capturing Excel ribbon width '$($widthSpec.Label)' ($($widthSpec.EvidencePurpose))"
    Set-ScreenshotCaptureWindowWidth $hwnd $widthSpec $scale $windowLogicalHeight {
        param($windowHandle)
        Set-ExcelForegroundWindow $windowHandle $wpid $expectedTitle "window resize capture setup"
    }

    foreach ($tabName in $script:availableTabNames) {
        Screenshot-Tab $tabName $widthSpec
    }
}

$finalRect = New-Object ScreenshotWin32+RECT
[ScreenshotWin32]::GetWindowRect($hwnd, [ref]$finalRect) | Out-Null
Write-RibbonScreenshotEvidenceManifest `
    -ToolName "screenshot_excel.ps1" `
    -OutputDirectory $outDir `
    -WindowRect $finalRect `
    -CaptureLogicalHeight 300 `
    -CapturePhysicalHeight $captureH `
    -Widths $captureWidths `
    -Captures $script:capturedFiles `
    -ExpectedProcessId $wpid `
    -ExpectedWindowTitle $expectedTitle `
    -EvidenceSubject "excel" `
    -EvidenceApp "Microsoft Excel" `
    -OutputNaming "excel_<WidthLabel>_<RibbonTab>.png" `
    -CounterpartSubject "freex" `
    -CounterpartTool "screenshot_ribbon.ps1" `
    -CounterpartOutputNaming "ribbon_<WidthLabel>_<RibbonTab>.png" `
    -RequestedTabs $script:requestedTabNames `
    -Tabs $script:availableTabNames `
    -SkippedTabs $script:skippedTabNames `
    -SkippedCaptureStatus "skipped-unavailable-excel-tab" `
    -SkippedCaptureReason "The requested Excel ribbon tab was not exposed by this installed Excel UI/profile during preflight tab discovery." `
    -Limitations $captureLimitations `
    -InteractiveCapturePlan $interactiveCapturePlan

# Close Excel gracefully
$xlProc = Get-Process -Id $wpid -ErrorAction SilentlyContinue
if ($xlProc) { $xlProc.Kill() }
Write-Host "Done."
