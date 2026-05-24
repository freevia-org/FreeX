Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class Win32c {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, ref RECT lpRect);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")]  public static extern int GetDeviceCaps(IntPtr hDC, int nIndex);
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
    public static int GetScreenDpi() {
        IntPtr dc = GetDC(IntPtr.Zero);
        int dpi = GetDeviceCaps(dc, 88); // LOGPIXELSX
        ReleaseDC(IntPtr.Zero, dc);
        return dpi;
    }
}
"@

$repoRoot = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $PSScriptRoot "screenshots"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
Get-ChildItem $outDir -Filter "*.png" | Remove-Item -Force

# Get screen DPI to calculate physical pixels for a 300px logical capture
$dpi   = [Win32c]::GetScreenDpi()
$scale = $dpi / 96.0
Write-Host "Screen DPI: $dpi  Scale: $scale"

$exe = Join-Path $repoRoot "src\Freexcel.App.Host\bin\Release\net10.0-windows10.0.19041.0\Freexcel.App.Host.exe"
$proc = Start-Process -FilePath $exe -PassThru
Write-Host "Launched PID $($proc.Id)"

$hwnd = [IntPtr]::Zero
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Milliseconds 500
    $hwnd = [Win32c]::FindWindowByPid($proc.Id)
    if ($hwnd -ne [IntPtr]::Zero) { break }
}
if ($hwnd -eq [IntPtr]::Zero) { Write-Error "No window"; $proc.Kill(); exit 1 }

Write-Host "HWND: $hwnd"
[Win32c]::ShowWindow($hwnd, 3) | Out-Null
[Win32c]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Seconds 3

$desktop = [System.Windows.Automation.AutomationElement]::RootElement
$cond    = New-Object System.Windows.Automation.PropertyCondition(
               [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
$appEl   = $desktop.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if ($appEl -eq $null) { Write-Error "UIA element not found"; $proc.Kill(); exit 1 }

# Capture height: 300 logical pixels covers title+ribbon fully even at 150% DPI
$captureH = [int]([Math]::Ceiling(300 * $scale))
Write-Host "Capture height: $captureH physical px (300 logical)"

function Screenshot-Tab($tabName) {
    $nameCond = New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::NameProperty, $tabName)
    $tabItemCond = New-Object System.Windows.Automation.PropertyCondition(
                       [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                       [System.Windows.Automation.ControlType]::TabItem)
    $tabCond = New-Object System.Windows.Automation.AndCondition($nameCond, $tabItemCond)
    $tabEl   = $appEl.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $tabCond)
    if ($tabEl -eq $null) { Write-Warning "Tab '$tabName' not found"; return }

    $selPat = $tabEl.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    if ($selPat -ne $null) { $selPat.Select() }
    Start-Sleep -Milliseconds 800

    $wrect = New-Object Win32c+RECT
    [Win32c]::GetWindowRect($hwnd, [ref]$wrect) | Out-Null
    $w = $wrect.Right - $wrect.Left

    $bmp = New-Object System.Drawing.Bitmap($w, $captureH)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($wrect.Left, $wrect.Top, 0, 0, [System.Drawing.Size]::new($w, $captureH))
    $g.Dispose()

    $safe = $tabName -replace '[^a-zA-Z0-9_]','_'
    $path = "$outDir\ribbon_$safe.png"
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

$proc.Kill()
Write-Host "Done."
