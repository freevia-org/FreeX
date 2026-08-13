if (-not ([System.Management.Automation.PSTypeName]'ScreenshotWin32').Type) {
    Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public class ScreenshotWin32 {
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
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern void mouse_event(int dwFlags, int dx, int dy, int dwData, int dwExtraInfo);
    [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] public static extern int GetDeviceCaps(IntPtr hDC, int nIndex);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public static IntPtr FindWindowByPid(int pid) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hWnd, lp) => {
            uint windowPid;
            GetWindowThreadProcessId(hWnd, out windowPid);
            if (windowPid == (uint)pid && IsWindowVisible(hWnd)) {
                var title = new StringBuilder(256);
                GetWindowText(hWnd, title, title.Capacity);
                if (title.Length > 0) { found = hWnd; return false; }
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static IntPtr FindWindowByClass(string className) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hWnd, lp) => {
            if (!IsWindowVisible(hWnd)) return true;
            var currentClass = new StringBuilder(256);
            GetClassName(hWnd, currentClass, currentClass.Capacity);
            if (currentClass.ToString() == className) {
                var title = new StringBuilder(256);
                GetWindowText(hWnd, title, title.Capacity);
                if (title.Length > 0) { found = hWnd; return false; }
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static ScreenshotWindowInfo[] GetVisibleWindowsByProcess(int processId) {
        var windows = new List<ScreenshotWindowInfo>();
        EnumWindows((hWnd, lp) => {
            if (!IsWindowVisible(hWnd)) return true;
            uint windowPid;
            GetWindowThreadProcessId(hWnd, out windowPid);
            if (windowPid != (uint)processId) return true;
            var title = new StringBuilder(512);
            GetWindowText(hWnd, title, title.Capacity);
            var className = new StringBuilder(256);
            GetClassName(hWnd, className, className.Capacity);
            var rect = new RECT();
            if (!GetWindowRect(hWnd, ref rect)) return true;
            if (rect.Right <= rect.Left || rect.Bottom <= rect.Top) return true;
            windows.Add(new ScreenshotWindowInfo {
                Handle = hWnd,
                ProcessId = (int)windowPid,
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
        int dpi = GetDeviceCaps(dc, 88);
        ReleaseDC(IntPtr.Zero, dc);
        return dpi;
    }
}

public class ScreenshotWindowInfo {
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
}

function Get-WindowTitle($windowHandle) {
    $title = New-Object System.Text.StringBuilder 512
    [ScreenshotWin32]::GetWindowText($windowHandle, $title, $title.Capacity) | Out-Null
    return $title.ToString()
}

function Get-ForegroundWindowInfo {
    $foreground = [ScreenshotWin32]::GetForegroundWindow()
    if ($foreground -eq [IntPtr]::Zero) {
        return [pscustomobject]@{
            Handle = "0"
            ProcessId = $null
            Title = ""
        }
    }

    $actualPid = 0
    [ScreenshotWin32]::GetWindowThreadProcessId($foreground, [ref]$actualPid) | Out-Null
    return [pscustomobject]@{
        Handle = $foreground.ToString()
        ProcessId = $actualPid
        Title = Get-WindowTitle $foreground
    }
}

function Assert-ForegroundWindowOwnership($expectedPid, $expectedTitle, $operation = "capture", [scriptblock]$failureAction = $null) {
    $foreground = [ScreenshotWin32]::GetForegroundWindow()
    if ($foreground -eq [IntPtr]::Zero) {
        if ($null -ne $failureAction) {
            & $failureAction $operation $expectedPid $expectedTitle "No foreground window was available."
        }

        throw "Blocked: no foreground window before $operation."
    }

    $actualPid = 0
    [ScreenshotWin32]::GetWindowThreadProcessId($foreground, [ref]$actualPid) | Out-Null
    $actualTitle = Get-WindowTitle $foreground
    if ($actualPid -ne $expectedPid -or $actualTitle -ne $expectedTitle) {
        $reason = "Foreground window '$actualTitle' (PID $actualPid) did not match expected '$expectedTitle' (PID $expectedPid)."
        if ($null -ne $failureAction) {
            & $failureAction $operation $expectedPid $expectedTitle $reason
        }

        throw "Blocked: foreground window '$actualTitle' (PID $actualPid) does not match expected '$expectedTitle' (PID $expectedPid) before $operation."
    }
}

function Clear-ScreenshotEvidenceArtifacts {
    param([string]$OutputDirectory = $outDir)

    Get-ChildItem $OutputDirectory -Filter "*.png" -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $OutputDirectory "screenshot_manifest.json") -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $OutputDirectory "screenshot_blocker_manifest.json") -Force -ErrorAction SilentlyContinue
}

function Clear-ScreenshotTourEvidenceArtifacts {
    param(
        [Parameter(Mandatory = $true)][string]$OutputDirectory,
        [Parameter(Mandatory = $true)][string]$ManifestFileName
    )

    if (-not (Test-Path -LiteralPath $OutputDirectory -PathType Container)) {
        return
    }

    Get-ChildItem $OutputDirectory -Filter "*.png" -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $OutputDirectory $ManifestFileName) -Force -ErrorAction SilentlyContinue
}

function Assert-ForegroundProcessOwnership {
    param(
        [Parameter(Mandatory = $true)][int]$ExpectedProcessId,
        [Parameter(Mandatory = $true)][string]$Operation,
        [Parameter(Mandatory = $true)][string]$ExpectedProcessName
    )

    $foreground = [ScreenshotWin32]::GetForegroundWindow()
    if ($foreground -eq [IntPtr]::Zero) {
        throw "Blocked: no foreground window before $Operation."
    }

    $actualProcessId = 0
    [ScreenshotWin32]::GetWindowThreadProcessId($foreground, [ref]$actualProcessId) | Out-Null
    if ($actualProcessId -ne $ExpectedProcessId) {
        $title = New-Object System.Text.StringBuilder 512
        [ScreenshotWin32]::GetWindowText($foreground, $title, $title.Capacity) | Out-Null
        throw "Blocked: foreground window '$($title.ToString())' (PID $actualProcessId) does not belong to expected $ExpectedProcessName PID $ExpectedProcessId before $Operation."
    }
}

function Set-ScreenshotCaptureWindowWidth {
    param(
        [Parameter(Mandatory = $true)]$WindowHandle,
        [Parameter(Mandatory = $true)]$WidthSpec,
        [Parameter(Mandatory = $true)][double]$Scale,
        [Parameter(Mandatory = $true)][double]$WindowLogicalHeight,
        [Parameter(Mandatory = $true)][scriptblock]$ActivateWindow
    )

    if ($null -eq $WidthSpec.WindowLogicalWidth) {
        [ScreenshotWin32]::ShowWindow($WindowHandle, 3) | Out-Null
        & $ActivateWindow $WindowHandle
        return
    }

    $physicalWidth = [int]([Math]::Ceiling([double]$WidthSpec.WindowLogicalWidth * $Scale))
    $physicalHeight = [int]([Math]::Ceiling($WindowLogicalHeight * $Scale))
    [ScreenshotWin32]::ShowWindow($WindowHandle, 1) | Out-Null
    Start-Sleep -Milliseconds 200
    [ScreenshotWin32]::SetWindowPos($WindowHandle, [IntPtr]::Zero, 0, 0, $physicalWidth, $physicalHeight, 0) | Out-Null
    & $ActivateWindow $WindowHandle
}

function Write-RibbonScreenshotEvidenceManifest {
    param(
        [Parameter(Mandatory = $true)][string]$ToolName,
        [Parameter(Mandatory = $true)][string]$OutputDirectory,
        [Parameter(Mandatory = $true)]$WindowRect,
        [Parameter(Mandatory = $true)][int]$CaptureLogicalHeight,
        [Parameter(Mandatory = $true)][int]$CapturePhysicalHeight,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Widths,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Captures,
        [Parameter(Mandatory = $true)][int]$ExpectedProcessId,
        [Parameter(Mandatory = $true)][string]$ExpectedWindowTitle,
        [Parameter(Mandatory = $true)][string]$EvidenceSubject,
        [Parameter(Mandatory = $true)][string]$EvidenceApp,
        [Parameter(Mandatory = $true)][string]$OutputNaming,
        [Parameter(Mandatory = $true)][string]$CounterpartSubject,
        [Parameter(Mandatory = $true)][string]$CounterpartTool,
        [Parameter(Mandatory = $true)][string]$CounterpartOutputNaming,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Tabs,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Limitations,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$InteractiveCapturePlan,
        [AllowEmptyCollection()][string[]]$RequestedTabs,
        [AllowEmptyCollection()][string[]]$SkippedTabs,
        [string]$SkippedCaptureStatus,
        [string]$SkippedCaptureReason
    )

    $manifestPath = Join-Path $OutputDirectory "screenshot_manifest.json"
    $plannedCaptureCount = $Tabs.Count * $Widths.Count
    if ($Captures.Count -ne $plannedCaptureCount) {
        Clear-ScreenshotEvidenceArtifacts -OutputDirectory $OutputDirectory
        throw "Blocked: captured $($Captures.Count) screenshot(s), expected $plannedCaptureCount. Discarded incomplete evidence matrix."
    }

    $skippedCaptures = @()
    foreach ($widthSpec in $Widths) {
        foreach ($tabName in @($SkippedTabs)) {
            $safe = $tabName -replace '[^a-zA-Z0-9_]','_'
            $skippedCaptures += [pscustomobject]@{
                CaptureKey = "ribbon:$($widthSpec.Label):$safe"
                PairKey = "ribbon:$($widthSpec.Label):$safe"
                EvidenceSubject = $EvidenceSubject
                CounterpartSubject = $CounterpartSubject
                CounterpartFileName = $CounterpartOutputNaming.Replace("<WidthLabel>", [string]$widthSpec.Label).Replace("<RibbonTab>", $safe)
                Tab = $tabName
                TabFileName = $safe
                WidthLabel = $widthSpec.Label
                WindowLogicalWidth = $widthSpec.WindowLogicalWidth
                EvidencePurpose = $widthSpec.EvidencePurpose
                CaptureStatus = $SkippedCaptureStatus
                SkipReason = $SkippedCaptureReason
            }
        }
    }

    $captureStatus = if (@($SkippedTabs).Count -gt 0) { "complete-with-skipped-unavailable-tabs" } else { "complete" }
    $manifest = [ordered]@{
        Tool = $ToolName
        EvidenceFamily = "ribbon"
        EvidenceSubject = $EvidenceSubject
        EvidenceApp = $EvidenceApp
        OutputDirectory = $OutputDirectory
        OutputNaming = $OutputNaming
        CatalogEvidenceTarget = "docs/testing/ui-test-catalog.md"
        WidthSource = "RibbonScreenshotTourPlanner.DefaultWidths"
        PlannedCaptureCount = $plannedCaptureCount
        ActualCaptureCount = $Captures.Count
        CaptureStatus = $captureStatus
        CaptureMethod = "CopyFromScreen-window-rectangle-top-band"
        ForegroundGuard = [pscustomobject]@{
            Required = $true
            ExpectedProcessId = $ExpectedProcessId
            ExpectedWindowTitle = $ExpectedWindowTitle
            Policy = "Abort and clear current PNG/manifest evidence unless the expected process and window title own the foreground window immediately before global input and screen capture."
        }
        Pairing = [pscustomobject]@{
            PairKeyPattern = "ribbon:<WidthLabel>:<TabFileName>"
            CounterpartSubject = $CounterpartSubject
            CounterpartTool = $CounterpartTool
            CounterpartOutputNaming = $CounterpartOutputNaming
        }
        WindowBounds = [pscustomobject]@{
            Left = $WindowRect.Left
            Top = $WindowRect.Top
            Right = $WindowRect.Right
            Bottom = $WindowRect.Bottom
            Width = $WindowRect.Right - $WindowRect.Left
            Height = $WindowRect.Bottom - $WindowRect.Top
        }
        CaptureLogicalHeight = $CaptureLogicalHeight
        CapturePhysicalHeight = $CapturePhysicalHeight
        Widths = $Widths
    }
    if ($null -ne $RequestedTabs) {
        $manifest["RequestedTabs"] = $RequestedTabs
    }
    $manifest["Tabs"] = $Tabs
    if ($null -ne $SkippedTabs) {
        $manifest["SkippedTabs"] = $SkippedTabs
        $manifest["SkippedCaptures"] = $skippedCaptures
    }
    $manifest["Limitations"] = $Limitations
    $manifest["InteractiveCapturePlan"] = $InteractiveCapturePlan
    $manifest["Captures"] = $Captures

    [pscustomobject]$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    Write-Host "Saved $manifestPath"
}

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

function Capture-ScreenRectangle($left, $top, $width, $height, $path) {
    $bmp = New-Object System.Drawing.Bitmap($width, $height)
    try {
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        try {
            $g.CopyFromScreen($left, $top, 0, 0, [System.Drawing.Size]::new($width, $height))
        }
        finally {
            $g.Dispose()
        }

        $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $bmp.Dispose()
    }
}
