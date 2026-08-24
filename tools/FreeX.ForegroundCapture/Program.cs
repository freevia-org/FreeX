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
using FreeX.ToolsShared.Wpf;

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
    private const int XlDialogFormatNumber = 42;
    private const int XlDialogActivate = 103;

    public CaptureResult Run()
    {
        Directory.CreateDirectory(options.OutputRoot);

        return options.Scenario.ToLowerInvariant() switch
        {
            "excel-autofilter" => RunExcelAutoFilterScenario(),
            "excel-number-format" => RunExcelNumberFormatScenario(),
            "excel-borders" => RunExcelPopupScenario("excel-borders", PrepareExcelBlankWorkbook, "%hb", "Net UI Tool Window"),
            "excel-cell-styles-gallery" => RunExcelCellStylesGalleryScenario(),
            "excel-conditional-formatting-gallery" => RunExcelConditionalFormattingGalleryScenario(),
            "excel-context-menu" => RunExcelContextMenuScenario(),
            "excel-format-cells-dialog" => RunExcelFormatCellsDialogScenario(),
            "excel-format-cells-context-dialog" => RunExcelFormatCellsContextDialogScenario(),
            "excel-data-validation-dropdown" => RunExcelDataValidationDropdownScenario(),
            "excel-data-validation-dropdown-prepared" => RunExcelDataValidationDropdownPreparedScenario(),
            "excel-open-dialog" => RunExcelDialogScenario("excel-open-dialog", PrepareExcelBlankWorkbook, "^{F12}", "#32770", "Open"),
            "excel-save-as-dialog" => RunExcelSaveAsDialogScenario(),
            "excel-sheet-tab-context-menu" => RunExcelSheetTabContextMenuScenario(),
            "excel-sheet-tab-overflow-activate-dialog" => RunExcelSheetTabOverflowActivateDialogScenario(),
            "excel-status-footer-reference" => RunExcelStatusFooterReferenceScenario(),
            "excel-formula-bar-name-box-reference" => RunExcelFormulaBarNameBoxReferenceScenario(),
            "freex-open-dialog" => RunFreeXDialogScenario("freex-open-dialog", "^{F12}", "#32770", "Open"),
            "freex-save-as-dialog" => RunFreeXDialogScenario("freex-save-as-dialog", "{F12}", "#32770", "Save As"),
            "freex-conditional-formatting-gallery" => RunFreeXConditionalFormattingGalleryScenario(),
            "avalonia-conditional-formatting-gallery" => RunAvaloniaConditionalFormattingGalleryScenario(),
            "freex-format-cells-dialog" => RunFreeXFormatCellsDialogScenario(),
            "freex-format-cells-context-dialog" => RunFreeXFormatCellsContextDialogScenario(),
            // S3 native-dialog continuation scenarios.
            "freex-save-as-dialog-cancel" => RunFreeXDialogCancelScenario("freex-save-as-dialog-cancel", "{F12}", "#32770", "Save As"),
            "freex-save-as-overwrite-prompt" => RunFreeXSaveAsOverwritePromptScenario(),
            "freex-save-as-invalid-path" => RunFreeXSaveAsInvalidPathScenario(),
            "freex-export-pdf-save-dialog-cancel" => RunFreeXExportSaveDialogCancelScenario(),
            "freex-export-overwrite-prompt" => RunFreeXExportOverwritePromptScenario(),
            "freex-export-xps-accept" => RunFreeXExportXpsAcceptScenario(),
            "freex-native-print-dialog" => RunFreeXNativePrintDialogScenario(),
            "freex-background-picker-cancel" => RunFreeXBackgroundPickerCancelScenario(),
            "freex-background-picker-select" => RunFreeXBackgroundPickerSelectScenario(),
            "freex-background-picker-replace" => RunFreeXBackgroundPickerReplaceScenario(),
            "freex-background-clear" => RunFreeXBackgroundClearScenario(),
            "freex-status-zoom-in-click" => RunFreeXMainWindowPointerScenario("freex-status-zoom-in-click", ClickAutomationIdExpectZoom("StatusZoomInButton", 105)),
            "freex-status-zoom-out-click" => RunFreeXMainWindowPointerScenario("freex-status-zoom-out-click", ClickAutomationIdExpectZoom("StatusZoomOutButton", 95)),
            "freex-status-zoom-slider-drag" => RunFreeXMainWindowPointerScenario("freex-status-zoom-slider-drag", DragFirstSliderExpectChangedZoom("Zoom", 100)),
            "freex-status-zoom-slider-rangevalue-set" => RunFreeXMainWindowPointerScenario("freex-status-zoom-slider-rangevalue-set", SetFirstSliderRangeValue("Zoom", 150)),
            "freex-status-zoom-min-max-rangevalue-set" => RunFreeXMainWindowPointerScenario("freex-status-zoom-min-max-rangevalue-set", SetZoomSliderMinMaxRangeValues()),
            "freex-status-ctrl-wheel-grid-zoom" => RunFreeXMainWindowPointerScenario("freex-status-ctrl-wheel-grid-zoom", CtrlWheelRelativeExpectZoom(0.36, 0.56, 120, 110)),
            "freex-status-wheel-modifier-breadth" => RunFreeXMainWindowPointerScenario("freex-status-wheel-modifier-breadth", WheelModifierBreadth()),
            "freex-status-view-shortcuts-click" => RunFreeXMainWindowPointerScenario("freex-status-view-shortcuts-click", ClickStatusViewShortcuts()),
            "freex-status-zoom-text-dialog-click" => RunFreeXMainWindowPointerScenario("freex-status-zoom-text-dialog-click", ClickZoomTextExpectDialog()),
            "freex-status-ctrl-alt-zoom-keys" => RunFreeXMainWindowPointerScenario("freex-status-ctrl-alt-zoom-keys", CtrlAltZoomKeysExpectRoundTrip()),
            "freex-status-live-stats-accessibility" => RunFreeXMainWindowPointerScenario("freex-status-live-stats-accessibility", StatusLiveStatsAccessibility(), CreateStatusStatsOptionsOverride),
            "freex-formula-bar-name-box-reference" => RunFreeXMainWindowPointerScenario("freex-formula-bar-name-box-reference", FormulaBarNameBoxReference()),
            "freex-autofilter" => RunFreeXMainWindowPointerScenario("freex-autofilter", FreeXAutoFilterOpenedState()),
            "freex-sheet-tab-context-menu" => RunFreeXMainWindowPointerScenario("freex-sheet-tab-context-menu", RightClickSheetTabContextMenu()),
            "freex-sheet-tab-click-select" => RunFreeXMainWindowPointerScenario("freex-sheet-tab-click-select", SheetTabClickSelect()),
            "freex-sheet-tab-double-click-rename" => RunFreeXMainWindowPointerScenario("freex-sheet-tab-double-click-rename", SheetTabDoubleClickRename()),
            "freex-sheet-tab-ctrl-click-grouping" => RunFreeXMainWindowPointerScenario("freex-sheet-tab-ctrl-click-grouping", SheetTabModifierGrouping(NativeMethods.VK_CONTROL, "Ctrl+click", "Sheet3")),
            "freex-sheet-tab-shift-click-grouping" => RunFreeXMainWindowPointerScenario("freex-sheet-tab-shift-click-grouping", SheetTabModifierGrouping(NativeMethods.VK_SHIFT, "Shift+click", "Sheet5")),
            "freex-sheet-tab-grouped-commands" => RunFreeXMainWindowPointerScenario("freex-sheet-tab-grouped-commands", SheetTabGroupedCommands()),
            "freex-sheet-tab-drag-reorder" => RunFreeXMainWindowPointerScenario("freex-sheet-tab-drag-reorder", SheetTabDragReorder()),
            "freex-sheet-tab-overflow-nav-click" => RunFreeXMainWindowPointerScenario("freex-sheet-tab-overflow-nav-click", SheetTabOverflowNavClick()),
            "freex-sheet-tab-overflow-activate-dialog" => RunFreeXMainWindowPointerScenario("freex-sheet-tab-overflow-activate-dialog", SheetTabOverflowActivateDialog()),
            "freex-grid-drag-select" => RunFreeXMainWindowPointerScenario("freex-grid-drag-select", DragRelative(0.14, 0.56, 0.37, 0.69)),
            "freex-s4-grid-drag-select-validated" => RunFreeXMainWindowPointerScenario("freex-s4-grid-drag-select-validated", DragCellRangeSelectValidated("A1", "D5")),
            "freex-s4-grid-autofill-handle-drag" => RunFreeXMainWindowPointerScenario("freex-s4-grid-autofill-handle-drag", AutofillHandleDragValidated()),
            "freex-s4-grid-double-click-autofit" => RunFreeXMainWindowPointerScenario("freex-s4-grid-double-click-autofit", DoubleClickAutoFitValidated()),
            "freex-grid-row-column-resize" => RunFreeXMainWindowPointerScenario("freex-grid-row-column-resize", DragColumnAndRowResizeHandles()),
            "freex-grid-wheel-scroll" => RunFreeXMainWindowPointerScenario("freex-grid-wheel-scroll", WheelVerticalThenShiftHorizontal()),
            _ => CaptureResult.Blocked(options.Scenario, "unsupported-scenario", $"Unsupported scenario '{options.Scenario}'.", options.OutputRoot, options.Subject)
        };
    }

    private string? _lastResultValidation;
    private WindowInfo? _lastCaptureWindow;

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

    private CaptureResult RunExcelCellStylesGalleryScenario()
    {
        const string scenario = "excel-cell-styles-gallery";
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
                return BlockedWithGuard(scenario, guard, "before-input");
            }

            if (!TryOpenExcelCellStylesGallery(hwnd))
            {
                SendKeys.SendWait("%hj");
            }

            Thread.Sleep(options.AfterInputDelay);

            var popup = FindExcelRibbonGalleryPopup(pid.Value, hwnd.ToInt64(), "Cell Styles", options.PopupTimeout);
            if (popup is null)
            {
                return CaptureResult.Blocked(scenario, "popup-not-found", "Did not detect foreground Excel Cell Styles gallery popup after UIA open or Alt,H,J fallback.", options.OutputRoot, "excel", guard);
            }

            return CaptureWindow(scenario, "excel", popup, guard, "complete");
        }
        finally
        {
            CloseExcel(excel, workbook);
            KillProcess(pid);
        }
    }

    private CaptureResult RunExcelConditionalFormattingGalleryScenario()
    {
        const string scenario = "excel-conditional-formatting-gallery";
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
                return BlockedWithGuard(scenario, guard, "before-input");
            }

            if (!TryOpenRibbonGalleryByText(hwnd, "Conditional Formatting"))
            {
                SendKeys.SendWait("%hl");
            }

            Thread.Sleep(options.AfterInputDelay);

            var popup = FindExcelRibbonGalleryPopup(pid.Value, hwnd.ToInt64(), "Conditional Formatting", options.PopupTimeout);
            if (popup is null)
            {
                return CaptureResult.Blocked(scenario, "popup-not-found", "Did not detect foreground Excel Conditional Formatting gallery popup after UIA open or Alt,H,L fallback.", options.OutputRoot, "excel", guard);
            }

            return CaptureWindow(scenario, "excel", popup, guard, "complete");
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

            SendCtrl1();
            Thread.Sleep(options.AfterInputDelay);

            var dialog = FindExcelFormatCellsDialog(pid.Value, hwnd.ToInt64(), options.PopupTimeout);
            if (dialog is null && TryExecuteExcelMso(excel, "FormatCellsDialog"))
            {
                Thread.Sleep(options.AfterInputDelay);
                dialog = FindExcelFormatCellsDialog(pid.Value, hwnd.ToInt64(), options.PopupTimeout);
            }

            var usedBuiltInDialogFallback = false;
            if (dialog is null && TryShowExcelBuiltInDialogAsync(excel, XlDialogFormatNumber))
            {
                usedBuiltInDialogFallback = true;
                Thread.Sleep(options.AfterInputDelay);
                dialog = FindExcelFormatCellsDialog(pid.Value, hwnd.ToInt64(), options.PopupTimeout);
            }

            if (dialog is null)
            {
                return CaptureResult.Blocked("excel-format-cells-dialog", "dialog-not-found", "Did not detect Excel Format Cells dialog after Ctrl+1, Excel's FormatCellsDialog command, or the built-in xlDialogFormatNumber dialog.", options.OutputRoot, "excel", guard);
            }

            Thread.Sleep(options.AfterDialogDetectedDelay);
            var validation = usedBuiltInDialogFallback ? "Captured Excel's built-in Format Cells dialog through xlDialogFormatNumber after keyboard/command-bar routes did not surface a foreground dialog in this Office automation state." : null;
            return CaptureWindow("excel-format-cells-dialog", "excel", dialog, guard, "complete", validation);
        }
        finally
        {
            CloseExcel(excel, workbook);
            KillProcess(pid);
        }
    }

    private CaptureResult RunExcelFormatCellsContextDialogScenario()
    {
        const string scenario = "excel-format-cells-context-dialog";
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
                return BlockedWithGuard(scenario, guard, "before-context-menu");
            }

            RightClickExcelRangeCenter(excel, worksheet, "B2");
            Thread.Sleep(options.AfterInputDelay);

            var usedCommandBarMenuFallback = false;
            var popup = WindowFinder.FindProcessPopup(pid.Value, hwnd.ToInt64(), TimeSpan.FromMilliseconds(1200), 120, 120);
            if (popup is null)
            {
                usedCommandBarMenuFallback = TryShowExcelCellCommandBar(excel);
                if (usedCommandBarMenuFallback)
                {
                    Thread.Sleep(options.AfterInputDelay);
                    popup = WindowFinder.FindProcessPopup(pid.Value, hwnd.ToInt64(), options.PopupTimeout, 120, 120);
                }
            }

            if (popup is null)
            {
                return CaptureResult.Blocked(scenario, "context-menu-not-found", "Did not detect foreground Excel worksheet context menu before invoking Format Cells after physical right-click and Cell command-bar fallback attempts.", options.OutputRoot, "excel", guard);
            }

            var invokedContextFormatCells = TryInvokeProcessMenuItem(pid.Value, "Format Cells");
            if (!invokedContextFormatCells)
            {
                SendKeys.SendWait("f");
            }

            Thread.Sleep(options.AfterInputDelay);
            var dialog = FindExcelFormatCellsDialog(pid.Value, hwnd.ToInt64(), options.PopupTimeout);
            var usedCommandBarFallback = false;
            if (dialog is null && TryExecuteExcelMso(excel, "FormatCellsDialog"))
            {
                usedCommandBarFallback = true;
                Thread.Sleep(options.AfterInputDelay);
                dialog = FindExcelFormatCellsDialog(pid.Value, hwnd.ToInt64(), options.PopupTimeout);
            }

            var usedBuiltInDialogFallback = false;
            if (dialog is null && TryShowExcelBuiltInDialogAsync(excel, XlDialogFormatNumber))
            {
                usedBuiltInDialogFallback = true;
                Thread.Sleep(options.AfterInputDelay);
                dialog = FindExcelFormatCellsDialog(pid.Value, hwnd.ToInt64(), options.PopupTimeout);
            }

            if (dialog is null)
            {
                return CaptureResult.Blocked(scenario, "dialog-not-found", "Detected the Excel worksheet context menu, but UI Automation, the context-menu mnemonic, Excel's FormatCellsDialog command, and the built-in xlDialogFormatNumber dialog did not expose a Format Cells dialog.", options.OutputRoot, "excel", guard);
            }

            Thread.Sleep(options.AfterDialogDetectedDelay);
            var menuRoute = usedCommandBarMenuFallback ? "Cell command-bar context menu fallback" : "physical right-click context menu";
            var route = usedBuiltInDialogFallback ? $"Excel built-in xlDialogFormatNumber fallback after the {menuRoute} was visible" : usedCommandBarFallback ? $"Excel built-in FormatCellsDialog command after the {menuRoute} was visible" : invokedContextFormatCells ? $"UI Automation invocation from the {menuRoute}" : $"keyboard mnemonic from the {menuRoute}";
            return CaptureWindow(scenario, "excel", dialog, guard, "complete", $"Opened Format Cells through the Excel worksheet context menu via {route}.");
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

    private CaptureResult RunExcelDataValidationDropdownPreparedScenario()
    {
        const string scenario = "excel-data-validation-dropdown-prepared";
        dynamic? excel = null;
        dynamic? workbook = null;
        int? pid = null;
        var preparedPath = Path.Combine(options.OutputRoot, scenario, "prepared-validation-list.xlsx");

        try
        {
            CreatePreparedExcelDataValidationWorkbook(preparedPath);

            (excel, workbook) = OpenExcelWorkbook(preparedPath);
            dynamic worksheet = excel.ActiveSheet;

            var hwnd = new IntPtr((int)excel.Hwnd);
            pid = NativeMethods.GetProcessId(hwnd);
            var guard = ForegroundGuard.FocusAndVerify(hwnd, pid.Value, "Excel", options.FocusTimeout);
            if (!guard.Success)
            {
                return BlockedWithGuard(scenario, guard, "before-prepared-dropdown-input");
            }

            worksheet.Range["A2"].Activate();
            ClickExcelCellDropdownArrow(excel, worksheet, "A2");
            Thread.Sleep(options.AfterInputDelay);

            var popup = FindExcelDataValidationListPopup(pid.Value, hwnd.ToInt64(), TimeSpan.FromMilliseconds(1500));
            if (popup is null)
            {
                guard = ForegroundGuard.FocusAndVerify(hwnd, pid.Value, "Excel", options.FocusTimeout);
                if (!guard.Success)
                {
                    return BlockedWithGuard(scenario, guard, "before-prepared-keyboard-dropdown");
                }

                worksheet.Range["A2"].Activate();
                SendKeys.SendWait("%{DOWN}");
                Thread.Sleep(options.AfterInputDelay);
                popup = FindExcelDataValidationListPopup(pid.Value, hwnd.ToInt64(), options.PopupTimeout);
            }

            if (popup is null)
            {
                return CaptureResult.Blocked(scenario, "popup-not-found", "Prepared and reopened a validation-list workbook, but did not detect a foreground Excel Data Validation dropdown after the physical in-cell arrow click or Alt+Down.", options.OutputRoot, "excel", guard);
            }

            return CaptureWindow(scenario, "excel", popup, guard, "complete", $"Opened Data Validation dropdown from prepared workbook '{preparedPath}' through physical in-cell arrow, with Alt+Down fallback available.");
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
                var commonDialog = TryContinueExcelNuiSaveAsToCommonDialog(hwnd, pid.Value, dialog);
                if (commonDialog is null)
                {
                    Thread.Sleep(options.AfterDialogDetectedDelay);
                    return CaptureWindow("excel-save-as-dialog", "excel", dialog, guard, "complete", "Captured the Office Save As NUIDialog after F12; this Office state did not expose a stable Browse/More Options continuation to the native '#32770' file dialog.");
                }

                Thread.Sleep(options.AfterDialogDetectedDelay);
                return CaptureWindow("excel-save-as-dialog", "excel", commonDialog, guard, "complete", "Continued the Office NUIDialog Save As surface to the native common '#32770' Save As dialog.");
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

    private WindowInfo? TryContinueExcelNuiSaveAsToCommonDialog(IntPtr excelHwnd, int processId, WindowInfo nuiDialog)
    {
        var nuiHandle = new IntPtr(nuiDialog.Handle);
        var guard = ForegroundGuard.FocusAndVerify(nuiHandle, processId, "Save", options.FocusTimeout);
        if (!guard.Success)
        {
            return null;
        }

        foreach (var action in FindExcelNuiSaveAsContinuationElements(nuiHandle))
        {
            if (!TryInvokeOrClickAutomationElement(action))
            {
                continue;
            }

            Thread.Sleep(options.AfterInputDelay);
            var commonDialog = WindowFinder.FindProcessWindow(
                processId,
                candidate => candidate.Handle != excelHwnd.ToInt64() &&
                    candidate.Handle != nuiDialog.Handle &&
                    candidate.ClassName.Equals("#32770", StringComparison.OrdinalIgnoreCase) &&
                    candidate.Title.Contains("Save As", StringComparison.OrdinalIgnoreCase) &&
                    candidate.Bounds.Width > 400 &&
                    candidate.Bounds.Height > 250,
                TimeSpan.FromMilliseconds(Math.Max(1200, options.PopupTimeout.TotalMilliseconds / 2.0)));
            commonDialog ??= WindowFinder.FindForegroundWindow(
                candidate => candidate.ProcessId == processId &&
                    candidate.Handle != excelHwnd.ToInt64() &&
                    candidate.Handle != nuiDialog.Handle &&
                    candidate.ClassName.Equals("#32770", StringComparison.OrdinalIgnoreCase) &&
                    candidate.Title.Contains("Save As", StringComparison.OrdinalIgnoreCase) &&
                    candidate.Bounds.Width > 400 &&
                    candidate.Bounds.Height > 250,
                TimeSpan.FromMilliseconds(1200));
            if (commonDialog is not null)
            {
                return commonDialog;
            }

            guard = ForegroundGuard.FocusAndVerify(nuiHandle, processId, "Save", TimeSpan.FromMilliseconds(1200));
            if (!guard.Success)
            {
                return null;
            }
        }

        return null;
    }

    private static IReadOnlyList<AutomationElement> FindExcelNuiSaveAsContinuationElements(IntPtr nuiHandle)
    {
        static int Rank(string name)
        {
            if (name.Equals("Browse", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Browse...", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (name.Contains("Browse", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            if (name.Contains("More options", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("More Options", StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }

            if (name.Contains("Save As", StringComparison.OrdinalIgnoreCase))
            {
                return 3;
            }

            if (name.Contains("This PC", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Computer", StringComparison.OrdinalIgnoreCase))
            {
                return 4;
            }

            return 100;
        }

        try
        {
            var root = AutomationElement.FromHandle(nuiHandle);
            var controlTypes = new[] { ControlType.Button, ControlType.Hyperlink, ControlType.MenuItem, ControlType.ListItem };
            return controlTypes
                .SelectMany(controlType => root.FindAll(
                        TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.ControlTypeProperty, controlType))
                    .Cast<AutomationElement>())
                .Where(IsVisibleElement)
                .Select(element => new { Element = element, Name = GetElementName(element) })
                .Where(candidate => Rank(candidate.Name) < 100)
                .OrderBy(candidate => Rank(candidate.Name))
                .ThenByDescending(candidate => GetElementArea(candidate.Element))
                .Select(candidate => candidate.Element)
                .ToArray();
        }
        catch (COMException)
        {
            return [];
        }
        catch (ElementNotAvailableException)
        {
            return [];
        }
    }

    private CaptureResult RunExcelSheetTabContextMenuScenario()
    {
        const string scenario = "excel-sheet-tab-context-menu";
        dynamic? excel = null;
        dynamic? workbook = null;
        int? pid = null;

        try
        {
            (excel, workbook) = CreateExcel();
            PrepareExcelSheetTabWorkbook(excel, 4);

            var hwnd = new IntPtr((int)excel.Hwnd);
            pid = NativeMethods.GetProcessId(hwnd);
            var guard = ForegroundGuard.FocusAndVerify(hwnd, pid.Value, "Excel", options.FocusTimeout);
            if (!guard.Success)
            {
                return BlockedWithGuard(scenario, guard, "before-sheet-tab-context-menu");
            }

            var popup = TryOpenExcelSheetTabContextMenu(pid.Value, hwnd, guard, out var openNote);
            if (popup is null)
            {
                return CaptureResult.Blocked(scenario, "popup-not-found", "Did not detect Excel sheet-tab context menu after UIA and coordinate fallback right-click attempts.", options.OutputRoot, "excel", guard);
            }

            return CaptureWindow(scenario, "excel", popup, guard, "complete", openNote);
        }
        finally
        {
            CloseExcel(excel, workbook);
            KillProcess(pid);
        }
    }

    private CaptureResult RunExcelSheetTabOverflowActivateDialogScenario()
    {
        const string scenario = "excel-sheet-tab-overflow-activate-dialog";
        dynamic? excel = null;
        dynamic? workbook = null;
        int? pid = null;

        try
        {
            (excel, workbook) = CreateExcel();
            PrepareExcelSheetTabWorkbook(excel, 40);

            var hwnd = new IntPtr((int)excel.Hwnd);
            pid = NativeMethods.GetProcessId(hwnd);
            var guard = ForegroundGuard.FocusAndVerify(hwnd, pid.Value, "Excel", options.FocusTimeout);
            if (!guard.Success)
            {
                return BlockedWithGuard(scenario, guard, "before-sheet-tab-overflow-activate");
            }

            var rightNavCandidates = FindSheetNavButtonCandidates(hwnd, right: true);
            WindowInfo? dialog = null;
            foreach (var rightNav in rightNavCandidates)
            {
                if (!TryRightClickAutomationElement(rightNav))
                {
                    continue;
                }

                dialog = FindActivateSheetListDialogWindow(pid.Value, hwnd.ToInt64(), options.PopupTimeout);
                if (dialog is not null)
                {
                    break;
                }
            }

            if (dialog is null)
            {
                dialog = TryOpenExcelActivateSheetListDialogFromSheetNavCoordinates(pid.Value, hwnd, options.PopupTimeout);
            }

            var usedWorkbookTabsCommandBarFallback = false;
            if (dialog is null)
            {
                usedWorkbookTabsCommandBarFallback = TryOpenExcelActivateSheetListDialogFromWorkbookTabsCommandBar(excel, pid.Value, hwnd, options.PopupTimeout, out dialog);
            }

            if (dialog is null)
            {
                return CaptureResult.Blocked(scenario, "dialog-not-found", "Did not detect Excel's sheet-list Activate dialog after right-clicking UIA sheet-tab navigation candidates, coordinate fallbacks beside the sheet tabs, or the Workbook Tabs command-bar More Sheets route. The harness intentionally rejects the built-in xlDialogActivate workbook/window dialog because it lists workbooks such as Book1 instead of worksheets.", options.OutputRoot, "excel", guard);
            }

            var dialogHandle = new IntPtr(dialog.Handle);
            var dialogGuard = ForegroundGuard.FocusAndVerify(dialogHandle, pid.Value, "Activate", options.FocusTimeout);
            if (!dialogGuard.Success)
            {
                return CaptureResult.Blocked(scenario, "foreground-guard-failed", "Excel Activate dialog was detected but could not be foreground-verified.", options.OutputRoot, "excel", dialogGuard);
            }

            var validation = usedWorkbookTabsCommandBarFallback
                ? "Captured Microsoft Excel's sheet-list Activate dialog through the Workbook Tabs command-bar More Sheets route after physical sheet-nav attempts did not expose it."
                : "Captured Microsoft Excel's sheet-list Activate dialog after a physical right-click on the sheet-tab navigation button.";
            return CaptureWindow(scenario, "excel", dialog, dialogGuard, "complete", validation);
        }
        finally
        {
            CloseExcel(excel, workbook);
            KillProcess(pid);
        }
    }

    private WindowInfo? TryOpenExcelSheetTabContextMenu(
        int processId,
        IntPtr hwnd,
        ForegroundGuardResult initialGuard,
        out string note)
    {
        note = string.Empty;

        var tab = FindVisibleExcelSheetTabElement(hwnd, "Sheet1") ?? GetVisibleExcelSheetTabElements(hwnd)
            .OrderBy(element => element.Current.BoundingRectangle.Left)
            .FirstOrDefault();
        if (tab is not null && TryRightClickAutomationElement(tab))
        {
            var popup = WindowFinder.FindProcessPopup(processId, hwnd.ToInt64(), options.PopupTimeout, 120, 120);
            if (popup is not null)
            {
                note = "Captured Microsoft Excel's sheet-tab context menu after a physical right-click on a UIA-discovered sheet tab.";
                return popup;
            }
        }

        var window = WindowFinder.GetWindowInfo(hwnd);
        if (window is null)
        {
            note = "Excel main-window bounds were unavailable for sheet-tab coordinate fallback.";
            return null;
        }

        var fallbackNotes = new List<string>();
        foreach (var point in GetSheetTabStripFallbackPoints(window.Bounds))
        {
            fallbackNotes.Add($"{point.Note}@{point.X},{point.Y}");
            var guard = ForegroundGuard.FocusAndVerify(hwnd, processId, "Excel", options.FocusTimeout);
            if (!guard.Success)
            {
                note = $"Excel foreground guard failed before sheet-tab fallback right-click; initial guard success={initialGuard.Success}.";
                return null;
            }

            RightClickScreenPoint(point.X, point.Y);
            Thread.Sleep(options.AfterInputDelay);
            var popup = WindowFinder.FindProcessPopup(processId, hwnd.ToInt64(), options.PopupTimeout, 120, 120);
            if (popup is not null)
            {
                note = $"Captured Microsoft Excel's sheet-tab context menu through guarded tab-strip coordinate fallback ({point.Note}).";
                return popup;
            }
        }

        note = $"Excel sheet-tab context menu did not open through UIA or coordinate fallbacks: {string.Join("; ", fallbackNotes)}.";
        return null;
    }

    private CaptureResult RunExcelStatusFooterReferenceScenario()
    {
        const string scenario = "excel-status-footer-reference";
        dynamic? excel = null;
        dynamic? workbook = null;
        int? pid = null;

        try
        {
            (excel, workbook) = CreateExcel();
            PrepareExcelStatusFooterWorkbook(excel);

            var hwnd = new IntPtr((int)excel.Hwnd);
            pid = NativeMethods.GetProcessId(hwnd);
            var guard = ForegroundGuard.FocusAndVerify(hwnd, pid.Value, "Excel", options.FocusTimeout);
            if (!guard.Success)
            {
                return BlockedWithGuard(scenario, guard, "before-capture");
            }

            var resizeBlocked = ResizeForStableForegroundCapture(hwnd, pid.Value, "after-excel-status-window-resize", "Excel");
            if (resizeBlocked is not null)
            {
                return resizeBlocked;
            }

            guard = ForegroundGuard.FocusAndVerify(hwnd, pid.Value, "Excel", options.FocusTimeout);
            if (!guard.Success)
            {
                return BlockedWithGuard(scenario, guard, "after-window-resize");
            }

            if (!TryValidateExcelStatusFooterStatisticsViaContextMenu(pid.Value, hwnd, out var statusReadback))
            {
                return CaptureResult.Blocked(scenario, "status-footer-validation-unavailable", $"Could not validate Excel status/footer statistic text through the native status-bar context menu before capture. Last visible text readback: '{statusReadback}'.", options.OutputRoot, "excel", guard);
            }

            SendKeys.SendWait("{ESC}");
            Thread.Sleep(options.AfterInputDelay);

            var window = WindowFinder.GetWindowInfo(hwnd);
            if (window is null)
            {
                return CaptureResult.Blocked(scenario, "window-info-unavailable", "Could not resolve the foreground Excel window bounds.", options.OutputRoot, "excel", guard);
            }

            return CaptureWindow(
                scenario,
                "excel",
                window,
                guard,
                "complete",
                $"Excel status/footer reference: workbook values in A1:A4 are selected with DisplayStatusBar enabled and visible footer statistics validated ({statusReadback}) so the native footer/status bar can be paired with FreeX S6 captures.");
        }
        finally
        {
            CloseExcel(excel, workbook);
            KillProcess(pid);
        }
    }

    private CaptureResult RunExcelFormulaBarNameBoxReferenceScenario()
    {
        const string scenario = "excel-formula-bar-name-box-reference";
        dynamic? excel = null;
        dynamic? workbook = null;
        int? pid = null;

        try
        {
            (excel, workbook) = CreateExcel();
            PrepareExcelFormulaBarNameBoxWorkbook(excel);

            var hwnd = new IntPtr((int)excel.Hwnd);
            pid = NativeMethods.GetProcessId(hwnd);
            var guard = ForegroundGuard.FocusAndVerify(hwnd, pid.Value, "Excel", options.FocusTimeout);
            if (!guard.Success)
            {
                return BlockedWithGuard(scenario, guard, "before-capture");
            }

            var resizeBlocked = ResizeForStableForegroundCapture(hwnd, pid.Value, "after-excel-formula-bar-window-resize", "Excel");
            if (resizeBlocked is not null)
            {
                return resizeBlocked;
            }

            guard = ForegroundGuard.FocusAndVerify(hwnd, pid.Value, "Excel", options.FocusTimeout);
            if (!guard.Success)
            {
                return BlockedWithGuard(scenario, guard, "after-window-resize");
            }

            dynamic worksheet = excel.ActiveSheet;
            var formula = (string)worksheet.Range["B4"].Formula;
            if (!formula.Equals("=B2-B3", StringComparison.OrdinalIgnoreCase))
            {
                return CaptureResult.Blocked(scenario, "formula-seed-validation-failed", $"Expected Excel B4 formula '=B2-B3'; observed '{formula}'.", options.OutputRoot, "excel", guard);
            }

            var window = WindowFinder.GetWindowInfo(hwnd);
            if (window is null)
            {
                return CaptureResult.Blocked(scenario, "window-info-unavailable", "Could not resolve the foreground Excel window bounds.", options.OutputRoot, "excel", guard);
            }

            return CaptureWindow(
                scenario,
                "excel",
                window,
                guard,
                "complete",
                "Excel formula bar/name box reference: B4 is selected on a seeded formula worksheet and the formula bar should show '=B2-B3' with the name box showing B4.");
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

    private CaptureResult RunFreeXConditionalFormattingGalleryScenario()
        => RunDesktopConditionalFormattingGalleryScenario(
            "freex-conditional-formatting-gallery",
            "freex",
            ResolveFreeXExePath);

    private CaptureResult RunAvaloniaConditionalFormattingGalleryScenario()
        => RunDesktopConditionalFormattingGalleryScenario(
            "avalonia-conditional-formatting-gallery",
            "avalonia",
            ResolveAvaloniaExePath);

    private CaptureResult RunDesktopConditionalFormattingGalleryScenario(
        string scenario,
        string subject,
        Func<string> resolveExePath)
    {
        Process? process = null;
        int? windowProcessId = null;

        try
        {
            var launch = LaunchDesktopApp(scenario, subject, resolveExePath());
            if (launch.Result is not null)
            {
                return launch.Result;
            }

            process = launch.Process!;
            var window = launch.Window!;
            windowProcessId = window.ProcessId;
            var handle = new IntPtr(window.Handle);
            var guard = ForegroundGuard.FocusAndVerify(handle, windowProcessId.Value, "FreeX", options.FocusTimeout);
            if (!guard.Success)
            {
                return CaptureResult.Blocked(
                    scenario,
                    "foreground-guard-failed",
                    "Foreground guard failed during before-input.",
                    options.OutputRoot,
                    subject,
                    guard);
            }

            if (!TryOpenRibbonGalleryByText(handle, "Conditional Formatting"))
            {
                SendKeys.SendWait("%hl");
            }

            Thread.Sleep(options.AfterInputDelay);

            var popup = WindowFinder.FindProcessPopup(windowProcessId.Value, window.Handle, options.PopupTimeout, 120, 80);
            if (popup is null)
            {
                return CaptureResult.Blocked(scenario, "popup-not-found", "Did not detect foreground FreeX Conditional Formatting popup after UIA open or Alt,H,L fallback.", options.OutputRoot, subject, guard);
            }

            return CaptureWindow(scenario, subject, popup, guard, "complete");
        }
        finally
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }

            if (windowProcessId.HasValue &&
                process is not null &&
                windowProcessId.Value != process.Id)
            {
                KillProcess(windowProcessId);
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

            if (!TryGetCellBounds(handle, "Cell_A1", out var a1Bounds))
            {
                return CaptureResult.Blocked("freex-format-cells-dialog", "uia-cell-bounds-unavailable", "Could not resolve A1 bounds for the Format Cells comparison fixture.", options.OutputRoot, "freex", guard);
            }

            const string seed = "score\r\n1\r\n2\r\n3";
            var seedBlocked = PasteCellText(handle, process.Id, a1Bounds, seed);
            if (seedBlocked is not null)
            {
                return seedBlocked;
            }

            if (!WaitForCellValue(handle, "Cell_A1", "score", TimeSpan.FromSeconds(3), out var observedSeedValue))
            {
                return CaptureResult.Blocked("freex-format-cells-dialog", "cell-seed-validation-failed", $"Expected A1 to contain the shared Format Cells fixture value 'score'; observed '{observedSeedValue}'.", options.OutputRoot, "freex", guard);
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
            return CaptureWindow("freex-format-cells-dialog", "freex", dialog, guard, "complete", "Seeded A1:A4 with the shared score/1/2/3 fixture and validated A1 before opening Format Cells.");
        }
        finally
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    private CaptureResult RunFreeXFormatCellsContextDialogScenario()
    {
        const string scenario = "freex-format-cells-context-dialog";
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
                return BlockedWithGuard(scenario, guard, "before-context-click");
            }

            if (!TryGetCellBounds(handle, "Cell_A1", out var cellBounds))
            {
                return CaptureResult.Blocked(scenario, "uia-cell-bounds-unavailable", "Could not resolve A1 bounds for FreeX worksheet context-menu Format Cells route.", options.OutputRoot, "freex", guard);
            }

            RightClickScreenPoint(CenterX(cellBounds), CenterY(cellBounds));
            Thread.Sleep(options.AfterInputDelay);

            if (!TryInvokeProcessMenuItem(process.Id, "Format Cells"))
            {
                return CaptureResult.Blocked(scenario, "context-menu-item-not-found", "Right-clicked A1 in FreeX, but could not find or invoke a visible Format Cells menu item through UI Automation.", options.OutputRoot, "freex", guard);
            }

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
                return CaptureResult.Blocked(scenario, "dialog-not-found", "Invoked the FreeX worksheet context-menu Format Cells route, but did not detect a Format Cells dialog.", options.OutputRoot, "freex", guard);
            }

            Thread.Sleep(options.AfterDialogDetectedDelay);
            return CaptureWindow(scenario, "freex", dialog, guard, "complete", "Opened Format Cells through the FreeX worksheet context menu instead of Ctrl+1.");
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

            TypeDialogPath(dialog.Handle, existingPath);
            var prompt = WindowFinder.FindProcessWindow(
                process.Id,
                window => window.ClassName.Equals("#32770", StringComparison.OrdinalIgnoreCase) &&
                    window.Handle != dialog.Handle &&
                    (window.Title.Contains("Confirm", StringComparison.OrdinalIgnoreCase) ||
                     window.Title.Contains("Save As", StringComparison.OrdinalIgnoreCase) ||
                     window.Title.Contains("already exists", StringComparison.OrdinalIgnoreCase)),
                options.PopupTimeout);
            prompt ??= WindowFinder.FindForegroundWindow(
                window => window.ClassName.Equals("#32770", StringComparison.OrdinalIgnoreCase) &&
                    window.Handle != dialog.Handle &&
                    (window.Title.Contains("Confirm", StringComparison.OrdinalIgnoreCase) ||
                     window.Title.Contains("Save As", StringComparison.OrdinalIgnoreCase) ||
                     window.Title.Contains("already exists", StringComparison.OrdinalIgnoreCase)),
                TimeSpan.FromMilliseconds(1500));
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

    private CaptureResult RunFreeXSaveAsInvalidPathScenario()
    {
        Process? process = null;
        var invalidDirectory = Path.Combine(options.OutputRoot, "s3-missing-save-as-directory");
        var invalidPath = Path.Combine(invalidDirectory, "invalid-path.xlsx");
        if (Directory.Exists(invalidDirectory))
        {
            Directory.Delete(invalidDirectory, recursive: true);
        }

        try
        {
            var launch = LaunchFreeX("freex-save-as-invalid-path");
            if (launch.Result is not null)
            {
                return launch.Result;
            }

            process = launch.Process!;
            var handle = new IntPtr(launch.Window!.Handle);
            var guard = ForegroundGuard.FocusAndVerify(handle, process.Id, "FreeX", options.FocusTimeout);
            if (!guard.Success)
            {
                return BlockedWithGuard("freex-save-as-invalid-path", guard, "before-input");
            }

            SendKeys.SendWait("{F12}");
            Thread.Sleep(options.AfterInputDelay);
            var dialog = WindowFinder.FindProcessWindow(process.Id, "#32770", "Save As", options.PopupTimeout);
            if (dialog is null)
            {
                return CaptureResult.Blocked("freex-save-as-invalid-path", "dialog-not-found", "Did not detect FreeX Save As common dialog before invalid path entry.", options.OutputRoot, "freex", guard);
            }

            TypeDialogPath(dialog.Handle, invalidPath);
            var prompt = WindowFinder.FindProcessWindow(
                process.Id,
                window => window.ClassName.Equals("#32770", StringComparison.OrdinalIgnoreCase) &&
                    window.Handle != dialog.Handle &&
                    (window.Title.Contains("Save As", StringComparison.OrdinalIgnoreCase) ||
                     window.Title.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                     window.Title.Contains("path", StringComparison.OrdinalIgnoreCase) ||
                     window.Title.Contains("not found", StringComparison.OrdinalIgnoreCase)),
                options.PopupTimeout);
            prompt ??= WindowFinder.FindForegroundWindow(
                window => window.ClassName.Equals("#32770", StringComparison.OrdinalIgnoreCase) &&
                    window.Handle != dialog.Handle &&
                    (window.Title.Contains("Save As", StringComparison.OrdinalIgnoreCase) ||
                     window.Title.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                     window.Title.Contains("path", StringComparison.OrdinalIgnoreCase) ||
                     window.Title.Contains("not found", StringComparison.OrdinalIgnoreCase)),
                TimeSpan.FromMilliseconds(1500));
            if (prompt is null)
            {
                return CaptureResult.Blocked("freex-save-as-invalid-path", "invalid-path-prompt-not-found", $"Typed a missing-directory .xlsx path but did not detect a native invalid-path prompt: {invalidPath}", options.OutputRoot, "freex", guard);
            }

            Thread.Sleep(options.AfterDialogDetectedDelay);
            var result = CaptureWindow("freex-save-as-invalid-path", "freex", prompt, guard, "complete", $"Missing-directory save path was rejected by the native Save As flow: {invalidPath}");
            SendKeys.SendWait("{ESC}");
            Thread.Sleep(options.AfterInputDelay);
            return result with { OutputPath = invalidPath };
        }
        finally
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    private CaptureResult RunFreeXExportSaveDialogCancelScenario()
    {
        Process? process = null;

        try
        {
            var launch = LaunchFreeX("freex-export-pdf-save-dialog-cancel");
            if (launch.Result is not null)
            {
                return launch.Result;
            }

            process = launch.Process!;
            var mainHandle = new IntPtr(launch.Window!.Handle);
            var guard = ForegroundGuard.FocusAndVerify(mainHandle, process.Id, "FreeX", options.FocusTimeout);
            if (!guard.Success)
            {
                return BlockedWithGuard("freex-export-pdf-save-dialog-cancel", guard, "before-export-open");
            }

            var blocked = OpenFreeXExportSaveDialog("freex-export-pdf-save-dialog-cancel", process.Id, mainHandle, guard, out var dialog);
            if (blocked is not null)
            {
                return blocked;
            }

            Thread.Sleep(options.AfterDialogDetectedDelay);
            var result = CaptureWindow("freex-export-pdf-save-dialog-cancel", "freex", dialog!, guard, "complete");
            SendKeys.SendWait("{ESC}");
            Thread.Sleep(options.AfterInputDelay);
            return AttachContinuationCapture(
                result,
                "freex-export-pdf-save-dialog-cancel",
                process.Id,
                mainHandle,
                "FreeX",
                "Escape canceled the PDF/XPS native SaveFileDialog and returned foreground focus to FreeX.");
        }
        finally
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    private CaptureResult RunFreeXExportOverwritePromptScenario()
    {
        Process? process = null;
        var existingPath = Path.Combine(options.OutputRoot, "s3-existing-export-overwrite.pdf");
        File.WriteAllText(existingPath, "existing");

        try
        {
            var launch = LaunchFreeX("freex-export-overwrite-prompt");
            if (launch.Result is not null)
            {
                return launch.Result;
            }

            process = launch.Process!;
            var mainHandle = new IntPtr(launch.Window!.Handle);
            var guard = ForegroundGuard.FocusAndVerify(mainHandle, process.Id, "FreeX", options.FocusTimeout);
            if (!guard.Success)
            {
                return BlockedWithGuard("freex-export-overwrite-prompt", guard, "before-export-open");
            }

            var blocked = OpenFreeXExportSaveDialog("freex-export-overwrite-prompt", process.Id, mainHandle, guard, out var dialog);
            if (blocked is not null)
            {
                return blocked;
            }

            TypeDialogPath(dialog!.Handle, existingPath);
            var prompt = WindowFinder.FindProcessWindow(
                process.Id,
                window => window.ClassName.Equals("#32770", StringComparison.OrdinalIgnoreCase) &&
                    window.Handle != dialog.Handle &&
                    (window.Title.Contains("Confirm", StringComparison.OrdinalIgnoreCase) ||
                     window.Title.Contains("Export as PDF / XPS", StringComparison.OrdinalIgnoreCase) ||
                     window.Title.Contains("Save As", StringComparison.OrdinalIgnoreCase) ||
                     window.Title.Contains("already exists", StringComparison.OrdinalIgnoreCase)),
                options.PopupTimeout);
            prompt ??= WindowFinder.FindForegroundWindow(
                window => window.ClassName.Equals("#32770", StringComparison.OrdinalIgnoreCase) &&
                    window.Handle != dialog.Handle &&
                    (window.Title.Contains("Confirm", StringComparison.OrdinalIgnoreCase) ||
                     window.Title.Contains("Export as PDF / XPS", StringComparison.OrdinalIgnoreCase) ||
                     window.Title.Contains("Save As", StringComparison.OrdinalIgnoreCase) ||
                     window.Title.Contains("already exists", StringComparison.OrdinalIgnoreCase)),
                TimeSpan.FromMilliseconds(1500));
            if (prompt is null)
            {
                return CaptureResult.Blocked("freex-export-overwrite-prompt", "overwrite-prompt-not-found", "Typed an existing PDF path but did not detect a native export overwrite confirmation prompt.", options.OutputRoot, "freex", guard);
            }

            Thread.Sleep(options.AfterDialogDetectedDelay);
            var result = CaptureWindow("freex-export-overwrite-prompt", "freex", prompt, guard, "complete", $"Existing export path used: {existingPath}");
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

    private CaptureResult RunFreeXExportXpsAcceptScenario()
    {
        Process? process = null;
        var xpsPath = Path.Combine(options.OutputRoot, "s3-explicit-export.xps");
        if (File.Exists(xpsPath))
        {
            File.Delete(xpsPath);
        }

        try
        {
            var launch = LaunchFreeX("freex-export-xps-accept");
            if (launch.Result is not null)
            {
                return launch.Result;
            }

            process = launch.Process!;
            var mainHandle = new IntPtr(launch.Window!.Handle);
            var guard = ForegroundGuard.FocusAndVerify(mainHandle, process.Id, "FreeX", options.FocusTimeout);
            if (!guard.Success)
            {
                return BlockedWithGuard("freex-export-xps-accept", guard, "before-export-open");
            }

            const string seededExportValue = "FreeX XPS export parity";
            if (!TryGetCellBounds(mainHandle, "Cell_A1", out var a1Bounds))
            {
                return CaptureResult.Blocked("freex-export-xps-accept", "uia-cell-bounds-unavailable", "Could not resolve A1 bounds before seeding printable content for XPS export.", options.OutputRoot, "freex", guard);
            }

            var seedBlocked = PasteCellText(mainHandle, process.Id, a1Bounds, seededExportValue);
            if (seedBlocked is not null)
            {
                return seedBlocked;
            }

            if (!WaitForCellValue(mainHandle, "Cell_A1", seededExportValue, TimeSpan.FromSeconds(2), out var observedSeedValue))
            {
                return CaptureResult.Blocked("freex-export-xps-accept", "cell-seed-validation-failed", $"Expected A1 to contain printable export content before XPS export; observed '{observedSeedValue}'.", options.OutputRoot, "freex", guard);
            }

            var blocked = OpenFreeXExportSaveDialog("freex-export-xps-accept", process.Id, mainHandle, guard, out var dialog);
            if (blocked is not null)
            {
                return blocked;
            }

            if (!TrySelectDialogComboBoxItem(dialog!.Handle, "XPS"))
            {
                return CaptureResult.Blocked("freex-export-xps-accept", "xps-filter-not-selected", "Could not select the XPS file type in the native export SaveFileDialog.", options.OutputRoot, "freex", guard);
            }

            TypeCommonDialogFileNamePath(dialog.Handle, xpsPath);

            var optionsDialog = FindFreeXExportOptionsDialog(process.Id, dialog.Handle, options.PopupTimeout);
            if (optionsDialog is null)
            {
                var foreground = WindowFinder.GetWindowInfo(NativeMethods.GetForegroundWindow());
                if (foreground is not null)
                {
                    return CaptureWindow(
                        "freex-export-xps-accept",
                        "freex",
                        foreground,
                        guard,
                        "blocked",
                        "options-dialog-not-found: Accepted an explicit .xps path but did not detect the PDF/XPS options dialog; captured the foreground window that remained after accepting the native SaveFileDialog.");
                }

                return CaptureResult.Blocked("freex-export-xps-accept", "options-dialog-not-found", "Accepted an explicit .xps path but did not detect the PDF/XPS options dialog.", options.OutputRoot, "freex", guard);
            }

            Thread.Sleep(options.AfterDialogDetectedDelay);
            var result = CaptureWindow("freex-export-xps-accept", "freex", optionsDialog, guard, "complete", $"Explicit XPS save path accepted by native SaveFileDialog; awaiting output: {xpsPath}") with { OutputPath = xpsPath };
            SendKeys.SendWait("{ENTER}");
            Thread.Sleep(options.AfterInputDelay);

            var completion = WindowFinder.FindProcessWindow(
                process.Id,
                candidate => candidate.Handle != optionsDialog.Handle &&
                    candidate.ClassName.Equals("#32770", StringComparison.OrdinalIgnoreCase) &&
                    (candidate.Title.Contains("Export XPS", StringComparison.OrdinalIgnoreCase) ||
                     candidate.Title.Contains("XPS", StringComparison.OrdinalIgnoreCase) ||
                     candidate.Title.Contains("Export Error", StringComparison.OrdinalIgnoreCase)),
                TimeSpan.FromMilliseconds(1500));
            if (completion is not null)
            {
                Thread.Sleep(options.AfterDialogDetectedDelay);
                var outputLength = TryGetFileLength(xpsPath);
                var hasOutputAfterDialog = outputLength > 0;
                var completionStatus = hasOutputAfterDialog ? "complete" : "blocked";
                var validationAfterDialog = hasOutputAfterDialog
                    ? $"Explicit .xps native save path was accepted and non-empty output exists ({outputLength} bytes): {xpsPath}"
                    : $"Explicit .xps native save path reached '{completion.Title}', but non-empty output was not created: {xpsPath}";
                var completionResult = CaptureWindow("freex-export-xps-accept", "freex", completion, guard, completionStatus, validationAfterDialog) with { OutputPath = xpsPath };
                RewriteManifest(completionResult);
                SendKeys.SendWait("{ENTER}");
                Thread.Sleep(options.AfterInputDelay);
                return completionResult;
            }

            if (!WaitForNonEmptyFile(xpsPath, options.PopupTimeout))
            {
                var blockedResult = result with
                {
                    CaptureStatus = "blocked",
                    BlockReason = $"xps-output-not-created: Explicit .xps path returned from options without completion dialog, but non-empty output was not created: {xpsPath}"
                };
                RewriteManifest(blockedResult);
                return blockedResult;
            }

            return AttachContinuationCapture(
                result,
                "freex-export-xps-accept",
                process.Id,
                mainHandle,
                "FreeX",
                $"Explicit .xps native save path was accepted, non-empty output exists, and foreground returned to FreeX: {xpsPath}") with
            {
                OutputPath = xpsPath
            };
        }
        finally
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    private CaptureResult RunFreeXNativePrintDialogScenario()
    {
        Process? process = null;

        try
        {
            var launch = LaunchFreeX("freex-native-print-dialog");
            if (launch.Result is not null)
            {
                return launch.Result;
            }

            process = launch.Process!;
            var mainHandle = new IntPtr(launch.Window!.Handle);
            var guard = ForegroundGuard.FocusAndVerify(mainHandle, process.Id, "FreeX", options.FocusTimeout);
            if (!guard.Success)
            {
                return BlockedWithGuard("freex-native-print-dialog", guard, "before-print-backstage-open");
            }

            var blocked = InvokeFreeXBackstageButton("freex-native-print-dialog", process.Id, mainHandle, "BackstagePrintButton", guard);
            if (blocked is not null)
            {
                return blocked;
            }

            var previewButton = FindVisibleElementByAutomationId(mainHandle, "BackstagePrintPreviewButton");
            if (previewButton is null)
            {
                return CaptureResult.Blocked("freex-native-print-dialog", "uia-target-not-found", "Could not find visible Backstage Print Preview button.", options.OutputRoot, "freex", guard);
            }

            blocked = InvokeOrClickElement("freex-native-print-dialog", process.Id, mainHandle, previewButton, "freex");
            if (blocked is not null)
            {
                return blocked;
            }

            var preview = WindowFinder.FindProcessWindow(
                process.Id,
                candidate => candidate.Handle != mainHandle.ToInt64() &&
                    candidate.Title.Contains("Print Preview", StringComparison.OrdinalIgnoreCase),
                options.PopupTimeout);
            if (preview is null)
            {
                return CaptureResult.Blocked("freex-native-print-dialog", "print-preview-not-found", "Did not detect the FreeX Print Preview dialog before native print launch.", options.OutputRoot, "freex", guard);
            }

            var printButton = FindVisibleElementByAutomationId(new IntPtr(preview.Handle), "PrintPreviewPrintButton");
            if (printButton is null)
            {
                return CaptureResult.Blocked("freex-native-print-dialog", "print-button-not-found", "Print Preview opened, but the Print button automation target was not visible.", options.OutputRoot, "freex", guard);
            }

            blocked = ClickPrintPreviewPrintButton("freex-native-print-dialog", process.Id, preview, printButton);
            if (blocked is not null)
            {
                return blocked;
            }

            var printDialog = FindNativePrintDialog(process.Id, preview.Handle, options.PopupTimeout);
            if (printDialog is null)
            {
                return CaptureResult.Blocked("freex-native-print-dialog", "native-print-dialog-not-found", "Clicked Print Preview's Print button but did not detect a native Windows Print dialog.", options.OutputRoot, "freex", guard);
            }

            Thread.Sleep(options.AfterDialogDetectedDelay);
            var dialogGuard = new ForegroundGuardResult(true, process.Id, printDialog.Handle, printDialog, null);
            var result = CaptureWindow("freex-native-print-dialog", "freex", printDialog, dialogGuard, "complete", "Print Preview's Print button opened the native Windows PrintDialog in the FreeX foreground-owned process.");
            SendKeys.SendWait("{ESC}");
            Thread.Sleep(options.AfterInputDelay);
            return result;
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

    private CaptureResult RunFreeXBackgroundPickerReplaceScenario()
    {
        Process? process = null;
        var firstImagePath = Path.Combine(options.OutputRoot, "s3-sheet-background-initial.png");
        var replacementImagePath = Path.Combine(options.OutputRoot, "s3-sheet-background-replacement.png");
        CreateTinyPng(firstImagePath, Color.LightSteelBlue, Color.DarkSlateBlue);
        CreateTinyPng(replacementImagePath, Color.LightGoldenrodYellow, Color.Firebrick);

        try
        {
            var launch = LaunchFreeX("freex-background-picker-replace");
            if (launch.Result is not null)
            {
                return launch.Result;
            }

            process = launch.Process!;
            var mainHandle = new IntPtr(launch.Window!.Handle);
            var guard = ForegroundGuard.FocusAndVerify(mainHandle, process.Id, "FreeX", options.FocusTimeout);
            if (!guard.Success)
            {
                return BlockedWithGuard("freex-background-picker-replace", guard, "before-initial-background");
            }

            var initial = SelectFreeXBackgroundImage(process, mainHandle, guard, firstImagePath, captureDialog: false);
            if (initial is not null)
            {
                return initial;
            }

            guard = ForegroundGuard.FocusAndVerify(mainHandle, process.Id, "FreeX", options.FocusTimeout);
            if (!guard.Success)
            {
                return BlockedWithGuard("freex-background-picker-replace", guard, "before-replacement-background");
            }

            var replacement = SelectFreeXBackgroundImage(process, mainHandle, guard, replacementImagePath, captureDialog: true);
            return replacement ?? CaptureResult.Blocked("freex-background-picker-replace", "replacement-result-missing", "Replacement picker flow returned no capture result.", options.OutputRoot, "freex", guard);
        }
        finally
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    private CaptureResult RunFreeXBackgroundClearScenario()
    {
        Process? process = null;
        var imagePath = Path.Combine(options.OutputRoot, "s3-sheet-background-clear-seed.png");
        CreateTinyPng(imagePath, Color.Honeydew, Color.SeaGreen);

        try
        {
            var launch = LaunchFreeX("freex-background-clear");
            if (launch.Result is not null)
            {
                return launch.Result;
            }

            process = launch.Process!;
            var mainHandle = new IntPtr(launch.Window!.Handle);
            var guard = ForegroundGuard.FocusAndVerify(mainHandle, process.Id, "FreeX", options.FocusTimeout);
            if (!guard.Success)
            {
                return BlockedWithGuard("freex-background-clear", guard, "before-background-seed");
            }

            var seed = SelectFreeXBackgroundImage(process, mainHandle, guard, imagePath, captureDialog: false);
            if (seed is not null)
            {
                return seed;
            }

            guard = ForegroundGuard.FocusAndVerify(mainHandle, process.Id, "FreeX", options.FocusTimeout);
            if (!guard.Success)
            {
                return BlockedWithGuard("freex-background-clear", guard, "before-background-clear");
            }

            var blocked = OpenFreeXBackgroundContextMenu(process, mainHandle, guard);
            if (blocked is not null)
            {
                return blocked;
            }

            if (!TryInvokeProcessMenuItem(process.Id, "Delete Background"))
            {
                return CaptureResult.Blocked("freex-background-clear", "delete-background-menu-item-not-found", "Opened the Background button context menu, but could not find or invoke Delete Background.", options.OutputRoot, "freex", guard);
            }

            Thread.Sleep(options.AfterInputDelay);
            var window = WindowFinder.GetWindowInfo(mainHandle);
            if (window is null)
            {
                return CaptureResult.Blocked("freex-background-clear", "main-window-not-found", "Could not read FreeX main window after Delete Background.", options.OutputRoot, "freex", guard);
            }

            return CaptureWindow("freex-background-clear", "freex", window, guard, "complete", $"Selected a PNG worksheet background and then invoked Delete Background; seed image: {imagePath}") with
            {
                OutputPath = imagePath
            };
        }
        finally
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }
        }
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
        => LaunchDesktopApp(scenario, "freex", ResolveFreeXExePath());

    private (Process? Process, WindowInfo? Window, CaptureResult? Result) LaunchDesktopApp(
        string scenario,
        string subject,
        string exePath)
    {
        var process = Process.Start(new ProcessStartInfo(exePath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory
        });

        if (process is null)
        {
            return (null, null, CaptureResult.Blocked(scenario, "launch-failed", $"Failed to launch '{exePath}'.", options.OutputRoot, subject));
        }

        var window = WindowFinder.WaitForMainWindow(process, exePath, options.LaunchTimeout);
        if (window is null)
        {
            var diagnostics = WindowFinder.DescribeLaunchWindowCandidates(process.Id, exePath);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            return (process, null, CaptureResult.Blocked(scenario, "window-not-found", $"FreeX process {process.Id} did not expose a visible main window. {diagnostics}", options.OutputRoot, subject));
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

    private CaptureResult? SelectFreeXBackgroundImage(
        Process process,
        IntPtr mainHandle,
        ForegroundGuardResult guard,
        string imagePath,
        bool captureDialog)
    {
        var blocked = OpenFreeXBackgroundPicker(process, mainHandle, guard);
        if (blocked is not null)
        {
            return blocked;
        }

        var dialog = WindowFinder.FindProcessWindow(process.Id, "#32770", "Sheet Background", options.PopupTimeout);
        if (dialog is null)
        {
            return CaptureResult.Blocked(options.Scenario, "dialog-not-found", "Did not detect FreeX native Sheet Background OpenFileDialog.", options.OutputRoot, "freex", guard);
        }

        CaptureResult? result = null;
        if (captureDialog)
        {
            Thread.Sleep(options.AfterDialogDetectedDelay);
            result = CaptureWindow(options.Scenario, "freex", dialog, guard, "complete", $"Replacing worksheet background through native picker with: {imagePath}");
        }

        TypeDialogPath(imagePath);
        Thread.Sleep(options.AfterInputDelay);

        if (result is null)
        {
            return null;
        }

        return AttachContinuationCapture(
            result,
            options.Scenario,
            process.Id,
            mainHandle,
            "FreeX",
            $"Selected replacement worksheet background path and returned focus to FreeX: {imagePath}") with
        {
            OutputPath = imagePath
        };
    }

    private CaptureResult? OpenFreeXBackgroundContextMenu(Process process, IntPtr mainHandle, ForegroundGuardResult guard)
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
            return CaptureResult.Blocked(options.Scenario, "uia-target-not-found", "Could not find the Page Layout Background button before opening its clear menu.", options.OutputRoot, "freex", guard);
        }

        var bounds = backgroundButton.Current.BoundingRectangle;
        if (bounds.IsEmpty || bounds.Width < 1 || bounds.Height < 1)
        {
            return CaptureResult.Blocked(options.Scenario, "uia-target-bounds-invalid", $"Background button bounds were not usable: {bounds}.", options.OutputRoot, "freex", guard);
        }

        var focused = ForegroundGuard.FocusAndVerify(mainHandle, process.Id, "FreeX", options.FocusTimeout);
        if (!focused.Success)
        {
            return CaptureResult.Blocked(options.Scenario, "foreground-guard-failed", "Foreground guard failed before opening the Background context menu.", options.OutputRoot, "freex", focused);
        }

        RightClickScreenPoint(CenterX(bounds), CenterY(bounds));
        Thread.Sleep(options.AfterInputDelay);
        return null;
    }

    private CaptureResult? OpenFreeXExportSaveDialog(
        string scenario,
        int processId,
        IntPtr mainHandle,
        ForegroundGuardResult guard,
        out WindowInfo? dialog)
    {
        dialog = null;
        var blocked = InvokeFreeXBackstageButton(scenario, processId, mainHandle, "BackstageExportButton", guard);
        if (blocked is not null)
        {
            return blocked;
        }

        dialog = WindowFinder.FindProcessWindow(processId, "#32770", "Export as PDF / XPS", options.PopupTimeout);
        if (dialog is null)
        {
            return CaptureResult.Blocked(scenario, "dialog-not-found", "Did not detect FreeX PDF/XPS native SaveFileDialog after invoking Backstage Export.", options.OutputRoot, "freex", guard);
        }

        return null;
    }

    private static WindowInfo? FindFreeXExportOptionsDialog(int processId, long saveDialogHandle, TimeSpan timeout)
    {
        return WindowFinder.FindProcessWindow(
            processId,
            candidate => candidate.Handle != saveDialogHandle &&
                candidate.Bounds.Width >= 300 &&
                candidate.Bounds.Height >= 250 &&
                (candidate.Title.Contains("Export Options", StringComparison.OrdinalIgnoreCase) ||
                 candidate.Title.Contains("PDF/XPS options", StringComparison.OrdinalIgnoreCase) ||
                 candidate.Title.Contains("PDF/XPS", StringComparison.OrdinalIgnoreCase) ||
                 candidate.Title.Contains("Publish", StringComparison.OrdinalIgnoreCase)),
            timeout);
    }

    private CaptureResult? ClickPrintPreviewPrintButton(string scenario, int processId, WindowInfo preview, AutomationElement printButton)
    {
        var bounds = printButton.Current.BoundingRectangle;
        if (bounds.IsEmpty || bounds.Width < 1 || bounds.Height < 1)
        {
            return CaptureResult.Blocked(scenario, "uia-target-bounds-invalid", $"Print Preview Print button bounds were not usable: {bounds}.", options.OutputRoot, "freex");
        }

        var previewHandle = new IntPtr(preview.Handle);
        var guard = ForegroundGuard.FocusAndVerify(previewHandle, processId, "Print Preview", options.FocusTimeout);
        if (!guard.Success)
        {
            return CaptureResult.Blocked(scenario, "foreground-guard-failed", "Foreground guard failed before clicking Print Preview's Print button.", options.OutputRoot, "freex", guard);
        }

        NativeMethods.SetCursorPos((int)(bounds.Left + bounds.Width / 2.0), (int)(bounds.Top + bounds.Height / 2.0));
        Thread.Sleep(100);
        NativeMethods.MouseEvent(NativeMethods.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(60);
        NativeMethods.MouseEvent(NativeMethods.MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(options.AfterInputDelay);
        return null;
    }

    private static WindowInfo? FindNativePrintDialog(int processId, long previewHandle, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var foreground = WindowFinder.GetWindowInfo(NativeMethods.GetForegroundWindow());
            if (IsNativePrintDialog(foreground, previewHandle, allowAnyProcess: true))
            {
                return foreground;
            }

            var processDialog = WindowFinder.FindProcessWindow(
                processId,
                candidate => IsNativePrintDialog(candidate, previewHandle, allowAnyProcess: false),
                TimeSpan.FromMilliseconds(150));
            if (processDialog is not null)
            {
                return processDialog;
            }

            Thread.Sleep(150);
        }

        return null;
    }

    private static bool IsNativePrintDialog(WindowInfo? candidate, long previewHandle, bool allowAnyProcess)
    {
        return candidate is not null &&
            candidate.Handle != previewHandle &&
            candidate.ClassName.Equals("#32770", StringComparison.OrdinalIgnoreCase) &&
            candidate.Title.Contains("Print", StringComparison.OrdinalIgnoreCase) &&
            candidate.Bounds.Width >= 300 &&
            candidate.Bounds.Height >= 200 &&
            (allowAnyProcess || candidate.ProcessId != 0);
    }

    private CaptureResult? InvokeFreeXBackstageButton(
        string scenario,
        int processId,
        IntPtr mainHandle,
        string automationId,
        ForegroundGuardResult guard)
    {
        var fileTab = FindDescendantByNameAndType(mainHandle, "File", ControlType.TabItem);
        if (fileTab is not null)
        {
            TrySelectOrInvoke(fileTab);
        }
        else
        {
            SendKeys.SendWait("%f");
        }

        Thread.Sleep(options.AfterInputDelay);
        var button = FindVisibleElementByAutomationId(mainHandle, automationId);
        if (button is null)
        {
            return CaptureResult.Blocked(scenario, "uia-target-not-found", $"Could not find visible Backstage button AutomationId '{automationId}'.", options.OutputRoot, "freex", guard);
        }

        return InvokeOrClickElement(scenario, processId, mainHandle, button, "freex");
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

    private static void TypeDialogPath(long dialogHandle, string path)
    {
        if (TrySetCommonDialogFileName(dialogHandle, path) &&
            TryInvokeCommonDialogDefaultButton(dialogHandle))
        {
            Thread.Sleep(250);
            return;
        }

        TypeDialogPath(path);
    }

    private static void TypeCommonDialogFileNamePath(long dialogHandle, string path)
    {
        var handle = new IntPtr(dialogHandle);
        NativeMethods.SetForegroundWindow(handle);
        NativeMethods.SetFocus(handle);
        Thread.Sleep(150);
        SendKeys.SendWait("%n");
        Thread.Sleep(100);
        Clipboard.SetText(path);
        Thread.Sleep(100);
        SendKeys.SendWait("^a");
        Thread.Sleep(50);
        SendKeys.SendWait("^v");
        Thread.Sleep(150);
        SendKeys.SendWait("{ENTER}");
        Thread.Sleep(300);
    }

    private static bool TrySetCommonDialogFileName(long dialogHandle, string path)
    {
        try
        {
            var root = AutomationElement.FromHandle(new IntPtr(dialogHandle));
            var edits = root.FindAll(
                    TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit))
                .Cast<AutomationElement>()
                .Where(IsVisibleElement)
                .Select(element => new
                {
                    Element = element,
                    Score = CommonDialogFileNameEditScore(element)
                })
                .Where(candidate => candidate.Score > 0)
                .OrderByDescending(candidate => candidate.Score)
                .ToArray();

            foreach (var candidate in edits)
            {
                var edit = candidate.Element;
                if (!edit.TryGetCurrentPattern(ValuePattern.Pattern, out var valueObject) ||
                    valueObject is not ValuePattern value ||
                    value.Current.IsReadOnly)
                {
                    continue;
                }

                edit.SetFocus();
                Thread.Sleep(100);
                value.SetValue(path);
                Thread.Sleep(150);
                if (TryReadElementValue(edit, out var observed) &&
                    observed.Equals(path, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                Clipboard.SetText(path);
                Thread.Sleep(100);
                SendKeys.SendWait("^a");
                Thread.Sleep(50);
                SendKeys.SendWait("^v");
                Thread.Sleep(150);
                if (TryReadElementValue(edit, out observed) &&
                    observed.Equals(path, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (ElementNotAvailableException)
        {
        }

        return false;
    }

    private static bool TryInvokeCommonDialogDefaultButton(long dialogHandle)
    {
        try
        {
            var root = AutomationElement.FromHandle(new IntPtr(dialogHandle));
            var buttons = root.FindAll(
                    TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button))
                .Cast<AutomationElement>()
                .Where(IsVisibleElement)
                .ToArray();

            var button = buttons.FirstOrDefault(candidate =>
                    candidate.Current.AutomationId.Equals("1", StringComparison.OrdinalIgnoreCase))
                ?? buttons.FirstOrDefault(candidate =>
                    candidate.Current.Name.Equals("Save", StringComparison.OrdinalIgnoreCase) ||
                    candidate.Current.Name.Equals("&Save", StringComparison.OrdinalIgnoreCase) ||
                    candidate.Current.Name.Equals("Open", StringComparison.OrdinalIgnoreCase) ||
                    candidate.Current.Name.Equals("&Open", StringComparison.OrdinalIgnoreCase));

            if (button is null)
            {
                return false;
            }

            if (button.TryGetCurrentPattern(InvokePattern.Pattern, out var invokeObject) &&
                invokeObject is InvokePattern invoke)
            {
                invoke.Invoke();
                Thread.Sleep(100);
                return true;
            }

            var bounds = button.Current.BoundingRectangle;
            if (bounds.IsEmpty || bounds.Width < 1 || bounds.Height < 1)
            {
                return false;
            }

            NativeMethods.SetCursorPos((int)(bounds.Left + bounds.Width / 2.0), (int)(bounds.Top + bounds.Height / 2.0));
            Thread.Sleep(100);
            NativeMethods.MouseEvent(NativeMethods.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(60);
            NativeMethods.MouseEvent(NativeMethods.MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(100);
            return true;
        }
        catch (InvalidOperationException)
        {
        }
        catch (ElementNotAvailableException)
        {
        }

        return false;
    }

    private static bool IsCommonDialogFileNameEdit(AutomationElement element)
        => CommonDialogFileNameEditScore(element) > 0;

    private static int CommonDialogFileNameEditScore(AutomationElement element)
    {
        try
        {
            var automationId = element.Current.AutomationId;
            var name = element.Current.Name;
            if (automationId.Equals("1148", StringComparison.OrdinalIgnoreCase))
            {
                return 100;
            }

            return name.Contains("File name", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("File name:", StringComparison.OrdinalIgnoreCase)
                ? 90
                : 0;
        }
        catch (ElementNotAvailableException)
        {
            return 0;
        }
    }

    private static bool TryReadElementValue(AutomationElement element, out string value)
    {
        value = string.Empty;
        try
        {
            if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out var patternObject) ||
                patternObject is not ValuePattern valuePattern)
            {
                return false;
            }

            value = valuePattern.Current.Value ?? string.Empty;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static bool TrySelectDialogComboBoxItem(long dialogHandle, string itemTextContains)
    {
        var root = AutomationElement.FromHandle(new IntPtr(dialogHandle));
        var comboBoxes = root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ComboBox))
            .Cast<AutomationElement>()
            .Where(IsVisibleElement)
            .Reverse()
            .ToArray();

        foreach (var comboBox in comboBoxes)
        {
            if (!comboBox.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var expandObject) ||
                expandObject is not ExpandCollapsePattern expand)
            {
                continue;
            }

            expand.Expand();
            Thread.Sleep(250);

            var items = comboBox.FindAll(
                    TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem))
                .Cast<AutomationElement>()
                .Where(IsVisibleElement)
                .ToArray();

            var match = items.FirstOrDefault(item =>
                item.Current.Name.Contains(itemTextContains, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                expand.Collapse();
                continue;
            }

            if (match.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var selectionObject) &&
                selectionObject is SelectionItemPattern selection)
            {
                selection.Select();
            }
            else if (match.TryGetCurrentPattern(InvokePattern.Pattern, out var invokeObject) &&
                invokeObject is InvokePattern invoke)
            {
                invoke.Invoke();
            }
            else
            {
                var bounds = match.Current.BoundingRectangle;
                if (bounds.IsEmpty || bounds.Width < 1 || bounds.Height < 1)
                {
                    expand.Collapse();
                    continue;
                }

                NativeMethods.SetCursorPos((int)(bounds.Left + bounds.Width / 2.0), (int)(bounds.Top + bounds.Height / 2.0));
                Thread.Sleep(100);
                NativeMethods.MouseEvent(NativeMethods.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                Thread.Sleep(60);
                NativeMethods.MouseEvent(NativeMethods.MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            }

            Thread.Sleep(250);
            return true;
        }

        return false;
    }

    private static void CreateTinyPng(string path) =>
        CreateTinyPng(path, Color.LightSteelBlue, Color.DarkSlateBlue);

    private static void CreateTinyPng(string path, Color background, Color accent)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        using var bitmap = new Bitmap(8, 8);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(background);
            using var brush = new SolidBrush(accent);
            graphics.FillRectangle(brush, 2, 2, 4, 4);
        }

        bitmap.Save(path, ImageFormat.Png);
    }

    private static bool WaitForFile(string path, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path))
            {
                return true;
            }

            Thread.Sleep(150);
        }

        return File.Exists(path);
    }

    private static bool WaitForNonEmptyFile(string path, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (TryGetFileLength(path) > 0)
            {
                return true;
            }

            Thread.Sleep(150);
        }

        return TryGetFileLength(path) > 0;
    }

    private static long TryGetFileLength(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
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

    private static AutomationElement? FindVisibleElementByAutomationId(IntPtr handle, string automationId)
    {
        var root = AutomationElement.FromHandle(handle);
        return root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, automationId))
            .Cast<AutomationElement>()
            .Where(IsVisibleElement)
            .OrderBy(element => element.Current.BoundingRectangle.Top)
            .ThenBy(element => element.Current.BoundingRectangle.Left)
            .FirstOrDefault();
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

    private static bool TryInvokeOrClickAutomationElement(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var invokePatternObject) &&
                invokePatternObject is InvokePattern invoke)
            {
                invoke.Invoke();
                return true;
            }

            if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var selectionPatternObject) &&
                selectionPatternObject is SelectionItemPattern selection)
            {
                selection.Select();
                return true;
            }

            var bounds = element.Current.BoundingRectangle;
            if (bounds.IsEmpty || bounds.Width < 1 || bounds.Height < 1)
            {
                return false;
            }

            NativeMethods.SetCursorPos((int)(bounds.Left + bounds.Width / 2.0), (int)(bounds.Top + bounds.Height / 2.0));
            Thread.Sleep(100);
            NativeMethods.MouseEvent(NativeMethods.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(60);
            NativeMethods.MouseEvent(NativeMethods.MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            return true;
        }
        catch (COMException)
        {
            return false;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private CaptureResult RunFreeXMainWindowPointerScenario(
        string scenario,
        Func<IntPtr, int, WindowInfo, ForegroundGuardResult, CaptureResult?> action,
        Func<string, IReadOnlyDictionary<string, string>>? createEnvironmentOverride = null)
    {
        Process? process = null;
        _lastResultValidation = null;
        _lastCaptureWindow = null;

        try
        {
            var exePath = ResolveFreeXExePath();
            var startInfo = new ProcessStartInfo(exePath)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory
            };
            // The capture runner can itself be launched under a developer-local DOTNET_ROOT that
            // lacks the runtime used by the built desktop host. Let the app host resolve the
            // installed runtime normally rather than opening the .NET runtime error dialog.
            startInfo.Environment.Remove("DOTNET_ROOT");
            startInfo.Environment.Remove("DOTNET_ROOT_X64");
            if (createEnvironmentOverride is not null)
            {
                foreach (var pair in createEnvironmentOverride(scenario))
                {
                    startInfo.Environment[pair.Key] = pair.Value;
                }
            }

            process = Process.Start(startInfo);

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

            var actionWindow = WindowFinder.GetWindowInfo(handle) ?? guard.ForegroundWindow ?? window;
            var blocked = action(handle, process.Id, actionWindow, guard);
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

            return CaptureWindow(scenario, "freex", _lastCaptureWindow ?? refreshedWindow, guard, "complete", _lastResultValidation);
        }
        finally
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    private static IReadOnlyDictionary<string, string> CreateStatusStatsOptionsOverride(string scenario)
    {
        var optionsPath = Path.Combine(
            Path.GetTempPath(),
            "FreeX.ForegroundCapture",
            $"{scenario}-{Guid.NewGuid():N}",
            "options.json");
        Directory.CreateDirectory(Path.GetDirectoryName(optionsPath)!);
        File.WriteAllText(
            optionsPath,
            """
            {
              "StatusBarShowAverage": true,
              "StatusBarShowCount": true,
              "StatusBarShowNumericalCount": true,
              "StatusBarShowSum": true,
              "StatusBarShowMinimum": true,
              "StatusBarShowMaximum": true,
              "StatusBarShowViewShortcuts": true,
              "StatusBarShowZoom": true,
              "StatusBarShowZoomSlider": true
            }
            """);

        return new Dictionary<string, string>
        {
            ["FREEX_OPTIONS_PATH"] = optionsPath
        };
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

            try
            {
                rangePattern.SetValue(targetSliderValue);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ElementNotAvailableException or TimeoutException or COMException)
            {
                var candidate = string.Empty;
                try
                {
                    candidate = slider.Current.Name ?? string.Empty;
                }
                catch (Exception candidateEx) when (candidateEx is InvalidOperationException or ElementNotAvailableException or COMException)
                {
                }

                var suffix = string.IsNullOrWhiteSpace(candidate)
                    ? string.Empty
                    : $" Last UIA candidate was '{candidate}'.";
                return CaptureResult.Blocked(options.Scenario, "uia-rangevalue-set-failed", $"Could not set the Zoom slider RangeValue to {targetSliderValue:0.###}: {ex.GetType().Name}: {ex.Message}.{suffix}", options.OutputRoot, "freex", guard);
            }
            Thread.Sleep(options.AfterInputDelay);

            return ValidateZoomSliderValue(handle, targetSliderValue, $"native UIA RangeValue.SetValue({targetSliderValue:0.###})");
        };

    private Func<IntPtr, int, WindowInfo, ForegroundGuardResult, CaptureResult?> SetZoomSliderMinMaxRangeValues()
        => (handle, processId, _, guard) =>
        {
            var zoomOutCenter = ResolveStatusZoomButtonCenter(handle, processId, "StatusZoomOutButton", "minimum", guard);
            if (zoomOutCenter.Blocked is not null)
            {
                return zoomOutCenter.Blocked;
            }

            var zoomInCenter = ResolveStatusZoomButtonCenter(handle, processId, "StatusZoomInButton", "maximum", guard);
            if (zoomInCenter.Blocked is not null)
            {
                return zoomInCenter.Blocked;
            }

            var validations = new List<string>();
            foreach (var (value, label, x, y, clickCount) in new[]
            {
                (0d, "minimum", zoomOutCenter.X, zoomOutCenter.Y, 24),
                (200d, "maximum", zoomInCenter.X, zoomInCenter.Y, 44)
            })
            {
                guard = ForegroundGuard.FocusAndVerify(handle, processId, "FreeX", options.FocusTimeout);
                if (!guard.Success)
                {
                    return BlockedWithGuard(options.Scenario, guard, $"before-zoom-button-clicks-{label}");
                }

                var blocked = ClickStatusZoomButtonRepeatedly(handle, processId, x, y, clickCount, label);
                if (blocked is not null)
                {
                    return blocked;
                }

                blocked = ValidateStatusZoomTextValue(handle, SliderToZoomPercent(value), $"foreground {label} zoom button clicks");
                if (blocked is not null)
                {
                    return blocked;
                }

                validations.Add($"{label} visible zoom={SliderToZoomPercent(value):0}%");
            }

            _lastResultValidation = "Status zoom min/max foreground button proof: " + string.Join("; ", validations) + ".";
            return null;
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

    private Func<IntPtr, int, WindowInfo, ForegroundGuardResult, CaptureResult?> ClickStatusViewShortcuts()
        => (handle, processId, _, guard) =>
        {
            var sequence = new[]
            {
                ("StatusPageLayoutViewButton", "Page Layout"),
                ("StatusPageBreakPreviewButton", "Page Break Preview"),
                ("StatusNormalViewButton", "Normal")
            };

            var validations = new List<string>();
            foreach (var (automationId, label) in sequence)
            {
                var element = FindElementByAutomationId(handle, automationId);
                if (element is null)
                {
                    return CaptureResult.Blocked(options.Scenario, "uia-target-not-found", $"Could not find AutomationId '{automationId}'.", options.OutputRoot, "freex", guard);
                }

                var blocked = GuardedClickElement(options.Scenario, processId, handle, element, MouseButtonKind.Left);
                if (blocked is not null)
                {
                    return blocked;
                }

                Thread.Sleep(options.AfterInputDelay);
                element = FindElementByAutomationId(handle, automationId);
                if (element is null || !TryGetToggleState(element, out var state))
                {
                    return CaptureResult.Blocked(options.Scenario, "toggle-validation-unavailable", $"Could not read TogglePattern state for '{automationId}' after physical click.", options.OutputRoot, "freex", guard);
                }

                if (state != ToggleState.On)
                {
                    return CaptureResult.Blocked(options.Scenario, "toggle-validation-failed", $"Expected '{automationId}' to be checked after physical {label} footer click, but UIA reported {state}.", options.OutputRoot, "freex", guard);
                }

                validations.Add($"{label} checked");
            }

            _lastResultValidation = "Physical footer view shortcut clicks: " + string.Join("; ", validations) + ".";
            return null;
        };

    private Func<IntPtr, int, WindowInfo, ForegroundGuardResult, CaptureResult?> ClickZoomTextExpectDialog()
        => (handle, processId, _, guard) =>
        {
            var element = FindElementByAutomationId(handle, "StatusZoomText");
            if (element is null)
            {
                return CaptureResult.Blocked(options.Scenario, "uia-target-not-found", "Could not find AutomationId 'StatusZoomText'.", options.OutputRoot, "freex", guard);
            }

            var blocked = GuardedClickElement(options.Scenario, processId, handle, element, MouseButtonKind.Left);
            if (blocked is not null)
            {
                return blocked;
            }

            var dialog = WindowFinder.FindProcessWindow(
                processId,
                window => window.Title.Equals("Zoom", StringComparison.OrdinalIgnoreCase),
                options.PopupTimeout);
            if (dialog is null)
            {
                return CaptureResult.Blocked(options.Scenario, "dialog-not-found", "Did not detect the Zoom dialog after physically clicking the status zoom percentage text.", options.OutputRoot, "freex", guard);
            }

            var dialogHandle = new IntPtr(dialog.Handle);
            guard = ForegroundGuard.FocusAndVerify(dialogHandle, processId, "Zoom", options.FocusTimeout);
            if (!guard.Success)
            {
                return BlockedWithGuard(options.Scenario, guard, "before-zoom-dialog-capture");
            }

            return CaptureWindow(
                options.Scenario,
                "freex",
                dialog,
                guard,
                "complete",
                "Physically clicked the status zoom percentage text and captured the foreground-owned FreeX Zoom dialog.");
        };

    private Func<IntPtr, int, WindowInfo, ForegroundGuardResult, CaptureResult?> CtrlAltZoomKeysExpectRoundTrip()
        => (handle, processId, _, guard) =>
        {
            var blocked = GuardedKeyChord(options.Scenario, processId, handle, [NativeMethods.VK_CONTROL, NativeMethods.VK_MENU], NativeMethods.VK_OEM_PLUS, "ctrl-alt-plus");
            if (blocked is not null)
            {
                return blocked;
            }

            blocked = ValidateZoomSliderValue(handle, 105, "Ctrl+Alt+= key chord");
            if (blocked is not null)
            {
                return blocked;
            }

            blocked = GuardedKeyChord(options.Scenario, processId, handle, [NativeMethods.VK_CONTROL, NativeMethods.VK_MENU], NativeMethods.VK_OEM_MINUS, "ctrl-alt-minus");
            if (blocked is not null)
            {
                return blocked;
            }

            blocked = ValidateZoomSliderValue(handle, 100, "Ctrl+Alt+- key chord");
            if (blocked is not null)
            {
                return blocked;
            }

            _lastResultValidation = "Foreground Ctrl+Alt+= then Ctrl+Alt+- changed the status zoom slider 100->105->100 and kept the visible zoom text in sync.";
            return null;
        };

    private Func<IntPtr, int, WindowInfo, ForegroundGuardResult, CaptureResult?> StatusLiveStatsAccessibility()
        => (handle, processId, _, guard) =>
        {
            var resizeBlocked = ResizeForStatusStatisticReadback(handle, processId, guard);
            if (resizeBlocked is not null)
            {
                return resizeBlocked;
            }

            if (!TryGetCellBounds(handle, "Cell_A1", out var a1Bounds) ||
                !TryGetCellBounds(handle, "Cell_A4", out var a4Bounds))
            {
                return CaptureResult.Blocked(options.Scenario, "uia-cell-bounds-unavailable", "Could not resolve A1/A4 bounds for status statistic setup.", options.OutputRoot, "freex", guard);
            }

            var blocked = PasteCellText(handle, processId, a1Bounds, "2\r\n4\r\n6\r\n8");
            if (blocked is not null)
            {
                return blocked;
            }

            blocked = GuardedDrag(
                options.Scenario,
                processId,
                handle,
                CenterX(a1Bounds),
                CenterY(a1Bounds),
                CenterX(a4Bounds),
                CenterY(a4Bounds));
            if (blocked is not null)
            {
                return blocked;
            }

            var expected = new[]
            {
                ("StatusAvgText", "Average: 5"),
                ("StatusCountText", "Count: 4"),
                ("StatusNumericalCountText", "Numerical Count: 4"),
                ("StatusSumText", "Sum: 20"),
                ("StatusMinText", "Min: 2"),
                ("StatusMaxText", "Max: 8")
            };

            var validations = new List<string>();
            foreach (var (automationId, expectedName) in expected)
            {
                if (!TryGetAutomationElementNameOrVisibleText(handle, automationId, expectedName, out var actualName))
                {
                    var suffix = string.IsNullOrWhiteSpace(actualName)
                        ? string.Empty
                        : $" Last UIA candidate was '{actualName}'.";
                    return CaptureResult.Blocked(options.Scenario, "status-stat-validation-unavailable", $"Could not read a visible UIA name/text value for '{automationId}'.{suffix}", options.OutputRoot, "freex", guard);
                }

                if (!string.Equals(actualName, expectedName, StringComparison.Ordinal))
                {
                    return CaptureResult.Blocked(options.Scenario, "status-stat-validation-failed", $"Expected '{automationId}' automation name '{expectedName}', but UIA reported '{actualName}'.", options.OutputRoot, "freex", guard);
                }

                validations.Add($"{automationId}='{actualName}'");
            }

            _lastResultValidation = "Foreground status stats after physical paste/select with min/max enabled: " + string.Join("; ", validations) + ".";
            return null;
        };

    private Func<IntPtr, int, WindowInfo, ForegroundGuardResult, CaptureResult?> FormulaBarNameBoxReference()
        => (handle, processId, _, guard) =>
        {
            var resizeBlocked = ResizeForStableForegroundCapture(handle, processId, "after-freex-formula-bar-window-resize");
            if (resizeBlocked is not null)
            {
                return resizeBlocked;
            }

            guard = ForegroundGuard.FocusAndVerify(handle, processId, "FreeX", options.FocusTimeout);
            if (!guard.Success)
            {
                return BlockedWithGuard(options.Scenario, guard, "before-formula-seed");
            }

            if (!TryGetCellBounds(handle, "Cell_A1", out var a1Bounds) ||
                !TryGetCellBounds(handle, "Cell_B4", out var b4Bounds))
            {
                return CaptureResult.Blocked(options.Scenario, "uia-cell-bounds-unavailable", "Could not resolve A1 and B4 bounds for formula bar/name box setup.", options.OutputRoot, "freex", guard);
            }

            const string seed = "Metric\tValue\r\nRevenue\t120\r\nCost\t45\r\nProfit\t=B2-B3";
            var blocked = PasteCellText(handle, processId, a1Bounds, seed);
            if (blocked is not null)
            {
                return blocked;
            }

            if (!WaitForCellValue(handle, "Cell_B4", "=B2-B3", TimeSpan.FromSeconds(3), out var b4Value))
            {
                return CaptureResult.Blocked(options.Scenario, "formula-cell-validation-failed", $"Expected B4 UIA value to retain formula text '=B2-B3' after paste; observed '{b4Value}'.", options.OutputRoot, "freex", guard);
            }

            blocked = GuardedClickPoint(options.Scenario, processId, handle, CenterX(b4Bounds), CenterY(b4Bounds), MouseButtonKind.Left);
            if (blocked is not null)
            {
                return blocked;
            }

            Thread.Sleep(options.AfterInputDelay);

            if (!TryGetAutomationElementText(handle, "CellAddressBox", out var nameBox) ||
                !nameBox.Contains("B4", StringComparison.OrdinalIgnoreCase))
            {
                return CaptureResult.Blocked(options.Scenario, "name-box-validation-failed", $"Expected Name Box AutomationId 'CellAddressBox' to show B4 after selecting B4; observed '{nameBox}'.", options.OutputRoot, "freex", guard);
            }

            if (!TryGetAutomationElementText(handle, "FormulaBar", out var formulaBar) ||
                !formulaBar.Equals("=B2-B3", StringComparison.OrdinalIgnoreCase))
            {
                return CaptureResult.Blocked(options.Scenario, "formula-bar-validation-failed", $"Expected Formula Bar AutomationId 'FormulaBar' to show '=B2-B3' after selecting B4; observed '{formulaBar}'.", options.OutputRoot, "freex", guard);
            }

            _lastResultValidation = "Seeded A1:B4 through foreground paste, selected B4, and validated Name Box 'B4' plus Formula Bar '=B2-B3' through UIA before capture.";
            return null;
        };

    private Func<IntPtr, int, WindowInfo, ForegroundGuardResult, CaptureResult?> FreeXAutoFilterOpenedState()
        => (handle, processId, _, guard) =>
        {
            var resizeBlocked = ResizeForStableForegroundCapture(handle, processId, "after-freex-autofilter-window-resize");
            if (resizeBlocked is not null)
            {
                return resizeBlocked;
            }

            guard = ForegroundGuard.FocusAndVerify(handle, processId, "FreeX", options.FocusTimeout);
            if (!guard.Success)
            {
                return BlockedWithGuard(options.Scenario, guard, "before-autofilter-seed");
            }

            if (!TryGetCellBounds(handle, "Cell_A1", out var a1Bounds))
            {
                return CaptureResult.Blocked(options.Scenario, "uia-cell-bounds-unavailable", "Could not resolve A1 bounds for AutoFilter setup.", options.OutputRoot, "freex", guard);
            }

            const string seed =
                "score\tregion\titem\tamount\r\n" +
                "1\tEast\tAlpha\t10\r\n" +
                "2\tWest\tBeta\t20\r\n" +
                "3\tEast\tGamma\t30\r\n" +
                "4\tWest\tDelta\t40\r\n" +
                "\tNorth\tBlank score\t50";
            var blocked = PasteCellText(handle, processId, a1Bounds, seed);
            if (blocked is not null)
            {
                return blocked;
            }

            if (!WaitForCellValue(handle, "Cell_D6", "50", TimeSpan.FromSeconds(3), out var d6Value))
            {
                return CaptureResult.Blocked(options.Scenario, "autofilter-seed-validation-failed", $"Expected D6 UIA value '50' after seeded paste; observed '{d6Value}'.", options.OutputRoot, "freex", guard);
            }

            blocked = GuardedClickPoint(options.Scenario, processId, handle, CenterX(a1Bounds), CenterY(a1Bounds), MouseButtonKind.Left);
            if (blocked is not null)
            {
                return blocked;
            }

            blocked = GuardedKeyChord(options.Scenario, processId, handle, [NativeMethods.VK_CONTROL, NativeMethods.VK_SHIFT], NativeMethods.VK_L, "ctrl-shift-l-autofilter");
            if (blocked is not null)
            {
                return blocked;
            }

            blocked = GuardedClickPoint(options.Scenario, processId, handle, CenterX(a1Bounds), CenterY(a1Bounds), MouseButtonKind.Left);
            if (blocked is not null)
            {
                return blocked;
            }

            blocked = GuardedKeyChord(options.Scenario, processId, handle, [NativeMethods.VK_MENU], NativeMethods.VK_DOWN, "alt-down-autofilter");
            if (blocked is not null)
            {
                return blocked;
            }

            var dialog = FindFreeXAutoFilterDialog(processId, handle.ToInt64(), options.PopupTimeout);
            if (dialog is null)
            {
                blocked = GuardedSendKeys(options.Scenario, processId, handle, "%{DOWN}", "sendkeys-alt-down-autofilter");
                if (blocked is not null)
                {
                    return blocked;
                }

                dialog = FindFreeXAutoFilterDialog(processId, handle.ToInt64(), TimeSpan.FromMilliseconds(1600));
            }

            if (dialog is null)
            {
                blocked = GuardedClickPoint(
                    options.Scenario,
                    processId,
                    handle,
                    Math.Max(CenterX(a1Bounds), (int)a1Bounds.Right - 12),
                    CenterY(a1Bounds),
                    MouseButtonKind.Left);
                if (blocked is not null)
                {
                    return blocked;
                }

                dialog = FindFreeXAutoFilterDialog(processId, handle.ToInt64(), options.PopupTimeout);
            }

            if (dialog is null)
            {
                return CaptureResult.Blocked(options.Scenario, "autofilter-dialog-not-found", "Did not detect a foreground FreeX AutoFilter dropdown after Ctrl+Shift+L, Alt+Down, SendKeys Alt+Down fallback, or guarded header-cell dropdown click.", options.OutputRoot, "freex", guard);
            }

            var validation = WindowHasUiaText(dialog.Handle, "Sort A to Z") &&
                             WindowHasUiaText(dialog.Handle, "Text Filters") &&
                             WindowHasUiaText(dialog.Handle, "Select All")
                ? "Seeded A1:D6 through foreground paste, toggled AutoFilter with Ctrl+Shift+L, opened the score-column dropdown with Alt+Down, and validated the Text Filters checklist surface through UIA."
                : "Seeded A1:D6 through foreground paste and opened the score-column AutoFilter dropdown with Ctrl+Shift+L then Alt+Down; UIA text validation was incomplete.";

            return CaptureWindow(options.Scenario, "freex", dialog, guard, "complete", validation);
        };

    private CaptureResult? ResizeForStatusStatisticReadback(IntPtr handle, int processId, ForegroundGuardResult guard)
        => ResizeForStableForegroundCapture(handle, processId, "after-status-stat-window-resize");

    private CaptureResult? ResizeForStableForegroundCapture(IntPtr handle, int processId, string failureStage, string titleContains = "FreeX")
    {
        var workingArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1600, 900);
        var width = Math.Min(1600, Math.Max(1200, workingArea.Width));
        var height = Math.Min(900, Math.Max(720, workingArea.Height));
        var x = workingArea.Left + Math.Max(0, (workingArea.Width - width) / 2);
        var y = workingArea.Top + Math.Max(0, (workingArea.Height - height) / 2);

        NativeMethods.SetWindowPos(handle, NativeMethods.HWND_NOTOPMOST, x, y, width, height, NativeMethods.SWP_SHOWWINDOW);
        Thread.Sleep(options.AfterInputDelay);

        var guard = ForegroundGuard.FocusAndVerify(handle, processId, titleContains, options.FocusTimeout);
        return guard.Success
            ? null
            : BlockedWithGuard(options.Scenario, guard, failureStage);
    }

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

    private Func<IntPtr, int, WindowInfo, ForegroundGuardResult, CaptureResult?> RightClickSheetTabContextMenu()
        => (handle, processId, window, guard) =>
        {
            var resizeBlocked = ResizeForStableForegroundCapture(handle, processId, "after-sheet-tab-context-window-resize");
            if (resizeBlocked is not null)
            {
                return resizeBlocked;
            }

            window = WindowFinder.GetWindowInfo(handle) ?? window;
            var seedBlocked = SeedSheetsWithAddButton(handle, processId, 4);
            if (seedBlocked is not null)
            {
                return seedBlocked;
            }

            var tab = FindVisibleSheetTabElement(handle, "Sheet1") ?? GetVisibleSheetTabElements(handle)
                .OrderBy(element => element.Current.BoundingRectangle.Left)
                .FirstOrDefault();
            WindowInfo? popup;
            if (tab is not null)
            {
                var blocked = GuardedClickElement(options.Scenario, processId, handle, tab, MouseButtonKind.Right);
                if (blocked is not null)
                {
                    return blocked;
                }

                popup = WindowFinder.FindProcessPopup(processId, handle.ToInt64(), options.PopupTimeout, 120, 80);
                if (popup is not null &&
                    ProcessHasVisibleMenuItems(processId, "Rename", "Move or Copy", "Select All Sheets"))
                {
                    _lastCaptureWindow = popup;
                    _lastResultValidation = "Opened the FreeX sheet-tab context menu by physically right-clicking the UIA-discovered sheet tab.";
                    return null;
                }

                SendKeys.SendWait("{ESC}");
                Thread.Sleep(options.AfterInputDelay);

                blocked = GuardedOpenContextMenuFromFocusedElement(options.Scenario, processId, handle, tab);
                if (blocked is not null)
                {
                    return blocked;
                }

                popup = WindowFinder.FindProcessPopup(processId, handle.ToInt64(), options.PopupTimeout, 120, 80);
                if (popup is not null &&
                    ProcessHasVisibleMenuItems(processId, "Rename", "Move or Copy", "Select All Sheets"))
                {
                    _lastCaptureWindow = popup;
                    _lastResultValidation = "Opened the FreeX sheet-tab context menu by focusing the UIA-discovered sheet tab and pressing Shift+F10.";
                    return null;
                }

                SendKeys.SendWait("{ESC}");
                Thread.Sleep(options.AfterInputDelay);
            }

            var addButtonBlocked = OpenFreeXSheetTabContextMenuNearAddButton(processId, handle, out var openedNearAddButton);
            if (addButtonBlocked is not null)
            {
                return addButtonBlocked;
            }

            if (openedNearAddButton)
            {
                _lastCaptureWindow = WindowFinder.FindProcessPopup(processId, handle.ToInt64(), TimeSpan.FromMilliseconds(250), 120, 80);
                _lastResultValidation = "Opened the FreeX sheet-tab context menu by physically right-clicking the Sheet1 tab area immediately left of the Insert Sheet button.";
                return null;
            }

            var keyboardBlocked = OpenFreeXSheetTabContextMenuByKeyboardCycle(processId, handle, out var openedByKeyboardCycle);
            if (keyboardBlocked is not null)
            {
                return keyboardBlocked;
            }

            if (openedByKeyboardCycle)
            {
                _lastCaptureWindow = WindowFinder.FindProcessPopup(processId, handle.ToInt64(), TimeSpan.FromMilliseconds(250), 120, 80);
                _lastResultValidation = "Opened the FreeX sheet-tab context menu by cycling focus with F6 and pressing Shift+F10 on the focused sheet tab.";
                return null;
            }

            var fallbackNotes = new List<string>();
            foreach (var point in GetSheetTabStripFallbackPoints(window.Bounds))
            {
                fallbackNotes.Add($"{point.Note}@{point.X},{point.Y}");
                var blocked = GuardedClickPoint(options.Scenario, processId, handle, point.X, point.Y, MouseButtonKind.Right);
                if (blocked is not null)
                {
                    return blocked;
                }

                popup = WindowFinder.FindProcessPopup(processId, handle.ToInt64(), options.PopupTimeout, 120, 80);
                if (popup is not null &&
                    ProcessHasVisibleMenuItems(processId, "Rename", "Move or Copy", "Select All Sheets"))
                {
                    _lastCaptureWindow = popup;
                    _lastResultValidation = $"Opened the FreeX sheet-tab context menu through guarded tab-strip coordinate fallback ({point.Note}).";
                    return null;
                }

                SendKeys.SendWait("{ESC}");
                Thread.Sleep(options.AfterInputDelay);
            }

            var visibleMenuItems = DescribeVisibleProcessMenuItems(processId);
            return CaptureResult.Blocked(
                options.Scenario,
                "sheet-tab-context-menu-not-found",
                $"Could not open the FreeX sheet-tab context menu through UIA tab lookup or coordinate fallbacks: {string.Join("; ", fallbackNotes)}. Visible menu items after final attempt: {visibleMenuItems}.",
                options.OutputRoot,
                "freex",
                guard);
        };

    private CaptureResult? OpenFreeXSheetTabContextMenuNearAddButton(int processId, IntPtr handle, out bool opened)
    {
        opened = false;
        var addButton = FindSheetAddButton(handle);
        if (addButton is null)
        {
            return null;
        }

        var bounds = addButton.Current.BoundingRectangle;
        if (bounds.IsEmpty || bounds.Width < 1 || bounds.Height < 1)
        {
            return null;
        }

        foreach (var xOffset in new[] { 12, 28, 44, 60 })
        {
            var x = (int)Math.Max(bounds.Left - xOffset, bounds.Left - 96);
            var y = (int)(bounds.Top + bounds.Height / 2.0);
            var blocked = GuardedClickPoint(options.Scenario, processId, handle, x, y, MouseButtonKind.Right);
            if (blocked is not null)
            {
                return blocked;
            }

            if (WindowFinder.FindProcessPopup(processId, handle.ToInt64(), options.PopupTimeout, 120, 80) is not null &&
                ProcessHasVisibleMenuItems(processId, "Rename", "Move or Copy", "Select All Sheets"))
            {
                opened = true;
                return null;
            }

            SendKeys.SendWait("{ESC}");
            Thread.Sleep(options.AfterInputDelay);
        }

        return null;
    }

    private CaptureResult? OpenFreeXSheetTabContextMenuByKeyboardCycle(int processId, IntPtr handle, out bool opened)
    {
        opened = false;
        var guard = ForegroundGuard.FocusAndVerify(handle, processId, "FreeX", options.FocusTimeout);
        if (!guard.Success)
        {
            return BlockedWithGuard(options.Scenario, guard, "before-sheet-tab-keyboard-cycle");
        }

        for (var attempt = 0; attempt < 8; attempt++)
        {
            SendKeys.SendWait("{F6}");
            Thread.Sleep(120);
            SendKeys.SendWait("+{F10}");
            Thread.Sleep(options.AfterInputDelay);

            if (WindowFinder.FindProcessPopup(processId, handle.ToInt64(), options.PopupTimeout, 120, 80) is not null &&
                ProcessHasVisibleMenuItems(processId, "Rename", "Move or Copy", "Select All Sheets"))
            {
                opened = true;
                return null;
            }

            SendKeys.SendWait("{ESC}");
            Thread.Sleep(120);
        }

        return null;
    }

    private Func<IntPtr, int, WindowInfo, ForegroundGuardResult, CaptureResult?> DragCellRangeSelectValidated(
        string startCell,
        string endCell)
        => (handle, processId, _, guard) =>
        {
            if (!TryGetCellBounds(handle, CellAutomationId(startCell), out var startBounds) ||
                !TryGetCellBounds(handle, CellAutomationId(endCell), out var endBounds))
            {
                return CaptureResult.Blocked(options.Scenario, "uia-cell-bounds-unavailable", $"Could not resolve {startCell}/{endCell} bounds for drag selection.", options.OutputRoot, "freex", guard);
            }

            var blocked = GuardedDrag(
                options.Scenario,
                processId,
                handle,
                CenterX(startBounds),
                CenterY(startBounds),
                CenterX(endBounds),
                CenterY(endBounds));
            if (blocked is not null)
            {
                return blocked;
            }

            if (!TryGetSelectedCellIds(handle, out var selectedIds))
            {
                return CaptureResult.Blocked(options.Scenario, "selection-validation-unavailable", "Could not read worksheet grid SelectionPattern after drag selection.", options.OutputRoot, "freex", guard);
            }

            var expectedIds = ExpectedCellIds(startCell, endCell).ToArray();
            var missing = expectedIds.Where(id => !selectedIds.Contains(id)).ToArray();
            if (missing.Length > 0)
            {
                return CaptureResult.Blocked(options.Scenario, "selection-validation-failed", $"Drag selection missed expected cells: {string.Join(", ", missing)}. Selected: {string.Join(", ", selectedIds.OrderBy(id => id))}.", options.OutputRoot, "freex", guard);
            }

            _lastResultValidation = $"foreground cell-range drag select {startCell}:{endCell}; UIA SelectionPattern includes {expectedIds.Length} expected cells ({string.Join(", ", expectedIds.Take(4))}...).";
            return null;
        };

    private Func<IntPtr, int, WindowInfo, ForegroundGuardResult, CaptureResult?> AutofillHandleDragValidated()
        => (handle, processId, _, guard) =>
        {
            if (!TryGetCellBounds(handle, "Cell_A1", out var a1) ||
                !TryGetCellBounds(handle, "Cell_A4", out var a4))
            {
                return CaptureResult.Blocked(options.Scenario, "uia-cell-bounds-unavailable", "Could not resolve A1/A4 bounds for autofill drag.", options.OutputRoot, "freex", guard);
            }

            var blocked = PasteCellText(handle, processId, a1, "11");
            if (blocked is not null)
            {
                return blocked;
            }

            if (!TryGetCellValue(handle, "Cell_A1", out var a1Value) ||
                !a1Value.Equals("11", StringComparison.Ordinal))
            {
                return CaptureResult.Blocked(options.Scenario, "cell-seed-validation-failed", $"Expected A1 to contain '11' before autofill; UIA reported '{a1Value}'.", options.OutputRoot, "freex", guard);
            }

            blocked = GuardedClickPoint(options.Scenario, processId, handle, CenterX(a1), CenterY(a1), MouseButtonKind.Left);
            if (blocked is not null)
            {
                return blocked;
            }

            if (!TryGetCellBounds(handle, "Cell_A1", out a1) ||
                !TryGetCellBounds(handle, "Cell_A4", out a4))
            {
                return CaptureResult.Blocked(options.Scenario, "uia-cell-bounds-unavailable", "Could not refresh A1/A4 bounds after seed paste.", options.OutputRoot, "freex", guard);
            }

            blocked = GuardedDrag(
                options.Scenario,
                processId,
                handle,
                (int)(a1.Right - 2),
                (int)(a1.Bottom - 2),
                (int)(a4.Right - 2),
                (int)(a4.Bottom - 2));
            if (blocked is not null)
            {
                return blocked;
            }

            var filled = new[] { "Cell_A2", "Cell_A3", "Cell_A4" };
            var unexpected = new List<string>();
            foreach (var id in filled)
            {
                if (!TryGetCellValue(handle, id, out var value) ||
                    !value.Equals("11", StringComparison.Ordinal))
                {
                    unexpected.Add($"{id}='{value}'");
                }
            }

            if (unexpected.Count > 0)
            {
                return CaptureResult.Blocked(options.Scenario, "autofill-validation-failed", $"Expected autofill to copy '11' into A2:A4; observed {string.Join(", ", unexpected)}.", options.OutputRoot, "freex", guard);
            }

            _lastResultValidation = "foreground autofill handle drag from A1 to A4; UIA ValuePattern confirms A2:A4 copied '11'.";
            return null;
        };

    private Func<IntPtr, int, WindowInfo, ForegroundGuardResult, CaptureResult?> DoubleClickAutoFitValidated()
        => (handle, processId, _, guard) =>
        {
            if (!TryGetCellBounds(handle, "Cell_A1", out var originalA1))
            {
                return CaptureResult.Blocked(options.Scenario, "uia-cell-bounds-unavailable", "Could not resolve A1 bounds before AutoFit.", options.OutputRoot, "freex", guard);
            }

            const string autoFitSeedText = "A very long foreground AutoFit validation value for S4";
            var blocked = PasteCellText(handle, processId, originalA1, autoFitSeedText);
            if (blocked is not null)
            {
                return blocked;
            }

            if (!WaitForCellValue(handle, "Cell_A1", autoFitSeedText, TimeSpan.FromSeconds(2), out var observedA1Value))
            {
                return CaptureResult.Blocked(options.Scenario, "cell-paste-validation-failed", $"Expected A1 to contain the AutoFit seed text before double-click; observed '{observedA1Value}'.", options.OutputRoot, "freex", guard);
            }

            if (!TryGetCellBounds(handle, "Cell_A1", out originalA1))
            {
                return CaptureResult.Blocked(options.Scenario, "uia-cell-bounds-unavailable", "Could not refresh A1 bounds before column AutoFit.", options.OutputRoot, "freex", guard);
            }

            blocked = GuardedDoubleClickPoint(
                options.Scenario,
                processId,
                handle,
                (int)(originalA1.Right - 1),
                (int)(originalA1.Top - 9));
            if (blocked is not null)
            {
                return blocked;
            }

            if (!TryGetCellBounds(handle, "Cell_A1", out var autoFitColumnA1))
            {
                return CaptureResult.Blocked(options.Scenario, "uia-cell-bounds-unavailable", "Could not resolve A1 bounds after column AutoFit.", options.OutputRoot, "freex", guard);
            }

            var columnDelta = autoFitColumnA1.Width - originalA1.Width;
            if (columnDelta < 30)
            {
                return CaptureResult.Blocked(options.Scenario, "column-autofit-validation-failed", $"Expected column A width to grow after double-click AutoFit; before {originalA1.Width:0.###}, after {autoFitColumnA1.Width:0.###}.", options.OutputRoot, "freex", guard);
            }

            blocked = GuardedDrag(
                options.Scenario,
                processId,
                handle,
                (int)(autoFitColumnA1.Left - 15),
                (int)autoFitColumnA1.Bottom,
                (int)(autoFitColumnA1.Left - 15),
                (int)(autoFitColumnA1.Bottom + 24));
            if (blocked is not null)
            {
                return blocked;
            }

            if (!TryGetCellBounds(handle, "Cell_A1", out var tallA1))
            {
                return CaptureResult.Blocked(options.Scenario, "uia-cell-bounds-unavailable", "Could not resolve A1 bounds after row height resize.", options.OutputRoot, "freex", guard);
            }

            blocked = GuardedDoubleClickPoint(
                options.Scenario,
                processId,
                handle,
                (int)(tallA1.Left - 15),
                (int)tallA1.Bottom);
            if (blocked is not null)
            {
                return blocked;
            }

            if (!TryGetCellBounds(handle, "Cell_A1", out var autoFitRowA1))
            {
                return CaptureResult.Blocked(options.Scenario, "uia-cell-bounds-unavailable", "Could not resolve A1 bounds after row AutoFit.", options.OutputRoot, "freex", guard);
            }

            var rowDelta = tallA1.Height - autoFitRowA1.Height;
            if (rowDelta < 8)
            {
                return CaptureResult.Blocked(options.Scenario, "row-autofit-validation-failed", $"Expected row 1 height to shrink after double-click AutoFit; tall {tallA1.Height:0.###}, after {autoFitRowA1.Height:0.###}.", options.OutputRoot, "freex", guard);
            }

            _lastResultValidation = $"foreground double-click AutoFit; column A width {originalA1.Width:0.###}->{autoFitColumnA1.Width:0.###}, row 1 height {tallA1.Height:0.###}->{autoFitRowA1.Height:0.###}.";
            return null;
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

    private Func<IntPtr, int, WindowInfo, ForegroundGuardResult, CaptureResult?> WheelModifierBreadth()
        => (handle, processId, window, guard) =>
        {
            var blocked = WheelVerticalThenShiftHorizontal()(handle, processId, window, guard);
            if (blocked is not null)
            {
                return blocked;
            }

            var scrollValidation = _lastResultValidation ?? "foreground ordinary wheel and Shift+wheel passed";
            blocked = CtrlWheelRelativeExpectZoom(0.36, 0.56, 120, 110)(handle, processId, window, guard);
            if (blocked is not null)
            {
                return blocked;
            }

            var ctrlValidation = _lastResultValidation ?? "foreground Ctrl+wheel zoom passed";
            _lastResultValidation = $"{scrollValidation}; {ctrlValidation}.";
            return null;
        };

    private Func<IntPtr, int, WindowInfo, ForegroundGuardResult, CaptureResult?> SheetTabClickSelect()
        => (handle, processId, _, _) =>
        {
            var blocked = SeedSheetsWithAddButton(handle, processId, 3);
            if (blocked is not null)
            {
                return blocked;
            }

            var tab = FindVisibleSheetTabElement(handle, "Sheet2");
            if (tab is null)
            {
                return CaptureResult.Blocked(options.Scenario, "uia-target-not-found", "Could not find seeded sheet tab 'Sheet2'.", options.OutputRoot, "freex");
            }

            blocked = GuardedClickElement(options.Scenario, processId, handle, tab, MouseButtonKind.Left);
            if (blocked is not null)
            {
                return blocked;
            }

            _lastResultValidation = "Created Sheet2 and Sheet3 through physical Insert Sheet button clicks, then physically left-clicked the Sheet2 tab. Screenshot should show Sheet2 selected in the tab strip.";
            return null;
        };

    private Func<IntPtr, int, WindowInfo, ForegroundGuardResult, CaptureResult?> SheetTabDoubleClickRename()
        => (handle, processId, _, _) =>
        {
            var blocked = SeedSheetsWithAddButton(handle, processId, 2);
            if (blocked is not null)
            {
                return blocked;
            }

            var tab = FindVisibleSheetTabElement(handle, "Sheet2");
            if (tab is null)
            {
                return CaptureResult.Blocked(options.Scenario, "uia-target-not-found", "Could not find seeded sheet tab 'Sheet2'.", options.OutputRoot, "freex");
            }

            blocked = GuardedDoubleClickElement(options.Scenario, processId, handle, tab);
            if (blocked is not null)
            {
                return blocked;
            }

            var dialog = WindowFinder.FindProcessWindow(
                processId,
                window => window.Title.Contains("Rename Sheet", StringComparison.OrdinalIgnoreCase),
                options.PopupTimeout);
            if (dialog is null)
            {
                return CaptureResult.Blocked(options.Scenario, "dialog-not-found", "Did not detect Rename Sheet dialog after the physical tab double-click.", options.OutputRoot, "freex");
            }

            var dialogHandle = new IntPtr(dialog.Handle);
            var guard = ForegroundGuard.FocusAndVerify(dialogHandle, processId, "Rename Sheet", options.FocusTimeout);
            if (!guard.Success)
            {
                return BlockedWithGuard(options.Scenario, guard, "before-rename-dialog-capture");
            }

            return CaptureWindow(
                options.Scenario,
                "freex",
                dialog,
                guard,
                "complete",
                "Created Sheet2 through the physical Insert Sheet button, then physically double-clicked the Sheet2 tab and captured the foreground Rename Sheet dialog.");
        };

    private Func<IntPtr, int, WindowInfo, ForegroundGuardResult, CaptureResult?> SheetTabModifierGrouping(byte modifierKey, string gestureName, string targetSheetName)
        => (handle, processId, _, _) =>
        {
            var targetCount = targetSheetName == "Sheet5" ? 5 : 3;
            var blocked = SeedSheetsWithAddButton(handle, processId, targetCount);
            if (blocked is not null)
            {
                return blocked;
            }

            var anchor = FindVisibleSheetTabElement(handle, "Sheet1");
            var target = FindVisibleSheetTabElement(handle, targetSheetName);
            if (anchor is null || target is null)
            {
                return CaptureResult.Blocked(options.Scenario, "uia-target-not-found", $"Could not find Sheet1 and {targetSheetName} for {gestureName} grouping.", options.OutputRoot, "freex");
            }

            blocked = GuardedClickElement(options.Scenario, processId, handle, anchor, MouseButtonKind.Left);
            if (blocked is not null)
            {
                return blocked;
            }

            target = FindVisibleSheetTabElement(handle, targetSheetName);
            if (target is null)
            {
                return CaptureResult.Blocked(options.Scenario, "uia-target-not-found", $"Could not re-resolve {targetSheetName} after selecting the Sheet1 grouping anchor.", options.OutputRoot, "freex");
            }

            blocked = GuardedModifiedClickElement(options.Scenario, processId, handle, target, modifierKey);
            if (blocked is not null)
            {
                return blocked;
            }

            _lastResultValidation = $"Created sheets through physical Insert Sheet clicks, selected Sheet1 as the grouping anchor, then performed a physical {gestureName} on {targetSheetName}. Screenshot should show grouped sheet-tab styling from the live modifier-click path.";
            return null;
        };

    private Func<IntPtr, int, WindowInfo, ForegroundGuardResult, CaptureResult?> SheetTabGroupedCommands()
        => (handle, processId, _, _) =>
        {
            var blocked = SeedSheetsWithAddButton(handle, processId, 4);
            if (blocked is not null)
            {
                return blocked;
            }

            var tab = FindVisibleSheetTabElement(handle, "Sheet2");
            if (tab is null)
            {
                return CaptureResult.Blocked(options.Scenario, "uia-target-not-found", "Could not find seeded sheet tab 'Sheet2' for grouped command proof.", options.OutputRoot, "freex");
            }

            blocked = GuardedClickElement(options.Scenario, processId, handle, tab, MouseButtonKind.Left);
            if (blocked is not null)
            {
                return blocked;
            }

            tab = FindVisibleSheetTabElement(handle, "Sheet2");
            if (tab is null)
            {
                return CaptureResult.Blocked(options.Scenario, "uia-target-not-found", "Could not re-resolve Sheet2 before opening the Select All Sheets context menu.", options.OutputRoot, "freex");
            }

            blocked = GuardedClickElement(options.Scenario, processId, handle, tab, MouseButtonKind.Right);
            if (blocked is not null)
            {
                return blocked;
            }

            if (!TryInvokeProcessMenuItem(processId, "Select All Sheets"))
            {
                return CaptureResult.Blocked(options.Scenario, "context-menu-item-not-found", "Opened the sheet-tab context menu but could not invoke Select All Sheets.", options.OutputRoot, "freex");
            }

            Thread.Sleep(options.AfterInputDelay);
            var groupedTitle = WindowFinder.GetWindowInfo(handle)?.Title ?? string.Empty;
            if (!groupedTitle.Contains("[Group]", StringComparison.OrdinalIgnoreCase))
            {
                return CaptureResult.Blocked(options.Scenario, "select-all-validation-failed", $"Expected workbook title to include [Group] after Select All Sheets; observed '{groupedTitle}'.", options.OutputRoot, "freex");
            }

            tab = FindVisibleSheetTabElement(handle, "Sheet2");
            if (tab is null)
            {
                return CaptureResult.Blocked(options.Scenario, "uia-target-not-found", "Could not re-resolve Sheet2 before opening the Ungroup Sheets context menu.", options.OutputRoot, "freex");
            }

            blocked = GuardedClickElement(options.Scenario, processId, handle, tab, MouseButtonKind.Right);
            if (blocked is not null)
            {
                return blocked;
            }

            if (!TryInvokeProcessMenuItem(processId, "Ungroup Sheets"))
            {
                return CaptureResult.Blocked(options.Scenario, "context-menu-item-not-found", "Opened the grouped sheet-tab context menu but could not invoke Ungroup Sheets.", options.OutputRoot, "freex");
            }

            Thread.Sleep(options.AfterInputDelay);
            var ungroupedTitle = WindowFinder.GetWindowInfo(handle)?.Title ?? string.Empty;
            if (ungroupedTitle.Contains("[Group]", StringComparison.OrdinalIgnoreCase))
            {
                return CaptureResult.Blocked(options.Scenario, "ungroup-validation-failed", $"Expected workbook title to clear [Group] after Ungroup Sheets; observed '{ungroupedTitle}'.", options.OutputRoot, "freex");
            }

            _lastResultValidation = $"Created Sheet2-Sheet4 through physical Insert Sheet clicks, invoked Select All Sheets from the sheet-tab context menu, verified grouped title '{groupedTitle}', then invoked Ungroup Sheets from the focused sheet-tab keyboard context menu and verified title '{ungroupedTitle}'.";
            return null;
        };

    private Func<IntPtr, int, WindowInfo, ForegroundGuardResult, CaptureResult?> SheetTabDragReorder()
        => (handle, processId, _, _) =>
        {
            var blocked = SeedSheetsWithAddButton(handle, processId, 4);
            if (blocked is not null)
            {
                return blocked;
            }

            var source = FindVisibleSheetTabElement(handle, "Sheet4");
            var target = FindVisibleSheetTabElement(handle, "Sheet2");
            if (source is null || target is null)
            {
                return CaptureResult.Blocked(options.Scenario, "uia-target-not-found", "Could not find Sheet4 and Sheet2 for drag reorder.", options.OutputRoot, "freex");
            }

            var sourceBounds = source.Current.BoundingRectangle;
            var targetBounds = target.Current.BoundingRectangle;
            blocked = GuardedDrag(
                options.Scenario,
                processId,
                handle,
                (int)(sourceBounds.Left + sourceBounds.Width / 2.0),
                (int)(sourceBounds.Top + sourceBounds.Height / 2.0),
                (int)(targetBounds.Left + Math.Max(4.0, targetBounds.Width * 0.08)),
                (int)(targetBounds.Top + targetBounds.Height / 2.0));
            if (blocked is not null)
            {
                return blocked;
            }

            Thread.Sleep(options.AfterInputDelay);
            var tabOrder = GetVisibleSheetTabOrder(handle);
            var sheet4Index = tabOrder.IndexOf("Sheet4");
            var sheet2Index = tabOrder.IndexOf("Sheet2");
            if (sheet4Index < 0 || sheet2Index < 0 || sheet4Index > sheet2Index)
            {
                return CaptureResult.Blocked(options.Scenario, "reorder-validation-failed", $"Expected Sheet4 to move before Sheet2 after drag; observed order: {string.Join(", ", tabOrder)}.", options.OutputRoot, "freex");
            }

            _lastResultValidation = $"Created Sheet2-Sheet4 through physical Insert Sheet clicks, physically dragged Sheet4 onto Sheet2, and validated visible tab order: {string.Join(", ", tabOrder)}.";
            return null;
        };

    private Func<IntPtr, int, WindowInfo, ForegroundGuardResult, CaptureResult?> SheetTabOverflowNavClick()
        => (handle, processId, _, _) =>
        {
            var blocked = SeedSheetsWithAddButton(handle, processId, 18);
            if (blocked is not null)
            {
                return blocked;
            }

            var rightNav = FindSheetNavButtonByAutomationId(handle, right: true);
            if (rightNav is null)
            {
                return CaptureResult.Blocked(options.Scenario, "uia-target-not-found", "Could not find the visible sheet-tab Scroll Tabs Right button after seeding overflow sheets.", options.OutputRoot, "freex");
            }

            blocked = GuardedClickElement(options.Scenario, processId, handle, rightNav, MouseButtonKind.Left);
            if (blocked is not null)
            {
                return blocked;
            }

            _lastResultValidation = "Created Sheet2-Sheet40 through physical Insert Sheet button clicks, then physically clicked the visible Scroll Tabs Right overflow navigation button.";
            return null;
        };

    private Func<IntPtr, int, WindowInfo, ForegroundGuardResult, CaptureResult?> SheetTabOverflowActivateDialog()
        => (handle, processId, _, _) =>
        {
            var resizeBlocked = ResizeForStableForegroundCapture(handle, processId, "after-sheet-tab-overflow-window-resize");
            if (resizeBlocked is not null)
            {
                return resizeBlocked;
            }

            var blocked = SeedSheetsWithAddButton(handle, processId, 40);
            if (blocked is not null)
            {
                return blocked;
            }

            var rightNavCandidates = FindSheetNavButtonByAutomationId(handle, right: true) is { } rightNavButton
                ? new[] { rightNavButton }
                : [];
            if (rightNavCandidates.Length == 0)
            {
                return CaptureResult.Blocked(options.Scenario, "uia-target-not-found", "Could not find the visible sheet-tab Scroll Tabs Right button after seeding overflow sheets.", options.OutputRoot, "freex");
            }

            var candidateDiagnostics = DescribeSheetNavButtonCandidates(rightNavCandidates);
            WindowInfo? dialog = null;
            CaptureResult? lastBlocked = null;
            foreach (var rightNav in rightNavCandidates)
            {
                lastBlocked = GuardedClickElement(options.Scenario, processId, handle, rightNav, MouseButtonKind.Right);
                if (lastBlocked is not null)
                {
                    continue;
                }

                dialog = FindActivateDialogWindow(processId, handle.ToInt64(), options.PopupTimeout);
                if (dialog is not null)
                {
                    break;
                }
            }

            if (dialog is null)
            {
                if (lastBlocked is not null)
                {
                    return lastBlocked;
                }

                return CaptureResult.Blocked(options.Scenario, "dialog-not-found", $"Did not detect Activate Sheet dialog after right-clicking the sheet-tab overflow navigation button. Candidates: {candidateDiagnostics}.", options.OutputRoot, "freex");
            }

            var dialogHandle = new IntPtr(dialog.Handle);
            var guard = ForegroundGuard.FocusAndVerify(dialogHandle, processId, "Activate", options.FocusTimeout);
            if (!guard.Success)
            {
                return BlockedWithGuard(options.Scenario, guard, "before-activate-dialog-capture");
            }

            return CaptureWindow(
                options.Scenario,
                "freex",
                dialog,
                guard,
                "complete",
                "Created Sheet2-Sheet40 through physical Insert Sheet button clicks, then physically right-clicked the sheet-tab overflow navigation button and captured the foreground Activate Sheet dialog.");
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

    private CaptureResult? GuardedClickPoint(string scenario, int processId, IntPtr handle, int x, int y, MouseButtonKind button)
    {
        var guard = ForegroundGuard.FocusAndVerify(handle, processId, "FreeX", options.FocusTimeout);
        if (!guard.Success)
        {
            return BlockedWithGuard(scenario, guard, "before-pointer-click");
        }

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

    private CaptureResult? GuardedKeyChord(string scenario, int processId, IntPtr handle, byte[] modifiers, byte key, string phase)
    {
        var guard = ForegroundGuard.FocusAndVerify(handle, processId, "FreeX", options.FocusTimeout);
        if (!guard.Success)
        {
            return BlockedWithGuard(scenario, guard, $"before-{phase}-keydown");
        }

        foreach (var modifier in modifiers)
        {
            NativeMethods.KeybdEvent(modifier, 0, 0, UIntPtr.Zero);
            Thread.Sleep(40);
        }

        try
        {
            guard = ForegroundGuard.FocusAndVerify(handle, processId, "FreeX", options.FocusTimeout);
            if (!guard.Success)
            {
                return BlockedWithGuard(scenario, guard, $"before-{phase}-key");
            }

            NativeMethods.KeybdEvent(key, 0, 0, UIntPtr.Zero);
            Thread.Sleep(50);
            NativeMethods.KeybdEvent(key, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
            Thread.Sleep(options.AfterInputDelay);
        }
        finally
        {
            for (var i = modifiers.Length - 1; i >= 0; i--)
            {
                NativeMethods.KeybdEvent(modifiers[i], 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
                Thread.Sleep(40);
            }
        }

        return null;
    }

    private CaptureResult? GuardedSendKeys(string scenario, int processId, IntPtr handle, string keys, string phase)
    {
        var guard = ForegroundGuard.FocusAndVerify(handle, processId, "FreeX", options.FocusTimeout);
        if (!guard.Success)
        {
            return BlockedWithGuard(scenario, guard, $"before-{phase}");
        }

        SendKeys.SendWait(keys);
        Thread.Sleep(options.AfterInputDelay);
        return null;
    }

    private CaptureResult? GuardedDoubleClickElement(string scenario, int processId, IntPtr handle, AutomationElement element)
    {
        var bounds = element.Current.BoundingRectangle;
        if (bounds.IsEmpty || bounds.Width < 1 || bounds.Height < 1)
        {
            return CaptureResult.Blocked(scenario, "uia-target-bounds-invalid", $"Element bounds were not usable: {bounds}.", options.OutputRoot, "freex");
        }

        var guard = ForegroundGuard.FocusAndVerify(handle, processId, "FreeX", options.FocusTimeout);
        if (!guard.Success)
        {
            return BlockedWithGuard(scenario, guard, "before-pointer-double-click");
        }

        var x = (int)(bounds.Left + bounds.Width / 2.0);
        var y = (int)(bounds.Top + bounds.Height / 2.0);
        NativeMethods.SetCursorPos(x, y);
        Thread.Sleep(100);
        for (var i = 0; i < 2; i++)
        {
            NativeMethods.MouseEvent(NativeMethods.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(45);
            NativeMethods.MouseEvent(NativeMethods.MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(90);
        }

        Thread.Sleep(options.AfterInputDelay);
        return null;
    }

    private CaptureResult? GuardedDoubleClickPoint(string scenario, int processId, IntPtr handle, int x, int y)
    {
        var guard = ForegroundGuard.FocusAndVerify(handle, processId, "FreeX", options.FocusTimeout);
        if (!guard.Success)
        {
            return BlockedWithGuard(scenario, guard, "before-pointer-double-click");
        }

        NativeMethods.SetCursorPos(x, y);
        Thread.Sleep(100);
        for (var i = 0; i < 2; i++)
        {
            NativeMethods.MouseEvent(NativeMethods.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(45);
            NativeMethods.MouseEvent(NativeMethods.MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(90);
        }

        Thread.Sleep(options.AfterInputDelay);
        return null;
    }

    private CaptureResult? GuardedModifiedClickElement(string scenario, int processId, IntPtr handle, AutomationElement element, byte modifierKey)
    {
        var guard = ForegroundGuard.FocusAndVerify(handle, processId, "FreeX", options.FocusTimeout);
        if (!guard.Success)
        {
            return BlockedWithGuard(scenario, guard, "before-modifier-keydown");
        }

        NativeMethods.KeybdEvent(modifierKey, 0, 0, UIntPtr.Zero);
        Thread.Sleep(80);
        try
        {
            return GuardedClickElement(scenario, processId, handle, element, MouseButtonKind.Left);
        }
        finally
        {
            NativeMethods.KeybdEvent(modifierKey, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
            Thread.Sleep(80);
        }
    }

    private CaptureResult? GuardedOpenContextMenuFromFocusedElement(string scenario, int processId, IntPtr handle, AutomationElement element)
    {
        var guard = ForegroundGuard.FocusAndVerify(handle, processId, "FreeX", options.FocusTimeout);
        if (!guard.Success)
        {
            return BlockedWithGuard(scenario, guard, "before-focused-context-menu");
        }

        try
        {
            element.SetFocus();
        }
        catch (InvalidOperationException)
        {
            return CaptureResult.Blocked(scenario, "uia-focus-failed", "Could not focus the sheet tab before opening its keyboard context menu.", options.OutputRoot, "freex", guard);
        }
        catch (ElementNotAvailableException)
        {
            return CaptureResult.Blocked(scenario, "uia-target-stale", "The sheet tab became unavailable before opening its keyboard context menu.", options.OutputRoot, "freex", guard);
        }

        Thread.Sleep(100);
        SendKeys.SendWait("+{F10}");
        Thread.Sleep(options.AfterInputDelay);
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

    private CaptureResult? PasteCellText(IntPtr handle, int processId, System.Windows.Rect cellBounds, string text)
    {
        var blocked = GuardedClickPoint(options.Scenario, processId, handle, CenterX(cellBounds), CenterY(cellBounds), MouseButtonKind.Left);
        if (blocked is not null)
        {
            return blocked;
        }

        var guard = ForegroundGuard.FocusAndVerify(handle, processId, "FreeX", options.FocusTimeout);
        if (!guard.Success)
        {
            return BlockedWithGuard(options.Scenario, guard, "before-cell-paste");
        }

        Clipboard.SetText(text);
        NativeMethods.KeybdEvent(NativeMethods.VK_CONTROL, 0, 0, UIntPtr.Zero);
        Thread.Sleep(50);
        NativeMethods.KeybdEvent(NativeMethods.VK_V, 0, 0, UIntPtr.Zero);
        Thread.Sleep(50);
        NativeMethods.KeybdEvent(NativeMethods.VK_V, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        Thread.Sleep(50);
        NativeMethods.KeybdEvent(NativeMethods.VK_CONTROL, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        Thread.Sleep(options.AfterInputDelay);
        return null;
    }

    private CaptureResult? SeedSheetsWithAddButton(IntPtr handle, int processId, int targetSheetCount)
    {
        for (var sheetNumber = 2; sheetNumber <= targetSheetCount; sheetNumber++)
        {
            var sheetName = $"Sheet{sheetNumber}";
            if (FindVisibleSheetTabElement(handle, sheetName) is not null)
            {
                continue;
            }

            var addButton = FindSheetAddButton(handle);
            if (addButton is null)
            {
                return CaptureResult.Blocked(options.Scenario, "uia-target-not-found", $"Could not find the Insert Sheet button before creating {sheetName}.", options.OutputRoot, "freex");
            }

            var blocked = GuardedClickElement(options.Scenario, processId, handle, addButton, MouseButtonKind.Left);
            if (blocked is not null)
            {
                return blocked;
            }

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while (DateTime.UtcNow < deadline)
            {
                if (FindVisibleSheetTabElement(handle, sheetName) is not null)
                {
                    break;
                }

                Thread.Sleep(100);
            }

            if (FindVisibleSheetTabElement(handle, sheetName) is null)
            {
                var visibleTabs = string.Join(", ", GetVisibleSheetTabOrder(handle));
                return CaptureResult.Blocked(options.Scenario, "sheet-seed-validation-failed", $"Insert Sheet activation did not expose expected tab {sheetName}. Visible tabs: {visibleTabs}.", options.OutputRoot, "freex");
            }
        }

        return null;
    }

    private static AutomationElement? FindNamedVisibleElement(IntPtr handle, string name, ControlType? controlType = null)
    {
        var root = AutomationElement.FromHandle(handle);
        Condition condition = new PropertyCondition(AutomationElement.NameProperty, name);
        if (controlType is not null)
        {
            condition = new AndCondition(
                condition,
                new PropertyCondition(AutomationElement.ControlTypeProperty, controlType));
        }

        return root.FindAll(TreeScope.Descendants, condition)
            .Cast<AutomationElement>()
            .Where(IsVisibleElement)
            .OrderBy(element => element.Current.BoundingRectangle.Top)
            .ThenBy(element => element.Current.BoundingRectangle.Left)
            .FirstOrDefault();
    }

    private static AutomationElement? FindSheetAddButton(IntPtr handle)
        => AutomationElement.FromHandle(handle)
            .FindAll(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.NameProperty, "Insert Sheet"),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)))
            .Cast<AutomationElement>()
            .Where(IsVisibleElement)
            .OrderByDescending(element => element.Current.BoundingRectangle.Top)
            .ThenByDescending(element => element.Current.BoundingRectangle.Left)
            .FirstOrDefault();

    private static AutomationElement? FindVisibleSheetTabElement(IntPtr handle, string name)
        => GetVisibleSheetTabElements(handle)
            .Where(element => GetSheetTabIdentity(element).Equals(name, StringComparison.Ordinal))
            .OrderByDescending(element => element.Current.BoundingRectangle.Width * element.Current.BoundingRectangle.Height)
            .ThenByDescending(element => element.Current.BoundingRectangle.Top)
            .FirstOrDefault();

    private static AutomationElement? FindSheetNavButton(IntPtr handle, bool right)
        => FindSheetNavButtonCandidates(handle, right).FirstOrDefault();

    private static AutomationElement? FindSheetNavButtonByAutomationId(IntPtr handle, bool right)
    {
        var expectedAutomationId = right ? "SheetNavRightBtn" : "SheetNavLeftBtn";
        return AutomationElement.FromHandle(handle)
            .FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, expectedAutomationId))
            .Cast<AutomationElement>()
            .Where(IsVisibleElement)
            .OrderByDescending(element => element.Current.BoundingRectangle.Width * element.Current.BoundingRectangle.Height)
            .FirstOrDefault();
    }

    private static string DescribeSheetNavButtonCandidates(IReadOnlyList<AutomationElement> candidates)
        => string.Join("; ", candidates.Take(8).Select((element, index) =>
        {
            var bounds = element.Current.BoundingRectangle;
            return $"#{index + 1} '{element.Current.Name}' {element.Current.ControlType.ProgrammaticName} [{bounds.Left:0},{bounds.Top:0},{bounds.Width:0}x{bounds.Height:0}]";
        }));

    private static IReadOnlyList<AutomationElement> FindSheetNavButtonCandidates(IntPtr handle, bool right)
    {
        var root = AutomationElement.FromHandle(handle);
        if (FindSheetNavButtonByAutomationId(handle, right) is { } automationIdMatch)
        {
            return [automationIdMatch];
        }

        var tabBounds = GetVisibleExcelSheetTabElements(handle)
            .Select(element => element.Current.BoundingRectangle)
            .Where(bounds => !bounds.IsEmpty)
            .ToList();
        if (tabBounds.Count == 0)
        {
            return [];
        }

        var tabCenterY = tabBounds.Average(bounds => bounds.Top + bounds.Height / 2.0);
        var tabLeft = tabBounds.Min(bounds => bounds.Left);
        var tabRight = tabBounds.Max(bounds => bounds.Right);
        var buttons = root
            .FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button))
            .Cast<AutomationElement>()
            .Where(IsVisibleElement)
            .Where(element =>
            {
                var bounds = element.Current.BoundingRectangle;
                var name = element.Current.Name ?? string.Empty;
                return !name.Equals("Insert Sheet", StringComparison.OrdinalIgnoreCase) &&
                       bounds.Width is >= 20 and <= 50 &&
                       bounds.Height is >= 20 and <= 35 &&
                       Math.Abs(bounds.Top + bounds.Height / 2.0 - tabCenterY) <= 16;
            })
            .OrderBy(element => element.Current.BoundingRectangle.Left)
            .ToList();

        if (buttons.Count == 0)
        {
            return [];
        }

        var expectedName = right ? "Scroll Tabs Right" : "Scroll Tabs Left";
        var namedButtons = buttons
            .Where(button => (button.Current.Name ?? string.Empty).Equals(expectedName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (namedButtons.Count > 0)
        {
            return namedButtons
                .Concat(buttons.Except(namedButtons))
                .Distinct()
                .ToList();
        }

        if (right)
        {
            var beforeTabs = buttons
                .Where(button => button.Current.BoundingRectangle.Right <= tabLeft + 2)
                .OrderByDescending(button => button.Current.BoundingRectangle.Right)
                .ToList();
            if (beforeTabs.Count >= 2)
            {
                return beforeTabs
                    .Concat(buttons.Except(beforeTabs))
                    .Distinct()
                    .ToList();
            }

            return buttons
                .OrderBy(button =>
                {
                    var bounds = button.Current.BoundingRectangle;
                    return Math.Abs((bounds.Left + bounds.Width / 2.0) - tabRight);
                })
                .ToList();
        }

        return buttons
            .OrderBy(button =>
            {
                var bounds = button.Current.BoundingRectangle;
                return Math.Abs((bounds.Left + bounds.Width / 2.0) - tabLeft);
            })
            .ToList();
    }

    private static WindowInfo? FindActivateDialogWindow(int processId, long ownerHandle, TimeSpan timeout)
    {
        var dialog = WindowFinder.FindProcessWindowIncludingChildren(
            processId,
            window => IsActivateDialogWindow(window, ownerHandle),
            timeout);
        if (dialog is not null)
        {
            return dialog;
        }

        return WindowFinder.FindForegroundWindow(
            window => window.ProcessId == processId && IsActivateDialogWindow(window, ownerHandle),
            TimeSpan.FromMilliseconds(Math.Max(1200, timeout.TotalMilliseconds / 2.0)));
    }

    private static WindowInfo? FindActivateSheetListDialogWindow(int processId, long ownerHandle, TimeSpan timeout)
    {
        var dialog = WindowFinder.FindProcessWindowIncludingChildren(
            processId,
            window => IsActivateSheetListDialogWindow(window, ownerHandle),
            timeout);
        if (dialog is not null)
        {
            return dialog;
        }

        return WindowFinder.FindForegroundWindow(
            window => window.ProcessId == processId && IsActivateSheetListDialogWindow(window, ownerHandle),
            TimeSpan.FromMilliseconds(Math.Max(1200, timeout.TotalMilliseconds / 2.0)));
    }

    private static WindowInfo? TryOpenExcelActivateSheetListDialogFromSheetNavCoordinates(int processId, IntPtr excelWindowHandle, TimeSpan timeout)
    {
        var tabBounds = GetVisibleExcelSheetTabElements(excelWindowHandle)
            .Select(element => element.Current.BoundingRectangle)
            .Where(bounds => !bounds.IsEmpty)
            .ToArray();
        if (tabBounds.Length == 0)
        {
            return null;
        }

        var firstTabLeft = tabBounds.Min(bounds => bounds.Left);
        var centerY = tabBounds.Average(bounds => bounds.Top + bounds.Height / 2.0);
        foreach (var offset in new[] { 18, 36, 54, 72 })
        {
            RightClickScreenPoint((int)(firstTabLeft - offset), (int)centerY);
            var dialog = FindActivateSheetListDialogWindow(processId, excelWindowHandle.ToInt64(), timeout);
            if (dialog is not null)
            {
                return dialog;
            }
        }

        return null;
    }

    private static bool TryOpenExcelActivateSheetListDialogFromWorkbookTabsCommandBar(
        dynamic excel,
        int processId,
        IntPtr excelWindowHandle,
        TimeSpan timeout,
        out WindowInfo? dialog)
    {
        dialog = null;
        if (!TryShowExcelWorkbookTabsCommandBar(excel))
        {
            return false;
        }

        Thread.Sleep(250);
        if (!TryInvokeProcessMenuItem(processId, "More Sheets") &&
            !TryInvokeProcessMenuItem(processId, "More...") &&
            !TryInvokeProcessMenuItem(processId, "More"))
        {
            return false;
        }

        Thread.Sleep(250);
        dialog = FindActivateSheetListDialogWindow(processId, excelWindowHandle.ToInt64(), timeout);
        return dialog is not null;
    }

    private static bool IsActivateDialogWindow(WindowInfo window, long ownerHandle)
    {
        if (window.Handle == ownerHandle ||
            window.Bounds.Width < 120 ||
            window.Bounds.Height < 90)
        {
            return false;
        }

        if (window.Title.Contains("Activate", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return WindowContainsSheetActivationList(window);
    }

    private static bool IsActivateSheetListDialogWindow(WindowInfo window, long ownerHandle)
    {
        if (window.Handle == ownerHandle ||
            window.Bounds.Width < 120 ||
            window.Bounds.Height < 90)
        {
            return false;
        }

        return window.Title.Contains("Activate", StringComparison.OrdinalIgnoreCase) &&
            WindowContainsSheetActivationList(window);
    }

    private static bool WindowContainsSheetActivationList(WindowInfo window)
    {
        try
        {
            var root = AutomationElement.FromHandle(new IntPtr(window.Handle));
            var descendants = root
                .FindAll(TreeScope.Descendants, Condition.TrueCondition)
                .Cast<AutomationElement>()
                .ToList();
            var hasSheetListEntry = descendants.Any(element =>
                Equals(element.Current.ControlType, ControlType.ListItem) &&
                IsDefaultSheetName(element.Current.Name));
            var hasConfirmationButton = descendants.Any(element =>
                Equals(element.Current.ControlType, ControlType.Button) &&
                (element.Current.Name.Equals("OK", StringComparison.OrdinalIgnoreCase) ||
                 element.Current.Name.Equals("Cancel", StringComparison.OrdinalIgnoreCase)));

            return hasSheetListEntry && hasConfirmationButton;
        }
        catch (COMException)
        {
            return false;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static List<string> GetVisibleSheetTabOrder(IntPtr handle)
    {
        return GetVisibleSheetTabElements(handle)
            .GroupBy(GetSheetTabIdentity)
            .Select(group => group.OrderBy(element => element.Current.BoundingRectangle.Width * element.Current.BoundingRectangle.Height).Last())
            .OrderBy(element => element.Current.BoundingRectangle.Left)
            .Select(GetSheetTabIdentity)
            .ToList();
    }

    private static List<AutomationElement> GetVisibleSheetTabElements(IntPtr handle)
    {
        var root = AutomationElement.FromHandle(handle);
        return root.FindAll(TreeScope.Descendants, Condition.TrueCondition)
            .Cast<AutomationElement>()
            .Where(IsVisibleElement)
            .Where(element => IsDefaultSheetName(GetSheetTabIdentity(element)))
            .ToList();
    }

    private static AutomationElement? FindVisibleExcelSheetTabElement(IntPtr handle, string name)
        => GetVisibleExcelSheetTabElements(handle)
            .Where(element => GetSheetTabIdentity(element).Equals(name, StringComparison.Ordinal))
            .OrderByDescending(element => element.Current.BoundingRectangle.Width * element.Current.BoundingRectangle.Height)
            .ThenByDescending(element => element.Current.BoundingRectangle.Top)
            .FirstOrDefault();

    private static List<AutomationElement> GetVisibleExcelSheetTabElements(IntPtr handle)
        => GetVisibleSheetTabElements(handle)
            .Where(element => Equals(element.Current.ControlType, ControlType.TabItem))
            .ToList();

    private static string GetSheetTabIdentity(AutomationElement element)
    {
        try
        {
            var name = element.Current.Name ?? string.Empty;
            if (IsDefaultSheetName(name))
            {
                return name;
            }

            var automationId = element.Current.AutomationId ?? string.Empty;
            if (IsDefaultSheetName(automationId))
            {
                return automationId;
            }

            return name;
        }
        catch (COMException)
        {
            return string.Empty;
        }
        catch (ElementNotAvailableException)
        {
            return string.Empty;
        }
    }

    private static bool IsDefaultSheetName(string name)
        => name.StartsWith("Sheet", StringComparison.Ordinal) &&
           name.Length > "Sheet".Length &&
           name["Sheet".Length..].All(char.IsDigit);

    private static IReadOnlyList<(int X, int Y, string Note)> GetSheetTabStripFallbackPoints(Rectangle windowBounds)
    {
        var xOffsets = new[] { 120, 165, 215, 275 };
        var yOffsets = new[] { 58, 72, 44 };
        var points = new List<(int X, int Y, string Note)>();

        foreach (var yOffset in yOffsets)
        {
            foreach (var xOffset in xOffsets)
            {
                var x = windowBounds.Left + Math.Min(Math.Max(xOffset, 24), Math.Max(24, windowBounds.Width - 24));
                var y = windowBounds.Bottom - Math.Min(Math.Max(yOffset, 24), Math.Max(24, windowBounds.Height - 24));
                points.Add((x, y, $"left+{xOffset}/bottom-{yOffset}"));
            }
        }

        return points;
    }

    private static bool IsVisibleElement(AutomationElement element)
    {
        try
        {
            var bounds = element.Current.BoundingRectangle;
            return !bounds.IsEmpty &&
                   bounds.Width >= 1 &&
                   bounds.Height >= 1 &&
                   !element.Current.IsOffscreen;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
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

    private CaptureResult? ValidateStatusZoomTextValue(IntPtr handle, double expectedZoomPercent, string trigger)
    {
        var expectedText = $"{expectedZoomPercent:0}%";
        if (!TryGetAutomationElementNameOrVisibleText(handle, "StatusZoomText", expectedText, out var actualText))
        {
            var suffix = string.IsNullOrWhiteSpace(actualText)
                ? string.Empty
                : $" Last UIA candidate was '{actualText}'.";
            return CaptureResult.Blocked(options.Scenario, "zoom-text-validation-unavailable", $"Could not read status zoom text '{expectedText}' after {trigger}.{suffix}", options.OutputRoot, "freex");
        }

        if (!string.Equals(actualText, expectedText, StringComparison.Ordinal))
        {
            return CaptureResult.Blocked(
                options.Scenario,
                "zoom-text-validation-failed",
                $"Expected status zoom text '{expectedText}' after {trigger}, but UIA reported '{actualText}'.",
                options.OutputRoot,
                "freex");
        }

        _lastResultValidation = $"{trigger}; status zoom text='{actualText}'.";
        return null;
    }

    private (CaptureResult? Blocked, int X, int Y) ResolveStatusZoomButtonCenter(IntPtr handle, int processId, string automationId, string label, ForegroundGuardResult guard)
    {
        guard = ForegroundGuard.FocusAndVerify(handle, processId, "FreeX", options.FocusTimeout);
        if (!guard.Success)
        {
            return (BlockedWithGuard(options.Scenario, guard, $"before-resolve-zoom-button-{label}"), 0, 0);
        }

        double left;
        double top;
        double width;
        double height;
        try
        {
            var button = FindVisibleElementByAutomationId(handle, automationId);
            if (button is null)
            {
                return (CaptureResult.Blocked(options.Scenario, "uia-target-not-found", $"Could not find status zoom button '{automationId}' for {label} bound proof.", options.OutputRoot, "freex", guard), 0, 0);
            }

            var bounds = button.Current.BoundingRectangle;
            left = bounds.Left;
            top = bounds.Top;
            width = bounds.Width;
            height = bounds.Height;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException)
        {
            return (CaptureResult.Blocked(options.Scenario, "uia-target-not-available", $"Could not resolve status zoom button '{automationId}' bounds for {label} bound proof: {ex.GetType().Name}: {ex.Message}", options.OutputRoot, "freex", guard), 0, 0);
        }

        if (width < 1 || height < 1)
        {
            return (CaptureResult.Blocked(options.Scenario, "uia-target-bounds-invalid", $"Status zoom button '{automationId}' bounds were not usable: {left:0.###},{top:0.###},{width:0.###},{height:0.###}.", options.OutputRoot, "freex", guard), 0, 0);
        }

        var x = (int)(left + width / 2.0);
        var y = (int)(top + height / 2.0);
        return (null, x, y);
    }

    private CaptureResult? ClickStatusZoomButtonRepeatedly(IntPtr handle, int processId, int x, int y, int clickCount, string label)
    {
        for (var i = 0; i < clickCount; i++)
        {
            var blocked = GuardedClickPoint(options.Scenario, processId, handle, x, y, MouseButtonKind.Left);
            if (blocked is not null)
            {
                return blocked;
            }

            Thread.Sleep(25);
        }

        Thread.Sleep(options.AfterInputDelay);
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

    private static bool TryGetToggleState(AutomationElement element, out ToggleState state)
    {
        state = ToggleState.Off;
        if (!element.TryGetCurrentPattern(TogglePattern.Pattern, out var patternObject) ||
            patternObject is not TogglePattern togglePattern)
        {
            return false;
        }

        state = togglePattern.Current.ToggleState;
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

    private static bool TryGetCellValue(IntPtr handle, string cellId, out string value)
    {
        value = string.Empty;
        var cell = FindElementByAutomationId(handle, cellId);
        if (cell is null ||
            !cell.TryGetCurrentPattern(ValuePattern.Pattern, out var patternObject) ||
            patternObject is not ValuePattern valuePattern)
        {
            return false;
        }

        value = valuePattern.Current.Value ?? string.Empty;
        return true;
    }

    private static bool WaitForCellValue(IntPtr handle, string cellId, string expectedValue, TimeSpan timeout, out string observedValue)
    {
        observedValue = string.Empty;
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            if (TryGetCellValue(handle, cellId, out observedValue) &&
                observedValue.Equals(expectedValue, StringComparison.Ordinal))
            {
                return true;
            }

            Thread.Sleep(100);
        }
        while (DateTime.UtcNow < deadline);

        return false;
    }

    private static bool TryGetSelectedCellIds(IntPtr handle, out HashSet<string> selectedIds)
    {
        selectedIds = [];
        var grid = FindElementByAutomationId(handle, "SheetGrid");
        if (grid is null ||
            !grid.TryGetCurrentPattern(SelectionPattern.Pattern, out var patternObject) ||
            patternObject is not SelectionPattern selectionPattern)
        {
            return false;
        }

        foreach (var selected in selectionPattern.Current.GetSelection())
        {
            var id = selected.Current.AutomationId;
            if (!string.IsNullOrWhiteSpace(id))
            {
                selectedIds.Add(id);
            }
        }

        return true;
    }

    private static string CellAutomationId(string address) => $"Cell_{address.ToUpperInvariant()}";

    private static IEnumerable<string> ExpectedCellIds(string startAddress, string endAddress)
    {
        var (startColumn, startRow) = ParseCellAddress(startAddress);
        var (endColumn, endRow) = ParseCellAddress(endAddress);
        var minColumn = Math.Min(startColumn, endColumn);
        var maxColumn = Math.Max(startColumn, endColumn);
        var minRow = Math.Min(startRow, endRow);
        var maxRow = Math.Max(startRow, endRow);

        for (var row = minRow; row <= maxRow; row++)
        {
            for (var column = minColumn; column <= maxColumn; column++)
            {
                yield return $"Cell_{ColumnName(column)}{row}";
            }
        }
    }

    private static (int Column, int Row) ParseCellAddress(string address)
    {
        var column = 0;
        var index = 0;
        while (index < address.Length && char.IsLetter(address[index]))
        {
            column = column * 26 + char.ToUpperInvariant(address[index]) - 'A' + 1;
            index++;
        }

        var row = int.Parse(address[index..], System.Globalization.CultureInfo.InvariantCulture);
        return (column, row);
    }

    private static string ColumnName(int column)
    {
        var builder = new StringBuilder();
        while (column > 0)
        {
            column--;
            builder.Insert(0, (char)('A' + column % 26));
            column /= 26;
        }

        return builder.ToString();
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

    private static bool TryGetAutomationElementName(IntPtr handle, string automationId, out string name)
    {
        name = string.Empty;
        var element = FindElementByAutomationId(handle, automationId);
        if (element is null)
        {
            return false;
        }

        name = element.Current.Name ?? string.Empty;
        return !string.IsNullOrWhiteSpace(name);
    }

    private static bool TryGetAutomationElementText(IntPtr handle, string automationId, out string text)
    {
        text = string.Empty;
        var element = FindVisibleElementByAutomationId(handle, automationId) ?? FindElementByAutomationId(handle, automationId);
        if (element is null)
        {
            return false;
        }

        var candidates = new List<string>();
        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePatternObject) &&
            valuePatternObject is ValuePattern valuePattern)
        {
            candidates.Add(valuePattern.Current.Value ?? string.Empty);
        }

        if (element.TryGetCurrentPattern(TextPattern.Pattern, out var textPatternObject) &&
            textPatternObject is TextPattern textPattern)
        {
            candidates.Add(textPattern.DocumentRange.GetText(256).TrimEnd('\r', '\n'));
        }

        candidates.Add(element.Current.Name ?? string.Empty);
        candidates.Add(element.Current.HelpText ?? string.Empty);

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                text = candidate.Trim();
                return true;
            }
        }

        return false;
    }

    private static bool TryGetAutomationElementNameOrVisibleText(IntPtr handle, string automationId, string expectedName, out string name)
    {
        var lastCandidate = string.Empty;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        do
        {
            var element = FindVisibleElementByAutomationId(handle, automationId);
            if (element is not null)
            {
                foreach (var candidate in ReadAutomationTextCandidates(element))
                {
                    if (string.Equals(candidate, expectedName, StringComparison.Ordinal))
                    {
                        name = candidate;
                        return true;
                    }

                    if (!string.IsNullOrWhiteSpace(candidate))
                    {
                        name = candidate;
                        lastCandidate = candidate;
                    }
                }
            }

            var root = AutomationElement.FromHandle(handle);
            var matches = root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.NameProperty, expectedName));
            foreach (AutomationElement match in matches)
            {
                if (IsVisibleElement(match))
                {
                    name = match.Current.Name ?? string.Empty;
                    return !string.IsNullOrWhiteSpace(name);
                }
            }

            Thread.Sleep(100);
        }
        while (DateTime.UtcNow < deadline);

        name = lastCandidate;
        return false;
    }

    private static IEnumerable<string> ReadAutomationTextCandidates(AutomationElement element)
    {
        yield return element.Current.Name ?? string.Empty;
        yield return element.Current.HelpText ?? string.Empty;

        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePatternObject) &&
            valuePatternObject is ValuePattern valuePattern)
        {
            yield return valuePattern.Current.Value ?? string.Empty;
        }

        if (element.TryGetCurrentPattern(TextPattern.Pattern, out var textPatternObject) &&
            textPatternObject is TextPattern textPattern)
        {
            yield return textPattern.DocumentRange.GetText(256).TrimEnd('\r', '\n');
        }

    }

    private static bool TryFindProcessText(int processId, IReadOnlyCollection<string> expectedTexts, out string readback)
    {
        readback = string.Empty;
        try
        {
            var names = AutomationElement.RootElement
                .FindAll(TreeScope.Descendants, Condition.TrueCondition)
                .Cast<AutomationElement>()
                .Where(element =>
                {
                    try
                    {
                        return element.Current.ProcessId == processId && !IsOffscreen(element);
                    }
                    catch (COMException)
                    {
                        return false;
                    }
                    catch (ElementNotAvailableException)
                    {
                        return false;
                    }
                })
                .SelectMany(ReadAutomationTextCandidatesSafely)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            readback = string.Join("; ", names.Where(name => name.Contains(":", StringComparison.Ordinal)).Take(20));
            return expectedTexts.All(expected => names.Any(name => name.Contains(expected, StringComparison.OrdinalIgnoreCase)));
        }
        catch (COMException)
        {
            return false;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static bool TryValidateExcelStatusFooterStatisticsViaContextMenu(int processId, IntPtr excelWindowHandle, out string readback)
    {
        readback = string.Empty;
        var window = WindowFinder.GetWindowInfo(excelWindowHandle);
        if (window is null)
        {
            return false;
        }

        RightClickScreenPoint(window.Bounds.Left + window.Bounds.Width / 2, window.Bounds.Bottom - 12);
        Thread.Sleep(350);
        return TryFindProcessText(processId, ["Average 5", "Count 4", "Sum 20"], out readback);
    }

    private static IEnumerable<string> ReadAutomationTextCandidatesSafely(AutomationElement element)
    {
        try
        {
            return ReadAutomationTextCandidates(element).ToList();
        }
        catch (COMException)
        {
            return [];
        }
        catch (ElementNotAvailableException)
        {
            return [];
        }
        catch (InvalidOperationException)
        {
            return [];
        }
    }

    private static string GetElementName(AutomationElement element)
    {
        try
        {
            return element.Current.Name ?? string.Empty;
        }
        catch (COMException)
        {
            return string.Empty;
        }
        catch (ElementNotAvailableException)
        {
            return string.Empty;
        }
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
            DateTimeOffset.UtcNow,
            EnvironmentSnapshot.Capture())
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
        dynamic excel = ExcelComAutomation.CreateExcelApplication(
            "Excel.Application COM ProgID is not available.",
            "Failed to create Excel.Application.");
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

    private static void PrepareExcelSheetTabWorkbook(dynamic excel, int targetSheetCount)
    {
        dynamic workbook = excel.ActiveWorkbook;
        while ((int)workbook.Worksheets.Count < targetSheetCount)
        {
            workbook.Worksheets.Add(After: workbook.Worksheets[workbook.Worksheets.Count]);
        }

        for (var i = 1; i <= (int)workbook.Worksheets.Count; i++)
        {
            workbook.Worksheets[i].Name = $"Sheet{i}";
        }

        workbook.Worksheets[1].Activate();
        dynamic worksheet = excel.ActiveSheet;
        worksheet.Range["A1"].Value2 = "Sheet tab parity";
        worksheet.Range["A1"].Select();
    }

    private static void PrepareExcelStatusFooterWorkbook(dynamic excel)
    {
        excel.DisplayStatusBar = true;
        dynamic worksheet = excel.ActiveSheet;
        worksheet.Range["A1"].Value2 = 2;
        worksheet.Range["A2"].Value2 = 4;
        worksheet.Range["A3"].Value2 = 6;
        worksheet.Range["A4"].Value2 = 8;
        worksheet.Range["A1:A4"].Select();
        excel.ActiveWindow.Zoom = 100;
    }

    private static void PrepareExcelFormulaBarNameBoxWorkbook(dynamic excel)
    {
        excel.DisplayFormulaBar = true;
        dynamic worksheet = excel.ActiveSheet;
        worksheet.Range["A1"].Value2 = "Metric";
        worksheet.Range["B1"].Value2 = "Value";
        worksheet.Range["A2"].Value2 = "Revenue";
        worksheet.Range["B2"].Value2 = 120;
        worksheet.Range["A3"].Value2 = "Cost";
        worksheet.Range["B3"].Value2 = 45;
        worksheet.Range["A4"].Value2 = "Profit";
        worksheet.Range["B4"].Formula = "=B2-B3";
        worksheet.Range["A:B"].EntireColumn.AutoFit();
        worksheet.Range["B4"].Select();
        excel.ActiveWindow.Zoom = 100;
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
        GetExcelRangeScreenBounds(excel, header, out int left, out int top, out int right, out int bottom);
        var clickX = right - 12;
        var clickY = top + (bottom - top) / 2;

        NativeMethods.SetCursorPos(clickX, clickY);
        Thread.Sleep(100);
        NativeMethods.MouseEvent(NativeMethods.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(60);
        NativeMethods.MouseEvent(NativeMethods.MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
    }

    private static void ClickExcelCellDropdownArrow(dynamic excel, dynamic worksheet, string address)
    {
        dynamic range = worksheet.Range[address];
        GetExcelRangeScreenBounds(excel, range, out int left, out int top, out int right, out int bottom);
        var clickX = right - 8;
        var clickY = top + (bottom - top) / 2;

        NativeMethods.SetCursorPos(clickX, clickY);
        Thread.Sleep(100);
        NativeMethods.MouseEvent(NativeMethods.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(60);
        NativeMethods.MouseEvent(NativeMethods.MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
    }

    private static void GetExcelRangeScreenBounds(dynamic excel, dynamic range, out int left, out int top, out int right, out int bottom)
    {
        dynamic window = excel.ActiveWindow;
        left = (int)window.PointsToScreenPixelsX(range.Left);
        top = (int)window.PointsToScreenPixelsY(range.Top);
        right = (int)window.PointsToScreenPixelsX(range.Left + range.Width);
        bottom = (int)window.PointsToScreenPixelsY(range.Top + range.Height);
    }

    private static WindowInfo? FindExcelDataValidationListPopup(int processId, long ownerHandle, TimeSpan timeout)
        => WindowFinder.FindProcessWindowIncludingChildren(
            processId,
            window => IsExcelDataValidationListPopupWindow(window, ownerHandle),
            timeout);

    private static WindowInfo? FindFreeXAutoFilterDialog(int processId, long ownerHandle, TimeSpan timeout)
        => WindowFinder.FindProcessWindowIncludingChildren(
            processId,
            window => window.Handle != ownerHandle &&
                window.Bounds.Width >= 220 &&
                window.Bounds.Height >= 220 &&
                (window.Title.Contains("score", StringComparison.OrdinalIgnoreCase) ||
                 WindowHasUiaText(window.Handle, "Number Filters") ||
                 WindowHasUiaText(window.Handle, "Select All")),
            timeout);

    private static bool IsExcelDataValidationListPopupWindow(WindowInfo window, long ownerHandle)
    {
        return window.Handle != ownerHandle &&
               !window.ClassName.Equals("XLMAIN", StringComparison.OrdinalIgnoreCase) &&
               !window.ClassName.Equals("NUIDialog", StringComparison.OrdinalIgnoreCase) &&
               window.Bounds.Width is >= 70 and <= 450 &&
               window.Bounds.Height is >= 40 and <= 500;
    }

    private static WindowInfo? FindExcelFormatCellsDialog(int processId, long ownerHandle, TimeSpan timeout)
        => WindowFinder.FindProcessWindow(
            processId,
            window => window.Handle != ownerHandle &&
                window.Bounds.Width > 350 &&
                window.Bounds.Height > 250 &&
                (window.Title.Contains("Format Cells", StringComparison.OrdinalIgnoreCase) ||
                 WindowHasUiaText(window.Handle, "Format Cells")),
            timeout);

    private static WindowInfo? FindExcelRibbonGalleryPopup(int processId, long ownerHandle, string galleryName, TimeSpan timeout)
        => WindowFinder.FindProcessWindow(
            processId,
            window => window.Handle != ownerHandle &&
                !window.ClassName.Equals("XLMAIN", StringComparison.OrdinalIgnoreCase) &&
                !window.ClassName.Equals("NUIDialog", StringComparison.OrdinalIgnoreCase) &&
                window.Bounds.Width >= 120 &&
                window.Bounds.Height >= 80 &&
                (window.ClassName.Equals("Net UI Tool Window", StringComparison.OrdinalIgnoreCase) ||
                 WindowHasUiaText(window.Handle, galleryName)),
            timeout);

    private static bool WindowHasUiaText(long handle, string text)
    {
        try
        {
            var normalizedText = NormalizeMenuSearchText(text);
            var root = AutomationElement.FromHandle(new IntPtr(handle));
            if ((root.Current.Name ?? string.Empty).Contains(text, StringComparison.OrdinalIgnoreCase) ||
                (root.Current.AutomationId ?? string.Empty).Contains(text, StringComparison.OrdinalIgnoreCase) ||
                NormalizeMenuSearchText(root.Current.Name).Contains(normalizedText, StringComparison.OrdinalIgnoreCase) ||
                NormalizeMenuSearchText(root.Current.AutomationId).Contains(normalizedText, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var descendants = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
            foreach (AutomationElement descendant in descendants)
            {
                var name = descendant.Current.Name ?? string.Empty;
                var automationId = descendant.Current.AutomationId ?? string.Empty;
                if (name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                    automationId.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                    NormalizeMenuSearchText(name).Contains(normalizedText, StringComparison.OrdinalIgnoreCase) ||
                    NormalizeMenuSearchText(automationId).Contains(normalizedText, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (COMException)
        {
        }
        catch (ElementNotAvailableException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        return false;
    }

    private static void RightClickScreenPoint(int x, int y)
    {
        NativeMethods.SetCursorPos(x, y);
        Thread.Sleep(100);
        NativeMethods.MouseEvent(NativeMethods.MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(60);
        NativeMethods.MouseEvent(NativeMethods.MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
    }

    private static bool TryRightClickAutomationElement(AutomationElement element)
    {
        var bounds = element.Current.BoundingRectangle;
        if (bounds.IsEmpty || bounds.Width < 1 || bounds.Height < 1)
        {
            return false;
        }

        RightClickScreenPoint(
            (int)(bounds.Left + bounds.Width / 2.0),
            (int)(bounds.Top + bounds.Height / 2.0));
        return true;
    }

    private static bool TryInvokeProcessMenuItem(int processId, string nameContains)
    {
        List<AutomationElement> menuItems;
        try
        {
            menuItems = AutomationElement.RootElement
                .FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem))
                .Cast<AutomationElement>()
                .Where(element => ElementMatchesProcessAndName(element, processId, nameContains))
                .OrderBy(element => IsOffscreen(element) ? 1 : 0)
                .ThenByDescending(GetElementArea)
                .ToList();
        }
        catch (COMException)
        {
            return false;
        }

        foreach (var item in menuItems)
        {
            try
            {
                if (item.TryGetCurrentPattern(InvokePattern.Pattern, out var invokePatternObject) &&
                    invokePatternObject is InvokePattern invokePattern)
                {
                    invokePattern.Invoke();
                    return true;
                }

                var bounds = item.Current.BoundingRectangle;
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
            catch (COMException)
            {
            }
            catch (ElementNotAvailableException)
            {
            }
        }

        return false;
    }

    private static bool ProcessHasVisibleMenuItems(int processId, params string[] expectedNames)
    {
        var visibleNames = GetVisibleProcessMenuItemNames(processId)
            .Select(NormalizeMenuSearchText)
            .ToList();

        return expectedNames.All(expected =>
        {
            var normalizedExpected = NormalizeMenuSearchText(expected);
            return visibleNames.Any(name => name.Contains(normalizedExpected, StringComparison.OrdinalIgnoreCase));
        });
    }

    private static string DescribeVisibleProcessMenuItems(int processId)
    {
        var names = GetVisibleProcessMenuItemNames(processId)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Take(12)
            .ToArray();
        return names.Length == 0 ? "<none>" : string.Join(", ", names);
    }

    private static IReadOnlyList<string> GetVisibleProcessMenuItemNames(int processId)
    {
        try
        {
            return AutomationElement.RootElement
                .FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem))
                .Cast<AutomationElement>()
                .Where(element => ElementBelongsToProcess(element, processId))
                .Where(element => !IsOffscreen(element))
                .Where(element => GetElementArea(element) > 0)
                .Select(element =>
                {
                    try
                    {
                        return element.Current.Name ?? string.Empty;
                    }
                    catch (ElementNotAvailableException)
                    {
                        return string.Empty;
                    }
                    catch (COMException)
                    {
                        return string.Empty;
                    }
                })
                .ToList();
        }
        catch (COMException)
        {
            return [];
        }
    }

    private static double GetElementArea(AutomationElement element)
    {
        try
        {
            var bounds = element.Current.BoundingRectangle;
            return bounds.Width * bounds.Height;
        }
        catch (COMException)
        {
            return 0;
        }
        catch (ElementNotAvailableException)
        {
            return 0;
        }
    }

    private static bool ElementMatchesProcessAndName(AutomationElement element, int processId, string nameContains)
    {
        try
        {
            if (!ElementBelongsToProcess(element, processId))
            {
                return false;
            }

            var expected = NormalizeMenuSearchText(nameContains);
            var name = NormalizeMenuSearchText(element.Current.Name);
            var automationId = NormalizeMenuSearchText(element.Current.AutomationId);
            return name.Contains(expected, StringComparison.OrdinalIgnoreCase) ||
                   automationId.Contains(expected, StringComparison.OrdinalIgnoreCase);
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
        catch (COMException)
        {
            return false;
        }
    }

    private static bool ElementBelongsToProcess(AutomationElement element, int processId)
    {
        try
        {
            return element.Current.ProcessId == processId;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
        catch (COMException)
        {
            return false;
        }
    }

    private static bool IsOffscreen(AutomationElement element)
    {
        try
        {
            return element.Current.IsOffscreen;
        }
        catch (ElementNotAvailableException)
        {
            return true;
        }
        catch (COMException)
        {
            return true;
        }
    }

    private static string NormalizeMenuSearchText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (char.IsLetterOrDigit(character) || char.IsWhiteSpace(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static void SendCtrl1()
    {
        SendKeys.SendWait("^1");
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
        GetExcelRangeScreenBounds(excel, range, out int left, out int top, out int right, out int bottom);
        var clickX = left + (right - left) / 2;
        var clickY = top + (bottom - top) / 2;

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

    private static bool TryOpenExcelCellStylesGallery(IntPtr excelWindowHandle)
        => TryOpenRibbonGalleryByText(excelWindowHandle, "Cell Styles");

    private static bool TryOpenRibbonGalleryByText(IntPtr windowHandle, string labelText)
    {
        try
        {
            var root = AutomationElement.FromHandle(windowHandle);
            var candidates = root.FindAll(TreeScope.Descendants, Condition.TrueCondition)
                .Cast<AutomationElement>()
                .Where(IsVisibleElement)
                .Where(element => ElementTextContains(element, labelText))
                .OrderBy(element => element.Current.BoundingRectangle.Top)
                .ThenByDescending(element => element.Current.BoundingRectangle.Width * element.Current.BoundingRectangle.Height)
                .ToArray();

            foreach (var candidate in candidates)
            {
                if (candidate.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var expandPatternObject) &&
                    expandPatternObject is ExpandCollapsePattern expandPattern)
                {
                    expandPattern.Expand();
                    return true;
                }

                if (candidate.TryGetCurrentPattern(InvokePattern.Pattern, out var invokePatternObject) &&
                    invokePatternObject is InvokePattern invokePattern)
                {
                    invokePattern.Invoke();
                    return true;
                }

                var bounds = candidate.Current.BoundingRectangle;
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
        }
        catch (COMException)
        {
        }
        catch (ElementNotAvailableException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        return false;
    }

    private static bool ElementTextContains(AutomationElement element, string text)
    {
        try
        {
            var name = element.Current.Name ?? string.Empty;
            var automationId = element.Current.AutomationId ?? string.Empty;
            var normalizedText = NormalizeMenuSearchText(text);
            return name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                   automationId.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                   NormalizeMenuSearchText(name).Contains(normalizedText, StringComparison.OrdinalIgnoreCase) ||
                   NormalizeMenuSearchText(automationId).Contains(normalizedText, StringComparison.OrdinalIgnoreCase);
        }
        catch (COMException)
        {
            return false;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static bool TryExecuteExcelMso(dynamic excel, string commandId)
    {
        try
        {
            excel.CommandBars.ExecuteMso(commandId);
            return true;
        }
        catch (RuntimeBinderException)
        {
            return false;
        }
        catch (COMException)
        {
            return false;
        }
    }

    private static bool TryShowExcelCellCommandBar(dynamic excel)
    {
        try
        {
            dynamic cellCommandBar = excel.CommandBars["Cell"];
            cellCommandBar.ShowPopup();
            return true;
        }
        catch (RuntimeBinderException)
        {
            return false;
        }
        catch (COMException)
        {
            return false;
        }
    }

    private static bool TryShowExcelWorkbookTabsCommandBar(dynamic excel)
    {
        foreach (var commandBarName in new[] { "Workbook Tabs", "Ply" })
        {
            var started = new ManualResetEventSlim(false);
            var failed = false;
            var thread = new Thread(() =>
            {
                try
                {
                    started.Set();
                    dynamic workbookTabsCommandBar = excel.CommandBars[commandBarName];
                    workbookTabsCommandBar.ShowPopup();
                }
                catch (RuntimeBinderException)
                {
                    failed = true;
                }
                catch (COMException)
                {
                    failed = true;
                }
                catch (InvalidOperationException)
                {
                    failed = true;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            started.Wait(TimeSpan.FromMilliseconds(500));
            Thread.Sleep(250);
            if (!failed)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryShowExcelBuiltInDialogAsync(dynamic excel, int dialogId)
    {
        var started = new ManualResetEventSlim(false);
        var failed = false;
        var thread = new Thread(() =>
        {
            try
            {
                started.Set();
                excel.Dialogs[dialogId].Show();
            }
            catch (RuntimeBinderException)
            {
                failed = true;
            }
            catch (COMException)
            {
                failed = true;
            }
            catch (InvalidOperationException)
            {
                failed = true;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        started.Wait(TimeSpan.FromMilliseconds(500));
        Thread.Sleep(250);
        return !failed;
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

    private static void CreatePreparedExcelDataValidationWorkbook(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

        dynamic? excel = null;
        dynamic? workbook = null;
        try
        {
            (excel, workbook) = CreateExcel();
            PrepareExcelDataValidationDropdownWorkbook(excel);
            workbook.SaveAs(path, 51);
        }
        finally
        {
            CloseExcel(excel, workbook);
        }
    }

    private static (dynamic Excel, dynamic Workbook) OpenExcelWorkbook(string path)
    {
        dynamic excel = ExcelComAutomation.CreateExcelApplication(
            "Excel.Application COM ProgID is not available.",
            "Failed to create Excel.Application.");
        excel.Visible = true;
        excel.DisplayAlerts = false;
        dynamic workbook = excel.Workbooks.Open(path);
        return (excel, workbook);
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

    private string ResolveAvaloniaExePath()
    {
        if (!string.IsNullOrWhiteSpace(options.AvaloniaExePath))
        {
            return options.AvaloniaExePath;
        }

        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var candidate = Path.Combine(repoRoot, "src", "FreeX.App.Avalonia", "bin", "Release", "net10.0", "FreeX.exe");
        if (!File.Exists(candidate))
        {
            throw new FileNotFoundException($"FreeX Avalonia executable was not found. Build Release or pass --avalonia-exe. Expected: {candidate}", candidate);
        }

        return candidate;
    }
}

internal sealed record CaptureOptions(
    string Scenario,
    string OutputRoot,
    string? FreeXExePath,
    string? AvaloniaExePath,
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
          excel-cell-styles-gallery
          excel-context-menu
          excel-format-cells-context-dialog
          excel-data-validation-dropdown-prepared
          excel-open-dialog
          excel-save-as-dialog
          excel-sheet-tab-context-menu
          excel-sheet-tab-overflow-activate-dialog
          excel-status-footer-reference
          excel-formula-bar-name-box-reference
          freex-open-dialog
          freex-save-as-dialog
          freex-conditional-formatting-gallery
          avalonia-conditional-formatting-gallery
          freex-format-cells-context-dialog
          freex-save-as-dialog-cancel
          freex-save-as-overwrite-prompt
          freex-save-as-invalid-path
          freex-export-pdf-save-dialog-cancel
          freex-export-overwrite-prompt
          freex-export-xps-accept
          freex-native-print-dialog
          freex-background-picker-cancel
          freex-background-picker-select
          freex-background-picker-replace
          freex-background-clear
          freex-status-zoom-in-click
          freex-status-zoom-out-click
          freex-status-zoom-slider-drag
          freex-status-zoom-slider-rangevalue-set
          freex-status-zoom-min-max-rangevalue-set
          freex-status-ctrl-wheel-grid-zoom
          freex-status-wheel-modifier-breadth
          freex-status-view-shortcuts-click
          freex-status-zoom-text-dialog-click
          freex-status-ctrl-alt-zoom-keys
          freex-status-live-stats-accessibility
          freex-formula-bar-name-box-reference
          freex-sheet-tab-context-menu
          freex-sheet-tab-click-select
          freex-sheet-tab-double-click-rename
          freex-sheet-tab-ctrl-click-grouping
          freex-sheet-tab-shift-click-grouping
          freex-sheet-tab-grouped-commands
          freex-sheet-tab-drag-reorder
          freex-sheet-tab-overflow-nav-click
          freex-sheet-tab-overflow-activate-dialog
          freex-grid-drag-select
          freex-s4-grid-drag-select-validated
          freex-s4-grid-autofill-handle-drag
          freex-s4-grid-double-click-autofit
          freex-grid-row-column-resize
          freex-grid-wheel-scroll

        Options:
          --output <path>       Default: tools/foreground-captures
          --freex-exe <path>    FreeX.App.Host.exe path for FreeX scenarios
          --avalonia-exe <path> FreeX Avalonia executable path for Avalonia scenarios
        """;

    public static CaptureOptions Parse(string[] args)
    {
        var scenario = string.Empty;
        var output = Path.Combine("tools", "foreground-captures");
        string? freexExe = null;
        string? avaloniaExe = null;
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
                case "--avalonia-exe" when i + 1 < args.Length:
                    avaloniaExe = args[++i];
                    break;
            }
        }

        return new CaptureOptions(
            scenario,
            Path.GetFullPath(output),
            freexExe,
            avaloniaExe,
            showHelp,
            listSlices,
            SubjectForScenario(scenario),
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(6),
            TimeSpan.FromMilliseconds(900),
            TimeSpan.FromMilliseconds(3000));
    }

    private static string SubjectForScenario(string scenario)
    {
        if (scenario.StartsWith("excel-", StringComparison.OrdinalIgnoreCase))
        {
            return "excel";
        }

        if (scenario.StartsWith("avalonia-", StringComparison.OrdinalIgnoreCase))
        {
            return "avalonia";
        }

        return "freex";
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
    DateTimeOffset CapturedAtUtc,
    EnvironmentSnapshot EnvironmentSnapshot)
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
            DateTimeOffset.UtcNow,
            EnvironmentSnapshot.Capture());

        var scenarioDir = Path.Combine(outputRoot, string.IsNullOrWhiteSpace(scenario) ? "unknown" : scenario);
        Directory.CreateDirectory(scenarioDir);
        var manifestPath = Path.Combine(scenarioDir, $"{(string.IsNullOrWhiteSpace(scenario) ? "unknown" : scenario)}_manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(result with { ManifestPath = manifestPath }, ProgramAccessor.JsonOptions));
        return result with { ManifestPath = manifestPath };
    }
}

internal sealed record EnvironmentSnapshot(
    string OperatingSystem,
    bool IsWindows,
    bool UserInteractive,
    int SessionId,
    int ProcessId,
    string ProcessArchitecture,
    WindowInfo? ForegroundWindowAtCapture)
{
    public static EnvironmentSnapshot Capture()
    {
        var currentProcess = Process.GetCurrentProcess();
        return new EnvironmentSnapshot(
            RuntimeInformation.OSDescription,
            System.OperatingSystem.IsWindows(),
            Environment.UserInteractive,
            currentProcess.SessionId,
            currentProcess.Id,
            RuntimeInformation.ProcessArchitecture.ToString(),
            WindowFinder.GetWindowInfo(NativeMethods.GetForegroundWindow()));
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
        var reason = IsWindowsLockScreen(current)
            ? "Windows lock screen is active; unlock the interactive console before running foreground capture."
            : current is null
                ? "No foreground window detected."
                : $"Foreground is PID {current.ProcessId} '{current.Title}' class '{current.ClassName}', expected PID {expectedProcessId} title containing '{titleContains}'.";
        return new ForegroundGuardResult(false, expectedProcessId, handle.ToInt64(), current, reason);
    }

    private static bool IsWindowsLockScreen(WindowInfo? window) =>
        window is not null &&
        window.Title.Equals("Windows Default Lock Screen", StringComparison.OrdinalIgnoreCase) &&
        window.ClassName.Equals("Windows.UI.Core.CoreWindow", StringComparison.OrdinalIgnoreCase);

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

    public static WindowInfo? WaitForMainWindow(Process process, string exePath, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var expectedProcessName = Path.GetFileNameWithoutExtension(exePath);
        var expectedExePath = Path.GetFullPath(exePath);
        while (DateTime.UtcNow < deadline)
        {
            var window = EnumerateVisibleWindows()
                .Where(candidate => IsLaunchMainWindowCandidate(candidate, process.Id, expectedProcessName, expectedExePath))
                .OrderByDescending(candidate => candidate.ProcessId == process.Id)
                .ThenByDescending(candidate => candidate.Bounds.Width * candidate.Bounds.Height)
                .FirstOrDefault();

            if (window is not null)
            {
                return window;
            }

            Thread.Sleep(150);
        }

        return null;
    }

    public static string DescribeLaunchWindowCandidates(int launcherProcessId, string exePath)
    {
        var expectedProcessName = Path.GetFileNameWithoutExtension(exePath);
        var expectedExePath = Path.GetFullPath(exePath);
        var candidates = EnumerateVisibleWindows()
            .Where(candidate =>
                candidate.ProcessId == launcherProcessId ||
                IsSameExecutableProcess(candidate.ProcessId, expectedProcessName, expectedExePath) ||
                candidate.Title.Contains("FreeX", StringComparison.OrdinalIgnoreCase))
            .OrderBy(candidate => candidate.ProcessId == launcherProcessId ? 0 : 1)
            .ThenByDescending(candidate => candidate.Bounds.Width * candidate.Bounds.Height)
            .Take(8)
            .Select(candidate => $"pid={candidate.ProcessId}, title='{candidate.Title}', class='{candidate.ClassName}', bounds={candidate.Bounds.Width}x{candidate.Bounds.Height}")
            .ToArray();

        return candidates.Length == 0
            ? "No visible direct, same-executable, or FreeX-titled windows were found."
            : $"Visible window candidates: {string.Join("; ", candidates)}.";
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

    private static bool IsLaunchMainWindowCandidate(
        WindowInfo candidate,
        int launcherProcessId,
        string expectedProcessName,
        string expectedExePath)
    {
        if (candidate.Title.Length == 0)
        {
            return false;
        }

        if (candidate.ProcessId == launcherProcessId)
        {
            return true;
        }

        if (!candidate.Title.Contains("FreeX", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return IsSameExecutableProcess(candidate.ProcessId, expectedProcessName, expectedExePath);
    }

    private static bool IsSameExecutableProcess(int processId, string expectedProcessName, string expectedExePath)
    {
        try
        {
            using var candidateProcess = Process.GetProcessById(processId);
            if (candidateProcess.ProcessName.Equals(expectedProcessName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var candidatePath = candidateProcess.MainModule?.FileName;
            return !string.IsNullOrWhiteSpace(candidatePath) &&
                Path.GetFullPath(candidatePath).Equals(expectedExePath, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
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

    public static WindowInfo? FindProcessWindowIncludingChildren(int processId, Func<WindowInfo, bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var window = EnumerateVisibleProcessWindowsIncludingChildren(processId)
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
                IsDistinctTopLevelWindow(foreground, ownerHandle) &&
                !foreground.ClassName.Equals("XLMAIN", StringComparison.OrdinalIgnoreCase) &&
                foreground.Bounds.Width >= minimumWidth &&
                foreground.Bounds.Height >= minimumHeight)
            {
                return foreground;
            }

            var popup = EnumerateVisibleWindows()
                .Where(candidate => candidate.ProcessId == processId)
                .Where(candidate => IsDistinctTopLevelWindow(candidate, ownerHandle))
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
                IsDistinctTopLevelWindow(foreground, ownerHandle) &&
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

    private static bool IsDistinctTopLevelWindow(WindowInfo candidate, long ownerHandle) =>
        candidate.Handle != ownerHandle &&
        NativeMethods.GetAncestor(new IntPtr(candidate.Handle), NativeMethods.GA_ROOT).ToInt64() != ownerHandle;

    public static WindowInfo? FindForegroundWindow(Func<WindowInfo, bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var foreground = GetWindowInfo(NativeMethods.GetForegroundWindow());
            if (foreground is not null && predicate(foreground))
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

    private static IEnumerable<WindowInfo> EnumerateVisibleProcessWindowsIncludingChildren(int processId)
    {
        var seen = new HashSet<long>();
        var windows = new List<WindowInfo>();

        void AddWindow(IntPtr handle)
        {
            var info = GetWindowInfo(handle);
            if (info is null ||
                info.ProcessId != processId ||
                info.Bounds.Width <= 0 ||
                info.Bounds.Height <= 0 ||
                !seen.Add(info.Handle))
            {
                return;
            }

            windows.Add(info);
        }

        NativeMethods.EnumWindows((handle, _) =>
        {
            AddWindow(handle);
            NativeMethods.EnumChildWindows(handle, (childHandle, _) =>
            {
                AddWindow(childHandle);
                return true;
            }, IntPtr.Zero);

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
    public const uint GA_ROOT = 2;
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
    public const byte VK_MENU = 0x12;
    public const byte VK_1 = 0x31;
    public const byte VK_L = 0x4C;
    public const byte VK_V = 0x56;
    public const byte VK_SHIFT = 0x10;
    public const byte VK_DOWN = 0x28;
    public const byte VK_OEM_PLUS = 0xBB;
    public const byte VK_OEM_MINUS = 0xBD;
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new(-2);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

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
    public static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

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
