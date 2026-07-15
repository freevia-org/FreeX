param(
    [string]$Widths = $env:FREEX_SS_TOUR_WIDTHS,
    [string]$ExePath = $env:FREEX_RIBBON_EXE_PATH,
    [string]$OpenWorkbookDialogTour = $env:FREEX_OPEN_WORKBOOK_DIALOG_TOUR,
    [string]$SaveAsWorkbookDialogTour = $env:FREEX_SAVE_AS_WORKBOOK_DIALOG_TOUR
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
trap {
    if ($proc -and -not $proc.HasExited) {
        $proc.Kill()
    }

    throw $_
}
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class Win32c {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr SetActiveWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr SetFocus(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, ref RECT lpRect);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")]  public static extern int GetDeviceCaps(IntPtr hDC, int nIndex);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
    public static IntPtr FindWindowByPid(int pid) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hWnd, lp) => {
            uint wPid;
            GetWindowThreadProcessId(hWnd, out wPid);
            if (wPid == (uint)pid && IsWindowVisible(hWnd)) {
                var sb = new StringBuilder(256);
                GetWindowText(hWnd, sb, 256);
                if (sb.Length > 0) { found = hWnd; return false; }
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
    public static WindowInfoC[] GetVisibleWindowsByProcess(int processId) {
        var windows = new System.Collections.Generic.List<WindowInfoC>();
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
            windows.Add(new WindowInfoC {
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
    public static int GetScreenDpi() {
        IntPtr dc = GetDC(IntPtr.Zero);
        int dpi = GetDeviceCaps(dc, 88); // LOGPIXELSX
        ReleaseDC(IntPtr.Zero, dc);
        return dpi;
    }
}
public class WindowInfoC {
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

$repoRoot = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $PSScriptRoot "screenshots"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
. (Join-Path $PSScriptRoot "ScreenshotCaptureSupport.ps1")
$openWorkbookDialogOutDir = Join-Path $outDir "open-workbook-dialog-tour"
$saveAsWorkbookDialogOutDir = Join-Path $outDir "save-as-workbook-dialog-tour"
function Clear-OpenWorkbookDialogEvidenceArtifacts {
    if (Test-Path -LiteralPath $openWorkbookDialogOutDir -PathType Container) {
        Get-ChildItem $openWorkbookDialogOutDir -Filter "*.png" -ErrorAction SilentlyContinue |
            Remove-Item -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath (Join-Path $openWorkbookDialogOutDir "freex_open_workbook_dialog_tour_manifest.json") -Force -ErrorAction SilentlyContinue
    }
}

function Clear-SaveAsWorkbookDialogEvidenceArtifacts {
    if (Test-Path -LiteralPath $saveAsWorkbookDialogOutDir -PathType Container) {
        Get-ChildItem $saveAsWorkbookDialogOutDir -Filter "*.png" -ErrorAction SilentlyContinue |
            Remove-Item -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath (Join-Path $saveAsWorkbookDialogOutDir "freex_save_as_workbook_dialog_tour_manifest.json") -Force -ErrorAction SilentlyContinue
    }
}

if ($OpenWorkbookDialogTour -ne "1" -and $SaveAsWorkbookDialogTour -ne "1") {
    Clear-ScreenshotEvidenceArtifacts
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
        EvidenceSubject = "freex"
        CaptureStatus = "planned-separate-foreground-guarded-capture"
        CaptureOutputNaming = "interactive_<ScenarioFileName>_<State>.png"
        PairKeyPattern = "interactive:table-autofilter-dropdown:<State>"
        Trigger = "Create or open a sample table/range with values and blanks, enable AutoFilter, then open the active header dropdown with Alt+Down or the header arrow."
        CaptureRequirement = "Capture the active popup/dialog/menu bounds, not the owner-window ribbon band."
        ForegroundGuard = "Re-check FreeX foreground ownership before each setup input and before the dropdown-opening input; discard captures when the expected FreeX window or popup is not foreground-owned."
        CounterpartSubject = "excel"
    },
    [pscustomobject]@{
        ScenarioId = "dropdown:home-number-format"
        ScenarioFileName = "home_number_format"
        Priority = 2
        EvidenceFamily = "dropdown"
        EvidenceSubject = "freex"
        CaptureStatus = "planned-separate-foreground-guarded-capture"
        CaptureOutputNaming = "interactive_<ScenarioFileName>_<State>.png"
        PairKeyPattern = "interactive:home-number-format:<State>"
        Trigger = "Select Home, open the Number Format combo box, and capture the opened dropdown with the selected format visible."
        CaptureRequirement = "Capture the active popup/dialog/menu bounds, not the owner-window ribbon band."
        ForegroundGuard = "Re-check FreeX foreground ownership before opening the dropdown and before the screenshot."
        CounterpartSubject = "excel"
    },
    [pscustomobject]@{
        ScenarioId = "dropdown:home-borders"
        ScenarioFileName = "home_borders"
        Priority = 3
        EvidenceFamily = "dropdown"
        EvidenceSubject = "freex"
        CaptureStatus = "planned-separate-foreground-guarded-capture"
        CaptureOutputNaming = "interactive_<ScenarioFileName>_<State>.png"
        PairKeyPattern = "interactive:home-borders:<State>"
        Trigger = "Select Home, open the Borders dropdown, and capture the opened menu."
        CaptureRequirement = "Capture the active popup/dialog/menu bounds, not the owner-window ribbon band."
        ForegroundGuard = "Re-check FreeX foreground ownership before opening the dropdown and before the screenshot."
        CounterpartSubject = "excel"
    },
    [pscustomobject]@{
        ScenarioId = "context-menu:worksheet-cell"
        ScenarioFileName = "worksheet_cell_context_menu"
        Priority = 4
        EvidenceFamily = "context-menu"
        EvidenceSubject = "freex"
        CaptureStatus = "planned-separate-foreground-guarded-capture"
        CaptureOutputNaming = "interactive_<ScenarioFileName>_<State>.png"
        PairKeyPattern = "interactive:worksheet-cell-context-menu:<State>"
        Trigger = "Select a representative cell and open the worksheet context menu with Shift+F10, the Menu key, or a guarded right-click."
        CaptureRequirement = "Capture the active popup/dialog/menu bounds, not the owner-window ribbon band."
        ForegroundGuard = "Re-check FreeX foreground ownership before the context-menu input and validate the menu belongs to the expected workbook window."
        CounterpartSubject = "excel"
    },
    [pscustomobject]@{
        ScenarioId = "native-dialog:open-workbook"
        ScenarioFileName = "open_workbook_dialog"
        Priority = 5
        EvidenceFamily = "native-dialog"
        EvidenceSubject = "freex"
        CaptureStatus = "planned-separate-foreground-guarded-capture"
        CaptureOutputNaming = "interactive_<ScenarioFileName>_<State>.png"
        PairKeyPattern = "interactive:open-workbook-dialog:<State>"
        Trigger = "Open File > Open or Ctrl+O to reach FreeX's native Open dialog."
        CaptureRequirement = "Capture the active popup/dialog/menu bounds, not the owner-window ribbon band."
        ForegroundGuard = "Treat the native dialog as the expected foreground target after the final launch input; abort if another process or unrelated dialog owns foreground focus."
        CounterpartSubject = "excel"
    },
    [pscustomobject]@{
        ScenarioId = "native-dialog:save-as-workbook"
        ScenarioFileName = "save_as_workbook_dialog"
        Priority = 6
        EvidenceFamily = "native-dialog"
        EvidenceSubject = "freex"
        CaptureStatus = "planned-separate-foreground-guarded-capture"
        CaptureOutputNaming = "interactive_<ScenarioFileName>_<State>.png"
        PairKeyPattern = "interactive:save-as-workbook-dialog:<State>"
        Trigger = "Open File > Save As or F12 to reach FreeX's native Save As dialog."
        CaptureRequirement = "Capture the active popup/dialog/menu bounds, not the owner-window ribbon band."
        ForegroundGuard = "Treat the native dialog as the expected foreground target after the final launch input; abort if another process or unrelated dialog owns foreground focus."
        CounterpartSubject = "excel"
    }
)
$windowLogicalHeight = 768

$captureWidths = @(Resolve-CaptureWidths $Widths)

function Resolve-FreeXExecutablePath($requestedExePath) {
    if (-not [string]::IsNullOrWhiteSpace($requestedExePath)) {
        $resolvedRequestedExePath = if ([System.IO.Path]::IsPathRooted($requestedExePath)) {
            $requestedExePath
        } else {
            Join-Path (Get-Location) $requestedExePath
        }

        if (Test-Path -LiteralPath $resolvedRequestedExePath -PathType Leaf) {
            return [System.IO.Path]::GetFullPath($resolvedRequestedExePath)
        }

        throw "FreeX executable was not found at $resolvedRequestedExePath. Pass -ExePath with an existing FreeX.App.Host.exe or build the host before running tools\screenshot_ribbon.ps1."
    }

    $releaseExe = Join-Path $repoRoot "src\FreeX.App.Host\bin\Release\net10.0-windows10.0.19041.0\FreeX.App.Host.exe"
    if (Test-Path -LiteralPath $releaseExe -PathType Leaf) {
        return $releaseExe
    }

    $binRoot = Join-Path $repoRoot "src\FreeX.App.Host\bin"
    if (Test-Path -LiteralPath $binRoot -PathType Container) {
        $discoveredExe = Get-ChildItem -LiteralPath $binRoot -Recurse -Filter "FreeX.App.Host.exe" -File |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1
        if ($null -ne $discoveredExe) {
            return $discoveredExe.FullName
        }
    }

    throw "FreeX executable was not found. Pass -ExePath with an existing FreeX.App.Host.exe or build the Release host before running tools\screenshot_ribbon.ps1."
}

# Get screen DPI to calculate physical pixels for a 300px logical capture
$dpi   = [Win32c]::GetScreenDpi()
$scale = $dpi / 96.0
Write-Host "Screen DPI: $dpi  Scale: $scale"

$exe = Resolve-FreeXExecutablePath $ExePath

$proc = Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe) -PassThru
Write-Host "Launched PID $($proc.Id)"

$hwnd = [IntPtr]::Zero
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Milliseconds 500
    $hwnd = [Win32c]::FindWindowByPid($proc.Id)
    if ($hwnd -ne [IntPtr]::Zero) { break }
}
if ($hwnd -eq [IntPtr]::Zero) {
    $visibleProcess = Get-Process FreeX.App.Host -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $exe -and $_.MainWindowHandle -ne 0 } |
        Sort-Object StartTime -Descending |
        Select-Object -First 1
    if ($null -ne $visibleProcess) {
        $proc = $visibleProcess
        $hwnd = [IntPtr]$visibleProcess.MainWindowHandle
        Write-Host "Reusing visible FreeX window PID $($proc.Id)"
    }
}
if ($hwnd -eq [IntPtr]::Zero) { Write-Error "No window"; $proc.Kill(); exit 1 }

function Get-WindowTitle($windowHandle) {
    $title = New-Object System.Text.StringBuilder 512
    [Win32c]::GetWindowText($windowHandle, $title, $title.Capacity) | Out-Null
    return $title.ToString()
}

function Get-ForegroundWindowInfo {
    $foreground = [Win32c]::GetForegroundWindow()
    if ($foreground -eq [IntPtr]::Zero) {
        return [pscustomobject]@{
            Handle = "0"
            ProcessId = $null
            Title = ""
        }
    }

    $actualPid = 0
    [Win32c]::GetWindowThreadProcessId($foreground, [ref]$actualPid) | Out-Null
    $title = New-Object System.Text.StringBuilder 512
    [Win32c]::GetWindowText($foreground, $title, $title.Capacity) | Out-Null
    return [pscustomobject]@{
        Handle = $foreground.ToString()
        ProcessId = $actualPid
        Title = $title.ToString()
    }
}

function Write-RootCaptureBlockerManifest($operation, $expectedPid, $expectedTitle, $reason) {
    $manifestPath = Join-Path $outDir "screenshot_blocker_manifest.json"
    [pscustomobject]@{
        Tool = "screenshot_ribbon.ps1"
        EvidenceFamily = "ribbon"
        EvidenceSubject = "freex"
        EvidenceApp = "FreeX"
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
        RequestedTabs = $tabNames
        Policy = "Root ribbon screenshots and screenshot_manifest.json are discarded unless the expected FreeX process and window title own foreground immediately before global input and screen capture."
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    Write-Warning "Saved blocker manifest $manifestPath"
}

function Assert-ForegroundWindowOwnership($expectedPid, $expectedTitle, $operation = "capture") {
    $foreground = [Win32c]::GetForegroundWindow()
    if ($foreground -eq [IntPtr]::Zero) {
        Clear-ScreenshotEvidenceArtifacts
        Write-RootCaptureBlockerManifest $operation $expectedPid $expectedTitle "No foreground window was available."
        throw "Blocked: no foreground window before $operation."
    }

    $actualPid = 0
    [Win32c]::GetWindowThreadProcessId($foreground, [ref]$actualPid) | Out-Null
    $title = New-Object System.Text.StringBuilder 512
    [Win32c]::GetWindowText($foreground, $title, $title.Capacity) | Out-Null
    $actualTitle = $title.ToString()
    if ($actualPid -ne $expectedPid -or $actualTitle -ne $expectedTitle) {
        Clear-ScreenshotEvidenceArtifacts
        Write-RootCaptureBlockerManifest $operation $expectedPid $expectedTitle "Foreground window '$actualTitle' (PID $actualPid) did not match expected '$expectedTitle' (PID $expectedPid)."
        throw "Blocked: foreground window '$actualTitle' (PID $actualPid) does not match expected '$expectedTitle' (PID $expectedPid) before $operation."
    }
}

function Assert-ForegroundProcessOwnership($expectedPid, $operation = "capture") {
    $foreground = [Win32c]::GetForegroundWindow()
    if ($foreground -eq [IntPtr]::Zero) {
        throw "Blocked: no foreground window before $operation."
    }

    $actualPid = 0
    [Win32c]::GetWindowThreadProcessId($foreground, [ref]$actualPid) | Out-Null
    if ($actualPid -ne $expectedPid) {
        $title = New-Object System.Text.StringBuilder 512
        [Win32c]::GetWindowText($foreground, $title, $title.Capacity) | Out-Null
        throw "Blocked: foreground window '$($title.ToString())' (PID $actualPid) does not belong to expected FreeX PID $expectedPid before $operation."
    }
}

function Set-FreeXForegroundWindow($windowHandle, $expectedPid, $expectedTitle, $operation) {
    $shell = $null
    try {
        $shell = New-Object -ComObject WScript.Shell
    }
    catch {
        $shell = $null
    }

    for ($attempt = 0; $attempt -lt 16; $attempt++) {
        [Win32c]::ShowWindow($windowHandle, 9) | Out-Null
        [Win32c]::SetWindowPos($windowHandle, [IntPtr](-1), 0, 0, 0, 0, 0x0043) | Out-Null
        Start-Sleep -Milliseconds 40
        [Win32c]::SetWindowPos($windowHandle, [IntPtr](-2), 0, 0, 0, 0, 0x0043) | Out-Null

        $foreground = [Win32c]::GetForegroundWindow()
        $foregroundPid = 0
        $targetPid = 0
        $foregroundThread = if ($foreground -ne [IntPtr]::Zero) { [Win32c]::GetWindowThreadProcessId($foreground, [ref]$foregroundPid) } else { 0 }
        $targetThread = [Win32c]::GetWindowThreadProcessId($windowHandle, [ref]$targetPid)
        $currentThread = [Win32c]::GetCurrentThreadId()
        $attachedTarget = $false
        $attachedForeground = $false
        try {
            if ($targetThread -ne 0 -and $targetThread -ne $currentThread) {
                $attachedTarget = [Win32c]::AttachThreadInput($currentThread, $targetThread, $true)
            }
            if ($foregroundThread -ne 0 -and $foregroundThread -ne $currentThread -and $foregroundThread -ne $targetThread) {
                $attachedForeground = [Win32c]::AttachThreadInput($currentThread, $foregroundThread, $true)
            }

            [Win32c]::BringWindowToTop($windowHandle) | Out-Null
            [Win32c]::SetActiveWindow($windowHandle) | Out-Null
            [Win32c]::SetFocus($windowHandle) | Out-Null
            [Win32c]::SetForegroundWindow($windowHandle) | Out-Null
        }
        finally {
            if ($attachedForeground) {
                [Win32c]::AttachThreadInput($currentThread, $foregroundThread, $false) | Out-Null
            }
            if ($attachedTarget) {
                [Win32c]::AttachThreadInput($currentThread, $targetThread, $false) | Out-Null
            }
        }

        if (($attempt % 4) -eq 3) {
            [Win32c]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
            [Win32c]::SetForegroundWindow($windowHandle) | Out-Null
            [Win32c]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
        }

        if ($null -ne $shell) {
            $shell.AppActivate([int]$expectedPid) | Out-Null
        }

        Start-Sleep -Milliseconds 300

        $foreground = [Win32c]::GetForegroundWindow()
        if ($foreground -eq [IntPtr]::Zero) {
            continue
        }

        $actualPid = 0
        [Win32c]::GetWindowThreadProcessId($foreground, [ref]$actualPid) | Out-Null
        $title = New-Object System.Text.StringBuilder 512
        [Win32c]::GetWindowText($foreground, $title, $title.Capacity) | Out-Null
        if ($actualPid -eq $expectedPid -and $title.ToString() -eq $expectedTitle) {
            return
        }
    }

    Assert-ForegroundWindowOwnership $expectedPid $expectedTitle $operation
}

function Find-FreeXOpenWorkbookDialogWindow($expectedPid, $ownerWindowHandle) {
    $windows = [Win32c]::GetVisibleWindowsByProcess($expectedPid) |
        Where-Object {
            $_.Handle -ne $ownerWindowHandle -and
            $_.ClassName -eq "#32770" -and
            ($_.Right - $_.Left) -gt 400 -and
            ($_.Bottom - $_.Top) -gt 300
        } |
        Sort-Object @{ Expression = { ($_.Right - $_.Left) * ($_.Bottom - $_.Top) }; Descending = $true }

    return $windows | Select-Object -First 1
}

function Find-FreeXSaveAsWorkbookDialogWindow($expectedPid, $ownerWindowHandle) {
    $windows = [Win32c]::GetVisibleWindowsByProcess($expectedPid) |
        Where-Object {
            $_.Handle -ne $ownerWindowHandle -and
            $_.ClassName -eq "#32770" -and
            $_.Title -eq "Save As" -and
            ($_.Right - $_.Left) -gt 400 -and
            ($_.Bottom - $_.Top) -gt 300
        } |
        Sort-Object @{ Expression = { ($_.Right - $_.Left) * ($_.Bottom - $_.Top) }; Descending = $true }

    return $windows | Select-Object -First 1
}

function Invoke-FreeXOpenWorkbookDialogTour($expectedPid, $ownerWindowHandle, $expectedTitle) {
    New-Item -ItemType Directory -Force -Path $openWorkbookDialogOutDir | Out-Null
    Clear-OpenWorkbookDialogEvidenceArtifacts

    Assert-ForegroundWindowOwnership $expectedPid $expectedTitle "FreeX native Open dialog keyboard input"
    [System.Windows.Forms.SendKeys]::SendWait("^o")
    Start-Sleep -Milliseconds 1200
    Assert-ForegroundProcessOwnership $expectedPid "FreeX native Open dialog capture"

    $dialog = Find-FreeXOpenWorkbookDialogWindow $expectedPid $ownerWindowHandle
    if ($null -eq $dialog) {
        Clear-OpenWorkbookDialogEvidenceArtifacts
        throw "FreeX native Open dialog tour did not detect a FreeX-owned '#32770' Open dialog after Ctrl+O."
    }

    $dialogDpi = [Win32c]::GetDpiForWindow($dialog.Handle)
    $dialogScale = if ($dialogDpi -gt 0) { [double]$dialogDpi / 96.0 } else { 1.0 }
    $captureSource = "native-dialog-window-rectangle"
    $captureBounds = [pscustomobject]@{
        Left = [int][Math]::Round($dialog.Left * $dialogScale)
        Top = [int][Math]::Round($dialog.Top * $dialogScale)
        Right = [int][Math]::Round($dialog.Right * $dialogScale)
        Bottom = [int][Math]::Round($dialog.Bottom * $dialogScale)
        Width = [int][Math]::Round(($dialog.Right - $dialog.Left) * $dialogScale)
        Height = [int][Math]::Round(($dialog.Bottom - $dialog.Top) * $dialogScale)
    }
    $dialogBounds = [pscustomobject]@{
        Handle = $dialog.Handle.ToString()
        ClassName = $dialog.ClassName
        Title = $dialog.Title
        Dpi = $dialogDpi
        CaptureScale = $dialogScale
        Left = $dialog.Left
        Top = $dialog.Top
        Right = $dialog.Right
        Bottom = $dialog.Bottom
        Width = $dialog.Right - $dialog.Left
        Height = $dialog.Bottom - $dialog.Top
    }

    $fileName = "freex_open_workbook_dialog_opened.png"
    $path = Join-Path $openWorkbookDialogOutDir $fileName
    Assert-ForegroundProcessOwnership $expectedPid "FreeX native Open dialog screen capture"
    Capture-ScreenRectangle $captureBounds.Left $captureBounds.Top $captureBounds.Width $captureBounds.Height $path

    $manifestPath = Join-Path $openWorkbookDialogOutDir "freex_open_workbook_dialog_tour_manifest.json"
    [pscustomobject]@{
        Tool = "FREEX_OPEN_WORKBOOK_DIALOG_TOUR"
        EvidenceFamily = "native-dialog"
        EvidenceSubject = "freex"
        EvidenceApp = "FreeX"
        OutputDirectory = $openWorkbookDialogOutDir
        OutputNaming = "freex_open_workbook_dialog_opened.png"
        CatalogEvidenceTarget = "docs/testing/ui-test-catalog.md"
        ScenarioId = "native-dialog:open-workbook"
        DialogClassName = "#32770"
        EntryPath = "Ctrl+O"
        CaptureStatus = "complete"
        CaptureMethod = $captureSource
        ForegroundGuard = [pscustomobject]@{
            Required = $true
            ExpectedProcessId = $expectedPid
            ExpectedWindowTitle = $expectedTitle
            Policy = "Abort and clear native Open dialog evidence unless FreeX owns foreground immediately before Ctrl+O and before screen capture."
        }
        Pairing = [pscustomobject]@{
            PairKeyPattern = "interactive:open-workbook-dialog:<State>"
            PairKey = "interactive:open-workbook-dialog:opened"
            CounterpartSubject = "excel"
            CounterpartTool = "FREEX_EXCEL_OPEN_WORKBOOK_DIALOG_TOUR"
            CounterpartFileName = "interactive_open_workbook_dialog_opened.png"
        }
        Scenario = [pscustomobject]@{
            ScenarioId = "native-dialog:open-workbook"
            ScenarioFileName = "open_workbook_dialog"
            State = "opened"
            Trigger = "A foreground-guarded Ctrl+O opens the FreeX native Open dialog."
        }
        WindowBounds = $captureBounds
        DialogBounds = $dialogBounds
        Captures = @(
            [pscustomobject]@{
                CaptureSequence = 1
                CaptureKey = "interactive:open-workbook-dialog:opened"
                PairKey = "interactive:open-workbook-dialog:opened"
                EvidenceSubject = "freex"
                CounterpartSubject = "excel"
                CounterpartFileName = "interactive_open_workbook_dialog_opened.png"
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

function Invoke-FreeXSaveAsWorkbookDialogTour($expectedPid, $ownerWindowHandle, $expectedTitle) {
    New-Item -ItemType Directory -Force -Path $saveAsWorkbookDialogOutDir | Out-Null
    Clear-SaveAsWorkbookDialogEvidenceArtifacts

    Assert-ForegroundWindowOwnership $expectedPid $expectedTitle "FreeX native Save As dialog keyboard input"
    [System.Windows.Forms.SendKeys]::SendWait("{F12}")
    Start-Sleep -Milliseconds 1200
    Assert-ForegroundProcessOwnership $expectedPid "FreeX native Save As dialog capture"

    $dialog = Find-FreeXSaveAsWorkbookDialogWindow $expectedPid $ownerWindowHandle
    if ($null -eq $dialog) {
        Clear-SaveAsWorkbookDialogEvidenceArtifacts
        throw "FreeX native Save As dialog tour did not detect a FreeX-owned '#32770' Save As dialog after F12."
    }

    $dialogDpi = [Win32c]::GetDpiForWindow($dialog.Handle)
    $dialogScale = if ($dialogDpi -gt 0) { [double]$dialogDpi / 96.0 } else { 1.0 }
    $captureSource = "native-dialog-window-rectangle"
    $captureBounds = [pscustomobject]@{
        Left = [int][Math]::Round($dialog.Left * $dialogScale)
        Top = [int][Math]::Round($dialog.Top * $dialogScale)
        Right = [int][Math]::Round($dialog.Right * $dialogScale)
        Bottom = [int][Math]::Round($dialog.Bottom * $dialogScale)
        Width = [int][Math]::Round(($dialog.Right - $dialog.Left) * $dialogScale)
        Height = [int][Math]::Round(($dialog.Bottom - $dialog.Top) * $dialogScale)
    }
    $dialogBounds = [pscustomobject]@{
        Handle = $dialog.Handle.ToString()
        ClassName = $dialog.ClassName
        Title = $dialog.Title
        Dpi = $dialogDpi
        CaptureScale = $dialogScale
        Left = $dialog.Left
        Top = $dialog.Top
        Right = $dialog.Right
        Bottom = $dialog.Bottom
        Width = $dialog.Right - $dialog.Left
        Height = $dialog.Bottom - $dialog.Top
    }

    $fileName = "freex_save_as_workbook_dialog_opened.png"
    $path = Join-Path $saveAsWorkbookDialogOutDir $fileName
    Assert-ForegroundProcessOwnership $expectedPid "FreeX native Save As dialog screen capture"
    Capture-ScreenRectangle $captureBounds.Left $captureBounds.Top $captureBounds.Width $captureBounds.Height $path

    $manifestPath = Join-Path $saveAsWorkbookDialogOutDir "freex_save_as_workbook_dialog_tour_manifest.json"
    [pscustomobject]@{
        Tool = "FREEX_SAVE_AS_WORKBOOK_DIALOG_TOUR"
        EvidenceFamily = "native-dialog"
        EvidenceSubject = "freex"
        EvidenceApp = "FreeX"
        OutputDirectory = $saveAsWorkbookDialogOutDir
        OutputNaming = "freex_save_as_workbook_dialog_opened.png"
        CatalogEvidenceTarget = "docs/testing/ui-test-catalog.md"
        ScenarioId = "native-dialog:save-as-workbook"
        DialogClassName = "#32770"
        EntryPath = "F12"
        CaptureStatus = "complete"
        CaptureMethod = $captureSource
        ForegroundGuard = [pscustomobject]@{
            Required = $true
            ExpectedProcessId = $expectedPid
            ExpectedWindowTitle = $expectedTitle
            Policy = "Abort and clear native Save As dialog evidence unless FreeX owns foreground immediately before F12 and before screen capture."
        }
        Pairing = [pscustomobject]@{
            PairKeyPattern = "interactive:save-as-workbook-dialog:<State>"
            PairKey = "interactive:save-as-workbook-dialog:opened"
            CounterpartSubject = "excel"
            CounterpartTool = "FREEX_EXCEL_SAVE_AS_WORKBOOK_DIALOG_TOUR"
            CounterpartFileName = "interactive_save_as_workbook_dialog_opened.png"
        }
        Scenario = [pscustomobject]@{
            ScenarioId = "native-dialog:save-as-workbook"
            ScenarioFileName = "save_as_workbook_dialog"
            State = "opened"
            Trigger = "A foreground-guarded F12 opens the FreeX native Save As dialog."
        }
        WindowBounds = $captureBounds
        DialogBounds = $dialogBounds
        Captures = @(
            [pscustomobject]@{
                CaptureSequence = 1
                CaptureKey = "interactive:save-as-workbook-dialog:opened"
                PairKey = "interactive:save-as-workbook-dialog:opened"
                EvidenceSubject = "freex"
                CounterpartSubject = "excel"
                CounterpartFileName = "interactive_save_as_workbook_dialog_opened.png"
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

Write-Host "HWND: $hwnd"
$expectedTitle = Get-WindowTitle $hwnd
[Win32c]::ShowWindow($hwnd, 1) | Out-Null
[Win32c]::SetWindowPos($hwnd, [IntPtr]::Zero, 0, 0, 0, 0, 0x0001) | Out-Null
Set-FreeXForegroundWindow $hwnd $proc.Id $expectedTitle "initial capture setup"

if ($OpenWorkbookDialogTour -eq "1") {
    Invoke-FreeXOpenWorkbookDialogTour $proc.Id $hwnd $expectedTitle
    $proc.Kill()
    Write-Host "Done."
    exit 0
}

if ($SaveAsWorkbookDialogTour -eq "1") {
    Invoke-FreeXSaveAsWorkbookDialogTour $proc.Id $hwnd $expectedTitle
    $proc.Kill()
    Write-Host "Done."
    exit 0
}

function Set-CaptureWindowWidth($windowHandle, $widthSpec) {
    if ($null -eq $widthSpec.WindowLogicalWidth) {
        [Win32c]::ShowWindow($windowHandle, 3) | Out-Null
        Set-FreeXForegroundWindow $windowHandle $proc.Id $expectedTitle "window resize capture setup"
        return
    }

    $physicalWidth = [int]([Math]::Ceiling([double]$widthSpec.WindowLogicalWidth * $scale))
    $physicalHeight = [int]([Math]::Ceiling($windowLogicalHeight * $scale))
    [Win32c]::ShowWindow($windowHandle, 1) | Out-Null
    Start-Sleep -Milliseconds 200
    [Win32c]::SetWindowPos($windowHandle, [IntPtr]::Zero, 0, 0, $physicalWidth, $physicalHeight, 0) | Out-Null
    Set-FreeXForegroundWindow $windowHandle $proc.Id $expectedTitle "window resize capture setup"
}

$desktop = [System.Windows.Automation.AutomationElement]::RootElement
$cond    = New-Object System.Windows.Automation.PropertyCondition(
               [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
$appEl   = $desktop.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if ($appEl -eq $null) { Write-Error "UIA element not found"; $proc.Kill(); exit 1 }

# Capture height: 300 logical pixels covers title+ribbon fully even at 150% DPI
$captureH = [int]([Math]::Ceiling(300 * $scale))
Write-Host "Capture height: $captureH physical px (300 logical)"

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
        EvidenceSubject = "freex"
        EvidenceApp = "FreeX"
        OutputDirectory = $scriptOutDir
        OutputNaming = "ribbon_<WidthLabel>_<RibbonTab>.png"
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
            CounterpartSubject = "excel"
            CounterpartTool = "screenshot_excel.ps1"
            CounterpartOutputNaming = "excel_<WidthLabel>_<RibbonTab>.png"
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
    $nameCond = New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::NameProperty, $tabName)
    $tabItemCond = New-Object System.Windows.Automation.PropertyCondition(
                       [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                       [System.Windows.Automation.ControlType]::TabItem)
    $tabCond = New-Object System.Windows.Automation.AndCondition($nameCond, $tabItemCond)
    $tabEl   = $appEl.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $tabCond)
    if ($tabEl -eq $null) {
        Clear-ScreenshotEvidenceArtifacts
        throw "Ribbon screenshot tab '$tabName' was not found; aborting instead of writing an incomplete evidence matrix."
    }

    $selPat = $tabEl.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    Assert-ForegroundWindowOwnership $proc.Id $expectedTitle "ribbon tab selection"
    if ($selPat -ne $null) { $selPat.Select() }
    Start-Sleep -Milliseconds 800

    $wrect = New-Object Win32c+RECT
    [Win32c]::GetWindowRect($hwnd, [ref]$wrect) | Out-Null
    $w = $wrect.Right - $wrect.Left
    Assert-ForegroundWindowOwnership $proc.Id $expectedTitle "screen capture"

    $bmp = New-Object System.Drawing.Bitmap($w, $captureH)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($wrect.Left, $wrect.Top, 0, 0, [System.Drawing.Size]::new($w, $captureH))
    $g.Dispose()

    $safe = $tabName -replace '[^a-zA-Z0-9_]','_'
    $fileName = "ribbon_$($widthSpec.Label)_$safe.png"
    $path = Join-Path $outDir $fileName
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $script:capturedFiles += [pscustomobject]@{
        CaptureSequence = $script:capturedFiles.Count + 1
        CaptureKey = "ribbon:$($widthSpec.Label):$safe"
        PairKey = "ribbon:$($widthSpec.Label):$safe"
        EvidenceSubject = "freex"
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
    Write-Host "Capturing FreeX ribbon width '$($widthSpec.Label)' ($($widthSpec.EvidencePurpose))"
    Set-CaptureWindowWidth $hwnd $widthSpec

    foreach ($tabName in $tabNames) {
        Screenshot-Tab $tabName $widthSpec
    }
}

$finalRect = New-Object Win32c+RECT
[Win32c]::GetWindowRect($hwnd, [ref]$finalRect) | Out-Null
Write-ScreenshotEvidenceManifest "screenshot_ribbon.ps1" $outDir $finalRect 300 $captureH $captureWidths $script:capturedFiles $proc.Id $expectedTitle

$proc.Kill()
Write-Host "Done."
