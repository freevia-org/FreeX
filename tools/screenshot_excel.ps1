param(
    [string]$Widths = $env:FREEX_SS_TOUR_WIDTHS
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
}
"@

$outDir = Join-Path $PSScriptRoot "screenshots_excel"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
Get-ChildItem $outDir -Filter "*.png" | Remove-Item -Force
$tabNames = @("Home", "Insert", "Draw", "Page Layout", "Formulas", "Data", "Review", "View", "Help")
$script:capturedFiles = @()
$captureLimitations = @(
    "Ribbon tab captures cover the top window band only.",
    "Transient popups, dropdowns, native dialogs, and context menus require separate guarded captures.",
    "Global input is blocked unless the expected process and window title own the foreground window."
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

function Get-WindowTitle($windowHandle) {
    $title = New-Object System.Text.StringBuilder 512
    [Win32e]::GetWindowText($windowHandle, $title, $title.Capacity) | Out-Null
    return $title.ToString()
}

function Assert-ForegroundWindowOwnership($expectedPid, $expectedTitle) {
    $foreground = [Win32e]::GetForegroundWindow()
    if ($foreground -eq [IntPtr]::Zero) {
        Get-ChildItem $outDir -Filter "*.png" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
        throw "Blocked: no foreground window before global input."
    }

    $actualPid = 0
    [Win32e]::GetWindowThreadProcessId($foreground, [ref]$actualPid) | Out-Null
    $title = New-Object System.Text.StringBuilder 512
    [Win32e]::GetWindowText($foreground, $title, $title.Capacity) | Out-Null
    $actualTitle = $title.ToString()
    if ($actualPid -ne $expectedPid -or $actualTitle -ne $expectedTitle) {
        Get-ChildItem $outDir -Filter "*.png" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
        throw "Blocked: foreground window '$actualTitle' (PID $actualPid) does not match expected '$expectedTitle' (PID $expectedPid)."
    }
}

$expectedTitle = Get-WindowTitle $hwnd

function Set-CaptureWindowWidth($windowHandle, $widthSpec) {
    if ($null -eq $widthSpec.WindowLogicalWidth) {
        [Win32e]::ShowWindow($windowHandle, 3) | Out-Null
        [Win32e]::SetForegroundWindow($windowHandle) | Out-Null
        Start-Sleep -Milliseconds 1200
        Assert-ForegroundWindowOwnership $wpid $expectedTitle
        return
    }

    $physicalWidth = [int]([Math]::Ceiling([double]$widthSpec.WindowLogicalWidth * $scale))
    $physicalHeight = [int]([Math]::Ceiling($windowLogicalHeight * $scale))
    [Win32e]::ShowWindow($windowHandle, 1) | Out-Null
    Start-Sleep -Milliseconds 200
    [Win32e]::SetWindowPos($windowHandle, [IntPtr]::Zero, 0, 0, $physicalWidth, $physicalHeight, 0) | Out-Null
    [Win32e]::SetForegroundWindow($windowHandle) | Out-Null
    Start-Sleep -Milliseconds 700
    Assert-ForegroundWindowOwnership $wpid $expectedTitle
}

function Write-ScreenshotEvidenceManifest($toolName, $scriptOutDir, $windowRect, $captureLogicalHeight, $capturePhysicalHeight, $widths, $files) {
    $manifestPath = Join-Path $scriptOutDir "screenshot_manifest.json"
    [pscustomobject]@{
        Tool = $toolName
        EvidenceFamily = "ribbon"
        EvidenceSubject = "excel"
        EvidenceApp = "Microsoft Excel"
        OutputDirectory = $scriptOutDir
        OutputNaming = "excel_<WidthLabel>_<RibbonTab>.png"
        CatalogEvidenceTarget = "docs/UI_TEST_CATALOG.md"
        WidthSource = "RibbonScreenshotTourPlanner.DefaultWidths"
        PlannedCaptureCount = $tabNames.Count * $widths.Count
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
        Captures = $files
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    Write-Host "Saved $manifestPath"
}

function Screenshot-Tab($tabName, $widthSpec) {
    $tabCond = New-Object System.Windows.Automation.PropertyCondition(
                   [System.Windows.Automation.AutomationElement]::NameProperty, $tabName)
    $tabEl   = $appEl.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $tabCond)
    if ($tabEl -eq $null) { Write-Warning "Tab '$tabName' not found"; return }

    # Click the tab via its bounding rectangle center (UIA patterns unsupported in Excel ribbon)
    $rect = $tabEl.Current.BoundingRectangle
    $cx   = [int]($rect.Left + $rect.Width  / 2)
    $cy   = [int]($rect.Top  + $rect.Height / 2)
    [System.Windows.Forms.Cursor]::Position = [System.Drawing.Point]::new($cx, $cy)
    Start-Sleep -Milliseconds 100
    Assert-ForegroundWindowOwnership $wpid $expectedTitle
    [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
    # Also try a real mouse click via mouse_event
    Add-Type -TypeDefinition 'using System.Runtime.InteropServices; public class Clicker { [DllImport("user32.dll")] public static extern void mouse_event(int f,int x,int y,int c,int e); }' -ErrorAction SilentlyContinue
    Assert-ForegroundWindowOwnership $wpid $expectedTitle
    [Clicker]::mouse_event(2,0,0,0,0)
    Start-Sleep -Milliseconds 50
    Assert-ForegroundWindowOwnership $wpid $expectedTitle
    [Clicker]::mouse_event(4,0,0,0,0)
    Start-Sleep -Milliseconds 800

    $wrect = New-Object Win32e+RECT
    [Win32e]::GetWindowRect($hwnd, [ref]$wrect) | Out-Null
    $w = $wrect.Right - $wrect.Left

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
        PairKey = "ribbon:$($widthSpec.Label):$safe"
        EvidenceSubject = "excel"
        CounterpartSubject = "freex"
        CounterpartFileName = "ribbon_$($widthSpec.Label)_$safe.png"
        Tab = $tabName
        TabFileName = $safe
        WidthLabel = $widthSpec.Label
        WindowLogicalWidth = $widthSpec.WindowLogicalWidth
        EvidencePurpose = $widthSpec.EvidencePurpose
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
Write-ScreenshotEvidenceManifest "screenshot_excel.ps1" $outDir $finalRect 300 $captureH $captureWidths $script:capturedFiles

# Close Excel gracefully
$xlProc = Get-Process -Id $wpid -ErrorAction SilentlyContinue
if ($xlProc) { $xlProc.Kill() }
Write-Host "Done."
