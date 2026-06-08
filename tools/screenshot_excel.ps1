param(
    [string]$Widths = $env:FREEX_SS_TOUR_WIDTHS,
    [string]$AutoFilterFlyoutTour = $env:FREEX_EXCEL_AUTOFILTER_FLYOUT_TOUR,
    [string]$NumberFormatDropdownTour = $env:FREEX_EXCEL_NUMBER_FORMAT_DROPDOWN_TOUR,
    [string]$WorksheetContextMenuTour = $env:FREEX_EXCEL_WORKSHEET_CONTEXT_MENU_TOUR
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
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
public class Win32e {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, ref RECT lpRect);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern void mouse_event(int dwFlags, int dx, int dy, int dwData, int dwExtraInfo);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")]  public static extern int GetDeviceCaps(IntPtr hDC, int nIndex);
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
    public static IntPtr FindWindowByClass(string className) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hWnd, lp) => {
            if (!IsWindowVisible(hWnd)) return true;
            var cn = new StringBuilder(256);
            GetClassName(hWnd, cn, 256);
            if (cn.ToString() == className) {
                var sb = new StringBuilder(256);
                GetWindowText(hWnd, sb, 256);
                if (sb.Length > 0) { found = hWnd; return false; }
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
    public static int GetScreenDpi() {
        IntPtr dc = GetDC(IntPtr.Zero);
        int dpi = GetDeviceCaps(dc, 88);
        ReleaseDC(IntPtr.Zero, dc);
        return dpi;
    }
    public static WindowInfoE[] GetVisibleWindowsByProcess(int processId) {
        var windows = new List<WindowInfoE>();
        EnumWindows((hWnd, lp) => {
            if (!IsWindowVisible(hWnd)) return true;
            uint wPid;
            GetWindowThreadProcessId(hWnd, out wPid);
            if (wPid != (uint)processId) return true;
            var title = new StringBuilder(512);
            GetWindowText(hWnd, title, title.Capacity);
            var className = new StringBuilder(256);
            GetClassName(hWnd, className, className.Capacity);
            var rect = new RECT();
            if (!GetWindowRect(hWnd, ref rect)) return true;
            if (rect.Right <= rect.Left || rect.Bottom <= rect.Top) return true;
            windows.Add(new WindowInfoE {
                Handle = hWnd,
                ProcessId = (int)wPid,
                Title = title.ToString(),
                ClassName = className.ToString(),
                Left = rect.Left,
                Top = rect.Top,
                Right = rect.Right,
                Bottom = rect.Bottom
            });
            return true;
        }, IntPtr.Zero);
        return windows.ToArray();
    }
}
public class WindowInfoE {
    public IntPtr Handle;
    public int ProcessId;
    public string Title;
    public string ClassName;
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}
"@

[Win32e]::SetProcessDPIAware() | Out-Null

$outDir = Join-Path $PSScriptRoot "screenshots_excel"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$autoFilterFlyoutOutDir = Join-Path $outDir "autofilter-flyout-tour"
$numberFormatDropdownOutDir = Join-Path $outDir "home-number-format-dropdown-tour"
$worksheetContextMenuOutDir = Join-Path $outDir "worksheet-context-menu-tour"
function Clear-ScreenshotEvidenceArtifacts {
    Get-ChildItem $outDir -Filter "*.png" -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $outDir "screenshot_manifest.json") -Force -ErrorAction SilentlyContinue
}

function Clear-AutoFilterFlyoutEvidenceArtifacts {
    if (Test-Path -LiteralPath $autoFilterFlyoutOutDir -PathType Container) {
        Get-ChildItem $autoFilterFlyoutOutDir -Filter "*.png" -ErrorAction SilentlyContinue |
            Remove-Item -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath (Join-Path $autoFilterFlyoutOutDir "excel_autofilter_flyout_tour_manifest.json") -Force -ErrorAction SilentlyContinue
    }
}

function Clear-NumberFormatDropdownEvidenceArtifacts {
    if (Test-Path -LiteralPath $numberFormatDropdownOutDir -PathType Container) {
        Get-ChildItem $numberFormatDropdownOutDir -Filter "*.png" -ErrorAction SilentlyContinue |
            Remove-Item -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath (Join-Path $numberFormatDropdownOutDir "excel_home_number_format_dropdown_tour_manifest.json") -Force -ErrorAction SilentlyContinue
    }
}

function Clear-WorksheetContextMenuEvidenceArtifacts {
    if (Test-Path -LiteralPath $worksheetContextMenuOutDir -PathType Container) {
        Get-ChildItem $worksheetContextMenuOutDir -Filter "*.png" -ErrorAction SilentlyContinue |
            Remove-Item -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath (Join-Path $worksheetContextMenuOutDir "excel_worksheet_context_menu_tour_manifest.json") -Force -ErrorAction SilentlyContinue
    }
}

$tabNames = @("Home", "Insert", "Draw", "Page Layout", "Formulas", "Data", "Review", "View", "Help")
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
        ScenarioId = "context-menu:worksheet-cell"
        ScenarioFileName = "worksheet_cell_context_menu"
        Priority = 3
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
        Priority = 4
        EvidenceFamily = "native-dialog"
        EvidenceSubject = "excel"
        CaptureStatus = "planned-separate-foreground-guarded-capture"
        CaptureOutputNaming = "interactive_<ScenarioFileName>_<State>.png"
        PairKeyPattern = "interactive:open-workbook-dialog:<State>"
        Trigger = "Open File > Open > Browse or the equivalent guarded keyboard path that reaches Excel's native Open dialog."
        CaptureRequirement = "Capture the active popup/dialog/menu bounds, not the owner-window ribbon band."
        ForegroundGuard = "Treat the native dialog as the expected foreground target after the final launch input; abort if another process or unrelated dialog owns foreground focus."
        CounterpartSubject = "freex"
    }
)
$windowLogicalHeight = 768

function Get-RibbonWidthEvidencePurpose($windowLogicalWidth) {
    if ($null -eq $windowLogicalWidth) {
        return "Maximized baseline before resize pressure."
    }

    if ($windowLogicalWidth -ge 1100) {
        return "Wide ribbon breakpoint before most command groups collapse."
    }

    if ($windowLogicalWidth -ge 900) {
        return "Medium ribbon breakpoint where grouped commands begin to compress."
    }

    return "Narrow ribbon breakpoint for overflow and compact command layouts."
}

$defaultCaptureWidths = @(
    [pscustomobject]@{ Label = "max"; WindowLogicalWidth = $null; EvidencePurpose = (Get-RibbonWidthEvidencePurpose $null) },
    [pscustomobject]@{ Label = "1100"; WindowLogicalWidth = 1100.0; EvidencePurpose = (Get-RibbonWidthEvidencePurpose 1100.0) },
    [pscustomobject]@{ Label = "900"; WindowLogicalWidth = 900.0; EvidencePurpose = (Get-RibbonWidthEvidencePurpose 900.0) },
    [pscustomobject]@{ Label = "750"; WindowLogicalWidth = 750.0; EvidencePurpose = (Get-RibbonWidthEvidencePurpose 750.0) }
)

function Resolve-CaptureWidths($requestedWidths) {
    if ([string]::IsNullOrWhiteSpace($requestedWidths)) {
        return $defaultCaptureWidths
    }

    $entries = $requestedWidths.Split(',')
    $widths = @()
    $invalid = @()
    $emptyPositions = @()
    for ($index = 0; $index -lt $entries.Length; $index++) {
        $value = $entries[$index].Trim()
        if ($value.Length -eq 0) {
            $emptyPositions += ($index + 1)
            continue
        }

        if ($value -ieq "max") {
            $widths += $defaultCaptureWidths[0]
            continue
        }

        $parsed = 0.0
        $canParse = [double]::TryParse(
            $value,
            [System.Globalization.NumberStyles]::Float,
            [System.Globalization.CultureInfo]::InvariantCulture,
            [ref]$parsed)
        if (-not $canParse -or [double]::IsNaN($parsed) -or [double]::IsInfinity($parsed) -or $parsed -le 0) {
            $invalid += $value
            continue
        }

        $label = $parsed.ToString([System.Globalization.CultureInfo]::InvariantCulture)
        $knownWidth = $defaultCaptureWidths | Where-Object { $_.Label -eq $label } | Select-Object -First 1
        if ($null -ne $knownWidth) {
            $widths += $knownWidth
            continue
        }

        $widths += [pscustomobject]@{
            Label = $label
            WindowLogicalWidth = $parsed
            EvidencePurpose = (Get-RibbonWidthEvidencePurpose $parsed)
        }
    }

    if ($emptyPositions.Count -gt 0) {
        throw "Ribbon screenshot width list contains empty entry at position(s): $($emptyPositions -join ', ')."
    }

    if ($invalid.Count -gt 0) {
        throw "Ribbon screenshot width list contains invalid width(s): $($invalid -join ', '). Use positive finite numbers or max."
    }

    return $widths
}

$captureWidths = @(Resolve-CaptureWidths $Widths)

$dpi   = [Win32e]::GetScreenDpi()
$scale = $dpi / 96.0
Write-Host "Screen DPI: $dpi  Scale: $scale"

function Get-WindowTitle($windowHandle) {
    $title = New-Object System.Text.StringBuilder 512
    [Win32e]::GetWindowText($windowHandle, $title, $title.Capacity) | Out-Null
    return $title.ToString()
}

function Assert-ForegroundWindowOwnership($expectedPid, $expectedTitle, $operation = "capture") {
    $foreground = [Win32e]::GetForegroundWindow()
    if ($foreground -eq [IntPtr]::Zero) {
        Clear-ScreenshotEvidenceArtifacts
        throw "Blocked: no foreground window before $operation."
    }

    $actualPid = 0
    [Win32e]::GetWindowThreadProcessId($foreground, [ref]$actualPid) | Out-Null
    $title = New-Object System.Text.StringBuilder 512
    [Win32e]::GetWindowText($foreground, $title, $title.Capacity) | Out-Null
    $actualTitle = $title.ToString()
    if ($actualPid -ne $expectedPid -or $actualTitle -ne $expectedTitle) {
        Clear-ScreenshotEvidenceArtifacts
        throw "Blocked: foreground window '$actualTitle' (PID $actualPid) does not match expected '$expectedTitle' (PID $expectedPid) before $operation."
    }
}

function Assert-ForegroundProcessOwnership($expectedPid, $operation = "capture") {
    $foreground = [Win32e]::GetForegroundWindow()
    if ($foreground -eq [IntPtr]::Zero) {
        throw "Blocked: no foreground window before $operation."
    }

    $actualPid = 0
    [Win32e]::GetWindowThreadProcessId($foreground, [ref]$actualPid) | Out-Null
    if ($actualPid -ne $expectedPid) {
        $title = New-Object System.Text.StringBuilder 512
        [Win32e]::GetWindowText($foreground, $title, $title.Capacity) | Out-Null
        throw "Blocked: foreground window '$($title.ToString())' (PID $actualPid) does not belong to expected Excel PID $expectedPid before $operation."
    }
}

function Set-ExcelForegroundWindow($excelHwnd, $excelPid, $expectedTitle, $operation) {
    $shell = $null
    try {
        $shell = New-Object -ComObject WScript.Shell
    }
    catch {
        $shell = $null
    }

    for ($attempt = 0; $attempt -lt 10; $attempt++) {
        [Win32e]::ShowWindow($excelHwnd, 1) | Out-Null
        [Win32e]::SetWindowPos($excelHwnd, [IntPtr](-1), 0, 0, 0, 0, 0x0043) | Out-Null
        [Win32e]::SetForegroundWindow($excelHwnd) | Out-Null
        if ($null -ne $shell) {
            $shell.AppActivate([int]$excelPid) | Out-Null
        }

        Start-Sleep -Milliseconds 250

        $foreground = [Win32e]::GetForegroundWindow()
        if ($foreground -eq [IntPtr]::Zero) {
            continue
        }

        $actualPid = 0
        [Win32e]::GetWindowThreadProcessId($foreground, [ref]$actualPid) | Out-Null
        $title = New-Object System.Text.StringBuilder 512
        [Win32e]::GetWindowText($foreground, $title, $title.Capacity) | Out-Null
        if ($actualPid -eq $excelPid -and $title.ToString() -eq $expectedTitle) {
            return
        }
    }

    Assert-ForegroundWindowOwnership $excelPid $expectedTitle $operation
}

function Find-ExcelPopupWindow($expectedPid, $ownerWindowHandle, $minimumWidth, $minimumHeight) {
    $windows = [Win32e]::GetVisibleWindowsByProcess($expectedPid) |
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

function Capture-ScreenRectangle($left, $top, $width, $height, $path) {
    $bmp = New-Object System.Drawing.Bitmap($width, $height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($left, $top, 0, 0, [System.Drawing.Size]::new($width, $height))
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

function Click-ExcelAutoFilterHeaderDropdown($excelApp, $worksheet, $headerAddress, $expectedPid, $expectedTitle) {
    $header = $worksheet.Range($headerAddress)
    $window = $excelApp.ActiveWindow
    $left = $window.PointsToScreenPixelsX($header.Left)
    $top = $window.PointsToScreenPixelsY($header.Top)
    $pointToScreenScale = 2.0
    $clickX = [int]($left + ($header.Width * $pointToScreenScale) - 12)
    $clickY = [int]($top + ($header.Height * $pointToScreenScale / 2.0))

    [Win32e]::SetCursorPos($clickX, $clickY) | Out-Null
    Start-Sleep -Milliseconds 100
    Assert-ForegroundWindowOwnership $expectedPid $expectedTitle "Excel AutoFilter dropdown mouse down"
    [Win32e]::mouse_event(2, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 60
    Assert-ForegroundWindowOwnership $expectedPid $expectedTitle "Excel AutoFilter dropdown mouse up"
    [Win32e]::mouse_event(4, 0, 0, 0, 0)
}

function Expand-ExcelNumberFormatDropdown($expectedPid, $excelElement, $expectedTitle) {
    $comboCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        "NumberFormatGallery")
    $combo = $excelElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $comboCondition)
    if ($null -eq $combo) {
        Clear-NumberFormatDropdownEvidenceArtifacts
        throw "Excel Home number-format dropdown tour could not find the NumberFormatGallery ComboBox."
    }

    $pattern = $null
    if (-not $combo.TryGetCurrentPattern(
            [System.Windows.Automation.ExpandCollapsePattern]::Pattern,
            [ref]$pattern)) {
        Clear-NumberFormatDropdownEvidenceArtifacts
        throw "Excel Home number-format dropdown tour could not expand NumberFormatGallery through UI Automation."
    }

    Assert-ForegroundWindowOwnership $expectedPid $expectedTitle "Excel Number Format dropdown expand"
    $pattern.Expand()
}

function Open-ExcelWorksheetContextMenu($expectedPid, $expectedTitle) {
    Assert-ForegroundWindowOwnership $expectedPid $expectedTitle "Excel worksheet context menu keyboard input"
    [System.Windows.Forms.SendKeys]::SendWait("+{F10}")
}

function Invoke-ExcelAutoFilterFlyoutTour {
    New-Item -ItemType Directory -Force -Path $autoFilterFlyoutOutDir | Out-Null
    Clear-AutoFilterFlyoutEvidenceArtifacts

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
            Clear-AutoFilterFlyoutEvidenceArtifacts
            throw "Excel AutoFilter flyout tour could not resolve the Excel window handle."
        }

        $excelPid = 0
        [Win32e]::GetWindowThreadProcessId($excelHwnd, [ref]$excelPid) | Out-Null
        $excelTitle = Get-WindowTitle $excelHwnd
        Set-ExcelForegroundWindow $excelHwnd $excelPid $excelTitle "Excel AutoFilter flyout setup"
        Assert-ForegroundWindowOwnership $excelPid $excelTitle "Excel AutoFilter flyout setup"

        Click-ExcelAutoFilterHeaderDropdown $excelApp $worksheet "A1" $excelPid $excelTitle
        Start-Sleep -Milliseconds 900
        Assert-ForegroundProcessOwnership $excelPid "Excel AutoFilter flyout capture"

        $popup = Find-ExcelAutoFilterPopupWindow $excelPid $excelHwnd
        if ($null -eq $popup) {
            Clear-AutoFilterFlyoutEvidenceArtifacts
            throw "Excel AutoFilter flyout tour did not detect a foreground Excel popup window after opening the header dropdown."
        }

        $windowRect = New-Object Win32e+RECT
        [Win32e]::GetWindowRect($excelHwnd, [ref]$windowRect) | Out-Null
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
        Assert-ForegroundProcessOwnership $excelPid "Excel AutoFilter flyout screen capture"
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
    Clear-NumberFormatDropdownEvidenceArtifacts

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
            Clear-NumberFormatDropdownEvidenceArtifacts
            throw "Excel Home number-format dropdown tour could not resolve the Excel window handle."
        }

        $excelPid = 0
        [Win32e]::GetWindowThreadProcessId($excelHwnd, [ref]$excelPid) | Out-Null
        $excelTitle = Get-WindowTitle $excelHwnd
        Set-ExcelForegroundWindow $excelHwnd $excelPid $excelTitle "Excel Number Format dropdown setup"
        Assert-ForegroundWindowOwnership $excelPid $excelTitle "Excel Number Format dropdown setup"

        $desktop = [System.Windows.Automation.AutomationElement]::RootElement
        $processCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
            [int]$excelPid)
        $excelElement = $desktop.FindFirst(
            [System.Windows.Automation.TreeScope]::Children,
            $processCondition)
        if ($null -eq $excelElement) {
            Clear-NumberFormatDropdownEvidenceArtifacts
            throw "Excel Home number-format dropdown tour could not find the Excel UI Automation root."
        }

        Expand-ExcelNumberFormatDropdown $excelPid $excelElement $excelTitle
        Start-Sleep -Milliseconds 900
        Assert-ForegroundProcessOwnership $excelPid "Excel Number Format dropdown capture"

        $popup = Find-ExcelPopupWindow $excelPid $excelHwnd 120 120
        if ($null -eq $popup) {
            Clear-NumberFormatDropdownEvidenceArtifacts
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
        Assert-ForegroundProcessOwnership $excelPid "Excel Number Format dropdown screen capture"
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

function Invoke-ExcelWorksheetContextMenuTour {
    New-Item -ItemType Directory -Force -Path $worksheetContextMenuOutDir | Out-Null
    Clear-WorksheetContextMenuEvidenceArtifacts

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
            Clear-WorksheetContextMenuEvidenceArtifacts
            throw "Excel worksheet context menu tour could not resolve the Excel window handle."
        }

        $excelPid = 0
        [Win32e]::GetWindowThreadProcessId($excelHwnd, [ref]$excelPid) | Out-Null
        $excelTitle = Get-WindowTitle $excelHwnd
        Set-ExcelForegroundWindow $excelHwnd $excelPid $excelTitle "Excel worksheet context menu setup"
        Assert-ForegroundWindowOwnership $excelPid $excelTitle "Excel worksheet context menu setup"

        Open-ExcelWorksheetContextMenu $excelPid $excelTitle
        Start-Sleep -Milliseconds 900
        Assert-ForegroundProcessOwnership $excelPid "Excel worksheet context menu capture"

        $popup = Find-ExcelPopupWindow $excelPid $excelHwnd 120 120
        if ($null -eq $popup) {
            Clear-WorksheetContextMenuEvidenceArtifacts
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
        Assert-ForegroundProcessOwnership $excelPid "Excel worksheet context menu screen capture"
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

if ($WorksheetContextMenuTour -eq "1") {
    Invoke-ExcelWorksheetContextMenuTour
    Write-Host "Done."
    exit 0
}

Clear-ScreenshotEvidenceArtifacts

# Launch Excel with a blank workbook to skip start screen
$exe = "C:\Program Files\Microsoft Office\root\Office16\EXCEL.EXE"
if (-not (Test-Path -LiteralPath $exe)) {
    throw "Excel executable was not found at $exe. Install Microsoft Excel or update tools\screenshot_excel.ps1 before running this capture."
}

Start-Process -FilePath $exe -ArgumentList "/e"
Write-Host "Launched Excel (searching by class XLMAIN)"

Start-Sleep -Seconds 8

$hwnd = [IntPtr]::Zero
for ($i = 0; $i -lt 30; $i++) {
    $hwnd = [Win32e]::FindWindowByClass("XLMAIN")
    if ($hwnd -ne [IntPtr]::Zero) { break }
    Start-Sleep -Milliseconds 500
}
if ($hwnd -eq [IntPtr]::Zero) { Write-Error "No Excel window found"; exit 1 }

Write-Host "HWND: $hwnd"
# Restore (not maximized), then move to primary monitor top-left. The width matrix loop
# controls maximized and fixed-width states.
[Win32e]::ShowWindow($hwnd, 1) | Out-Null   # SW_RESTORE
Start-Sleep -Milliseconds 300
# SWP_NOSIZE=0x0001 - move to primary monitor origin without resizing
[Win32e]::SetWindowPos($hwnd, [IntPtr]::Zero, 0, 0, 0, 0, 0x0001) | Out-Null
Start-Sleep -Milliseconds 300
[Win32e]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Seconds 2

# Get PID from window for UIA lookup
$wpid = 0
[Win32e]::GetWindowThreadProcessId($hwnd, [ref]$wpid) | Out-Null
Write-Host "Excel PID: $wpid"

$desktop = [System.Windows.Automation.AutomationElement]::RootElement
$cond    = New-Object System.Windows.Automation.PropertyCondition(
               [System.Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$wpid)
$appEl   = $desktop.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if ($appEl -eq $null) { Write-Error "UIA element not found"; exit 1 }

$captureH = [int]([Math]::Ceiling(300 * $scale))
Write-Host "Capture height: $captureH physical px (300 logical)"

$expectedTitle = Get-WindowTitle $hwnd

function Set-CaptureWindowWidth($windowHandle, $widthSpec) {
    if ($null -eq $widthSpec.WindowLogicalWidth) {
        [Win32e]::ShowWindow($windowHandle, 3) | Out-Null
        [Win32e]::SetForegroundWindow($windowHandle) | Out-Null
        Start-Sleep -Milliseconds 1200
        Assert-ForegroundWindowOwnership $wpid $expectedTitle "window resize capture setup"
        return
    }

    $physicalWidth = [int]([Math]::Ceiling([double]$widthSpec.WindowLogicalWidth * $scale))
    $physicalHeight = [int]([Math]::Ceiling($windowLogicalHeight * $scale))
    [Win32e]::ShowWindow($windowHandle, 1) | Out-Null
    Start-Sleep -Milliseconds 200
    [Win32e]::SetWindowPos($windowHandle, [IntPtr]::Zero, 0, 0, $physicalWidth, $physicalHeight, 0) | Out-Null
    [Win32e]::SetForegroundWindow($windowHandle) | Out-Null
    Start-Sleep -Milliseconds 700
    Assert-ForegroundWindowOwnership $wpid $expectedTitle "window resize capture setup"
}

function Write-ScreenshotEvidenceManifest($toolName, $scriptOutDir, $windowRect, $captureLogicalHeight, $capturePhysicalHeight, $widths, $files, $expectedPid, $expectedTitle) {
    $manifestPath = Join-Path $scriptOutDir "screenshot_manifest.json"
    $plannedCaptureCount = $tabNames.Count * $widths.Count
    if ($files.Count -ne $plannedCaptureCount) {
        Clear-ScreenshotEvidenceArtifacts
        throw "Blocked: captured $($files.Count) screenshot(s), expected $plannedCaptureCount. Discarded incomplete evidence matrix."
    }

    [pscustomobject]@{
        Tool = $toolName
        EvidenceFamily = "ribbon"
        EvidenceSubject = "excel"
        EvidenceApp = "Microsoft Excel"
        OutputDirectory = $scriptOutDir
        OutputNaming = "excel_<WidthLabel>_<RibbonTab>.png"
        CatalogEvidenceTarget = "docs/testing/ui-test-catalog.md"
        WidthSource = "RibbonScreenshotTourPlanner.DefaultWidths"
        PlannedCaptureCount = $plannedCaptureCount
        ActualCaptureCount = $files.Count
        CaptureStatus = "complete"
        CaptureMethod = "CopyFromScreen-window-rectangle-top-band"
        ForegroundGuard = [pscustomobject]@{
            Required = $true
            ExpectedProcessId = $expectedPid
            ExpectedWindowTitle = $expectedTitle
            Policy = "Abort and clear current PNG/manifest evidence unless the expected process and window title own the foreground window immediately before global input and screen capture."
        }
        Pairing = [pscustomobject]@{
            PairKeyPattern = "ribbon:<WidthLabel>:<TabFileName>"
            CounterpartSubject = "freex"
            CounterpartTool = "screenshot_ribbon.ps1"
            CounterpartOutputNaming = "ribbon_<WidthLabel>_<RibbonTab>.png"
        }
        WindowBounds = [pscustomobject]@{
            Left = $windowRect.Left
            Top = $windowRect.Top
            Right = $windowRect.Right
            Bottom = $windowRect.Bottom
            Width = $windowRect.Right - $windowRect.Left
            Height = $windowRect.Bottom - $windowRect.Top
        }
        CaptureLogicalHeight = $captureLogicalHeight
        CapturePhysicalHeight = $capturePhysicalHeight
        Widths = $widths
        Tabs = $tabNames
        Limitations = $captureLimitations
        InteractiveCapturePlan = $interactiveCapturePlan
        Captures = $files
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    Write-Host "Saved $manifestPath"
}

function Screenshot-Tab($tabName, $widthSpec) {
    $tabCond = New-Object System.Windows.Automation.PropertyCondition(
                   [System.Windows.Automation.AutomationElement]::NameProperty, $tabName)
    $tabEl   = $appEl.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $tabCond)
    if ($tabEl -eq $null) {
        Clear-ScreenshotEvidenceArtifacts
        throw "Ribbon screenshot tab '$tabName' was not found; aborting instead of writing an incomplete evidence matrix."
    }

    # Click the tab via its bounding rectangle center (UIA patterns unsupported in Excel ribbon)
    $rect = $tabEl.Current.BoundingRectangle
    $cx   = [int]($rect.Left + $rect.Width  / 2)
    $cy   = [int]($rect.Top  + $rect.Height / 2)
    [System.Windows.Forms.Cursor]::Position = [System.Drawing.Point]::new($cx, $cy)
    Start-Sleep -Milliseconds 100
    Assert-ForegroundWindowOwnership $wpid $expectedTitle "ribbon tab keyboard input"
    [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
    # Also try a real mouse click via mouse_event
    Add-Type -TypeDefinition 'using System.Runtime.InteropServices; public class Clicker { [DllImport("user32.dll")] public static extern void mouse_event(int f,int x,int y,int c,int e); }' -ErrorAction SilentlyContinue
    Assert-ForegroundWindowOwnership $wpid $expectedTitle "ribbon tab mouse down"
    [Clicker]::mouse_event(2,0,0,0,0)
    Start-Sleep -Milliseconds 50
    Assert-ForegroundWindowOwnership $wpid $expectedTitle "ribbon tab mouse up"
    [Clicker]::mouse_event(4,0,0,0,0)
    Start-Sleep -Milliseconds 800

    $wrect = New-Object Win32e+RECT
    [Win32e]::GetWindowRect($hwnd, [ref]$wrect) | Out-Null
    $w = $wrect.Right - $wrect.Left
    Assert-ForegroundWindowOwnership $wpid $expectedTitle "screen capture"

    $bmp = New-Object System.Drawing.Bitmap($w, $captureH)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($wrect.Left, $wrect.Top, 0, 0, [System.Drawing.Size]::new($w, $captureH))
    $g.Dispose()

    $safe = $tabName -replace '[^a-zA-Z0-9_]','_'
    $fileName = "excel_$($widthSpec.Label)_$safe.png"
    $path = Join-Path $outDir $fileName
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
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
    Set-CaptureWindowWidth $hwnd $widthSpec

    foreach ($tabName in $tabNames) {
        Screenshot-Tab $tabName $widthSpec
    }
}

$finalRect = New-Object Win32e+RECT
[Win32e]::GetWindowRect($hwnd, [ref]$finalRect) | Out-Null
Write-ScreenshotEvidenceManifest "screenshot_excel.ps1" $outDir $finalRect 300 $captureH $captureWidths $script:capturedFiles $wpid $expectedTitle

# Close Excel gracefully
$xlProc = Get-Process -Id $wpid -ErrorAction SilentlyContinue
if ($xlProc) { $xlProc.Kill() }
Write-Host "Done."
