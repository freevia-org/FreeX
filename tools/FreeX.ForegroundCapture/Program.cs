using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.CSharp.RuntimeBinder;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using System.Windows.Automation;

namespace FreeX.ForegroundCapture;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();

        var options = CaptureOptions.Parse(args);
        if (options.ShowHelp)
        {
            Console.WriteLine(CaptureOptions.Usage);
            return 0;
        }

        if (options.ListSlices)
        {
            foreach (var slice in RemainingSlices.All)
            {
                Console.WriteLine($"{slice.Id}: {slice.Name} ({slice.Status})");
            }

            return 0;
        }

        if (string.IsNullOrWhiteSpace(options.Scenario))
        {
            Console.Error.WriteLine("Missing --scenario. Use --help for usage.");
            return 2;
        }

        try
        {
            var runner = new ScenarioRunner(options);
            var result = runner.Run();
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return result.CaptureStatus == "complete" ? 0 : 1;
        }
        catch (Exception ex)
        {
            var result = CaptureResult.Blocked(
                options.Scenario,
                "exception",
                ex.Message,
                options.OutputRoot,
                options.Subject);
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return 1;
        }
    }
}

internal sealed class ScenarioRunner(CaptureOptions options)
{
    public CaptureResult Run()
    {
        Directory.CreateDirectory(options.OutputRoot);

        return options.Scenario.ToLowerInvariant() switch
        {
            "excel-autofilter" => RunExcelAutoFilterScenario(),
            "excel-number-format" => RunExcelNumberFormatScenario(),
            "excel-borders" => RunExcelPopupScenario("excel-borders", PrepareExcelBlankWorkbook, "%hb", "Net UI Tool Window"),
            "excel-context-menu" => RunExcelContextMenuScenario(),
            "excel-format-cells-dialog" => RunExcelFormatCellsDialogScenario(),
            "excel-data-validation-dropdown" => RunExcelDataValidationDropdownScenario(),
            "excel-open-dialog" => RunExcelDialogScenario("excel-open-dialog", PrepareExcelBlankWorkbook, "^{F12}", "#32770", "Open"),
            "excel-save-as-dialog" => RunExcelSaveAsDialogScenario(),
            "freex-open-dialog" => RunFreeXDialogScenario("freex-open-dialog", "^{F12}", "#32770", "Open"),
            "freex-save-as-dialog" => RunFreeXDialogScenario("freex-save-as-dialog", "{F12}", "#32770", "Save As"),
            "freex-format-cells-dialog" => RunFreeXFormatCellsDialogScenario(),
            // S3 native-dialog continuation scenarios.
            "freex-save-as-dialog-cancel" => RunFreeXDialogCancelScenario("freex-save-as-dialog-cancel", "{F12}", "#32770", "Save As"),
            "freex-save-as-overwrite-prompt" => RunFreeXSaveAsOverwritePromptScenario(),
            "freex-background-picker-cancel" => RunFreeXBackgroundPickerCancelScenario(),
            "freex-background-picker-select" => RunFreeXBackgroundPickerSelectScenario(),
            "freex-status-zoom-in-click" => RunFreeXMainWindowPointerScenario("freex-status-zoom-in-click", ClickAutomationIdExpectZoom("StatusZoomInButton", 105)),
            "freex-status-zoom-out-click" => RunFreeXMainWindowPointerScenario("freex-status-zoom-out-click", ClickAutomationIdExpectZoom("StatusZoomOutButton", 95)),
            "freex-status-zoom-slider-drag" => RunFreeXMainWindowPointerScenario("freex-status-zoom-slider-drag", DragFirstSliderExpectChangedZoom("Zoom", 100)),
            "freex-status-zoom-slider-rangevalue-set" => RunFreeXMainWindowPointerScenario("freex-status-zoom-slider-rangevalue-set", SetFirstSliderRangeValue("Zoom", 150)),
            "freex-status-ctrl-wheel-grid-zoom" => RunFreeXMainWindowPointerScenario("freex-status-ctrl-wheel-grid-zoom", CtrlWheelRelativeExpectZoom(0.36, 0.56, 120, 110)),
            "freex-sheet-tab-context-menu" => RunFreeXMainWindowPointerScenario("freex-sheet-tab-context-menu", RightClickNamedElement("Sheet1", ControlType.TabItem)),
            "freex-grid-drag-select" => RunFreeXMainWindowPointerScenario("freex-grid-drag-select", DragRelative(0.14, 0.56, 0.37, 0.69)),
            "freex-grid-row-column-resize" => RunFreeXMainWindowPointerScenario("freex-grid-row-column-resize", DragColumnAndRowResizeHandles()),
            "freex-grid-wheel-scroll" => RunFreeXMainWindowPointerScenario("freex-grid-wheel-scroll", WheelVerticalThenShiftHorizontal()),
            _ => CaptureResult.Blocked(options.Scenario, "unsupported-scenario", $"Unsupported scenario '{options.Scenario}'.", options.OutputRoot, options.Subject)
        };
    }

    private string? _lastResultValidation;

    private CaptureResult RunExcelAutoFilterScenario()
    {
        dynamic? excel = null;
        dynamic? workbook = null;
        int? pid = null;

        try
        {
            (excel, workbook) = CreateExcel();
            dynamic worksheet = PrepareExcelAutoFilter(excel);

            var hwnd = new IntPtr((int)excel.Hwnd);
            pid = NativeMethods.GetProcessId(hwnd);
            var guard = ForegroundGuard.FocusAndVerify(hwnd, pid.Value, "Excel", options.FocusTimeout);
            if (!guard.Success)
            {
                return BlockedWithGuard("excel-autofilter", guard, "before-input");
            }

            WindowInfo? popup = null;
            if (TryOpenExcelAutoFilterWithUia(hwnd))
            {
                Thread.Sleep(options.AfterInputDelay);
                popup = WindowFinder.FindProcessPopup(pid.Value, hwnd.ToInt64(), TimeSpan.FromMilliseconds(1200), 120, 80);
            }

            if (popup is null)
            {
                guard = ForegroundGuard.FocusAndVerify(hwnd, pid.Value, "Excel", options.FocusTimeout);
                if (!guard.Success)
                {
                    return BlockedWithGuard("excel-autofilter", guard, "before-com-sendkeys");
                }

                worksheet.Range["A1"].Activate();
                excel.SendKeys("%{DOWN}");
                Thread.Sleep(options.AfterInputDelay);
                popup = WindowFinder.FindForegroundProcessPopup(pid.Value, hwnd.ToInt64(), TimeSpan.FromMilliseconds(1200), 120, 80);
            }

            if (popup is null)
            {
                guard = ForegroundGuard.FocusAndVerify(hwnd, pid.Value, "Excel", options.FocusTimeout);
                if (!guard.Success)
                {
                    return BlockedWithGuard("excel-autofilter", guard, "before-winforms-sendkeys");
                }

                worksheet.Range["A1"].Activate();
                SendKeys.SendWait("%{DOWN}");
                Thread.Sleep(options.AfterInputDelay);
                popup = WindowFinder.FindForegroundProcessPopup(pid.Value, hwnd.ToInt64(), TimeSpan.FromMilliseconds(1200), 120, 80);
            }

            if (popup is null)
            {
                guard = ForegroundGuard.FocusAndVerify(hwnd, pid.Value, "Excel", options.FocusTimeout);
                if (!guard.Success)
                {
                    return BlockedWithGuard("excel-autofilter", guard, "before-coordinate-click");
                }

                ClickExcelAutoFilterHeaderDropdown(excel, worksheet);
                Thread.Sleep(options.AfterInputDelay);
                popup = WindowFinder.FindForegroundProcessPopup(pid.Value, hwnd.ToInt64(), options.PopupTimeout, 120, 80);
            }

            if (popup is null)
            {
                return CaptureResult.Blocked("excel-autofilter", "popup-not-found", "Did not detect foreground Excel AutoFilter popup after the guarded header-arrow click.", options.OutputRoot, "excel", guard);
            }

            return CaptureWindow("excel-autofilter", "excel", popup, guard, "complete");
        }
        finally
        {
            CloseExcel(excel, workbook);
            KillProcess(pid);
        }
    }

    private CaptureResult RunExcelPopupScenario(
        string scenario,
        Action<dynamic> prepare,
        string keys,
        string expectedPopupClass)
    {
        dynamic? excel = null;
        dynamic? workbook = null;
        int? pid = null;

        try
        {
            (excel, workbook) = CreateExcel();
            prepare(excel);

            var hwnd = new IntPtr((int)excel.Hwnd);
            pid = NativeMethods.GetProcessId(hwnd);
            var guard = ForegroundGuard.FocusAndVerify(hwnd, pid.Value, "Excel", options.FocusTimeout);
            if (!guard.Success)
            {
                return BlockedWithGuard(scenario, guard, "before-input");
            }

            SendKeys.SendWait(keys);
            Thread.Sleep(options.AfterInputDelay);

            var popup = WindowFinder.FindOwnedOrForegroundPopup(pid.Value, expectedPopupClass, options.PopupTimeout);
            if (popup is null)
            {
                return CaptureResult.Blocked(scenario, "popup-not-found", $"Did not detect foreground Excel popup class '{expectedPopupClass}'.", options.OutputRoot, "excel", guard);
            }

            return CaptureWindow(scenario, "excel", popup, guard, "complete");
        }
        finally
        {
            CloseExcel(excel, workbook);
            KillProcess(pid);
        }
    }

    private CaptureResult RunExcelNumberFormatScenario()
    {
        dynamic? excel = null;
        dynamic? workbook = null;
        int? pid = null;

        try
        {
            (excel, workbook) = CreateExcel();
            PrepareExcelBlankWorkbook(excel);

            var hwnd = new IntPtr((int)excel.Hwnd);
            pid = NativeMethods.GetProcessId(hwnd);
            var guard = ForegroundGuard.FocusAndVerify(hwnd, pid.Value, "Excel", options.FocusTimeout);
            if (!guard.Success)
            {
                return BlockedWithGuard("excel-number-format", guard, "before-input");
            }

            var expanded = TryExpandExcelNumberFormatGallery(hwnd);
            if (!expanded)
            {
                return CaptureResult.Blocked("excel-number-format", "uia-expand-failed", "Could not find or expand Excel NumberFormatGallery through UI Automation.", options.OutputRoot, "excel", guard);
            }

            Thread.Sleep(options.AfterInputDelay);

            var popup = WindowFinder.FindOwnedOrForegroundPopup(pid.Value, "Net UI Tool Window", options.PopupTimeout);
            if (popup is null)
            {
                return CaptureResult.Blocked("excel-number-format", "popup-not-found", "Did not detect foreground Excel Number Format popup after UIA ExpandCollapse.", options.OutputRoot, "excel", guard);
            }

            return CaptureWindow("excel-number-format", "excel", popup, guard, "complete");
        }
        finally
        {
            CloseExcel(excel, workbook);
            KillProcess(pid);
        }
    }

    private CaptureResult RunExcelContextMenuScenario()
    {
        dynamic? excel = null;
        dynamic? workbook = null;
        int? pid = null;

        try
        {
            (excel, workbook) = CreateExcel();
            dynamic worksheet = PrepareExcelContextMenuWorkbook(excel);

            var hwnd = new IntPtr((int)excel.Hwnd);
            pid = NativeMethods.GetProcessId(hwnd);
            var guard = ForegroundGuard.FocusAndVerify(hwnd, pid.Value, "Excel", options.FocusTimeout);
            if (!guard.Success)
            {
                return BlockedWithGuard("excel-context-menu", guard, "before-input");
            }

            RightClickExcelRangeCenter(excel, worksheet, "B2");
            Thread.Sleep(options.AfterInputDelay);

            var popup = WindowFinder.FindProcessPopup(pid.Value, hwnd.ToInt64(), options.PopupTimeout, 120, 120);
            if (popup is null)
            {
                return CaptureResult.Blocked("excel-context-menu", "popup-not-found", "Did not detect foreground Excel worksheet context menu after guarded right-click.", options.OutputRoot, "excel", guard);
            }

            return CaptureWindow("excel-context-menu", "excel", popup, guard, "complete");
        }
        finally
        {
            CloseExcel(excel, workbook);
            KillProcess(pid);
        }
    }

    private CaptureResult RunExcelFormatCellsDialogScenario()
    {
        dynamic? excel = null;
        dynamic? workbook = null;
        int? pid = null;

        try
        {
            (excel, workbook) = CreateExcel();
            PrepareExcelBlankWorkbook(excel);

            var hwnd = new IntPtr((int)excel.Hwnd);
            pid = NativeMethods.GetProcessId(hwnd);
            var guard = ForegroundGuard.FocusAndVerify(hwnd, pid.Value, "Excel", options.FocusTimeout);
            if (!guard.Success)
            {
                return BlockedWithGuard("excel-format-cells-dialog", guard, "before-input");
            }

            SendKeys.SendWait("%hoe");
            Thread.Sleep(options.AfterInputDelay);

            var dialog = WindowFinder.FindProcessWindow(
                pid.Value,
                window => window.Handle != hwnd.ToInt64() &&
                    window.Title.Contains("Format Cells", StringComparison.OrdinalIgnoreCase) &&
                    window.Bounds.Width > 350 &&
                    window.Bounds.Height > 250,
                options.PopupTimeout);
            if (dialog is null)
            {
                return CaptureResult.Blocked("excel-format-cells-dialog", "dialog-not-found", "Did not detect Excel Format Cells dialog after Alt,H,O,E.", options.OutputRoot, "excel", guard);
            }

            Thread.Sleep(options.AfterDialogDetectedDelay);
            return CaptureWindow("excel-format-cells-dialog", "excel", dialog, guard, "complete");
        }
        finally
        {
            CloseExcel(excel, workbook);
            KillProcess(pid);
        }
    }

    private CaptureResult RunExcelDataValidationDropdownScenario()
    {
        dynamic? excel = null;
        dynamic? workbook = null;
        int? pid = null;

        try
        {
            (excel, workbook) = CreateExcel();
            dynamic worksheet = PrepareExcelDataValidationDropdownWorkbook(excel);

            var hwnd = new IntPtr((int)excel.Hwnd);
            pid = NativeMethods.GetProcessId(hwnd);
            var guard = ForegroundGuard.FocusAndVerify(hwnd, pid.Value, "Excel", options.FocusTimeout);
            if (!guard.Success)
            {
                return BlockedWithGuard("excel-data-validation-dropdown", guard, "before-input");
            }

            worksheet.Range["A2"].Activate();
            SendKeys.SendWait("%{DOWN}");
            Thread.Sleep(options.AfterInputDelay);

            var popup = WindowFinder.FindForegroundProcessPopup(pid.Value, hwnd.ToInt64(), TimeSpan.FromMilliseconds(1200), 70, 40);
            if (popup is null)
            {
                guard = ForegroundGuard.FocusAndVerify(hwnd, pid.Value, "Excel", options.FocusTimeout);
                if (!guard.Success)
                {
                    return BlockedWithGuard("excel-data-validation-dropdown", guard, "before-coordinate-click");
                }

                ClickExcelCellDropdownArrow(excel, worksheet, "A2");
                Thread.Sleep(options.AfterInputDelay);
                popup = WindowFinder.FindForegroundProcessPopup(pid.Value, hwnd.ToInt64(), options.PopupTimeout, 70, 40);
            }

            if (popup is null)
            {
                return CaptureResult.Blocked("excel-data-validation-dropdown", "popup-not-found", "Did not detect foreground Excel Data Validation list dropdown after Alt+Down or guarded in-cell arrow click.", options.OutputRoot, "excel", guard);
            }

            return CaptureWindow("excel-data-validation-dropdown", "excel", popup, guard, "complete");
        }
        finally
        {
            CloseExcel(excel, workbook);
            KillProcess(pid);
        }
    }

    private CaptureResult RunExcelDialogScenario(
        string scenario,
        Action<dynamic> prepare,
        string keys,
        string expectedClass,
        string titleContains)
    {
        dynamic? excel = null;
        dynamic? workbook = null;
        int? pid = null;

        try
        {
            (excel, workbook) = CreateExcel();
            prepare(excel);

            var hwnd = new IntPtr((int)excel.Hwnd);
            pid = NativeMethods.GetProcessId(hwnd);
            var guard = ForegroundGuard.FocusAndVerify(hwnd, pid.Value, "Excel", options.FocusTimeout);
            if (!guard.Success)
            {
                return BlockedWithGuard(scenario, guard, "before-input");
            }

            SendKeys.SendWait(keys);
            Thread.Sleep(options.AfterInputDelay);

            var dialog = WindowFinder.FindProcessWindow(pid.Value, expectedClass, titleContains, options.PopupTimeout);
            if (dialog is null)
            {
                return CaptureResult.Blocked(scenario, "dialog-not-found", $"Did not detect Excel dialog class '{expectedClass}' title containing '{titleContains}'.", options.OutputRoot, "excel", guard);
            }

            Thread.Sleep(options.AfterDialogDetectedDelay);
            return CaptureWindow(scenario, "excel", dialog, guard, "complete");
        }
        finally
        {
            CloseExcel(excel, workbook);
            KillProcess(pid);
        }
    }

    private CaptureResult RunExcelSaveAsDialogScenario()
    {
        dynamic? excel = null;
        dynamic? workbook = null;
        int? pid = null;

        try
        {
            (excel, workbook) = CreateExcel();
            PrepareExcelBlankWorkbook(excel);

            var hwnd = new IntPtr((int)excel.Hwnd);
            pid = NativeMethods.GetProcessId(hwnd);
            var guard = ForegroundGuard.FocusAndVerify(hwnd, pid.Value, "Excel", options.FocusTimeout);
            if (!guard.Success)
            {
                return BlockedWithGuard("excel-save-as-dialog", guard, "before-input");
            }

            SendKeys.SendWait("{F12}");
            Thread.Sleep(options.AfterInputDelay);

            var dialog = WindowFinder.FindProcessWindow(
                pid.Value,
                window => window.Handle != hwnd.ToInt64() &&
                    (window.ClassName.Equals("NUIDialog", StringComparison.OrdinalIgnoreCase) ||
                    (window.ClassName.Equals("#32770", StringComparison.OrdinalIgnoreCase) &&
                     window.Title.Contains("Save As", StringComparison.OrdinalIgnoreCase))) &&
                    window.Bounds.Width > 400 &&
                    window.Bounds.Height > 250,
                options.PopupTimeout);
            if (dialog is null)
            {
                return CaptureResult.Blocked("excel-save-as-dialog", "dialog-not-found", "Did not detect Excel Save As NUIDialog or '#32770' dialog after F12.", options.OutputRoot, "excel", guard);
            }

            if (dialog.ClassName.Equals("NUIDialog", StringComparison.OrdinalIgnoreCase))
            {
                return CaptureResult.Blocked("excel-save-as-dialog", "nuidialog-not-capturable", "Detected an Office NUIDialog after F12, but it is not a capturable native Save As file dialog in this Office state.", options.OutputRoot, "excel", guard);
            }

            Thread.Sleep(options.AfterDialogDetectedDelay);
            return CaptureWindow("excel-save-as-dialog", "excel", dialog, guard, "complete");
        }
        finally
        {
            CloseExcel(excel, workbook);
            KillProcess(pid);
        }
    }

    private CaptureResult RunFreeXDialogScenario(
        string scenario,
        string keys,
        string expectedClass,
        string titleContains)
    {
        Process? process = null;

        try
        {
            var exePath = ResolveFreeXExePath();
            process = Process.Start(new ProcessStartInfo(exePath)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory
            });

            if (process is null)
            {
                return CaptureResult.Blocked(scenario, "launch-failed", $"Failed to launch '{exePath}'.", options.OutputRoot, "freex");
            }

            var window = WindowFinder.WaitForMainWindow(process.Id, options.LaunchTimeout);
            if (window is null)
            {
                return CaptureResult.Blocked(scenario, "window-not-found", $"FreeX process {process.Id} did not expose a visible main window.", options.OutputRoot, "freex");
            }

            var guard = ForegroundGuard.FocusAndVerify(new IntPtr(window.Handle), process.Id, "FreeX", options.FocusTimeout);
            if (!guard.Success)
            {
                return BlockedWithGuard(scenario, guard, "before-input");
            }

            SendKeys.SendWait(keys);
            Thread.Sleep(options.AfterInputDelay);

            var dialog = WindowFinder.FindProcessWindow(process.Id, expectedClass, titleContains, options.PopupTimeout);
            if (dialog is null)
            {
                return CaptureResult.Blocked(scenario, "dialog-not-found", $"Did not detect FreeX dialog class '{expectedClass}' title containing '{titleContains}'.", options.OutputRoot, "freex", guard);
            }

            Thread.Sleep(options.AfterDialogDetectedDelay);
            return CaptureWindow(scenario, "freex", dialog, guard, "complete");
        }
        finally
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    private CaptureResult RunFreeXFormatCellsDialogScenario()
    {
        Process? process = null;

        try
        {
            var exePath = ResolveFreeXExePath();
            process = Process.Start(new ProcessStartInfo(exePath)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory
            });

            if (process is null)
            {
                return CaptureResult.Blocked("freex-format-cells-dialog", "launch-failed", $"Failed to launch '{exePath}'.", options.OutputRoot, "freex");
            }

            var window = WindowFinder.WaitForMainWindow(process.Id, options.LaunchTimeout);
            if (window is null)
            {
                return CaptureResult.Blocked("freex-format-cells-dialog", "window-not-found", $"FreeX process {process.Id} did not expose a visible main window.", options.OutputRoot, "freex");
            }

            var handle = new IntPtr(window.Handle);
            var guard = ForegroundGuard.FocusAndVerify(handle, process.Id, "FreeX", options.FocusTimeout);
            if (!guard.Success)
            {
                return BlockedWithGuard("freex-format-cells-dialog", guard, "before-input");
            }

            SendCtrl1();
            Thread.Sleep(options.AfterInputDelay);

            var dialog = WindowFinder.FindProcessWindow(
                process.Id,
                candidate => candidate.Handle != window.Handle &&
                    candidate.Title.Contains("Format Cells", StringComparison.OrdinalIgnoreCase) &&
                    candidate.Bounds.Width > 350 &&
                    candidate.Bounds.Height > 250,
                options.PopupTimeout);
            if (dialog is null)
            {
                return CaptureResult.Blocked("freex-format-cells-dialog", "dialog-not-found", "Did not detect FreeX Format Cells dialog after Ctrl+1.", options.OutputRoot, "freex", guard);
            }

            Thread.Sleep(options.AfterDialogDetectedDelay);
            return CaptureWindow("freex-format-cells-dialog", "freex", dialog, guard, "complete");
        }
        finally
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    // S3 native-dialog continuation helpers.
    private CaptureResult RunFreeXDialogCancelScenario(
        string scenario,
        string keys,
        string expectedClass,
        string titleContains)
    {
        Process? process = null;

        try
        {
            var launch = LaunchFreeX(scenario);
            if (launch.Result is not null)
            {
                return launch.Result;
            }

            process = launch.Process!;
            var window = launch.Window!;
            var handle = new IntPtr(window.Handle);
            var guard = ForegroundGuard.FocusAndVerify(handle, process.Id, "FreeX", options.FocusTimeout);
            if (!guard.Success)
            {
                return BlockedWithGuard(scenario, guard, "before-input");
            }

            SendKeys.SendWait(keys);
            Thread.Sleep(options.AfterInputDelay);

            var dialog = WindowFinder.FindProcessWindow(process.Id, expectedClass, titleContains, options.PopupTimeout);
            if (dialog is null)
            {
                return CaptureResult.Blocked(scenario, "dialog-not-found", $"Did not detect FreeX dialog class '{expectedClass}' title containing '{titleContains}'.", options.OutputRoot, "freex", guard);
            }

            Thread.Sleep(options.AfterDialogDetectedDelay);
            var result = CaptureWindow(scenario, "freex", dialog, guard, "complete");
            SendKeys.SendWait("{ESC}");
            Thread.Sleep(options.AfterInputDelay);
            return AttachContinuationCapture(
                result,
                scenario,
                process.Id,
                handle,
                "FreeX",
                "Escape canceled the native dialog and returned foreground focus to FreeX.");
        }
        finally
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    private CaptureResult RunFreeXSaveAsOverwritePromptScenario()
    {
        Process? process = null;
        var existingPath = Path.Combine(options.OutputRoot, "s3-existing-save-as-overwrite.xlsx");
        File.WriteAllText(existingPath, "existing");

        try
        {
            var launch = LaunchFreeX("freex-save-as-overwrite-prompt");
            if (launch.Result is not null)
            {
                return launch.Result;
            }

            process = launch.Process!;
            var handle = new IntPtr(launch.Window!.Handle);
            var guard = ForegroundGuard.FocusAndVerify(handle, process.Id, "FreeX", options.FocusTimeout);
            if (!guard.Success)
            {
                return BlockedWithGuard("freex-save-as-overwrite-prompt", guard, "before-input");
            }

            SendKeys.SendWait("{F12}");
            Thread.Sleep(options.AfterInputDelay);
            var dialog = WindowFinder.FindProcessWindow(process.Id, "#32770", "Save As", options.PopupTimeout);
            if (dialog is null)
            {
                return CaptureResult.Blocked("freex-save-as-overwrite-prompt", "dialog-not-found", "Did not detect FreeX Save As common dialog before overwrite prompt.", options.OutputRoot, "freex", guard);
            }

            TypeDialogPath(existingPath);
            var prompt = WindowFinder.FindProcessWindow(
                process.Id,
                window => window.ClassName.Equals("#32770", StringComparison.OrdinalIgnoreCase) &&
                    window.Handle != dialog.Handle &&
                    (window.Title.Contains("Confirm", StringComparison.OrdinalIgnoreCase) ||
                     window.Title.Contains("Save As", StringComparison.OrdinalIgnoreCase) ||
                     window.Title.Contains("already exists", StringComparison.OrdinalIgnoreCase)),
                options.PopupTimeout);
            if (prompt is null)
            {
                return CaptureResult.Blocked("freex-save-as-overwrite-prompt", "overwrite-prompt-not-found", "Typed an existing .xlsx path but did not detect a native overwrite confirmation prompt.", options.OutputRoot, "freex", guard);
            }

            Thread.Sleep(options.AfterDialogDetectedDelay);
            var result = CaptureWindow("freex-save-as-overwrite-prompt", "freex", prompt, guard, "complete", $"Existing path used: {existingPath}");
            SendKeys.SendWait("{ESC}");
            Thread.Sleep(options.AfterInputDelay);
            return result with { OutputPath = existingPath };
        }
        finally
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    private CaptureResult RunFreeXBackgroundPickerCancelScenario()
    {
        return RunFreeXNativeDialogOpenedByAction(
            "freex-background-picker-cancel",
            OpenFreeXBackgroundPicker,
            "#32770",
            "Sheet Background",
            dialogContinuation: (process, mainHandle, result) =>
            {
                SendKeys.SendWait("{ESC}");
                Thread.Sleep(options.AfterInputDelay);
                return AttachContinuationCapture(
                    result,
                    "freex-background-picker-cancel",
                    process.Id,
                    mainHandle,
                    "FreeX",
                    "Escape canceled the sheet-background OpenFileDialog and returned foreground focus to FreeX.");
            });
    }

    private CaptureResult RunFreeXBackgroundPickerSelectScenario()
    {
        var imagePath = Path.Combine(options.OutputRoot, "s3-sheet-background.png");
        CreateTinyPng(imagePath);

        return RunFreeXNativeDialogOpenedByAction(
            "freex-background-picker-select",
            OpenFreeXBackgroundPicker,
            "#32770",
            "Sheet Background",
            dialogContinuation: (process, mainHandle, result) =>
            {
                TypeDialogPath(imagePath);
                Thread.Sleep(options.AfterInputDelay);
                return AttachContinuationCapture(
                    result,
                    "freex-background-picker-select",
                    process.Id,
                    mainHandle,
                    "FreeX",
                    $"Selected supported PNG background path and returned focus to FreeX: {imagePath}") with
                {
                    OutputPath = imagePath
                };
            });
    }

    private CaptureResult RunFreeXNativeDialogOpenedByAction(
        string scenario,
        Func<Process, IntPtr, ForegroundGuardResult, CaptureResult?> openAction,
        string expectedClass,
        string titleContains,
        Func<Process, IntPtr, CaptureResult, CaptureResult>? dialogContinuation = null)
    {
        Process? process = null;

        try
        {
            var launch = LaunchFreeX(scenario);
            if (launch.Result is not null)
            {
                return launch.Result;
            }

            process = launch.Process!;
            var mainHandle = new IntPtr(launch.Window!.Handle);
            var guard = ForegroundGuard.FocusAndVerify(mainHandle, process.Id, "FreeX", options.FocusTimeout);
            if (!guard.Success)
            {
                return BlockedWithGuard(scenario, guard, "before-open-action");
            }

            var blocked = openAction(process, mainHandle, guard);
            if (blocked is not null)
            {
                return blocked;
            }

            var dialog = WindowFinder.FindProcessWindow(process.Id, expectedClass, titleContains, options.PopupTimeout);
            if (dialog is null)
            {
                return CaptureResult.Blocked(scenario, "dialog-not-found", $"Did not detect FreeX native dialog class '{expectedClass}' title containing '{titleContains}'.", options.OutputRoot, "freex", guard);
            }

            Thread.Sleep(options.AfterDialogDetectedDelay);
            var result = CaptureWindow(scenario, "freex", dialog, guard, "complete");
            return dialogContinuation is null ? result : dialogContinuation(process, mainHandle, result);
        }
        finally
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    private (Process? Process, WindowInfo? Window, CaptureResult? Result) LaunchFreeX(string scenario)
    {
        var exePath = ResolveFreeXExePath();
        var process = Process.Start(new ProcessStartInfo(exePath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory
        });

        if (process is null)
        {
            return (null, null, CaptureResult.Blocked(scenario, "launch-failed", $"Failed to launch '{exePath}'.", options.OutputRoot, "freex"));
        }

        var window = WindowFinder.WaitForMainWindow(process.Id, options.LaunchTimeout);
        if (window is null)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            return (process, null, CaptureResult.Blocked(scenario, "window-not-found", $"FreeX process {process.Id} did not expose a visible main window.", options.OutputRoot, "freex"));
        }

        return (process, window, null);
    }

    private CaptureResult? OpenFreeXBackgroundPicker(Process process, IntPtr mainHandle, ForegroundGuardResult guard)
    {
        var pageLayoutTab = FindDescendantByNameAndType(mainHandle, "Page Layout", ControlType.TabItem);
        if (pageLayoutTab is not null)
        {
            TrySelectOrInvoke(pageLayoutTab);
            Thread.Sleep(options.AfterInputDelay);
        }
        else
        {
            SendKeys.SendWait("%p");
            Thread.Sleep(options.AfterInputDelay);
        }

        var backgroundButton = FindDescendantByNameAndType(mainHandle, "Background", ControlType.Button);
        if (backgroundButton is null)
        {
            SendKeys.SendWait("bg");
            Thread.Sleep(options.AfterInputDelay);
            return null;
        }

        return InvokeOrClickElement(options.Scenario, process.Id, mainHandle, backgroundButton, "freex");
    }

    private CaptureResult AttachContinuationCapture(
        CaptureResult result,
        string scenario,
        int processId,
        IntPtr mainHandle,
        string titleContains,
        string validation)
    {
        var guard = ForegroundGuard.FocusAndVerify(mainHandle, processId, titleContains, options.FocusTimeout);
        if (!guard.Success)
        {
            var blocked = result with
            {
                CaptureStatus = "blocked",
                ForegroundGuard = guard,
                BlockReason = $"focus-return-failed: {guard.Reason}",
                ResultValidation = validation
            };
            RewriteManifest(blocked);
            return blocked;
        }

        var window = WindowFinder.GetWindowInfo(mainHandle);
        if (window is null)
        {
            var blocked = result with
            {
                CaptureStatus = "blocked",
                ForegroundGuard = guard,
                BlockReason = "focus-return-window-not-found: Could not read FreeX main window after continuation.",
                ResultValidation = validation
            };
            RewriteManifest(blocked);
            return blocked;
        }

        var scenarioDir = Path.Combine(options.OutputRoot, scenario);
        Directory.CreateDirectory(scenarioDir);
        var continuationPath = Path.Combine(scenarioDir, $"{scenario}_continuation_{DateTime.UtcNow:yyyyMMdd_HHmmss}.png");
        ScreenshotCapture.Capture(window.Bounds, continuationPath);

        var completed = result with
        {
            ForegroundGuard = guard,
            ContinuationScreenshotPath = continuationPath,
            ResultValidation = validation
        };
        RewriteManifest(completed);
        return completed;
    }

    private static void RewriteManifest(CaptureResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.ManifestPath))
        {
            File.WriteAllText(result.ManifestPath, JsonSerializer.Serialize(result, ProgramAccessor.JsonOptions));
        }
    }

    private static void TypeDialogPath(string path)
    {
        Clipboard.SetText(path);
        Thread.Sleep(100);
        SendKeys.SendWait("^v");
        Thread.Sleep(150);
        SendKeys.SendWait("{ENTER}");
    }

    private static void CreateTinyPng(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        using var bitmap = new Bitmap(8, 8);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.LightSteelBlue);
            using var brush = new SolidBrush(Color.DarkSlateBlue);
            graphics.FillRectangle(brush, 2, 2, 4, 4);
        }

        bitmap.Save(path, ImageFormat.Png);
    }

    private CaptureResult? InvokeOrClickElement(string scenario, int processId, IntPtr ownerHandle, AutomationElement element, string subject)
    {
        if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var patternObject) &&
            patternObject is InvokePattern invoke)
        {
            invoke.Invoke();
            Thread.Sleep(options.AfterInputDelay);
            return null;
        }

        if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var selectionPatternObject) &&
            selectionPatternObject is SelectionItemPattern selection)
        {
            selection.Select();
            Thread.Sleep(options.AfterInputDelay);
            return null;
        }

        var bounds = element.Current.BoundingRectangle;
        if (bounds.IsEmpty || bounds.Width < 1 || bounds.Height < 1)
        {
            return CaptureResult.Blocked(scenario, "uia-target-bounds-invalid", $"Element bounds were not usable: {bounds}.", options.OutputRoot, subject);
        }

        var title = subject.Equals("excel", StringComparison.OrdinalIgnoreCase) ? "Excel" : "FreeX";
        var guard = ForegroundGuard.FocusAndVerify(ownerHandle, processId, title, options.FocusTimeout);
        if (!guard.Success)
        {
            return CaptureResult.Blocked(scenario, "foreground-guard-failed", "Foreground guard failed before UIA fallback click.", options.OutputRoot, subject, guard);
        }

        NativeMethods.SetCursorPos((int)(bounds.Left + bounds.Width / 2.0), (int)(bounds.Top + bounds.Height / 2.0));
        Thread.Sleep(100);
        NativeMethods.MouseEvent(NativeMethods.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(60);
        NativeMethods.MouseEvent(NativeMethods.MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(options.AfterInputDelay);
        return null;
    }

    private static AutomationElement? FindDescendantByNameAndType(IntPtr handle, string nameContains, ControlType controlType)
    {
        var root = AutomationElement.FromHandle(handle);
        return root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, controlType))
            .Cast<AutomationElement>()
            .FirstOrDefault(candidate => (candidate.Current.Name ?? string.Empty).Contains(nameContains, StringComparison.OrdinalIgnoreCase));
    }

    private static void TrySelectOrInvoke(AutomationElement element)
    {
        if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var selectionPatternObject) &&
            selectionPatternObject is SelectionItemPattern selection)
        {
            selection.Select();
            return;
        }

        if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var invokePatternObject) &&
            invokePatternObject is InvokePattern invoke)
        {
            invoke.Invoke();
        }
    }

    private CaptureResult RunFreeXMainWindowPointerScenario(
        string scenario,
        Func<IntPtr, int, WindowInfo, ForegroundGuardResult, CaptureResult?> action)
    {
        Process? process = null;
        _lastResultValidation = null;

        try
        {
            var exePath = ResolveFreeXExePath();
            process = Process.Start(new ProcessStartInfo(exePath)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory
            });

            if (process is null)
            {
                return CaptureResult.Blocked(scenario, "launch-failed", $"Failed to launch '{exePath}'.", options.OutputRoot, "freex");
            }

            var window = WindowFinder.WaitForMainWindow(process.Id, options.LaunchTimeout);
            if (window is null)
            {
                return CaptureResult.Blocked(scenario, "window-not-found", $"FreeX process {process.Id} did not expose a visible main window.", options.OutputRoot, "freex");
            }

            var handle = new IntPtr(window.Handle);
            var guard = ForegroundGuard.FocusAndVerify(handle, process.Id, "FreeX", options.FocusTimeout);
            if (!guard.Success)
            {
                return BlockedWithGuard(scenario, guard, "before-pointer-input");
            }

            var blocked = action(handle, process.Id, window, guard);
            if (blocked is not null)
            {
                return blocked;
            }

            Thread.Sleep(options.AfterDialogDetectedDelay);
            var refreshedWindow = WindowFinder.GetWindowInfo(handle) ?? window;
            guard = ForegroundGuard.FocusAndVerify(handle, process.Id, "FreeX", options.FocusTimeout);
            if (!guard.Success)
            {
                return BlockedWithGuard(scenario, guard, "before-capture");
            }

            return CaptureWindow(scenario, "freex", refreshedWindow, guard, "complete", _lastResultValidation);
        }
        finally
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    private Func<IntPtr, int, WindowInfo, ForegroundGuardResult, CaptureResult?> ClickAutomationIdExpectZoom(
        string automationId,
        double expectedSliderValue)
        => (handle, processId, _, guard) =>
        {
            var root = AutomationElement.FromHandle(handle);
            var element = root.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));
            if (element is null)
            {
                return CaptureResult.Blocked(options.Scenario, "uia-target-not-found", $"Could not find AutomationId '{automationId}'.", options.OutputRoot, "freex", guard);
            }

            var blocked = GuardedClickElement(options.Scenario, processId, handle, element, MouseButtonKind.Left);
            if (blocked is not null)
            {
                return blocked;
            }

            return ValidateZoomSliderValue(handle, expectedSliderValue, $"AutomationId '{automationId}' click");
        };

    private Func<IntPtr, int, WindowInfo, ForegroundGuardResult, CaptureResult?> DragFirstSliderExpectChangedZoom(
        string nameContains,
        double originalSliderValue)
        => (handle, processId, _, guard) =>
        {
            var slider = FindFirstSlider(handle, nameContains);
            if (slider is null)
            {
                return CaptureResult.Blocked(options.Scenario, "uia-target-not-found", $"Could not find a slider named like '{nameContains}'.", options.OutputRoot, "freex", guard);
            }

            var bounds = slider.Current.BoundingRectangle;
            if (bounds.IsEmpty || bounds.Width < 20 || bounds.Height < 8)
            {
                return CaptureResult.Blocked(options.Scenario, "uia-target-bounds-invalid", $"Slider bounds were not usable: {bounds}.", options.OutputRoot, "freex", guard);
            }

            var blocked = GuardedDrag(
                options.Scenario,
                processId,
                handle,
                (int)(bounds.Left + bounds.Width * 0.35),
                (int)(bounds.Top + bounds.Height / 2.0),
                (int)(bounds.Left + bounds.Width * 0.82),
                (int)(bounds.Top + bounds.Height / 2.0));
            if (blocked is not null)
            {
                return blocked;
            }

            return ValidateZoomSliderChanged(handle, originalSliderValue, "foreground slider drag");
        };

    private Func<IntPtr, int, WindowInfo, ForegroundGuardResult, CaptureResult?> SetFirstSliderRangeValue(
        string nameContains,
        double targetSliderValue)
        => (handle, processId, _, guard) =>
        {
            var slider = FindFirstSlider(handle, nameContains);
            if (slider is null)
            {
                return CaptureResult.Blocked(options.Scenario, "uia-target-not-found", $"Could not find a slider named like '{nameContains}'.", options.OutputRoot, "freex", guard);
            }

            if (!slider.TryGetCurrentPattern(RangeValuePattern.Pattern, out var patternObject) ||
                patternObject is not RangeValuePattern rangePattern)
            {
                return CaptureResult.Blocked(options.Scenario, "uia-rangevalue-unavailable", $"Slider named like '{nameContains}' did not expose RangeValuePattern.", options.OutputRoot, "freex", guard);
            }

            guard = ForegroundGuard.FocusAndVerify(handle, processId, "FreeX", options.FocusTimeout);
            if (!guard.Success)
            {
                return BlockedWithGuard(options.Scenario, guard, "before-uia-rangevalue-set");
            }

            rangePattern.SetValue(targetSliderValue);
            Thread.Sleep(options.AfterInputDelay);

            return ValidateZoomSliderValue(handle, targetSliderValue, $"native UIA RangeValue.SetValue({targetSliderValue:0.###})");
        };

    private Func<IntPtr, int, WindowInfo, ForegroundGuardResult, CaptureResult?> CtrlWheelRelativeExpectZoom(
        double x,
        double y,
        int wheelDelta,
        double expectedSliderValue)
        => (handle, processId, window, _) =>
        {
            var guard = ForegroundGuard.FocusAndVerify(handle, processId, "FreeX", options.FocusTimeout);
            if (!guard.Success)
            {
                return BlockedWithGuard(options.Scenario, guard, "before-ctrl-keydown");
            }

            NativeMethods.SetCursorPos(
                window.Bounds.Left + (int)(window.Bounds.Width * x),
                window.Bounds.Top + (int)(window.Bounds.Height * y));
            NativeMethods.KeybdEvent(NativeMethods.VK_CONTROL, 0, 0, UIntPtr.Zero);
            Thread.Sleep(80);

            try
            {
                guard = ForegroundGuard.FocusAndVerify(handle, processId, "FreeX", options.FocusTimeout);
                if (!guard.Success)
                {
                    return BlockedWithGuard(options.Scenario, guard, "before-ctrl-wheel");
                }

                NativeMethods.MouseEvent(NativeMethods.MOUSEEVENTF_WHEEL, 0, 0, wheelDelta, UIntPtr.Zero);
                Thread.Sleep(options.AfterInputDelay);
            }
            finally
            {
                NativeMethods.KeybdEvent(NativeMethods.VK_CONTROL, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
            }

            return ValidateZoomSliderValue(handle, expectedSliderValue, $"Ctrl+wheel delta {wheelDelta} over worksheet grid");
        };

    private Func<IntPtr, int, WindowInfo, ForegroundGuardResult, CaptureResult?> RightClickNamedElement(string name, ControlType controlType)
        => (handle, processId, _, guard) =>
        {
            var root = AutomationElement.FromHandle(handle);
            var condition = new AndCondition(
                new PropertyCondition(AutomationElement.NameProperty, name),
                new PropertyCondition(AutomationElement.ControlTypeProperty, controlType));
            var element = root.FindFirst(TreeScope.Descendants, condition)
                ?? root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, name))
                    .Cast<AutomationElement>()
                    .FirstOrDefault(candidate => !candidate.Current.BoundingRectangle.IsEmpty);
            if (element is null)
            {
                return CaptureResult.Blocked(options.Scenario, "uia-target-not-found", $"Could not find element named '{name}'.", options.OutputRoot, "freex", guard);
            }

            return GuardedClickElement(options.Scenario, processId, handle, element, MouseButtonKind.Right);
        };

    private Func<IntPtr, int, WindowInfo, ForegroundGuardResult, CaptureResult?> DragColumnAndRowResizeHandles()
        => (handle, processId, _, guard) =>
        {
            if (!TryGetCellBounds(handle, "Cell_A1", out var originalA1) ||
                !TryGetCellBounds(handle, "Cell_A2", out var ignoredA2Bounds))
            {
                return CaptureResult.Blocked(options.Scenario, "uia-cell-bounds-unavailable", "Could not resolve initial A1/A2 bounds for resize drag.", options.OutputRoot, "freex", guard);
            }

            var columnBlocked = GuardedDrag(
                options.Scenario,
                processId,
                handle,
                (int)originalA1.Right,
                (int)(originalA1.Top - 9),
                (int)(originalA1.Right + 48),
                (int)(originalA1.Top - 9));
            if (columnBlocked is not null)
            {
                return columnBlocked;
            }

            if (!TryGetCellBounds(handle, "Cell_A1", out var widenedA1))
            {
                return CaptureResult.Blocked(options.Scenario, "uia-cell-bounds-unavailable", "Could not resolve A1 bounds after column resize.", options.OutputRoot, "freex", guard);
            }

            var rowBlocked = GuardedDrag(
                options.Scenario,
                processId,
                handle,
                (int)(widenedA1.Left - 15),
                (int)widenedA1.Bottom,
                (int)(widenedA1.Left - 15),
                (int)(widenedA1.Bottom + 18));
            if (rowBlocked is not null)
            {
                return rowBlocked;
            }

            if (!TryGetCellBounds(handle, "Cell_A1", out var resizedA1))
            {
                return CaptureResult.Blocked(options.Scenario, "uia-cell-bounds-unavailable", "Could not resolve A1 bounds after row resize.", options.OutputRoot, "freex", guard);
            }

            var widthDelta = resizedA1.Width - originalA1.Width;
            var heightDelta = resizedA1.Height - originalA1.Height;
            if (widthDelta < 20 || heightDelta < 8)
            {
                return CaptureResult.Blocked(
                    options.Scenario,
                    "resize-validation-failed",
                    $"Expected A1 bounds to grow after header drags; width delta {widthDelta:0.###}, height delta {heightDelta:0.###}.",
                    options.OutputRoot,
                    "freex");
            }

            _lastResultValidation = $"foreground row/column resize drags; A1 width {originalA1.Width:0.###}->{resizedA1.Width:0.###}, height {originalA1.Height:0.###}->{resizedA1.Height:0.###}";
            return null;
        };

    private Func<IntPtr, int, WindowInfo, ForegroundGuardResult, CaptureResult?> WheelVerticalThenShiftHorizontal()
        => (handle, processId, _, guard) =>
        {
            if (!TryGetCellBounds(handle, "Cell_C12", out var wheelTarget))
            {
                return CaptureResult.Blocked(options.Scenario, "uia-cell-bounds-unavailable", "Could not resolve C12 bounds for wheel target.", options.OutputRoot, "freex", guard);
            }

            if (!TryGetScrollBarValue(handle, "Vertical", out var verticalBefore))
            {
                return CaptureResult.Blocked(options.Scenario, "scrollbar-validation-unavailable", "Could not read the vertical worksheet scrollbar before wheel input.", options.OutputRoot, "freex", guard);
            }

            var blocked = GuardedWheel(options.Scenario, processId, handle, CenterX(wheelTarget), CenterY(wheelTarget), -360, holdShift: false);
            if (blocked is not null)
            {
                return blocked;
            }

            if (!TryGetScrollBarValue(handle, "Vertical", out var verticalAfter) ||
                verticalAfter <= verticalBefore)
            {
                return CaptureResult.Blocked(options.Scenario, "wheel-validation-failed", $"Expected vertical scrollbar to increase after wheel; before {verticalBefore:0.###}, after {verticalAfter:0.###}.", options.OutputRoot, "freex");
            }

            if (!TryGetScrollBarValue(handle, "Horizontal", out var horizontalBefore))
            {
                return CaptureResult.Blocked(options.Scenario, "scrollbar-validation-unavailable", "Could not read the horizontal worksheet scrollbar before Shift+wheel input.", options.OutputRoot, "freex", guard);
            }

            blocked = GuardedWheel(options.Scenario, processId, handle, CenterX(wheelTarget), CenterY(wheelTarget), -360, holdShift: true);
            if (blocked is not null)
            {
                return blocked;
            }

            if (!TryGetScrollBarValue(handle, "Horizontal", out var horizontalAfter) ||
                horizontalAfter <= horizontalBefore)
            {
                return CaptureResult.Blocked(options.Scenario, "shift-wheel-validation-failed", $"Expected horizontal scrollbar to increase after Shift+wheel; before {horizontalBefore:0.###}, after {horizontalAfter:0.###}.", options.OutputRoot, "freex");
            }

            _lastResultValidation = $"foreground wheel scroll; vertical scrollbar {verticalBefore:0.###}->{verticalAfter:0.###}; Shift+wheel horizontal scrollbar {horizontalBefore:0.###}->{horizontalAfter:0.###}";
            return null;
        };

    private Func<IntPtr, int, WindowInfo, ForegroundGuardResult, CaptureResult?> DragRelative(double startX, double startY, double endX, double endY)
        => (handle, processId, window, _) => GuardedDrag(
            options.Scenario,
            processId,
            handle,
            window.Bounds.Left + (int)(window.Bounds.Width * startX),
            window.Bounds.Top + (int)(window.Bounds.Height * startY),
            window.Bounds.Left + (int)(window.Bounds.Width * endX),
            window.Bounds.Top + (int)(window.Bounds.Height * endY));

    private CaptureResult? GuardedClickElement(string scenario, int processId, IntPtr handle, AutomationElement element, MouseButtonKind button)
    {
        var bounds = element.Current.BoundingRectangle;
        if (bounds.IsEmpty || bounds.Width < 1 || bounds.Height < 1)
        {
            return CaptureResult.Blocked(scenario, "uia-target-bounds-invalid", $"Element bounds were not usable: {bounds}.", options.OutputRoot, "freex");
        }

        var guard = ForegroundGuard.FocusAndVerify(handle, processId, "FreeX", options.FocusTimeout);
        if (!guard.Success)
        {
            return BlockedWithGuard(scenario, guard, "before-pointer-click");
        }

        var x = (int)(bounds.Left + bounds.Width / 2.0);
        var y = (int)(bounds.Top + bounds.Height / 2.0);
        NativeMethods.SetCursorPos(x, y);
        Thread.Sleep(100);
        NativeMethods.MouseEvent(button == MouseButtonKind.Left ? NativeMethods.MOUSEEVENTF_LEFTDOWN : NativeMethods.MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(60);
        NativeMethods.MouseEvent(button == MouseButtonKind.Left ? NativeMethods.MOUSEEVENTF_LEFTUP : NativeMethods.MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(options.AfterInputDelay);
        return null;
    }

    private CaptureResult? GuardedWheel(string scenario, int processId, IntPtr handle, int x, int y, int wheelDelta, bool holdShift)
    {
        var guard = ForegroundGuard.FocusAndVerify(handle, processId, "FreeX", options.FocusTimeout);
        if (!guard.Success)
        {
            return BlockedWithGuard(scenario, guard, "before-pointer-wheel");
        }

        NativeMethods.SetCursorPos(x, y);
        Thread.Sleep(100);
        if (holdShift)
        {
            NativeMethods.KeybdEvent(NativeMethods.VK_SHIFT, 0, 0, UIntPtr.Zero);
            Thread.Sleep(80);
        }

        try
        {
            guard = ForegroundGuard.FocusAndVerify(handle, processId, "FreeX", options.FocusTimeout);
            if (!guard.Success)
            {
                return BlockedWithGuard(scenario, guard, holdShift ? "before-shift-wheel" : "before-wheel");
            }

            NativeMethods.MouseEvent(NativeMethods.MOUSEEVENTF_WHEEL, 0, 0, wheelDelta, UIntPtr.Zero);
            Thread.Sleep(options.AfterInputDelay);
        }
        finally
        {
            if (holdShift)
            {
                NativeMethods.KeybdEvent(NativeMethods.VK_SHIFT, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
        }

        return null;
    }
    private CaptureResult? GuardedDrag(string scenario, int processId, IntPtr handle, int startX, int startY, int endX, int endY)
    {
        var guard = ForegroundGuard.FocusAndVerify(handle, processId, "FreeX", options.FocusTimeout);
        if (!guard.Success)
        {
            return BlockedWithGuard(scenario, guard, "before-pointer-drag");
        }

        NativeMethods.SetCursorPos(startX, startY);
        Thread.Sleep(120);
        NativeMethods.MouseEvent(NativeMethods.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        const int steps = 12;
        for (var i = 1; i <= steps; i++)
        {
            var x = startX + (endX - startX) * i / steps;
            var y = startY + (endY - startY) * i / steps;
            NativeMethods.SetCursorPos(x, y);
            NativeMethods.MouseEvent(NativeMethods.MOUSEEVENTF_MOVE, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(35);
        }

        Thread.Sleep(80);
        NativeMethods.MouseEvent(NativeMethods.MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(options.AfterInputDelay);
        return null;
    }

    private CaptureResult? ValidateZoomSliderValue(IntPtr handle, double expectedSliderValue, string trigger)
    {
        var slider = FindFirstSlider(handle, "Zoom");
        if (slider is null || !TryGetRangeValue(slider, out var actualSliderValue))
        {
            return CaptureResult.Blocked(options.Scenario, "zoom-validation-unavailable", "Could not read the Zoom slider RangeValue after input.", options.OutputRoot, "freex");
        }

        if (Math.Abs(actualSliderValue - expectedSliderValue) > 0.75)
        {
            return CaptureResult.Blocked(
                options.Scenario,
                "zoom-validation-failed",
                $"Expected Zoom slider value near {expectedSliderValue:0.###} after {trigger}, but UIA reported {actualSliderValue:0.###}.",
                options.OutputRoot,
                "freex");
        }

        var zoomPercent = SliderToZoomPercent(actualSliderValue);
        _lastResultValidation = $"{trigger}; UIA Zoom slider={actualSliderValue:0.###}; expected slider={expectedSliderValue:0.###}; expected visible zoom text about {zoomPercent:0}%";
        return null;
    }

    private CaptureResult? ValidateZoomSliderChanged(IntPtr handle, double originalSliderValue, string trigger)
    {
        var slider = FindFirstSlider(handle, "Zoom");
        if (slider is null || !TryGetRangeValue(slider, out var actualSliderValue))
        {
            return CaptureResult.Blocked(options.Scenario, "zoom-validation-unavailable", "Could not read the Zoom slider RangeValue after input.", options.OutputRoot, "freex");
        }

        if (Math.Abs(actualSliderValue - originalSliderValue) < 2.0)
        {
            return CaptureResult.Blocked(
                options.Scenario,
                "zoom-validation-failed",
                $"Expected Zoom slider value to move away from {originalSliderValue:0.###} after {trigger}, but UIA reported {actualSliderValue:0.###}.",
                options.OutputRoot,
                "freex");
        }

        var zoomPercent = SliderToZoomPercent(actualSliderValue);
        _lastResultValidation = $"{trigger}; UIA Zoom slider moved from {originalSliderValue:0.###} to {actualSliderValue:0.###}; expected visible zoom text about {zoomPercent:0}%";
        return null;
    }

    private static AutomationElement? FindFirstSlider(IntPtr handle, string nameContains)
    {
        var root = AutomationElement.FromHandle(handle);
        var sliders = root.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Slider));
        return sliders
            .Cast<AutomationElement>()
            .FirstOrDefault(candidate => candidate.Current.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryGetRangeValue(AutomationElement element, out double value)
    {
        value = 0;
        if (!element.TryGetCurrentPattern(RangeValuePattern.Pattern, out var patternObject) ||
            patternObject is not RangeValuePattern rangePattern)
        {
            return false;
        }

        value = rangePattern.Current.Value;
        return true;
    }

    private static double SliderToZoomPercent(double sliderValue)
    {
        sliderValue = Math.Max(0, Math.Min(200, sliderValue));
        return sliderValue <= 100
            ? 10 + sliderValue / 100 * 90
            : 100 + (sliderValue - 100) / 100 * 300;
    }

    private static AutomationElement? FindElementByAutomationId(IntPtr handle, string automationId)
    {
        var root = AutomationElement.FromHandle(handle);
        return root.FindFirst(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));
    }

    private static bool TryGetCellBounds(IntPtr handle, string cellId, out System.Windows.Rect bounds)
    {
        bounds = default;
        var cell = FindElementByAutomationId(handle, cellId);
        if (cell is null)
        {
            return false;
        }

        bounds = cell.Current.BoundingRectangle;
        return !bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0;
    }

    private static bool TryGetScrollBarValue(IntPtr handle, string nameContains, out double value)
    {
        value = 0;
        var root = AutomationElement.FromHandle(handle);
        var scrollbars = root.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ScrollBar));
        foreach (AutomationElement scrollbar in scrollbars)
        {
            if (!scrollbar.Current.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return TryGetRangeValue(scrollbar, out value);
        }

        return false;
    }

    private static int CenterX(System.Windows.Rect bounds) => (int)(bounds.Left + bounds.Width / 2.0);

    private static int CenterY(System.Windows.Rect bounds) => (int)(bounds.Top + bounds.Height / 2.0);

    private CaptureResult CaptureWindow(string scenario, string subject, WindowInfo window, ForegroundGuardResult guard, string status, string? resultValidation = null)
    {
        var scenarioDir = Path.Combine(options.OutputRoot, scenario);
        Directory.CreateDirectory(scenarioDir);

        var fileName = $"{scenario}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.png";
        var filePath = Path.Combine(scenarioDir, fileName);
        ScreenshotCapture.Capture(window.Bounds, filePath);

        var result = new CaptureResult(
            scenario,
            subject,
            status,
            "foreground-guarded-uia-win32",
            filePath,
            window,
            guard,
            null,
            DateTimeOffset.UtcNow)
        {
            ResultValidation = resultValidation
        };

        var manifestPath = Path.Combine(scenarioDir, $"{scenario}_manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(result with { ManifestPath = manifestPath }, ProgramAccessor.JsonOptions));
        return result with { ManifestPath = manifestPath };
    }

    private CaptureResult BlockedWithGuard(string scenario, ForegroundGuardResult guard, string phase)
        => CaptureResult.Blocked(scenario, "foreground-guard-failed", $"Foreground guard failed during {phase}.", options.OutputRoot, options.Subject, guard);

    private static (dynamic Excel, dynamic Workbook) CreateExcel()
    {
        var excelType = Type.GetTypeFromProgID("Excel.Application")
            ?? throw new InvalidOperationException("Excel.Application COM ProgID is not available.");
        dynamic excel = Activator.CreateInstance(excelType)
            ?? throw new InvalidOperationException("Failed to create Excel.Application.");
        excel.Visible = true;
        excel.DisplayAlerts = false;
        dynamic workbook = excel.Workbooks.Add();
        return (excel, workbook);
    }

    private static void PrepareExcelBlankWorkbook(dynamic excel)
    {
        dynamic worksheet = excel.ActiveSheet;
        worksheet.Range["A1"].Value2 = "score";
        worksheet.Range["A2"].Value2 = 1;
        worksheet.Range["A3"].Value2 = 2;
        worksheet.Range["A4"].Value2 = 3;
        worksheet.Range["A1"].Select();
    }

    private static dynamic PrepareExcelAutoFilter(dynamic excel)
    {
        dynamic worksheet = excel.ActiveSheet;
        worksheet.Range["A1"].Value2 = "score";
        worksheet.Range["B1"].Value2 = "region";
        worksheet.Range["C1"].Value2 = "item";
        worksheet.Range["D1"].Value2 = "amount";
        worksheet.Range["A2"].Value2 = 1;
        worksheet.Range["B2"].Value2 = "East";
        worksheet.Range["C2"].Value2 = "Alpha";
        worksheet.Range["D2"].Value2 = 10;
        worksheet.Range["A3"].Value2 = 2;
        worksheet.Range["B3"].Value2 = "West";
        worksheet.Range["C3"].Value2 = "Beta";
        worksheet.Range["D3"].Value2 = 20;
        worksheet.Range["A4"].Value2 = 3;
        worksheet.Range["B4"].Value2 = "East";
        worksheet.Range["C4"].Value2 = "Gamma";
        worksheet.Range["D4"].Value2 = 30;
        worksheet.Range["A5"].Value2 = 4;
        worksheet.Range["B5"].Value2 = "West";
        worksheet.Range["C5"].Value2 = "Delta";
        worksheet.Range["D5"].Value2 = 40;
        worksheet.Range["A6"].Value2 = string.Empty;
        worksheet.Range["B6"].Value2 = "North";
        worksheet.Range["C6"].Value2 = "Blank score";
        worksheet.Range["D6"].Value2 = 50;
        dynamic range = worksheet.Range["A1:D6"];
        range.AutoFilter();
        worksheet.Range["A:D"].EntireColumn.AutoFit();
        worksheet.Range["A1"].Select();
        return worksheet;
    }

    private static dynamic PrepareExcelContextMenuWorkbook(dynamic excel)
    {
        dynamic worksheet = excel.ActiveSheet;
        worksheet.Range["A1"].Value2 = "Region";
        worksheet.Range["B1"].Value2 = "Score";
        worksheet.Range["C1"].Value2 = "Note";
        worksheet.Range["A2"].Value2 = "North";
        worksheet.Range["B2"].Value2 = 1234.56;
        worksheet.Range["C2"].Value2 = "Worksheet context menu";
        worksheet.Range["A:C"].EntireColumn.AutoFit();
        worksheet.Range["B2"].Select();
        return worksheet;
    }

    private static dynamic PrepareExcelDataValidationDropdownWorkbook(dynamic excel)
    {
        dynamic worksheet = excel.ActiveSheet;
        worksheet.Range["A1"].Value2 = "Region";
        worksheet.Range["A2"].Value2 = string.Empty;
        worksheet.Range["B1"].Value2 = "Allowed values";
        worksheet.Range["B2"].Value2 = "North";
        worksheet.Range["B3"].Value2 = "South";
        worksheet.Range["B4"].Value2 = "West";
        dynamic target = worksheet.Range["A2"];
        target.Validation.Delete();
        target.Validation.Add(Type: 3, AlertStyle: 1, Operator: 1, Formula1: "North,South,West");
        target.Validation.InCellDropdown = true;
        worksheet.Range["A:B"].EntireColumn.AutoFit();
        target.Select();
        return worksheet;
    }

    private static void ClickExcelAutoFilterHeaderDropdown(dynamic excel, dynamic worksheet)
    {
        dynamic header = worksheet.Range["A1"];
        dynamic window = excel.ActiveWindow;
        var left = (int)window.PointsToScreenPixelsX(header.Left);
        var top = (int)window.PointsToScreenPixelsY(header.Top);
        const double pointToScreenScale = 2.0;
        var clickX = (int)(left + (header.Width * pointToScreenScale) - 12);
        var clickY = (int)(top + (header.Height * pointToScreenScale / 2.0));

        NativeMethods.SetCursorPos(clickX, clickY);
        Thread.Sleep(100);
        NativeMethods.MouseEvent(NativeMethods.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(60);
        NativeMethods.MouseEvent(NativeMethods.MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
    }

    private static void ClickExcelCellDropdownArrow(dynamic excel, dynamic worksheet, string address)
    {
        dynamic range = worksheet.Range[address];
        dynamic window = excel.ActiveWindow;
        var left = (int)window.PointsToScreenPixelsX(range.Left);
        var top = (int)window.PointsToScreenPixelsY(range.Top);
        const double pointToScreenScale = 2.0;
        var clickX = (int)(left + (range.Width * pointToScreenScale) - 8);
        var clickY = (int)(top + (range.Height * pointToScreenScale / 2.0));

        NativeMethods.SetCursorPos(clickX, clickY);
        Thread.Sleep(100);
        NativeMethods.MouseEvent(NativeMethods.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(60);
        NativeMethods.MouseEvent(NativeMethods.MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
    }

    private static void SendCtrl1()
    {
        NativeMethods.KeybdEvent(NativeMethods.VK_CONTROL, 0, 0, UIntPtr.Zero);
        Thread.Sleep(60);
        NativeMethods.KeybdEvent(NativeMethods.VK_1, 0, 0, UIntPtr.Zero);
        Thread.Sleep(60);
        NativeMethods.KeybdEvent(NativeMethods.VK_1, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        Thread.Sleep(60);
        NativeMethods.KeybdEvent(NativeMethods.VK_CONTROL, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    private static bool TryOpenExcelAutoFilterWithUia(IntPtr excelWindowHandle)
    {
        var root = AutomationElement.FromHandle(excelWindowHandle);
        var buttons = root.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));

        foreach (AutomationElement button in buttons)
        {
            var name = button.Current.Name ?? string.Empty;
            var automationId = button.Current.AutomationId ?? string.Empty;
            var candidateText = $"{name} {automationId}";
            if (!candidateText.Contains("score", StringComparison.OrdinalIgnoreCase) ||
                !(candidateText.Contains("filter", StringComparison.OrdinalIgnoreCase) ||
                  candidateText.Contains("drop", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (button.TryGetCurrentPattern(InvokePattern.Pattern, out var invokePattern) &&
                invokePattern is InvokePattern invoke)
            {
                invoke.Invoke();
                return true;
            }

            var bounds = button.Current.BoundingRectangle;
            if (!bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0)
            {
                NativeMethods.SetCursorPos((int)(bounds.Left + bounds.Width / 2.0), (int)(bounds.Top + bounds.Height / 2.0));
                Thread.Sleep(100);
                NativeMethods.MouseEvent(NativeMethods.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                Thread.Sleep(60);
                NativeMethods.MouseEvent(NativeMethods.MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                return true;
            }
        }

        return false;
    }

    private static void RightClickExcelRangeCenter(dynamic excel, dynamic worksheet, string address)
    {
        dynamic range = worksheet.Range[address];
        dynamic window = excel.ActiveWindow;
        var left = (int)window.PointsToScreenPixelsX(range.Left);
        var top = (int)window.PointsToScreenPixelsY(range.Top);
        const double pointToScreenScale = 2.0;
        var clickX = (int)(left + (range.Width * pointToScreenScale / 2.0));
        var clickY = (int)(top + (range.Height * pointToScreenScale / 2.0));

        NativeMethods.SetCursorPos(clickX, clickY);
        Thread.Sleep(100);
        NativeMethods.MouseEvent(NativeMethods.MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(60);
        NativeMethods.MouseEvent(NativeMethods.MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
    }

    private static bool TryExpandExcelNumberFormatGallery(IntPtr excelWindowHandle)
    {
        var root = AutomationElement.FromHandle(excelWindowHandle);
        var condition = new PropertyCondition(AutomationElement.AutomationIdProperty, "NumberFormatGallery");
        var combo = root.FindFirst(TreeScope.Descendants, condition);
        if (combo is null)
        {
            return false;
        }

        if (!combo.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var pattern) ||
            pattern is not ExpandCollapsePattern expandCollapse)
        {
            return false;
        }

        expandCollapse.Expand();
        return true;
    }

    private static void CloseExcel(dynamic? excel, dynamic? workbook)
    {
        try
        {
            workbook?.Close(false);
        }
        catch (RuntimeBinderException)
        {
        }
        catch (COMException)
        {
        }

        try
        {
            excel?.Quit();
        }
        catch (RuntimeBinderException)
        {
        }
        catch (COMException)
        {
        }

        ReleaseCom(workbook);
        ReleaseCom(excel);
    }

    private static void ReleaseCom(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }

    private static void KillProcess(int? processId)
    {
        if (processId is null)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(processId.Value);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private string ResolveFreeXExePath()
    {
        if (!string.IsNullOrWhiteSpace(options.FreeXExePath))
        {
            return options.FreeXExePath;
        }

        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var candidate = Path.Combine(repoRoot, "src", "FreeX.App.Host", "bin", "Release", "net10.0-windows10.0.19041.0", "FreeX.App.Host.exe");
        if (!File.Exists(candidate))
        {
            throw new FileNotFoundException($"FreeX host executable was not found. Build Release or pass --freex-exe. Expected: {candidate}", candidate);
        }

        return candidate;
    }
}

internal sealed record CaptureOptions(
    string Scenario,
    string OutputRoot,
    string? FreeXExePath,
    bool ShowHelp,
    bool ListSlices,
    string Subject,
    TimeSpan LaunchTimeout,
    TimeSpan FocusTimeout,
    TimeSpan PopupTimeout,
    TimeSpan AfterInputDelay,
    TimeSpan AfterDialogDetectedDelay)
{
    public const string Usage = """
        FreeX.ForegroundCapture

        Usage:
          dotnet run --project tools/FreeX.ForegroundCapture -- --scenario <name>
          dotnet run --project tools/FreeX.ForegroundCapture -- --list-slices

        Scenarios:
          excel-autofilter
          excel-number-format
          excel-borders
          excel-context-menu
          excel-open-dialog
          excel-save-as-dialog
          freex-open-dialog
          freex-save-as-dialog
          freex-save-as-dialog-cancel
          freex-save-as-overwrite-prompt
          freex-background-picker-cancel
          freex-background-picker-select
          freex-status-zoom-in-click
          freex-status-zoom-out-click
          freex-status-zoom-slider-drag
          freex-status-zoom-slider-rangevalue-set
          freex-status-ctrl-wheel-grid-zoom
          freex-sheet-tab-context-menu
          freex-grid-drag-select
          freex-grid-row-column-resize
          freex-grid-wheel-scroll

        Options:
          --output <path>       Default: tools/foreground-captures
          --freex-exe <path>    FreeX.App.Host.exe path for FreeX scenarios
        """;

    public static CaptureOptions Parse(string[] args)
    {
        var scenario = string.Empty;
        var output = Path.Combine("tools", "foreground-captures");
        string? freexExe = null;
        var showHelp = false;
        var listSlices = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
                case "--list-slices":
                    listSlices = true;
                    break;
                case "--scenario" when i + 1 < args.Length:
                    scenario = args[++i];
                    break;
                case "--output" when i + 1 < args.Length:
                    output = args[++i];
                    break;
                case "--freex-exe" when i + 1 < args.Length:
                    freexExe = args[++i];
                    break;
            }
        }

        return new CaptureOptions(
            scenario,
            Path.GetFullPath(output),
            freexExe,
            showHelp,
            listSlices,
            scenario.StartsWith("excel-", StringComparison.OrdinalIgnoreCase) ? "excel" : "freex",
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(6),
            TimeSpan.FromMilliseconds(900),
            TimeSpan.FromMilliseconds(3000));
    }
}

internal static class RemainingSlices
{
    public static readonly RemainingSlice[] All =
    [
        new("S1", "Excel/FreeX paired main ribbon capture matrix", "foreground harness"),
        new("S2", "Popup, dropdown, and gallery captures", "foreground harness"),
        new("S3", "Native Open/Save/Background picker dialogs", "foreground harness"),
        new("S4", "Grid pointer mechanics: drag select, autofill, resize, split panes", "foreground harness plus mouse drags"),
        new("S5", "Sheet-tab pointer mechanics: rename, reorder, grouping, overflow/context", "foreground harness plus mouse drags"),
        new("S6", "Status/footer pointer mechanics: zoom buttons, zoom slider, Ctrl/Shift wheel", "foreground harness plus wheel input"),
        new("S7", "Excel-paired popup/dialog captures for comparison", "foreground harness")
    ];
}

internal sealed record RemainingSlice(string Id, string Name, string Status);

internal sealed record CaptureResult(
    string Scenario,
    string Subject,
    string CaptureStatus,
    string CaptureMode,
    string? ScreenshotPath,
    WindowInfo? Window,
    ForegroundGuardResult? ForegroundGuard,
    string? BlockReason,
    DateTimeOffset CapturedAtUtc)
{
    public string? ManifestPath { get; init; }
    public string? ResultValidation { get; init; }
    public string? ContinuationScreenshotPath { get; init; }
    public string? OutputPath { get; init; }

    public static CaptureResult Blocked(
        string scenario,
        string reason,
        string message,
        string outputRoot,
        string subject,
        ForegroundGuardResult? guard = null)
    {
        var result = new CaptureResult(
            scenario,
            subject,
            "blocked",
            "foreground-guarded-uia-win32",
            null,
            null,
            guard,
            $"{reason}: {message}",
            DateTimeOffset.UtcNow);

        var scenarioDir = Path.Combine(outputRoot, string.IsNullOrWhiteSpace(scenario) ? "unknown" : scenario);
        Directory.CreateDirectory(scenarioDir);
        var manifestPath = Path.Combine(scenarioDir, $"{(string.IsNullOrWhiteSpace(scenario) ? "unknown" : scenario)}_manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(result with { ManifestPath = manifestPath }, ProgramAccessor.JsonOptions));
        return result with { ManifestPath = manifestPath };
    }
}

internal sealed record ForegroundGuardResult(
    bool Success,
    int ExpectedProcessId,
    long ExpectedHandle,
    WindowInfo? ForegroundWindow,
    string? Reason);

internal static class ForegroundGuard
{
    public static ForegroundGuardResult FocusAndVerify(IntPtr handle, int expectedProcessId, string titleContains, TimeSpan timeout)
    {
        ForceForeground(handle);

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var foreground = NativeMethods.GetForegroundWindow();
            var info = WindowFinder.GetWindowInfo(foreground);
            if (info is not null &&
                info.ProcessId == expectedProcessId &&
                info.Title.Contains(titleContains, StringComparison.OrdinalIgnoreCase))
            {
                return new ForegroundGuardResult(true, expectedProcessId, handle.ToInt64(), info, null);
            }

            Thread.Sleep(100);
            ForceForeground(handle);
        }

        var current = WindowFinder.GetWindowInfo(NativeMethods.GetForegroundWindow());
        var reason = current is null
            ? "No foreground window detected."
            : $"Foreground is PID {current.ProcessId} '{current.Title}' class '{current.ClassName}', expected PID {expectedProcessId} title containing '{titleContains}'.";
        return new ForegroundGuardResult(false, expectedProcessId, handle.ToInt64(), current, reason);
    }

    private static void ForceForeground(IntPtr handle)
    {
        NativeMethods.ShowWindow(handle, NativeMethods.SW_RESTORE);

        var foreground = NativeMethods.GetForegroundWindow();
        var foregroundThread = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var targetThread = NativeMethods.GetWindowThreadProcessId(handle, out _);
        var currentThread = NativeMethods.GetCurrentThreadId();

        var attachedForeground = foregroundThread != 0 && foregroundThread != currentThread &&
            NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);
        var attachedTarget = targetThread != 0 && targetThread != currentThread &&
            NativeMethods.AttachThreadInput(currentThread, targetThread, true);

        try
        {
            NativeMethods.SetWindowPos(handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
            NativeMethods.SetWindowPos(handle, NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
            NativeMethods.BringWindowToTop(handle);
            NativeMethods.SetActiveWindow(handle);
            NativeMethods.SetFocus(handle);
            NativeMethods.SetForegroundWindow(handle);
        }
        finally
        {
            if (attachedTarget)
            {
                NativeMethods.AttachThreadInput(currentThread, targetThread, false);
            }

            if (attachedForeground)
            {
                NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
    }
}

internal static class ScreenshotCapture
{
    public static void Capture(Rectangle bounds, string filePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");
        using var bitmap = new Bitmap(bounds.Width, bounds.Height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
        bitmap.Save(filePath, ImageFormat.Png);
    }
}

internal static class WindowFinder
{
    public static WindowInfo? WaitForMainWindow(int processId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var window = EnumerateVisibleWindows()
                .Where(candidate => candidate.ProcessId == processId)
                .OrderByDescending(candidate => candidate.Bounds.Width * candidate.Bounds.Height)
                .FirstOrDefault(candidate => candidate.Title.Length > 0);

            if (window is not null)
            {
                return window;
            }

            Thread.Sleep(150);
        }

        return null;
    }

    public static WindowInfo? FindOwnedOrForegroundPopup(int processId, string className, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var foreground = GetWindowInfo(NativeMethods.GetForegroundWindow());
            if (foreground is not null &&
                foreground.ProcessId == processId &&
                foreground.ClassName.Equals(className, StringComparison.OrdinalIgnoreCase))
            {
                return foreground;
            }

            var popup = EnumerateVisibleWindows()
                .Where(window => window.ProcessId == processId)
                .Where(window => window.ClassName.Equals(className, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(window => window.Bounds.Width * window.Bounds.Height)
                .FirstOrDefault();

            if (popup is not null)
            {
                return popup;
            }

            Thread.Sleep(150);
        }

        return null;
    }

    public static WindowInfo? FindProcessWindow(int processId, string className, string titleContains, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var window = EnumerateVisibleWindows()
                .Where(candidate => candidate.ProcessId == processId)
                .Where(candidate => candidate.ClassName.Equals(className, StringComparison.OrdinalIgnoreCase))
                .Where(candidate => candidate.Title.Contains(titleContains, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(candidate => candidate.Bounds.Width * candidate.Bounds.Height)
                .FirstOrDefault();

            if (window is not null)
            {
                return window;
            }

            Thread.Sleep(150);
        }

        return null;
    }

    public static WindowInfo? FindProcessWindow(int processId, Func<WindowInfo, bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var window = EnumerateVisibleWindows()
                .Where(candidate => candidate.ProcessId == processId)
                .Where(predicate)
                .OrderByDescending(candidate => candidate.Bounds.Width * candidate.Bounds.Height)
                .FirstOrDefault();

            if (window is not null)
            {
                return window;
            }

            Thread.Sleep(150);
        }

        return null;
    }

    public static WindowInfo? FindProcessPopup(int processId, long ownerHandle, TimeSpan timeout, int minimumWidth, int minimumHeight)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var foreground = GetWindowInfo(NativeMethods.GetForegroundWindow());
            if (foreground is not null &&
                foreground.ProcessId == processId &&
                foreground.Handle != ownerHandle &&
                !foreground.ClassName.Equals("XLMAIN", StringComparison.OrdinalIgnoreCase) &&
                foreground.Bounds.Width >= minimumWidth &&
                foreground.Bounds.Height >= minimumHeight)
            {
                return foreground;
            }

            var popup = EnumerateVisibleWindows()
                .Where(candidate => candidate.ProcessId == processId)
                .Where(candidate => candidate.Handle != ownerHandle)
                .Where(candidate => !candidate.ClassName.Equals("XLMAIN", StringComparison.OrdinalIgnoreCase))
                .Where(candidate => candidate.Bounds.Width >= minimumWidth && candidate.Bounds.Height >= minimumHeight)
                .OrderByDescending(candidate => candidate.Bounds.Width * candidate.Bounds.Height)
                .FirstOrDefault();

            if (popup is not null)
            {
                return popup;
            }

            Thread.Sleep(150);
        }

        return null;
    }

    public static WindowInfo? FindForegroundProcessPopup(int processId, long ownerHandle, TimeSpan timeout, int minimumWidth, int minimumHeight)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var foreground = GetWindowInfo(NativeMethods.GetForegroundWindow());
            if (foreground is not null &&
                foreground.ProcessId == processId &&
                foreground.Handle != ownerHandle &&
                !foreground.ClassName.Equals("XLMAIN", StringComparison.OrdinalIgnoreCase) &&
                foreground.Bounds.Width >= minimumWidth &&
                foreground.Bounds.Height >= minimumHeight)
            {
                return foreground;
            }

            Thread.Sleep(150);
        }

        return null;
    }

    public static WindowInfo? GetWindowInfo(IntPtr handle)
    {
        if (handle == IntPtr.Zero || !NativeMethods.IsWindowVisible(handle))
        {
            return null;
        }

        var title = new StringBuilder(512);
        NativeMethods.GetWindowText(handle, title, title.Capacity);
        var className = new StringBuilder(256);
        NativeMethods.GetClassName(handle, className, className.Capacity);
        _ = NativeMethods.GetWindowThreadProcessId(handle, out var processId);

        var rect = new NativeMethods.RECT();
        if (!NativeMethods.GetWindowRect(handle, ref rect))
        {
            return null;
        }

        return new WindowInfo(
            handle.ToInt64(),
            (int)processId,
            title.ToString(),
            className.ToString(),
            new Rectangle(rect.Left, rect.Top, Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top)));
    }

    private static IEnumerable<WindowInfo> EnumerateVisibleWindows()
    {
        var windows = new List<WindowInfo>();
        NativeMethods.EnumWindows((handle, _) =>
        {
            var info = GetWindowInfo(handle);
            if (info is { Bounds.Width: > 0, Bounds.Height: > 0 })
            {
                windows.Add(info);
            }

            return true;
        }, IntPtr.Zero);

        return windows;
    }
}

internal sealed record WindowInfo(
    long Handle,
    int ProcessId,
    string Title,
    string ClassName,
    Rectangle Bounds);

internal enum MouseButtonKind
{
    Left,
    Right
}

internal static class NativeMethods
{
    public const int SW_RESTORE = 9;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const int MOUSEEVENTF_MOVE = 0x0001;
    public const int MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const int MOUSEEVENTF_LEFTUP = 0x0004;
    public const int MOUSEEVENTF_RIGHTDOWN = 0x0008;
    public const int MOUSEEVENTF_RIGHTUP = 0x0010;
    public const int MOUSEEVENTF_WHEEL = 0x0800;
    public const int KEYEVENTF_KEYUP = 0x0002;
    public const byte VK_CONTROL = 0x11;
    public const byte VK_1 = 0x31;
    public const byte VK_SHIFT = 0x10;
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new(-2);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr SetActiveWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", EntryPoint = "mouse_event")]
    public static extern void MouseEvent(int dwFlags, int dx, int dy, int dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll", EntryPoint = "keybd_event")]
    public static extern void KeybdEvent(byte bVk, byte bScan, int dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, ref RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public static int GetProcessId(IntPtr handle)
    {
        _ = GetWindowThreadProcessId(handle, out var pid);
        return (int)pid;
    }
}

internal static class ProgramAccessor
{
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
