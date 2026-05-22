using FluentAssertions;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Xunit;

namespace Freexcel.App.Host.Tests;

public sealed class FormulaEditingUiE2eTests
{
    [Fact]
    [Trait("Category", "UIE2E")]
    public void FormulaEditing_CoversExcelReferenceEditingScenarios()
    {
        FormulaEditingUiE2eHarness.Run();
    }
}

internal static class FormulaEditingUiE2eHarness
{
    public static void Run()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var run = FreexcelUiRun.Start();

        run.Capture("00-start");
        EnterNumber(run, col: 1, row: 1, "10");
        EnterNumber(run, col: 1, row: 2, "20");
        EnterNumber(run, col: 2, row: 1, "5");
        run.Capture("01-numbers-entered");

        run.ClickCell(col: 3, row: 1);
        run.TypeText("=SUM(");
        run.Capture("02-formula-point-mode-start");
        run.ClickCell(col: 1, row: 1);
        run.HoldShiftAndPress(VirtualKey.Down);
        run.TypeText(")");
        run.Press(VirtualKey.Enter);
        run.Capture("03-mouse-reference-then-keyboard-extend");

        run.ClickCell(col: 4, row: 1);
        run.TypeText("=SUM(");
        run.Press(VirtualKey.Left);
        run.HoldShiftAndPress(VirtualKey.Down);
        run.HoldShiftAndPress(VirtualKey.Down);
        run.TypeText(")");
        run.Press(VirtualKey.Enter);
        run.Capture("04-keyboard-reference-range");

        run.ClickCell(col: 4, row: 1);
        run.Press(VirtualKey.F2);
        run.Press(VirtualKey.Left);
        run.Capture("05-f2-edit-mode-arrow-caret");
        run.Press(VirtualKey.F2);
        run.Press(VirtualKey.Right);
        run.Capture("06-f2-point-mode-arrow-reference");
        run.Press(VirtualKey.Escape);

        run.ClickCell(col: 3, row: 1);
        run.Press(VirtualKey.F2);
        run.Capture("07-reference-highlighting-existing-formula");

        var screenshots = run.Artifacts.GetFiles("*.png").OrderBy(file => file.Name).ToList();
        screenshots.Should().HaveCountGreaterThanOrEqualTo(8);
        foreach (var screenshot in screenshots)
            screenshot.Length.Should().BeGreaterThan(10_000, screenshot.Name);

        File.WriteAllText(
            Path.Combine(run.Artifacts.FullName, "formula-editing-ui-e2e-analysis.md"),
            BuildAnalysis(run.Artifacts, screenshots),
            Encoding.UTF8);
    }

    private static void EnterNumber(FreexcelUiRun run, int col, int row, string value)
    {
        run.ClickCell(col, row);
        run.TypeText(value);
        run.Press(VirtualKey.Enter);
    }

    private static string BuildAnalysis(DirectoryInfo artifactDir, IReadOnlyList<FileInfo> screenshots)
    {
        var imageList = string.Join(Environment.NewLine, screenshots.Select(file => $"- `{file.Name}`"));
        return $$"""
        # Formula Editing UI E2E Analysis

        Artifact directory: `{{artifactDir.FullName}}`

        ## Excel Reference Behavior

        - Microsoft Support documents `F2` as editing the active cell and, while editing a formula, toggling Point mode so arrow keys can create a reference.
        - Microsoft Support's status-bar documentation identifies Point as formula cell selection mode and Edit as the mode entered by double-clicking or pressing `F2`.

        Sources:
        - https://support.microsoft.com/en-gb/office/keyboard-shortcuts-in-excel-1798d9d5-842a-42b8-9c99-9b7213f0040f
        - https://support.microsoft.com/en-us/office/excel-status-bar-options-6055ecd9-e20f-4a7a-a611-4481bd488c55

        ## Scenarios Driven

        - Entered numbers into `A1`, `A2`, and `B1`.
        - Started a formula with `=SUM(` and selected a referenced range with mouse plus `Shift+Down`.
        - Started a formula with `=SUM(` and selected/extended a referenced range with keyboard navigation.
        - Pressed `F2` in an existing formula and verified the visual state is captured for Edit mode arrow/caret movement.
        - Pressed `F2` again and captured Point mode arrow/reference behavior.
        - Re-opened an existing formula to capture reference highlighting.

        ## Captured Screenshots

        {{imageList}}

        ## Expected Comparison Against Excel

        - Formula entry after typing `=` should be in a Point/Enter-style mode where mouse and arrow navigation insert references.
        - `Shift+Arrow` while in formula reference selection should extend the highlighted referenced range.
        - `F2` while editing a formula should toggle between text edit behavior and Point mode behavior.
        - Existing formula references should be visually color-highlighted in both the formula text and the grid.

        ## What This Test Currently Checks

        This test is a full UI smoke/evidence test: it launches the real WPF app, drives user input, and saves screenshots for human review. It asserts that screenshots were captured and are non-empty. Fine-grained visual assertions are intentionally left to the artifact review because the exact pixels vary with Windows theme, DPI, and font rendering.

        ## Current Known Gaps To Inspect In Artifacts

        - If the mouse-reference scenario leaves formula editing and selects another cell, that differs from Excel Point mode.
        - If keyboard Point mode produces adjacent references without a separator, for example `C3E1`, that differs from Excel's reference/range entry behavior.
        - Existing formula reference highlighting should show matching formula text color and grid range color; this is easiest to inspect in `07-reference-highlighting-existing-formula.png`.
        """;
    }
}

internal sealed class FreexcelUiRun : IDisposable
{
    private const int WindowWidth = 1280;
    private const int WindowHeight = 720;
    private const int GridLeft = 53;
    private const int GridTop = 328;
    private const int CellWidth = 101;
    private const int CellHeight = 30;

    private readonly Process _process;
    private readonly IntPtr _window;

    private FreexcelUiRun(Process process, IntPtr window, DirectoryInfo artifacts)
    {
        _process = process;
        _window = window;
        Artifacts = artifacts;
    }

    public DirectoryInfo Artifacts { get; }

    public static FreexcelUiRun Start()
    {
        var appExe = ResolveAppExecutable();
        var artifacts = CreateArtifactDirectory();
        var process = Process.Start(new ProcessStartInfo(appExe)
        {
            WorkingDirectory = Path.GetDirectoryName(appExe)!,
            UseShellExecute = false
        }) ?? throw new InvalidOperationException("Failed to launch Freexcel.");

        var window = WaitForWindow(process, TimeSpan.FromSeconds(20));
        Native.ShowWindow(window, Native.SW_RESTORE);
        Native.SetWindowPos(window, IntPtr.Zero, 20, 20, WindowWidth, WindowHeight, Native.SWP_SHOWWINDOW);
        Native.SetForegroundWindow(window);
        Thread.Sleep(600);

        return new FreexcelUiRun(process, window, artifacts);
    }

    public void ClickCell(int col, int row)
    {
        var rect = GetWindowRect();
        var x = rect.Left + GridLeft + (col - 1) * CellWidth + CellWidth / 2;
        var y = rect.Top + GridTop + (row - 1) * CellHeight + CellHeight / 2;
        Native.SetForegroundWindow(_window);
        Native.SetCursorPos(x, y);
        Thread.Sleep(80);
        Native.mouse_event(Native.LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        Native.mouse_event(Native.LEFTUP, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(160);
    }

    public void TypeText(string text)
    {
        Native.SetForegroundWindow(_window);
        foreach (var ch in text)
        {
            Native.SendUnicode(ch);
            Thread.Sleep(20);
        }

        Thread.Sleep(160);
    }

    public void Press(ushort key)
    {
        Native.SetForegroundWindow(_window);
        Native.SendKey(key);
        Thread.Sleep(180);
    }

    public void HoldShiftAndPress(ushort key)
    {
        Native.SetForegroundWindow(_window);
        Native.KeyDown(VirtualKey.Shift);
        Native.SendKey(key);
        Native.KeyUp(VirtualKey.Shift);
        Thread.Sleep(180);
    }

    public void Capture(string name)
    {
        var rect = GetWindowRect();
        using var bitmap = new Bitmap(rect.Right - rect.Left, rect.Bottom - rect.Top);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, bitmap.Size);
        bitmap.Save(Path.Combine(Artifacts.FullName, $"{name}.png"), ImageFormat.Png);
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best-effort cleanup only.
        }

        _process.Dispose();
    }

    private Native.RECT GetWindowRect()
    {
        Native.GetWindowRect(_window, out var rect).Should().BeTrue();
        return rect;
    }

    private static string ResolveAppExecutable()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!;
        var appExe = Path.Combine(assemblyDirectory, "Freexcel.App.Host.exe");
        if (File.Exists(appExe))
            return appExe;

        throw new FileNotFoundException("Could not find Freexcel.App.Host.exe in test output.", appExe);
    }

    private static DirectoryInfo CreateArtifactDirectory()
    {
        var root = Path.Combine(
            AppContext.BaseDirectory,
            "FormulaEditingUiE2E",
            DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        return Directory.CreateDirectory(root);
    }

    private static IntPtr WaitForWindow(Process process, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            process.Refresh();
            var mainWindow = process.MainWindowHandle;
            if (mainWindow != IntPtr.Zero && Native.IsWindowVisible(mainWindow))
                return mainWindow;

            var enumerated = Native.FindVisibleWindowForProcess(process.Id);
            if (enumerated != IntPtr.Zero)
                return enumerated;

            Thread.Sleep(250);
        }

        throw new InvalidOperationException($"No visible Freexcel window appeared for process {process.Id}.");
    }
}

internal static class VirtualKey
{
    public const ushort Enter = 0x0D;
    public const ushort Escape = 0x1B;
    public const ushort Shift = 0x10;
    public const ushort Left = 0x25;
    public const ushort Up = 0x26;
    public const ushort Right = 0x27;
    public const ushort Down = 0x28;
    public const ushort F2 = 0x71;
}

internal static class Native
{
    public const int SW_RESTORE = 9;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint LEFTDOWN = 0x0002;
    public const uint LEFTUP = 0x0004;

    private const uint KEYEVENTF_KEYUP = 0x0002;

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern short VkKeyScan(char ch);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    public static IntPtr FindVisibleWindowForProcess(int processId)
    {
        var target = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd))
                return true;

            GetWindowThreadProcessId(hWnd, out var windowProcessId);
            if (windowProcessId != processId)
                return true;

            target = hWnd;
            return false;
        }, IntPtr.Zero);
        return target;
    }

    public static void SendUnicode(char ch)
    {
        var scan = VkKeyScan(ch);
        scan.Should().NotBe(-1, $"the test driver must be able to type '{ch}'");

        var virtualKey = (byte)(scan & 0xff);
        var shiftState = (scan >> 8) & 0xff;
        if ((shiftState & 1) != 0)
            keybd_event((byte)VirtualKey.Shift, 0, 0, UIntPtr.Zero);

        keybd_event(virtualKey, 0, 0, UIntPtr.Zero);
        keybd_event(virtualKey, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

        if ((shiftState & 1) != 0)
            keybd_event((byte)VirtualKey.Shift, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    public static void SendKey(ushort key)
    {
        keybd_event((byte)key, 0, 0, UIntPtr.Zero);
        keybd_event((byte)key, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    public static void KeyDown(ushort key)
    {
        keybd_event((byte)key, 0, 0, UIntPtr.Zero);
    }

    public static void KeyUp(ushort key)
    {
        keybd_event((byte)key, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

}
