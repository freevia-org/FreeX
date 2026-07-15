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

function Clear-ScreenshotEvidenceArtifacts {
    Get-ChildItem $outDir -Filter "*.png" -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $outDir "screenshot_manifest.json") -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $outDir "screenshot_blocker_manifest.json") -Force -ErrorAction SilentlyContinue
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
