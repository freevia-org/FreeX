Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
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

$dpi   = [Win32e]::GetScreenDpi()
$scale = $dpi / 96.0
Write-Host "Screen DPI: $dpi  Scale: $scale"

# Launch Excel with a blank workbook to skip start screen
$exe = "C:\Program Files\Microsoft Office\root\Office16\EXCEL.EXE"
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
# Restore (not maximized) then move to primary monitor top-left, then maximize
[Win32e]::ShowWindow($hwnd, 1) | Out-Null   # SW_RESTORE
Start-Sleep -Milliseconds 300
# SWP_NOSIZE=0x0001 — move to primary monitor origin without resizing
[Win32e]::SetWindowPos($hwnd, [IntPtr]::Zero, 0, 0, 0, 0, 0x0001) | Out-Null
Start-Sleep -Milliseconds 300
[Win32e]::ShowWindow($hwnd, 3) | Out-Null   # SW_MAXIMIZE
[Win32e]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Seconds 3

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

function Screenshot-Tab($tabName) {
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
    [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
    # Also try a real mouse click via mouse_event
    Add-Type -TypeDefinition 'using System.Runtime.InteropServices; public class Clicker { [DllImport("user32.dll")] public static extern void mouse_event(int f,int x,int y,int c,int e); }' -ErrorAction SilentlyContinue
    try { [Clicker]::mouse_event(2,0,0,0,0); Start-Sleep -Milliseconds 50; [Clicker]::mouse_event(4,0,0,0,0) } catch {}
    Start-Sleep -Milliseconds 800

    $wrect = New-Object Win32e+RECT
    [Win32e]::GetWindowRect($hwnd, [ref]$wrect) | Out-Null
    $w = $wrect.Right - $wrect.Left

    $bmp = New-Object System.Drawing.Bitmap($w, $captureH)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($wrect.Left, $wrect.Top, 0, 0, [System.Drawing.Size]::new($w, $captureH))
    $g.Dispose()

    $safe = $tabName -replace '[^a-zA-Z0-9_]','_'
    $path = "$outDir\excel_$safe.png"
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "Saved $path ($w x $captureH)"
}

Screenshot-Tab "Home"
Screenshot-Tab "Insert"
Screenshot-Tab "Draw"
Screenshot-Tab "Page Layout"
Screenshot-Tab "Formulas"
Screenshot-Tab "Data"
Screenshot-Tab "Review"
Screenshot-Tab "View"
Screenshot-Tab "Help"

# Close Excel gracefully
$xlProc = Get-Process -Id $wpid -ErrorAction SilentlyContinue
if ($xlProc) { $xlProc.Kill() }
Write-Host "Done."
