using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private const double ScreenshotTourCaptureHeight = 300;
    private const string ScreenshotTourTableName = "TourTable";
    private const string ScreenshotTourPivotTableName = "TourPivotTable";
    private const string RibbonScreenshotTourManifestFileName = "ribbon_screenshot_tour_manifest.json";
    private const string AutoFilterFlyoutTourManifestFileName = "autofilter_flyout_tour_manifest.json";
    private const string AutoFilterFlyoutTourCaptureFileName = "freex_table_autofilter_dropdown";
    private const string HomeNumberFormatDropdownTourManifestFileName = "home_number_format_dropdown_tour_manifest.json";
    private const string HomeNumberFormatDropdownTourCaptureFileName = "freex_dropdown_home_number_format_opened";
    private const string HomeAlignmentNumberTourManifestFileName = "home_alignment_number_tour_manifest.json";
    private const string HomeAlignmentNumberTourOutputDirectoryName = "home-alignment-number-tour";
    private const string HomeBordersDropdownTourManifestFileName = "home_borders_dropdown_tour_manifest.json";
    private const string HomeBordersDropdownTourCaptureFileName = "freex_dropdown_home_borders_opened";
    private const string WorksheetContextMenuTourManifestFileName = "worksheet_context_menu_tour_manifest.json";
    private const string WorksheetContextMenuTourCaptureFileName = "freex_context_menu_worksheet_cell_opened";
    private const string KeyTipOverlayTourManifestFileName = "keytip_overlay_tour_manifest.json";
    private const string PrintPreviewTourManifestFileName = "print_preview_tour_manifest.json";
    private const string OptionsAccountTourManifestFileName = "options_account_tour_manifest.json";
    private const string OptionsAccountTourOutputDirectoryName = "options-account-tour";
    private const string QatUndoRedoTourManifestFileName = "qat_undo_redo_tour_manifest.json";
    private const string QatUndoRedoTourOutputDirectoryName = "qat-undo-redo-tour";
    private const string SheetTabTourManifestFileName = "sheet_tabs_tour_manifest.json";
    private const string SheetTabTourOutputDirectoryName = "sheet-tabs-tour";
    private const string TitlebarWindowChromeTourManifestFileName = "titlebar_window_chrome_tour_manifest.json";
    private const string TitlebarWindowChromeTourOutputDirectoryName = "titlebar-window-chrome-tour";
    private const string TitlebarWindowChromeTourSavedWorkbookFileName = "freex_titlebar_renamed_workbook.xlsx";
    private const string FormulaBarNameBoxTourManifestFileName = "formula_bar_name_box_tour_manifest.json";
    private const string FormulaBarNameBoxTourOutputDirectoryName = "formula-bar-name-box-tour";
    private const string StatusFooterTourManifestFileName = "status_footer_tour_manifest.json";
    private const string StatusFooterTourOutputDirectoryName = "status-footer-tour";
    private const string FormulaDiagnosticsTourManifestFileName = "formula_diagnostics_tour_manifest.json";
    private const string FormulaDiagnosticsTourOutputDirectoryName = "formula-diagnostics-tour";
    private const string ScreenshotTourAllowBackgroundRenderEnvVar = "FREEX_SS_TOUR_ALLOW_BACKGROUND_RENDER";
    private const string ScreenshotTourOutputSubdirectoryEnvVar = "FREEX_SS_TOUR_OUTPUT_SUBDIR";

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr ho);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    // Activated by FREEX_SS_TOUR=1 env var.  Output lands in <repo-root>/screenshots/.
    private async void TryStartScreenshotTour()
    {
        var ribbonBurstTour = Environment.GetEnvironmentVariable("FREEX_SS_TOUR_BURST") == "1";
        var ribbonTour = ribbonBurstTour || Environment.GetEnvironmentVariable("FREEX_SS_TOUR") == "1";
        var backstageTour = Environment.GetEnvironmentVariable("FREEX_BACKSTAGE_TOUR") == "1";
        var autoFilterFlyoutTour = Environment.GetEnvironmentVariable("FREEX_AUTOFILTER_FLYOUT_TOUR") == "1";
        var homeNumberFormatDropdownTour = Environment.GetEnvironmentVariable("FREEX_HOME_NUMBER_FORMAT_DROPDOWN_TOUR") == "1";
        var homeAlignmentNumberTour = Environment.GetEnvironmentVariable("FREEX_HOME_ALIGNMENT_NUMBER_TOUR") == "1";
        var homeBordersDropdownTour = Environment.GetEnvironmentVariable("FREEX_HOME_BORDERS_DROPDOWN_TOUR") == "1";
        var worksheetContextMenuTour = Environment.GetEnvironmentVariable("FREEX_WORKSHEET_CONTEXT_MENU_TOUR") == "1";
        var keyTipOverlayTour = Environment.GetEnvironmentVariable("FREEX_KEYTIP_OVERLAY_TOUR") == "1";
        var printPreviewTour = Environment.GetEnvironmentVariable("FREEX_PRINT_PREVIEW_TOUR") == "1";
        var optionsAccountTour = Environment.GetEnvironmentVariable("FREEX_OPTIONS_ACCOUNT_TOUR") == "1";
        var qatUndoRedoTour = Environment.GetEnvironmentVariable("FREEX_QAT_UNDO_REDO_TOUR") == "1";
        var titlebarWindowChromeTour = Environment.GetEnvironmentVariable("FREEX_TITLEBAR_WINDOW_CHROME_TOUR") == "1";
        var formulaBarNameBoxTour = Environment.GetEnvironmentVariable("FREEX_FORMULA_BAR_NAME_BOX_TOUR") == "1";
        var statusFooterTour = Environment.GetEnvironmentVariable("FREEX_STATUS_FOOTER_TOUR") == "1";
        var formulaDiagnosticsTour = Environment.GetEnvironmentVariable("FREEX_FORMULA_DIAGNOSTICS_TOUR") == "1";
        if (!ribbonTour && !backstageTour && !autoFilterFlyoutTour && !homeNumberFormatDropdownTour && !homeAlignmentNumberTour && !homeBordersDropdownTour && !worksheetContextMenuTour && !keyTipOverlayTour && !printPreviewTour && !optionsAccountTour && !qatUndoRedoTour && !titlebarWindowChromeTour && !statusFooterTour && !formulaBarNameBoxTour && !formulaDiagnosticsTour)
            return;

        var ribbonPlan = ribbonTour
            ? RibbonScreenshotTourPlanner.CreatePlan(
                Environment.GetEnvironmentVariable("FREEX_SS_TOUR_TABS"),
                Environment.GetEnvironmentVariable("FREEX_SS_TOUR_WIDTHS"),
                ribbonBurstTour,
                Environment.GetEnvironmentVariable("FREEX_SS_TOUR_CONTEXT"))
            : null;

        var screenshotsRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "screenshots"));
        var outputDir = ResolveScreenshotTourOutputDirectory(
            screenshotsRoot,
            Environment.GetEnvironmentVariable(ScreenshotTourOutputSubdirectoryEnvVar));
        Directory.CreateDirectory(outputDir);
        await RunScreenshotTourAsync(outputDir, ribbonPlan, backstageTour, autoFilterFlyoutTour, homeNumberFormatDropdownTour, homeAlignmentNumberTour, homeBordersDropdownTour, worksheetContextMenuTour, keyTipOverlayTour, printPreviewTour, optionsAccountTour, qatUndoRedoTour, titlebarWindowChromeTour, statusFooterTour, formulaBarNameBoxTour, formulaDiagnosticsTour);
    }

    private static string ResolveScreenshotTourOutputDirectory(string screenshotsRoot, string? requestedSubdirectory)
    {
        if (string.IsNullOrWhiteSpace(requestedSubdirectory))
            return screenshotsRoot;

        if (Path.IsPathRooted(requestedSubdirectory))
            throw new InvalidOperationException($"{ScreenshotTourOutputSubdirectoryEnvVar} must be a relative path under screenshots.");

        var root = Path.GetFullPath(screenshotsRoot);
        var resolved = Path.GetFullPath(Path.Combine(root, requestedSubdirectory));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{ScreenshotTourOutputSubdirectoryEnvVar} must stay under screenshots.");

        return resolved;
    }

    private async Task RunScreenshotTourAsync(
        string outputDir,
        RibbonScreenshotTourPlan? ribbonPlan,
        bool backstageTour,
        bool autoFilterFlyoutTour,
        bool homeNumberFormatDropdownTour,
        bool homeAlignmentNumberTour,
        bool homeBordersDropdownTour,
        bool worksheetContextMenuTour,
        bool keyTipOverlayTour,
        bool printPreviewTour,
        bool optionsAccountTour,
        bool qatUndoRedoTour,
        bool titlebarWindowChromeTour,
        bool statusFooterTour,
        bool formulaBarNameBoxTour,
        bool formulaDiagnosticsTour)
    {
        if (ribbonPlan is not null)
            await CaptureRibbonTourAsync(outputDir, ribbonPlan);

        if (backstageTour)
            await CaptureBackstageAsync(outputDir);

        if (autoFilterFlyoutTour)
            await CaptureAutoFilterFlyoutTourAsync(Path.Combine(outputDir, "autofilter-flyout-tour"));

        if (homeNumberFormatDropdownTour)
            await CaptureHomeNumberFormatDropdownTourAsync(Path.Combine(outputDir, "home-number-format-dropdown-tour"));

        if (homeAlignmentNumberTour)
            await CaptureHomeAlignmentNumberTourAsync(Path.Combine(outputDir, HomeAlignmentNumberTourOutputDirectoryName));

        if (homeBordersDropdownTour)
            await CaptureHomeBordersDropdownTourAsync(Path.Combine(outputDir, "home-borders-dropdown-tour"));

        if (worksheetContextMenuTour)
            await CaptureWorksheetContextMenuTourAsync(Path.Combine(outputDir, "worksheet-context-menu-tour"));

        if (keyTipOverlayTour)
            await CaptureKeyTipOverlayTourAsync(Path.Combine(outputDir, "keytip-overlay-tour"));

        if (printPreviewTour)
            await CapturePrintPreviewTourAsync(Path.Combine(outputDir, "print-preview-tour"));

        if (optionsAccountTour)
            await CaptureOptionsAccountTourAsync(Path.Combine(outputDir, OptionsAccountTourOutputDirectoryName));

        if (qatUndoRedoTour)
            await CaptureQatUndoRedoTourAsync(Path.Combine(outputDir, QatUndoRedoTourOutputDirectoryName));

        if (titlebarWindowChromeTour)
            await CaptureTitlebarWindowChromeTourAsync(Path.Combine(outputDir, TitlebarWindowChromeTourOutputDirectoryName));
        if (statusFooterTour)
            await CaptureStatusFooterTourAsync(Path.Combine(outputDir, StatusFooterTourOutputDirectoryName));

        if (formulaBarNameBoxTour)
            await CaptureFormulaBarNameBoxTourAsync(Path.Combine(outputDir, FormulaBarNameBoxTourOutputDirectoryName));

        if (formulaDiagnosticsTour)
            await CaptureFormulaDiagnosticsTourAsync(Path.Combine(outputDir, FormulaDiagnosticsTourOutputDirectoryName));

        _suppressClosePrompt = true;
        Application.Current.Shutdown();
    }

    private async Task CaptureBackstageAsync(string outputDir)
    {
        WindowState = WindowState.Normal;
        Width = 1100;
        Height = 768;
        await Task.Delay(800);

        ShowStartScreen();
        UpdateLayout();
        await Task.Delay(350);
        UpdateLayout();

        await CaptureCurrentWindowAsync(outputDir, "backstage_home", 760);
    }

    private async Task CaptureAutoFilterFlyoutTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteAutoFilterFlyoutTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1100;
        Height = 768;
        await Task.Delay(700);

        var headerCell = EnsureAutoFilterFlyoutTourContext();
        UpdateViewport();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await Task.Delay(250);

        if (_workbook.GetSheet(_currentSheetId) is not { } sheet ||
            CreateAutoFilterFlyoutDialog(sheet, headerCell, null, out var plan) is not { } dialog ||
            plan is null)
        {
            throw new InvalidOperationException("AutoFilter flyout tour could not create the live AutoFilter flyout.");
        }

        try
        {
            dialog.Show();
            dialog.Activate();
            dialog.UpdateLayout();
            await Task.Delay(350);
            dialog.UpdateLayout();
            await WaitForRibbonScreenshotRenderPassAsync();

            await CaptureElementAsync(dialog, outputDir, AutoFilterFlyoutTourCaptureFileName);
            ValidateAutoFilterFlyoutTourEvidence(outputDir);
            await WriteAutoFilterFlyoutTourManifestAsync(outputDir, dialog, plan);
        }
        catch
        {
            DeleteAutoFilterFlyoutTourEvidence(outputDir);
            throw;
        }
        finally
        {
            dialog.Close();
        }
    }

    private CellAddress EnsureAutoFilterFlyoutTourContext()
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
        {
            foreach (var candidate in _workbook.Sheets)
            {
                sheet = candidate;
                break;
            }
        }

        if (sheet is null)
            throw new InvalidOperationException("AutoFilter flyout tour requires an active worksheet.");

        _currentSheetId = sheet.Id;

        var headers = new[] { "score", "name", "date", "note" };
        object?[][] rows =
        [
            [1d, "North", "2026-06-01", "alpha"],
            [2d, "South", "2026-06-02", "beta"],
            [3d, "East", "2026-06-03", "gamma"],
            [4d, "West", "2026-06-04", "delta"],
            [null, "Blank score", "2026-06-05", "blank"]
        ];

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 4));
        foreach (var address in range.AllCells())
            sheet.ClearCell(address);

        for (var col = 0; col < headers.Length; col++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, (uint)(col + 1)), new TextValue(headers[col]));

        for (var row = 0; row < rows.Length; row++)
        {
            for (var col = 0; col < headers.Length; col++)
            {
                var address = new CellAddress(sheet.Id, (uint)(row + 2), (uint)(col + 1));
                switch (rows[row][col])
                {
                    case double number:
                        sheet.SetCell(address, new NumberValue(number));
                        break;
                    case string text:
                        sheet.SetCell(address, new TextValue(text));
                        break;
                    case null:
                        sheet.ClearCell(address);
                        break;
                }
            }
        }

        sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);
        sheet.FilterHiddenRows.Clear();
        sheet.HiddenRows.Clear();
        ClearRememberedAutoFilterCommand();

        var headerCell = range.Start;
        SetActiveCell(headerCell);
        if (SheetGrid is not null)
        {
            SheetGrid.SelectedRange = new GridRange(headerCell, headerCell);
            SheetGrid.SelectedRanges = null;
        }

        return headerCell;
    }

    private static void DeleteAutoFilterFlyoutTourEvidence(string outputDir)
    {
        foreach (var fileName in new[]
        {
            $"{AutoFilterFlyoutTourCaptureFileName}.png",
            AutoFilterFlyoutTourManifestFileName
        })
        {
            var path = Path.Combine(outputDir, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void ValidateAutoFilterFlyoutTourEvidence(string outputDir)
    {
        var path = Path.Combine(outputDir, $"{AutoFilterFlyoutTourCaptureFileName}.png");
        if (!File.Exists(path))
            throw new InvalidOperationException("AutoFilter flyout tour did not create the planned FreeX dropdown capture.");
    }

    private async Task CaptureHomeNumberFormatDropdownTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteHomeNumberFormatDropdownTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1100;
        Height = 768;
        await Task.Delay(700);

        var homeTab = RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == "Home");
        SelectRibbonTourTab(homeTab);
        NumberFormatBox.SelectedIndex = HomeNumberFormatDropdownPlanner.DefaultSelectionIndex;
        NumberFormatBox.Focus();
        NumberFormatBox.ApplyTemplate();
        NumberFormatBox.IsDropDownOpen = true;
        NumberFormatBox.UpdateLayout();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await Task.Delay(350);
        NumberFormatBox.UpdateLayout();

        try
        {
            var popupChild = FindOpenPopupChild(NumberFormatBox)
                ?? throw new InvalidOperationException("Home number format dropdown tour could not locate the open ComboBox popup.");

            await CaptureElementAsync(popupChild, outputDir, HomeNumberFormatDropdownTourCaptureFileName);
            ValidateHomeNumberFormatDropdownTourEvidence(outputDir);
            await WriteHomeNumberFormatDropdownTourManifestAsync(outputDir, popupChild);
        }
        catch
        {
            DeleteHomeNumberFormatDropdownTourEvidence(outputDir);
            throw;
        }
        finally
        {
            NumberFormatBox.IsDropDownOpen = false;
        }
    }

    private static FrameworkElement? FindOpenPopupChild(DependencyObject root)
    {
        if (root is Popup { IsOpen: true, Child: FrameworkElement child })
            return child;

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            var candidate = VisualTreeHelper.GetChild(root, i);
            var match = FindOpenPopupChild(candidate);
            if (match is not null)
                return match;
        }

        return null;
    }

    private static void DeleteHomeNumberFormatDropdownTourEvidence(string outputDir)
    {
        foreach (var fileName in new[]
        {
            $"{HomeNumberFormatDropdownTourCaptureFileName}.png",
            HomeNumberFormatDropdownTourManifestFileName
        })
        {
            var path = Path.Combine(outputDir, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void ValidateHomeNumberFormatDropdownTourEvidence(string outputDir)
    {
        var path = Path.Combine(outputDir, $"{HomeNumberFormatDropdownTourCaptureFileName}.png");
        if (!File.Exists(path))
            throw new InvalidOperationException("Home number format dropdown tour did not create the planned FreeX dropdown capture.");
    }

    private async Task CaptureHomeAlignmentNumberTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteHomeAlignmentNumberTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1180;
        Height = 768;
        await Task.Delay(700);

        var context = EnsureHomeAlignmentNumberTourContext();
        var captures = new List<HomeAlignmentNumberTourManifestCapture>();
        FormatCellsDialog? alignmentDialog = null;
        FormatCellsDialog? numberDialog = null;

        try
        {
            captures.Add(await CaptureHomeAlignmentNumberWindowStateAsync(
                outputDir,
                "alignment-grid",
                "freex_home_alignment_grid_commands",
                "window-full",
                "Home Alignment group focused with rendered left/center/right, top/middle/bottom, wrap, indent, rotation, and merged-center worksheet examples."));

            OpenRibbonContextMenu(OrientationPickerButton, OrientationPickerButton.ContextMenu);
            OrientationPickerButton.ContextMenu!.UpdateLayout();
            await Task.Delay(350);
            await WaitForRibbonScreenshotRenderPassAsync();
            await CaptureElementAsync(OrientationPickerButton.ContextMenu!, outputDir, "freex_home_alignment_orientation_menu_opened");
            captures.Add(CreateHomeAlignmentNumberTourCapture(
                "orientation-menu-opened",
                "freex_home_alignment_orientation_menu_opened",
                "orientation-menu",
                "RenderTargetBitmap-context-menu",
                OrientationPickerButton.ContextMenu!.ActualWidth,
                OrientationPickerButton.ContextMenu!.ActualHeight,
                "Production Orientation menu opened from the Home Alignment group."));
            OrientationPickerButton.ContextMenu!.IsOpen = false;

            SetSelectionRange(context.NumberRange, context.NumberRange.Start);
            RefreshToolbar();
            UpdateLayout();
            await Task.Delay(250);
            captures.Add(await CaptureHomeAlignmentNumberWindowStateAsync(
                outputDir,
                "number-format-grid",
                "freex_home_number_format_grid_commands",
                "window-full",
                "Home Number group focused with rendered Accounting, Percent, Short Date, and custom number format examples."));

            alignmentDialog = new FormatCellsDialog(
                new CellStyle
                {
                    HorizontalAlignment = FreeX.Core.Model.HorizontalAlignment.Distributed,
                    VerticalAlignment = FreeX.Core.Model.VerticalAlignment.Center,
                    WrapText = true,
                    ShrinkToFit = true,
                    IndentLevel = 2,
                    TextRotation = 45
                },
                FormatCellsDialogTab.Alignment)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            alignmentDialog.Show();
            alignmentDialog.Activate();
            alignmentDialog.UpdateLayout();
            await Task.Delay(450);
            await WaitForRibbonScreenshotRenderPassAsync();
            await CaptureWindowElementForScreenshotTourAsync(alignmentDialog, outputDir, "freex_home_alignment_format_cells_dialog");
            captures.Add(CreateHomeAlignmentNumberTourCapture(
                "format-cells-alignment-dialog",
                "freex_home_alignment_format_cells_dialog",
                "format-cells-dialog",
                "RenderTargetBitmap-format-cells-dialog",
                alignmentDialog.ActualWidth,
                alignmentDialog.ActualHeight,
                "Format Cells dialog opened directly to the Alignment tab with wrap, shrink, indent, rotation, and distributed alignment state."));
            alignmentDialog.Close();
            alignmentDialog = null;

            numberDialog = new FormatCellsDialog(
                new CellStyle
                {
                    NumberFormat = "[$-409]mmmm d, yyyy;@"
                },
                FormatCellsDialogTab.Number)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            numberDialog.Show();
            numberDialog.Activate();
            numberDialog.UpdateLayout();
            await Task.Delay(450);
            await WaitForRibbonScreenshotRenderPassAsync();
            await CaptureWindowElementForScreenshotTourAsync(numberDialog, outputDir, "freex_home_number_format_cells_dialog");
            captures.Add(CreateHomeAlignmentNumberTourCapture(
                "format-cells-number-dialog",
                "freex_home_number_format_cells_dialog",
                "format-cells-dialog",
                "RenderTargetBitmap-format-cells-dialog",
                numberDialog.ActualWidth,
                numberDialog.ActualHeight,
                "Format Cells dialog opened directly to the Number tab with a locale/custom date format scenario."));
            numberDialog.Close();
            numberDialog = null;

            ValidateHomeAlignmentNumberTourEvidence(outputDir, captures);
            await WriteHomeAlignmentNumberTourManifestAsync(outputDir, context, captures);
        }
        catch
        {
            DeleteHomeAlignmentNumberTourEvidence(outputDir);
            throw;
        }
        finally
        {
            if (OrientationPickerButton.ContextMenu is { IsOpen: true } menu)
                menu.IsOpen = false;
            alignmentDialog?.Close();
            numberDialog?.Close();
        }
    }

    private HomeAlignmentNumberTourContext EnsureHomeAlignmentNumberTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Home alignment/number tour requires an active worksheet.");

        _currentSheetId = sheet.Id;
        for (uint row = 1; row <= 9; row++)
        {
            for (uint col = 1; col <= 6; col++)
                sheet.ClearCell(new CellAddress(sheet.Id, row, col));
        }

        sheet.ColumnWidths[1] = 18;
        sheet.ColumnWidths[2] = 20;
        sheet.ColumnWidths[3] = 18;
        sheet.ColumnWidths[4] = 17;
        sheet.ColumnWidths[5] = 18;
        sheet.ColumnWidths[6] = 18;
        sheet.RowHeights[2] = 42;
        sheet.RowHeights[3] = 38;
        sheet.RowHeights[4] = 44;

        SetTourCell(sheet, 1, 1, new TextValue("Alignment"));
        SetTourCell(sheet, 1, 4, new TextValue("Number formats"));
        SetTourCell(sheet, 2, 1, new TextValue("Left / top"));
        SetTourCell(sheet, 2, 2, new TextValue("Centered with wrap text"));
        SetTourCell(sheet, 2, 3, new TextValue("Right / bottom"));
        SetTourCell(sheet, 3, 1, new TextValue("Indented text"));
        SetTourCell(sheet, 3, 2, new TextValue("Rotated"));
        SetTourCell(sheet, 4, 1, new TextValue("Merged & Centered"));
        SetTourCell(sheet, 2, 4, new NumberValue(1234.5));
        SetTourCell(sheet, 3, 4, new NumberValue(0.425));
        SetTourCell(sheet, 4, 4, new NumberValue(new DateTime(2026, 6, 10).ToOADate()));
        SetTourCell(sheet, 5, 4, new NumberValue(-1200.34));

        var headerRange = Range(sheet.Id, 1, 1, 1, 6);
        ApplyHomeAlignmentNumberTourStyle(headerRange, new StyleDiff(Bold: true, FillColor: new CellColor(217, 225, 242)));
        ApplyHomeAlignmentNumberTourStyle(Range(sheet.Id, 2, 1, 2, 1), new StyleDiff(HAlign: FreeX.Core.Model.HorizontalAlignment.Left, VAlign: FreeX.Core.Model.VerticalAlignment.Top));
        ApplyHomeAlignmentNumberTourStyle(Range(sheet.Id, 2, 2, 2, 2), new StyleDiff(HAlign: FreeX.Core.Model.HorizontalAlignment.Center, VAlign: FreeX.Core.Model.VerticalAlignment.Center, WrapText: true));
        ApplyHomeAlignmentNumberTourStyle(Range(sheet.Id, 2, 3, 2, 3), new StyleDiff(HAlign: FreeX.Core.Model.HorizontalAlignment.Right, VAlign: FreeX.Core.Model.VerticalAlignment.Bottom));
        ApplyHomeAlignmentNumberTourStyle(Range(sheet.Id, 3, 1, 3, 1), new StyleDiff(IndentLevel: 2));
        ApplyHomeAlignmentNumberTourStyle(Range(sheet.Id, 3, 2, 3, 2), new StyleDiff(TextRotation: 45));
        ApplyHomeAlignmentNumberTourStyle(Range(sheet.Id, 2, 4, 2, 4), new StyleDiff(NumberFormat: HomeNumberFormatDropdownPlanner.AccountingNumberFormatCode));
        ApplyHomeAlignmentNumberTourStyle(Range(sheet.Id, 3, 4, 3, 4), new StyleDiff(NumberFormat: "0%"));
        ApplyHomeAlignmentNumberTourStyle(Range(sheet.Id, 4, 4, 4, 4), new StyleDiff(NumberFormat: "m/d/yyyy"));
        ApplyHomeAlignmentNumberTourStyle(Range(sheet.Id, 5, 4, 5, 4), new StyleDiff(NumberFormat: "[Red]#,##0.00;[Blue]-#,##0.00;0"));

        var mergeRange = Range(sheet.Id, 4, 1, 4, 3);
        if (!TryExecuteCommand(CreateMergeAndCenterCommand(mergeRange), "Merge & Center"))
            throw new InvalidOperationException("Home alignment/number tour could not create the Merge & Center sample.");

        var alignmentRange = Range(sheet.Id, 2, 1, 4, 3);
        var numberRange = Range(sheet.Id, 2, 4, 5, 4);
        SetSelectionRange(alignmentRange, alignmentRange.Start);
        EnsureCellVisible(alignmentRange.Start);
        RefreshToolbar();
        RefreshStatusBar();
        UpdateViewport();
        UpdateLayout();

        return new HomeAlignmentNumberTourContext(
            SheetName: sheet.Name,
            AlignmentRange: alignmentRange,
            NumberRange: numberRange,
            SampleFormats:
            [
                HomeNumberFormatDropdownPlanner.AccountingNumberFormatCode,
                "0%",
                "m/d/yyyy",
                "[Red]#,##0.00;[Blue]-#,##0.00;0"
            ]);
    }

    private static GridRange Range(SheetId sheetId, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheetId, startRow, startCol), new CellAddress(sheetId, endRow, endCol));

    private static void SetTourCell(Sheet sheet, uint row, uint col, ScalarValue value) =>
        sheet.SetCell(new CellAddress(sheet.Id, row, col), value);

    private void ApplyHomeAlignmentNumberTourStyle(GridRange range, StyleDiff diff)
    {
        if (!TryExecuteApplyStyle(range, diff, "Apply Style"))
            throw new InvalidOperationException($"Home alignment/number tour could not apply style to {range}.");
    }

    private async Task<HomeAlignmentNumberTourManifestCapture> CaptureHomeAlignmentNumberWindowStateAsync(
        string outputDir,
        string state,
        string fileName,
        string surface,
        string evidencePurpose)
    {
        RefreshToolbar();
        RefreshStatusBar();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await CaptureCurrentWindowAsync(outputDir, fileName, 760);
        return CreateHomeAlignmentNumberTourCapture(
            state,
            fileName,
            surface,
            "RenderTargetBitmap-main-window",
            ActualWidth,
            Math.Min(ActualHeight, 760),
            evidencePurpose);
    }

    private static HomeAlignmentNumberTourManifestCapture CreateHomeAlignmentNumberTourCapture(
        string state,
        string fileName,
        string surface,
        string captureMethod,
        double captureLogicalWidth,
        double captureLogicalHeight,
        string evidencePurpose) =>
        new(
            CaptureKey: $"interactive:home-alignment-number:{state}",
            PairKey: $"interactive:home-alignment-number:{state}",
            ScenarioId: "home:alignment-number",
            State: state,
            Surface: surface,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CounterpartFileName: $"interactive_home_alignment_number_{state.Replace('-', '_')}.png",
            CaptureMethod: captureMethod,
            CaptureLogicalWidth: captureLogicalWidth,
            CaptureLogicalHeight: captureLogicalHeight,
            EvidencePurpose: evidencePurpose);

    private static void DeleteHomeAlignmentNumberTourEvidence(string outputDir)
    {
        foreach (var fileName in new[]
        {
            "freex_home_alignment_grid_commands.png",
            "freex_home_alignment_orientation_menu_opened.png",
            "freex_home_number_format_grid_commands.png",
            "freex_home_alignment_format_cells_dialog.png",
            "freex_home_number_format_cells_dialog.png",
            HomeAlignmentNumberTourManifestFileName
        })
        {
            var path = Path.Combine(outputDir, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void ValidateHomeAlignmentNumberTourEvidence(
        string outputDir,
        IReadOnlyCollection<HomeAlignmentNumberTourManifestCapture> captures)
    {
        foreach (var capture in captures)
        {
            var path = Path.Combine(outputDir, capture.OutputFileName);
            if (!File.Exists(path))
                throw new InvalidOperationException($"Home alignment/number tour did not create {capture.OutputFileName}.");
        }
    }

    private async Task CaptureHomeBordersDropdownTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteHomeBordersDropdownTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1100;
        Height = 768;
        await Task.Delay(700);

        var homeTab = RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == "Home");
        SelectRibbonTourTab(homeTab);
        BordersMenuButton.Focus();
        BordersMenuButton.UpdateLayout();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await Task.Delay(250);

        var menu = BordersMenuButton.ContextMenu
            ?? throw new InvalidOperationException("Home Borders dropdown tour could not locate the Borders context menu.");

        try
        {
            menu.PlacementTarget = BordersMenuButton;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
            menu.UpdateLayout();
            await Task.Delay(350);
            menu.UpdateLayout();
            await WaitForRibbonScreenshotRenderPassAsync();

            await CaptureElementAsync(menu, outputDir, HomeBordersDropdownTourCaptureFileName);
            ValidateHomeBordersDropdownTourEvidence(outputDir);
            await WriteHomeBordersDropdownTourManifestAsync(outputDir, menu);
        }
        catch
        {
            DeleteHomeBordersDropdownTourEvidence(outputDir);
            throw;
        }
        finally
        {
            menu.IsOpen = false;
        }
    }

    private static void DeleteHomeBordersDropdownTourEvidence(string outputDir)
    {
        foreach (var fileName in new[]
        {
            $"{HomeBordersDropdownTourCaptureFileName}.png",
            HomeBordersDropdownTourManifestFileName
        })
        {
            var path = Path.Combine(outputDir, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void ValidateHomeBordersDropdownTourEvidence(string outputDir)
    {
        var path = Path.Combine(outputDir, $"{HomeBordersDropdownTourCaptureFileName}.png");
        if (!File.Exists(path))
            throw new InvalidOperationException("Home Borders dropdown tour did not create the planned FreeX dropdown capture.");
    }

    private async Task CaptureWorksheetContextMenuTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteWorksheetContextMenuTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1100;
        Height = 768;
        await Task.Delay(700);

        var address = EnsureWorksheetContextMenuTourContext();
        UpdateViewport();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await Task.Delay(250);

        ContextMenu? menu = null;
        try
        {
            OnGridContextMenuRequested(address, GetKeyboardContextMenuGridPoint(address));
            await Task.Delay(350);
            menu = SheetGrid.ContextMenu
                ?? throw new InvalidOperationException("Worksheet context menu tour could not locate the open context menu.");
            menu.UpdateLayout();
            await WaitForRibbonScreenshotRenderPassAsync();

            await CaptureElementAsync(menu, outputDir, WorksheetContextMenuTourCaptureFileName);
            ValidateWorksheetContextMenuTourEvidence(outputDir);
            await WriteWorksheetContextMenuTourManifestAsync(outputDir, menu, address);
        }
        catch
        {
            DeleteWorksheetContextMenuTourEvidence(outputDir);
            throw;
        }
        finally
        {
            if (menu is not null)
                menu.IsOpen = false;
        }
    }

    private CellAddress EnsureWorksheetContextMenuTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Worksheet context menu tour requires an active worksheet.");

        _currentSheetId = sheet.Id;
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new TextValue("Worksheet context menu"));
        sheet.ClearCell(new CellAddress(sheet.Id, 1, 2));
        SetActiveCell(address);
        if (SheetGrid is not null)
        {
            SheetGrid.SelectedRange = new GridRange(address, address);
            SheetGrid.SelectedRanges = null;
        }

        return address;
    }

    private static void DeleteWorksheetContextMenuTourEvidence(string outputDir)
    {
        foreach (var fileName in new[]
        {
            $"{WorksheetContextMenuTourCaptureFileName}.png",
            WorksheetContextMenuTourManifestFileName
        })
        {
            var path = Path.Combine(outputDir, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void ValidateWorksheetContextMenuTourEvidence(string outputDir)
    {
        var path = Path.Combine(outputDir, $"{WorksheetContextMenuTourCaptureFileName}.png");
        if (!File.Exists(path))
            throw new InvalidOperationException("Worksheet context menu tour did not create the planned FreeX context menu capture.");
    }

    private async Task CapturePrintPreviewTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeletePrintPreviewTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1180;
        Height = 768;
        await Task.Delay(700);

        var sheet = EnsurePrintPreviewTourContext();
        UpdateViewport();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await Task.Delay(250);

        OpenPrintBackstage();
        UpdateLayout();
        await Task.Delay(350);
        await WaitForRibbonScreenshotRenderPassAsync();
        await CaptureCurrentWindowAsync(outputDir, "freex_print_backstage_file_print_entry", 760);

        var totalPages = Math.Max(1, PrintRenderer.RenderWorksheet(_workbook, _currentSheetId, _viewportService).Pages.Count);
        var initialPreview = CreatePrintPreviewTourDialog();
        try
        {
            initialPreview.Show();
            initialPreview.Activate();
            initialPreview.UpdateLayout();
            await Task.Delay(550);
            await WaitForRibbonScreenshotRenderPassAsync();
            await CaptureWindowElementForScreenshotTourAsync(initialPreview, outputDir, "freex_print_preview_ctrlp_entry_opened");
        }
        finally
        {
            initialPreview.Close();
        }

        var dialog = CreatePrintPreviewTourDialog();
        var closedViaEscape = false;
        var focusReturned = false;
        try
        {
            dialog.Show();
            dialog.Activate();
            dialog.UpdateLayout();
            await Task.Delay(550);
            await WaitForRibbonScreenshotRenderPassAsync();

            await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, "freex_print_preview_toolbar_first_page");

            var pageNumberBox = FindDescendantByAutomationId<TextBox>(dialog, "PrintPreviewPageNumberBox");
            if (pageNumberBox is not null && totalPages > 1)
            {
                pageNumberBox.Text = totalPages.ToString(System.Globalization.CultureInfo.InvariantCulture);
                pageNumberBox.Focus();
                Keyboard.Focus(pageNumberBox);
                NavigationCommands.GoToPage.Execute(null, pageNumberBox);
                await Task.Delay(350);
                await WaitForRibbonScreenshotRenderPassAsync();
                await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, "freex_print_preview_toolbar_last_page");
            }

            var zoomBox = FindDescendantByAutomationId<ComboBox>(dialog, "PrintPreviewZoomBox");
            if (zoomBox is not null)
            {
                zoomBox.SelectedItem = UiText.Get("PrintPreview_ZoomPageWidth");
                await Task.Delay(350);
                await WaitForRibbonScreenshotRenderPassAsync();
                await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, "freex_print_preview_zoom_settings_summary");
            }

            closedViaEscape = ClosePrintPreviewTourDialogWithEscape(dialog);
            await Task.Delay(350);
            Activate();
            SsPrintPreviewButton.Focus();
            Keyboard.Focus(SsPrintPreviewButton);
            focusReturned = IsActive && Keyboard.FocusedElement == SsPrintPreviewButton;
            await CaptureCurrentWindowAsync(outputDir, "freex_print_preview_closed_focus_return", 760);
        }
        finally
        {
            if (dialog.IsVisible)
                dialog.Close();
        }

        ValidatePrintPreviewTourEvidence(outputDir, totalPages);
        await WritePrintPreviewTourManifestAsync(outputDir, sheet, totalPages, closedViaEscape, focusReturned);
    }

    private PrintPreviewDialog CreatePrintPreviewTourDialog()
    {
        var doc = PrintRenderer.RenderWorksheet(_workbook, _currentSheetId, _viewportService);
        var sheet = _workbook.GetSheet(_currentSheetId);
        var settings = sheet is null
            ? new PrintSettingsPlan([UiText.Get("MainWindowPrintSettings_ActiveSheet")])
            : PrintSettingsPlanner.Build(sheet);
        return new PrintPreviewDialog(
            _workbook.Name,
            doc,
            settings,
            showMargins: () => PageMarginsBtn_Click(this, new RoutedEventArgs()),
            showPageSetup: () => PageSetupDialogBtn_Click(this, new RoutedEventArgs()),
            refreshPreviewWithSettings: BuildActiveSheetPrintPreview,
            sheetId: _currentSheetId,
            sheet: sheet,
            executeCommand: cmd => TryExecuteCommand(cmd, "Print Settings"))
        {
            Owner = this,
            Width = 2600,
            Height = 820,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
    }

    private Sheet EnsurePrintPreviewTourContext()
    {
        var sheet = _workbook.GetSheet(_currentSheetId) ?? _workbook.Sheets.FirstOrDefault();
        if (sheet is null)
            throw new InvalidOperationException("Print Preview tour requires an active worksheet.");

        _currentSheetId = sheet.Id;
        sheet.PageOrientation = WorksheetPageOrientation.Landscape;
        sheet.PaperSize = WorksheetPaperSize.Letter;
        sheet.PrintGridlines = true;
        sheet.PrintHeadings = true;
        sheet.ScaleToFit = new WorksheetScaleToFit(100, 1, 0);

        for (uint row = 1; row <= 140; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue(row == 1 ? "Print Preview Tour" : $"Line {row - 1:000}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(row == 1 ? "State" : $"Toolbar navigation sample {row - 1:000}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(row));
        }

        var activeCell = new CellAddress(sheet.Id, 1, 1);
        SetActiveCell(activeCell);
        if (SheetGrid is not null)
        {
            SheetGrid.SelectedRange = new GridRange(activeCell, activeCell);
            SheetGrid.SelectedRanges = null;
        }

        return sheet;
    }

    private static void DeletePrintPreviewTourEvidence(string outputDir)
    {
        foreach (var fileName in PrintPreviewTourExpectedFileNames(includeLastPage: true).Append(PrintPreviewTourManifestFileName))
        {
            var path = Path.Combine(outputDir, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void ValidatePrintPreviewTourEvidence(string outputDir, int totalPages)
    {
        var missing = PrintPreviewTourExpectedFileNames(totalPages > 1)
            .Where(fileName => !File.Exists(Path.Combine(outputDir, fileName)))
            .ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Print Preview tour did not capture expected evidence: {string.Join(", ", missing)}.");
    }

    private static IReadOnlyList<string> PrintPreviewTourExpectedFileNames(bool includeLastPage)
    {
        var files = new List<string>
        {
            "freex_print_backstage_file_print_entry.png",
            "freex_print_preview_ctrlp_entry_opened.png",
            "freex_print_preview_toolbar_first_page.png",
            "freex_print_preview_zoom_settings_summary.png",
            "freex_print_preview_closed_focus_return.png"
        };
        if (includeLastPage)
            files.Insert(3, "freex_print_preview_toolbar_last_page.png");

        return files;
    }

    private async Task CaptureWindowElementForScreenshotTourAsync(Window window, string outputDir, string fileName)
    {
        await EnsureWindowForegroundForScreenshotTourAsync(window, $"capturing {fileName}.png");
        await CaptureElementAsync(window, outputDir, fileName);
        AssertWindowForegroundForScreenshotTour(window, $"saved {fileName}.png");
    }

    private static bool ClosePrintPreviewTourDialogWithEscape(PrintPreviewDialog dialog)
    {
        var closeButton = FindDescendantByAutomationId<Button>(dialog, "PrintPreviewCloseButton");
        if (closeButton?.IsCancel != true)
            return false;

        closeButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        return !dialog.IsVisible;
    }

    private async Task CaptureOptionsAccountTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteOptionsAccountTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1120;
        Height = 768;
        await Task.Delay(700);

        ShowStartScreen();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await Task.Delay(350);

        SsAccountNavBtn.Focus();
        Keyboard.Focus(SsAccountNavBtn);
        await CaptureCurrentWindowAsync(outputDir, "freex_account_backstage_entry_focused", 760);

        var accountPlan = LocalAccountPlanner.Create(
            _options,
            _currentFilePath,
            _workbook.Name,
            workbook: _workbook,
            hasSelection: SheetGrid.SelectedRange is not null);
        var accountMessageCapture = CaptureOwnedNativeDialogWhenShownAsync(
            UiText.Get("DeferredCommand_LocalAccount_Title"),
            outputDir,
            "freex_account_local_account_message");
        SsAccountBtn_Click(SsAccountNavBtn, new RoutedEventArgs(ButtonBase.ClickEvent, SsAccountNavBtn));
        var accountMessage = await accountMessageCapture;

        Activate();
        SsAccountNavBtn.Focus();
        Keyboard.Focus(SsAccountNavBtn);
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await CaptureCurrentWindowAsync(outputDir, "freex_account_backstage_focus_return", 760);

        var optionCaptures = new List<OptionsAccountTourManifestCapture>();
        var dialog = new OptionsDialog(_options, _workbook.DisabledFormulaErrorCodes)
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowActivated = true
        };
        bool categoryListFocused;
        bool closedViaCancelEquivalent;
        bool focusReturned;
        try
        {
            dialog.Show();
            dialog.Activate();
            dialog.UpdateLayout();
            await dialog.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(450);

            var categories = FindDescendantByAutomationId<ListBox>(dialog, "OptionsCategoryList")
                ?? throw new InvalidOperationException("Options Account tour could not find the Options category list.");

            categories.Focus();
            Keyboard.Focus(categories);
            categoryListFocused = Keyboard.FocusedElement == categories;
            optionCaptures.Add(await CaptureOptionsDialogCategoryAsync(
                dialog,
                categories,
                outputDir,
                0,
                "options:default-category-list",
                "default-general",
                "freex_options_default_general_category_list",
                "Default Options dialog opens on General with the category list focused and OK/Cancel visible."));

            optionCaptures.Add(await CaptureOptionsDialogCategoryAsync(
                dialog,
                categories,
                outputDir,
                1,
                "options:category-navigation",
                "formulas",
                "freex_options_formulas_category_navigation",
                "Category navigation selects Formulas and shows calculation/error-checking options."));

            optionCaptures.Add(await CaptureOptionsDialogCategoryAsync(
                dialog,
                categories,
                outputDir,
                8,
                "options:category-navigation",
                "quick-access-toolbar",
                "freex_options_quick_access_toolbar_category_navigation",
                "Category navigation selects Quick Access Toolbar and shows command-list customization controls."));

            optionCaptures.Add(await CaptureOptionsDialogCategoryAsync(
                dialog,
                categories,
                outputDir,
                11,
                "options:category-navigation",
                "view",
                "freex_options_view_category_navigation",
                "Category navigation selects View and shows formula-bar view toggles."));

            closedViaCancelEquivalent = CloseOptionsTourDialogWithCancel(dialog);
        }
        finally
        {
            if (dialog.IsVisible)
                dialog.Close();
        }

        Activate();
        ShowStartScreen();
        SsOptionsNavBtn.Focus();
        Keyboard.Focus(SsOptionsNavBtn);
        focusReturned = IsActive && Keyboard.FocusedElement == SsOptionsNavBtn;
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await CaptureCurrentWindowAsync(outputDir, "freex_options_cancel_focus_return", 760);

        ValidateOptionsAccountTourEvidence(outputDir);
        await WriteOptionsAccountTourManifestAsync(
            outputDir,
            accountPlan,
            accountMessage,
            optionCaptures,
            categoryListFocused,
            closedViaCancelEquivalent,
            focusReturned);
    }

    private async Task<OptionsAccountTourManifestCapture> CaptureOptionsDialogCategoryAsync(
        OptionsDialog dialog,
        ListBox categories,
        string outputDir,
        int selectedIndex,
        string captureKey,
        string state,
        string fileName,
        string evidenceSummary)
    {
        if (selectedIndex < 0 || selectedIndex >= categories.Items.Count)
            throw new InvalidOperationException($"Options Account tour category index {selectedIndex} is outside the category list.");

        categories.SelectedIndex = selectedIndex;
        categories.Focus();
        Keyboard.Focus(categories);
        dialog.UpdateLayout();
        await dialog.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(250);
        await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, fileName);

        var categoryName = categories.Items[selectedIndex] is ListBoxItem item
            ? item.Content?.ToString() ?? state
            : state;

        return new OptionsAccountTourManifestCapture(
            CaptureKey: captureKey,
            PairKey: $"interactive:options-account:{state}",
            ScenarioId: "options-account:options-dialog",
            State: state,
            Surface: "Options dialog",
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: "RenderTargetBitmap-options-dialog-window",
            EvidenceSummary: evidenceSummary,
            CategoryName: categoryName,
            CategoryIndex: selectedIndex,
            FocusedElementAutomationId: Keyboard.FocusedElement is DependencyObject focusedElement
                ? AutomationProperties.GetAutomationId(focusedElement)
                : null,
            CaptureLogicalWidth: dialog.ActualWidth,
            CaptureLogicalHeight: dialog.ActualHeight);
    }

    private static bool CloseOptionsTourDialogWithCancel(OptionsDialog dialog)
    {
        var cancelButton = FindDescendantByAutomationId<Button>(dialog, "OptionsCancelButton");
        if (cancelButton?.IsCancel != true)
            return false;

        dialog.Close();
        return !dialog.IsVisible;
    }

    private async Task<OptionsAccountTourManifestCapture> CaptureOwnedNativeDialogWhenShownAsync(
        string caption,
        string outputDir,
        string fileName)
    {
        var owner = new WindowInteropHelper(this).Handle;
        if (owner == IntPtr.Zero)
            throw new InvalidOperationException("Options Account tour could not resolve the FreeX owner window handle.");

        return await Task.Run(() =>
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            IntPtr dialogHandle;
            do
            {
                dialogHandle = FindOwnedNativeWindow(owner, caption);
                if (dialogHandle != IntPtr.Zero)
                    break;

                Task.Delay(100).GetAwaiter().GetResult();
            }
            while (DateTime.UtcNow < deadline);

            if (dialogHandle == IntPtr.Zero)
                throw new InvalidOperationException($"Options Account tour did not find the owned native dialog '{caption}'.");

            var size = CaptureNativeWindow(dialogHandle, outputDir, fileName);
            PostMessage(dialogHandle, 0x0010, IntPtr.Zero, IntPtr.Zero);

            return new OptionsAccountTourManifestCapture(
                CaptureKey: "account:local-account-message:opened",
                PairKey: "interactive:options-account:local-account-message",
                ScenarioId: "options-account:account-message",
                State: "local-account-message",
                Surface: "Account owned native message",
                FileName: fileName,
                OutputFileName: $"{fileName}.png",
                CaptureMethod: "PrintWindow-owned-native-dialog",
                EvidenceSummary: "Account command opens the FreeX-owned local-account information message with explicit Microsoft 365 sign-in/cloud/coauthoring exclusion.",
                CategoryName: null,
                CategoryIndex: null,
                FocusedElementAutomationId: null,
                CaptureLogicalWidth: size.Width,
                CaptureLogicalHeight: size.Height);
        });
    }

    private static IntPtr FindOwnedNativeWindow(IntPtr owner, string caption)
    {
        var result = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd) || GetWindow(hWnd, 4) != owner)
                return true;

            var title = GetNativeWindowTitle(hWnd);
            if (!string.Equals(title, caption, StringComparison.CurrentCulture))
                return true;

            result = hWnd;
            return false;
        }, IntPtr.Zero);

        return result;
    }

    private static string GetNativeWindowTitle(IntPtr hWnd)
    {
        var length = GetWindowTextLength(hWnd);
        if (length <= 0)
            return string.Empty;

        var builder = new StringBuilder(length + 1);
        _ = GetWindowText(hWnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private static OptionsAccountTourNativeCaptureSize CaptureNativeWindow(IntPtr hWnd, string outputDir, string fileName)
    {
        if (!GetWindowRect(hWnd, out var rect))
            throw new InvalidOperationException($"Options Account tour could not read native window bounds for {fileName}.png.");

        var width = Math.Max(1, rect.Right - rect.Left);
        var height = Math.Max(1, rect.Bottom - rect.Top);
        var windowDc = GetWindowDC(hWnd);
        if (windowDc == IntPtr.Zero)
            throw new InvalidOperationException($"Options Account tour could not acquire native window DC for {fileName}.png.");

        var memoryDc = IntPtr.Zero;
        var bitmap = IntPtr.Zero;
        var oldBitmap = IntPtr.Zero;
        try
        {
            memoryDc = CreateCompatibleDC(windowDc);
            bitmap = CreateCompatibleBitmap(windowDc, width, height);
            if (memoryDc == IntPtr.Zero || bitmap == IntPtr.Zero)
                throw new InvalidOperationException($"Options Account tour could not allocate native capture bitmap for {fileName}.png.");

            oldBitmap = SelectObject(memoryDc, bitmap);
            if (!PrintWindow(hWnd, memoryDc, 0))
                throw new InvalidOperationException($"Options Account tour PrintWindow failed for {fileName}.png.");

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                bitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            var path = Path.Combine(outputDir, $"{fileName}.png");
            using var stream = File.Create(path);
            encoder.Save(stream);
            return new OptionsAccountTourNativeCaptureSize(width, height);
        }
        finally
        {
            if (oldBitmap != IntPtr.Zero)
                SelectObject(memoryDc, oldBitmap);
            if (bitmap != IntPtr.Zero)
                DeleteObject(bitmap);
            if (memoryDc != IntPtr.Zero)
                DeleteDC(memoryDc);
            ReleaseDC(hWnd, windowDc);
        }
    }

    private static void DeleteOptionsAccountTourEvidence(string outputDir)
    {
        foreach (var fileName in OptionsAccountTourExpectedFileNames().Append(OptionsAccountTourManifestFileName))
        {
            var path = Path.Combine(outputDir, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void ValidateOptionsAccountTourEvidence(string outputDir)
    {
        var missing = OptionsAccountTourExpectedFileNames()
            .Where(fileName => !File.Exists(Path.Combine(outputDir, fileName)))
            .ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Options Account tour did not capture expected evidence: {string.Join(", ", missing)}.");
    }

    private static IReadOnlyList<string> OptionsAccountTourExpectedFileNames() =>
    [
        "freex_account_backstage_entry_focused.png",
        "freex_account_local_account_message.png",
        "freex_account_backstage_focus_return.png",
        "freex_options_default_general_category_list.png",
        "freex_options_formulas_category_navigation.png",
        "freex_options_quick_access_toolbar_category_navigation.png",
        "freex_options_view_category_navigation.png",
        "freex_options_cancel_focus_return.png"
    ];

    private static T? FindDescendantByAutomationId<T>(DependencyObject root, string automationId)
        where T : FrameworkElement
    {
        if (root is T element && AutomationProperties.GetAutomationId(element) == automationId)
            return element;

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            var match = FindDescendantByAutomationId<T>(child, automationId);
            if (match is not null)
                return match;
        }

        return null;
    }

    private static Button? FindDescendantButtonByContent(DependencyObject root, string content)
    {
        if (root is Button button && string.Equals(button.Content?.ToString(), content, StringComparison.Ordinal))
            return button;

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            var match = FindDescendantButtonByContent(child, content);
            if (match is not null)
                return match;
        }

        return null;
    }

    private async Task CaptureQatUndoRedoTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteQatUndoRedoTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1100;
        Height = 768;
        await Task.Delay(700);

        var address = EnsureQatUndoRedoTourContext();
        var captures = new List<QatUndoRedoTourManifestCapture>();

        try
        {
            captures.Add(await CaptureQatUndoRedoWindowStateAsync(
                outputDir,
                "fresh-disabled",
                "freex_qat_initial_disabled",
                address));

            ExecuteQatUndoRedoTourMutation(address);
            captures.Add(await CaptureQatUndoRedoWindowStateAsync(
                outputDir,
                "after-edit-undo-enabled",
                "freex_qat_after_edit_undo_enabled",
                address));

            captures.Add(await CaptureQatUndoRedoHistoryMenuAsync(
                outputDir,
                QuickAccessToolbarCommandIds.Undo,
                "undo-history-opened",
                "freex_qat_undo_history_menu_opened",
                address));

            if (!ExecuteUndo())
                throw new InvalidOperationException("QAT undo/redo tour could not execute the first Undo action.");

            captures.Add(await CaptureQatUndoRedoWindowStateAsync(
                outputDir,
                "after-one-undo-redo-enabled",
                "freex_qat_after_one_undo_redo_enabled",
                address));

            if (!ExecuteUndo())
                throw new InvalidOperationException("QAT undo/redo tour could not execute the second Undo action.");

            captures.Add(await CaptureQatUndoRedoWindowStateAsync(
                outputDir,
                "after-two-undos-redo-menu-ready",
                "freex_qat_after_two_undos_redo_menu_ready",
                address));

            captures.Add(await CaptureQatUndoRedoHistoryMenuAsync(
                outputDir,
                QuickAccessToolbarCommandIds.Redo,
                "redo-history-opened",
                "freex_qat_redo_history_menu_opened",
                address));

            if (!ExecuteRedo() || !ExecuteRedo())
                throw new InvalidOperationException("QAT undo/redo tour could not execute both Redo actions.");

            captures.Add(await CaptureQatUndoRedoWindowStateAsync(
                outputDir,
                "after-redo-restored",
                "freex_qat_after_redo_restored",
                address));

            ValidateQatUndoRedoTourEvidence(outputDir, captures);
            await WriteQatUndoRedoTourManifestAsync(outputDir, address, captures);
        }
        catch
        {
            DeleteQatUndoRedoTourEvidence(outputDir);
            throw;
        }
    }

    private CellAddress EnsureQatUndoRedoTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("QAT undo/redo tour requires an active worksheet.");

        _currentSheetId = sheet.Id;
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.ClearCell(address);
        sheet.ClearCell(new CellAddress(sheet.Id, 1, 2));
        sheet.ClearCell(new CellAddress(sheet.Id, 2, 1));
        SetActiveCell(address);
        if (SheetGrid is not null)
        {
            SheetGrid.SelectedRange = new GridRange(address, address);
            SheetGrid.SelectedRanges = null;
            SheetGrid.Focus();
        }

        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
        return address;
    }

    private void ExecuteQatUndoRedoTourMutation(CellAddress address)
    {
        var edit = (address, Cell.FromValue(new TextValue("QAT undo redo proof")));
        if (!TryExecuteEditCells([edit], "Edit Cell", out var editOutcome))
            throw new InvalidOperationException(editOutcome.ErrorMessage ?? "QAT undo/redo tour cell edit failed.");

        var styleRange = new GridRange(address, address);
        var diff = new StyleDiff(FillColor: new CellColor(255, 242, 204), Bold: true);
        if (!TryExecuteApplyStyle(styleRange, diff, "Apply Style"))
            throw new InvalidOperationException("QAT undo/redo tour style mutation failed.");

        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private async Task<QatUndoRedoTourManifestCapture> CaptureQatUndoRedoWindowStateAsync(
        string outputDir,
        string state,
        string fileName,
        CellAddress address)
    {
        RefreshToolbar();
        RefreshStatusBar();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await CaptureCurrentWindowAsync(outputDir, fileName, 760);
        return CreateQatUndoRedoTourCapture(state, "window", fileName, address, "RenderTargetBitmap-window-full", ActualWidth, Math.Min(ActualHeight, 760), []);
    }

    private async Task<QatUndoRedoTourManifestCapture> CaptureQatUndoRedoHistoryMenuAsync(
        string outputDir,
        string commandId,
        string state,
        string fileName,
        CellAddress address)
    {
        var historyButton = FindName(GetQuickAccessHistoryButtonName(commandId)) as ButtonBase
            ?? throw new InvalidOperationException($"QAT undo/redo tour could not find history button for '{commandId}'.");
        var menu = CreateQuickAccessHistoryMenu(commandId, historyButton);
        try
        {
            menu.IsOpen = true;
            menu.UpdateLayout();
            await Task.Delay(350);
            menu.UpdateLayout();
            await WaitForRibbonScreenshotRenderPassAsync();

            await CaptureElementAsync(menu, outputDir, fileName);
            var menuHeaders = menu.Items
                .OfType<MenuItem>()
                .Select(item => item.Header?.ToString() ?? string.Empty)
                .Where(header => !string.IsNullOrWhiteSpace(header))
                .ToArray();
            return CreateQatUndoRedoTourCapture(state, "history-menu", fileName, address, "RenderTargetBitmap-qat-history-context-menu", menu.ActualWidth, menu.ActualHeight, menuHeaders);
        }
        finally
        {
            menu.IsOpen = false;
        }
    }

    private QatUndoRedoTourManifestCapture CreateQatUndoRedoTourCapture(
        string state,
        string surface,
        string fileName,
        CellAddress address,
        string captureMethod,
        double logicalWidth,
        double logicalHeight,
        IReadOnlyList<string> menuHeaders)
    {
        var sheet = _workbook.GetSheet(address.Sheet);
        var cell = sheet?.GetCell(address);
        var style = cell is null ? _workbook.GetStyle(StyleId.Default) : _workbook.GetStyle(cell.StyleId);
        var undoButton = GetQuickAccessToolbarButton(QuickAccessToolbarCommandIds.Undo);
        var redoButton = GetQuickAccessToolbarButton(QuickAccessToolbarCommandIds.Redo);
        var undoHistoryButton = FindName(GetQuickAccessHistoryButtonName(QuickAccessToolbarCommandIds.Undo)) as ButtonBase;
        var redoHistoryButton = FindName(GetQuickAccessHistoryButtonName(QuickAccessToolbarCommandIds.Redo)) as ButtonBase;
        var undoHistory = GetQuickAccessHistoryEntries(QuickAccessToolbarCommandIds.Undo)
            .Select(entry => entry.Label)
            .ToArray();
        var redoHistory = GetQuickAccessHistoryEntries(QuickAccessToolbarCommandIds.Redo)
            .Select(entry => entry.Label)
            .ToArray();

        return new QatUndoRedoTourManifestCapture(
            CaptureKey: $"interactive:qat-undo-redo:{state}",
            PairKey: $"interactive:qat-undo-redo:{state}",
            ScenarioId: "qat:undo-redo",
            State: state,
            Surface: surface,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: captureMethod,
            CaptureLogicalWidth: logicalWidth,
            CaptureLogicalHeight: logicalHeight,
            UndoButtonEnabled: undoButton?.IsEnabled == true,
            UndoHistoryButtonEnabled: undoHistoryButton?.IsEnabled == true,
            RedoButtonEnabled: redoButton?.IsEnabled == true,
            RedoHistoryButtonEnabled: redoHistoryButton?.IsEnabled == true,
            CanUndo: _commandBus.CanUndo(_workbook.Id),
            CanRedo: _commandBus.CanRedo(_workbook.Id),
            ActiveCell: address.ToA1(),
            ActiveCellText: FormatQatUndoRedoTourValue(cell?.Value),
            ActiveCellBold: style.Bold,
            ActiveCellFillColor: FormatQatUndoRedoTourColor(style.FillColor),
            StatusText: StatusReadyText.Text,
            UndoHistoryLabels: undoHistory,
            RedoHistoryLabels: redoHistory,
            MenuHeaders: menuHeaders);
    }

    private static string FormatQatUndoRedoTourValue(ScalarValue? value) =>
        value switch
        {
            null or BlankValue => string.Empty,
            TextValue text => text.Value,
            NumberValue number => number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
            DateTimeValue dateTime => dateTime.ToDateTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ErrorValue error => error.Code,
            RangeValue range => $"{range.RowCount}x{range.ColCount} range",
            _ => value.ToString() ?? string.Empty
        };

    private static string? FormatQatUndoRedoTourColor(CellColor? color) =>
        color is { } value ? $"#{value.R:X2}{value.G:X2}{value.B:X2}" : null;

    private static void DeleteQatUndoRedoTourEvidence(string outputDir)
    {
        foreach (var file in Directory.EnumerateFiles(outputDir, "freex_qat_*.png"))
            File.Delete(file);

        var manifestPath = Path.Combine(outputDir, QatUndoRedoTourManifestFileName);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);
    }

    private static void ValidateQatUndoRedoTourEvidence(string outputDir, IReadOnlyList<QatUndoRedoTourManifestCapture> captures)
    {
        foreach (var capture in captures)
        {
            var path = Path.Combine(outputDir, capture.OutputFileName);
            if (!File.Exists(path))
                throw new InvalidOperationException($"QAT undo/redo tour did not create planned capture '{capture.OutputFileName}'.");
        }
    }

    private async Task CaptureTitlebarWindowChromeTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteTitlebarWindowChromeTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1100;
        Height = 768;
        await Task.Delay(700);

        var address = EnsureTitlebarWindowChromeTourContext();
        var captures = new List<TitlebarWindowChromeTourManifestCapture>();
        var savedWorkbookPath = Path.Combine(outputDir, TitlebarWindowChromeTourSavedWorkbookFileName);

        try
        {
            UpdateTitleBar();
            captures.Add(await CaptureTitlebarWindowChromeStateAsync(
                outputDir,
                "unsaved-restored",
                "freex_titlebar_unsaved_restored",
                "Fresh workbook titlebar shows Book1, QAT Save/Undo/Redo, and custom minimize/maximize/close buttons in restored state."));

            ExecuteTitlebarWindowChromeTourDirtyMutation(address);
            captures.Add(await CaptureTitlebarWindowChromeStateAsync(
                outputDir,
                "dirty-marker-restored",
                "freex_titlebar_dirty_marker_restored",
                "Dirty marker appears in the workbook title after a command-stack edit."));

            await SaveTitlebarWindowChromeTourWorkbookAsync(savedWorkbookPath);
            captures.Add(await CaptureTitlebarWindowChromeStateAsync(
                outputDir,
                "saved-renamed-restored",
                "freex_titlebar_saved_renamed_restored",
                "Real save-to-XLSX path renames the title and clears the dirty marker in restored state."));

            WindowState = WindowState.Maximized;
            UpdateMaxRestoreButtonState();
            UpdateLayout();
            await Task.Delay(450);
            captures.Add(await CaptureTitlebarWindowChromeStateAsync(
                outputDir,
                "saved-renamed-maximized",
                "freex_titlebar_saved_renamed_maximized",
                "Maximized window state shows the saved title and restore-down system-button state."));

            WindowState = WindowState.Normal;
            Width = 1100;
            Height = 768;
            UpdateMaxRestoreButtonState();
            UpdateLayout();
            await Task.Delay(450);
            captures.Add(await CaptureTitlebarWindowChromeStateAsync(
                outputDir,
                "saved-renamed-restored-after-maximize",
                "freex_titlebar_saved_renamed_restored_after_maximize",
                "Restored-after-maximize state shows the saved title and maximize system-button state."));

            ValidateTitlebarWindowChromeTourEvidence(outputDir, captures);
            await WriteTitlebarWindowChromeTourManifestAsync(outputDir, captures, savedWorkbookPath);
        }
        catch
        {
            DeleteTitlebarWindowChromeTourEvidence(outputDir);
            throw;
        }
    }

    private CellAddress EnsureTitlebarWindowChromeTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Titlebar/window chrome tour requires an active worksheet.");

        _currentSheetId = sheet.Id;
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.ClearCell(address);
        SetActiveCell(address);
        _workbook.Name = "Book1";
        _currentFilePath = null;
        MarkWorkbookSaved();
        if (SheetGrid is not null)
        {
            SheetGrid.SelectedRange = new GridRange(address, address);
            SheetGrid.SelectedRanges = null;
            SheetGrid.Focus();
        }

        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
        return address;
    }

    private void ExecuteTitlebarWindowChromeTourDirtyMutation(CellAddress address)
    {
        var edit = (address, Cell.FromValue(new TextValue("Titlebar dirty marker proof")));
        if (!TryExecuteEditCells([edit], "Edit Cell", out var outcome))
            throw new InvalidOperationException(outcome.ErrorMessage ?? "Titlebar/window chrome tour cell edit failed.");

        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private async Task SaveTitlebarWindowChromeTourWorkbookAsync(string savedWorkbookPath)
    {
        if (File.Exists(savedWorkbookPath))
            File.Delete(savedWorkbookPath);

        var adapter = FileDialogFilterBuilder.FindSaveAdapter(_fileAdapters, ".xlsx", out _)
            ?? throw new InvalidOperationException("Titlebar/window chrome tour could not find an XLSX save adapter.");

        var saved = await SaveWorkbookToTargetAsync(new FileSaveTarget(savedWorkbookPath, adapter));
        if (!saved)
            throw new InvalidOperationException("Titlebar/window chrome tour could not save the renamed workbook.");

        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
    }

    private async Task<TitlebarWindowChromeTourManifestCapture> CaptureTitlebarWindowChromeStateAsync(
        string outputDir,
        string state,
        string fileName,
        string evidenceSummary)
    {
        RefreshToolbar();
        RefreshStatusBar();
        UpdateMaxRestoreButtonState();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await CaptureCurrentWindowAsync(outputDir, fileName, 220);
        return CreateTitlebarWindowChromeTourCapture(state, fileName, evidenceSummary);
    }

    private TitlebarWindowChromeTourManifestCapture CreateTitlebarWindowChromeTourCapture(
        string state,
        string fileName,
        string evidenceSummary)
    {
        return new TitlebarWindowChromeTourManifestCapture(
            CaptureKey: $"window-chrome:titlebar:{state}",
            PairKey: $"interactive:titlebar-window-chrome:{state}",
            ScenarioId: "window-chrome:titlebar",
            State: state,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: "RenderTargetBitmap-window-top-band",
            CaptureLogicalWidth: ActualWidth,
            CaptureLogicalHeight: Math.Min(ActualHeight, 220),
            EvidenceSummary: evidenceSummary,
            WindowState: WindowState.ToString(),
            WindowTitle: Title,
            WorkbookNameText: WorkbookNameText.Text,
            WorkbookName: _workbook.Name,
            WorkbookDirty: _workbookDirty,
            CurrentFileName: string.IsNullOrWhiteSpace(_currentFilePath) ? null : Path.GetFileName(_currentFilePath),
            TitleBarQatVisible: TitleBarQatPanel.Visibility == Visibility.Visible,
            TitleBarQatCommandIds: GetTitlebarWindowChromeVisibleQatCommandIds(),
            MinimizeButton: CreateTitlebarWindowChromeButtonState(MinimizeBtn),
            MaxRestoreButton: CreateTitlebarWindowChromeButtonState(MaxRestoreBtn),
            CloseButton: CreateTitlebarWindowChromeButtonState(CloseSysBtn),
            MaxRestoreIconKind: MaxRestoreIcon.Kind.ToString());
    }

    private IReadOnlyList<string> GetTitlebarWindowChromeVisibleQatCommandIds()
    {
        var result = new List<string>();
        foreach (var command in QuickAccessToolbarCatalog.Commands)
        {
            var button = GetQuickAccessToolbarButton(command.Id);
            if (button is { Visibility: Visibility.Visible })
                result.Add(command.Id);
        }

        return result;
    }

    private static TitlebarWindowChromeTourManifestButtonState CreateTitlebarWindowChromeButtonState(ButtonBase button)
    {
        return new TitlebarWindowChromeTourManifestButtonState(
            AutomationId: AutomationProperties.GetAutomationId(button),
            AutomationName: AutomationProperties.GetName(button),
            HelpText: AutomationProperties.GetHelpText(button),
            IsVisible: button.Visibility == Visibility.Visible,
            IsEnabled: button.IsEnabled,
            ActualWidth: button.ActualWidth,
            ActualHeight: button.ActualHeight);
    }

    private static void DeleteTitlebarWindowChromeTourEvidence(string outputDir)
    {
        foreach (var file in Directory.EnumerateFiles(outputDir, "freex_titlebar_*.png"))
            File.Delete(file);

        var savedWorkbookPath = Path.Combine(outputDir, TitlebarWindowChromeTourSavedWorkbookFileName);
        if (File.Exists(savedWorkbookPath))
            File.Delete(savedWorkbookPath);

        var manifestPath = Path.Combine(outputDir, TitlebarWindowChromeTourManifestFileName);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);
    }

    private async Task CaptureFormulaBarNameBoxTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteFormulaBarNameBoxTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1180;
        Height = 768;
        await Task.Delay(700);

        var context = EnsureFormulaBarNameBoxTourContext();
        var captures = new List<FormulaBarNameBoxTourManifestCapture>();
        InsertFunctionDialog? insertFunctionDialog = null;

        try
        {
            captures.Add(await CaptureFormulaBarNameBoxWindowStateAsync(
                outputDir,
                "initial-named-range-selection",
                "freex_formula_name_box_named_range_selected",
                "window-full",
                "Selected Sales named range displays in the Name Box, with the formula bar showing B2's content."));

            CellAddressBox.Focus();
            Keyboard.Focus(CellAddressBox);
            CellAddressBox.IsDropDownOpen = true;
            CellAddressBox.UpdateLayout();
            await Task.Delay(350);
            await WaitForRibbonScreenshotRenderPassAsync();
            var nameBoxPopup = FindOpenPopupChild(CellAddressBox)
                ?? throw new InvalidOperationException("Formula bar/name box tour could not locate the open Name Box dropdown.");
            await CaptureElementAsync(nameBoxPopup, outputDir, "freex_formula_name_box_dropdown_opened");
            captures.Add(CreateFormulaBarNameBoxCapture(
                "name-box-dropdown-opened",
                "freex_formula_name_box_dropdown_opened",
                "name-box-dropdown",
                "RenderTargetBitmap-name-box-combobox-popup",
                nameBoxPopup.ActualWidth,
                nameBoxPopup.ActualHeight,
                "Name Box dropdown lists workbook defined names including Sales."));

            CellAddressBox.SelectedItem = "Sales";
            CellAddressBox.IsDropDownOpen = false;
            await Task.Delay(250);
            UpdateLayout();
            await WaitForRibbonScreenshotRenderPassAsync();
            captures.Add(await CaptureFormulaBarNameBoxWindowStateAsync(
                outputDir,
                "name-box-dropdown-navigation",
                "freex_formula_name_box_dropdown_navigation",
                "window-full",
                "Selecting SalesData from the Name Box dropdown navigates to B2:C3 and returns focus to the worksheet."));

            BeginFormulaBarFormulaEdit("=SUM(B2:C3)");
            FormulaBar.CaretIndex = FormulaBar.Text.Length;
            FormulaBarCancelButton.Focus();
            Keyboard.Focus(FormulaBarCancelButton);
            captures.Add(await CaptureFormulaBarNameBoxWindowStateAsync(
                outputDir,
                "formula-edit-cancel-focused",
                "freex_formula_bar_edit_mode_cancel_focused",
                "window-full",
                "Formula bar edit mode shows the draft formula with the Cancel control focused."));

            FormulaBarCancelButton_Click(FormulaBarCancelButton, new RoutedEventArgs(ButtonBase.ClickEvent));
            await Task.Delay(250);
            captures.Add(await CaptureFormulaBarNameBoxWindowStateAsync(
                outputDir,
                "formula-edit-canceled",
                "freex_formula_bar_cancel_restored_selection",
                "window-full",
                "Cancel restores the selected cell's formula bar text and worksheet focus."));

            BeginFormulaBarFormulaEdit("=SUM(B2:C3)");
            FormulaBar.CaretIndex = FormulaBar.Text.Length;
            FormulaBarEnterButton.Focus();
            Keyboard.Focus(FormulaBarEnterButton);
            captures.Add(await CaptureFormulaBarNameBoxWindowStateAsync(
                outputDir,
                "formula-edit-enter-focused",
                "freex_formula_bar_edit_mode_enter_focused",
                "window-full",
                "Formula bar edit mode shows the draft formula with the Enter control focused."));

            FormulaBarEnterButton_Click(FormulaBarEnterButton, new RoutedEventArgs(ButtonBase.ClickEvent));
            await Task.Delay(250);
            captures.Add(await CaptureFormulaBarNameBoxWindowStateAsync(
                outputDir,
                "formula-edit-enter-committed",
                "freex_formula_bar_enter_committed",
                "window-full",
                "Enter commits the formula-bar edit and returns focus to the worksheet."));

            FormulaBarFxButton.Focus();
            Keyboard.Focus(FormulaBarFxButton);
            captures.Add(await CaptureFormulaBarNameBoxWindowStateAsync(
                outputDir,
                "fx-button-focused",
                "freex_formula_bar_fx_button_focused",
                "window-full",
                "Formula bar fx button is focused beside the Cancel/Enter controls."));

            insertFunctionDialog = new InsertFunctionDialog
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            insertFunctionDialog.Show();
            insertFunctionDialog.Activate();
            insertFunctionDialog.UpdateLayout();
            await Task.Delay(450);
            await WaitForRibbonScreenshotRenderPassAsync();
            await CaptureWindowElementForScreenshotTourAsync(insertFunctionDialog, outputDir, "freex_formula_bar_fx_insert_function_dialog");
            captures.Add(CreateFormulaBarNameBoxCapture(
                "fx-insert-function-dialog-opened",
                "freex_formula_bar_fx_insert_function_dialog",
                "insert-function-dialog",
                "RenderTargetBitmap-insert-function-dialog",
                insertFunctionDialog.ActualWidth,
                insertFunctionDialog.ActualHeight,
                "Production Insert Function dialog shown from the formula-bar fx surface scenario."));
            insertFunctionDialog.Close();
            insertFunctionDialog = null;

            if (!_formulaBarExpanded)
                FormulaBarExpandBtn_Click(FormulaBarExpandBtn, new RoutedEventArgs(ButtonBase.ClickEvent));
            await Task.Delay(250);
            captures.Add(await CaptureFormulaBarNameBoxWindowStateAsync(
                outputDir,
                "formula-bar-expanded",
                "freex_formula_bar_expanded",
                "window-full",
                "Expanded formula bar shows the taller multiline editor and collapse chevron state."));

            FormulaBar.Focus();
            Keyboard.Focus(FormulaBar);
            captures.Add(await CaptureFormulaBarNameBoxWindowStateAsync(
                outputDir,
                "formula-bar-focus",
                "freex_formula_bar_focus",
                "window-full",
                "Formula bar accepts keyboard focus after the expand/collapse interaction."));

            CellAddressBox.Focus();
            Keyboard.Focus(CellAddressBox);
            EnterRibbonKeyTipMode(RibbonKeyTipScope.TopLevel);
            UpdateLayout();
            await Task.Delay(350);
            await WaitForRibbonScreenshotRenderPassAsync();
            captures.Add(await CaptureFormulaBarNameBoxWindowStateAsync(
                outputDir,
                "name-box-focus-top-level-keytips",
                "freex_formula_keytips_from_name_box_focus",
                "window-top-band",
                "Top-level keytip overlay is visible while focus starts from the Name Box."));
            ExitRibbonKeyTipMode();

            ValidateFormulaBarNameBoxTourEvidence(outputDir, captures);
            await WriteFormulaBarNameBoxTourManifestAsync(outputDir, context, captures);
        }
        catch
        {
            DeleteFormulaBarNameBoxTourEvidence(outputDir);
            throw;
        }
        finally
        {
            ExitRibbonKeyTipMode();
            if (insertFunctionDialog is { IsVisible: true })
                insertFunctionDialog.Close();
        }
    }

    private FormulaBarNameBoxTourContext EnsureFormulaBarNameBoxTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Formula bar/name box tour requires an active worksheet.");

        _currentSheetId = sheet.Id;
        var cells = new (uint Row, uint Col, ScalarValue Value)[]
        {
            (1, 1, new TextValue("Formula bar/name box tour")),
            (1, 2, new TextValue("Q1")),
            (1, 3, new TextValue("Q2")),
            (2, 1, new TextValue("North")),
            (2, 2, new NumberValue(10)),
            (2, 3, new NumberValue(15)),
            (3, 1, new TextValue("South")),
            (3, 2, new NumberValue(12)),
            (3, 3, new NumberValue(18))
        };

        foreach (var (row, col, value) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, row, col), value);

        var namedRange = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 3, 3));
        _workbook.DefineNamedRange("Sales", namedRange);
        SetSelectionRange(namedRange, namedRange.Start);
        EnsureCellVisible(namedRange.Start);
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
        UpdateLayout();

        return new FormulaBarNameBoxTourContext(
            SheetName: sheet.Name,
            NamedRangeName: "Sales",
            NamedRangeAddress: namedRange.ToString(),
            StartCell: namedRange.Start.ToA1());
    }

    private async Task<FormulaBarNameBoxTourManifestCapture> CaptureFormulaBarNameBoxWindowStateAsync(
        string outputDir,
        string state,
        string fileName,
        string surface,
        string evidenceSummary)
    {
        RefreshToolbar();
        RefreshStatusBar();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await Task.Delay(150);

        var height = surface == "window-top-band" ? ScreenshotTourCaptureHeight : 760;
        await CaptureCurrentWindowAsync(outputDir, fileName, height);
        return CreateFormulaBarNameBoxCapture(
            state,
            fileName,
            surface,
            surface == "window-top-band" ? "RenderTargetBitmap-window-top-band" : "RenderTargetBitmap-window-full",
            ActualWidth,
            Math.Min(ActualHeight, height),
            evidenceSummary);
    }
    private FormulaBarNameBoxTourManifestCapture CreateFormulaBarNameBoxCapture(
        string state,
        string fileName,
        string surface,
        string captureMethod,
        double logicalWidth,
        double logicalHeight,
        string evidenceSummary)
    {
        var selectedRange = SheetGrid.SelectedRange;
        var activeCell = selectedRange?.Start;
        var activeCellText = activeCell is { } cellAddress
            ? FormatQatUndoRedoTourValue(_workbook.GetSheet(cellAddress.Sheet)?.GetCell(cellAddress)?.Value)
            : string.Empty;
        var focusElement = Keyboard.FocusedElement as DependencyObject;

        return new FormulaBarNameBoxTourManifestCapture(
            CaptureKey: $"formula-bar-name-box:{state}",
            PairKey: $"interactive:formula-bar-name-box:{state}",
            ScenarioId: "formula-bar-name-box:visual-evidence",
            State: state,
            Surface: surface,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: captureMethod,
            CaptureLogicalWidth: logicalWidth,
            CaptureLogicalHeight: logicalHeight,
            NameBoxText: CellAddressBox.Text,
            NameBoxDropDownOpen: CellAddressBox.IsDropDownOpen,
            FormulaBarText: FormulaBar.Text,
            FormulaBarAcceptsReturn: FormulaBar.AcceptsReturn,
            FormulaBarExpanded: _formulaBarExpanded,
            SelectedRange: selectedRange?.ToString() ?? string.Empty,
            ActiveCellText: activeCellText,
            FocusedAutomationId: FormatFormulaBarNameBoxFocusedAutomationId(focusElement),
            KeyTipBadgeCount: KeyTipOverlay.Children.OfType<Border>().Count(),
            EvidenceSummary: evidenceSummary);
    }

    private static string FormatFormulaBarNameBoxFocusedAutomationId(DependencyObject? focusedElement)
    {
        if (focusedElement is null)
            return string.Empty;

        var automationId = AutomationProperties.GetAutomationId(focusedElement);
        if (!string.IsNullOrWhiteSpace(automationId))
            return automationId;

        return focusedElement.GetType().Name;
    }

    private static void DeleteFormulaBarNameBoxTourEvidence(string outputDir)
    {
        foreach (var file in Directory.EnumerateFiles(outputDir, "freex_formula_*.png"))
            File.Delete(file);

        var manifestPath = Path.Combine(outputDir, FormulaBarNameBoxTourManifestFileName);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);
    }

    private async Task CaptureStatusFooterTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteStatusFooterTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1180;
        Height = 760;
        await Task.Delay(700);

        var sheet = EnsureStatusFooterTourContext();
        var captures = new List<StatusFooterTourManifestCapture>();

        try
        {
            captures.Add(await CaptureStatusFooterWindowStateAsync(
                outputDir,
                "ready-baseline",
                "freex_status_footer_ready_baseline",
                "Ready footer with Normal view shortcut, 100% zoom text, zoom buttons, and slider visible.",
                captureFullWindow: false));

            SelectStatusFooterTourRange(new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 3)));
            captures.Add(await CaptureStatusFooterWindowStateAsync(
                outputDir,
                "selection-stats",
                "freex_status_footer_selection_stats_numeric_mixed",
                "Numeric plus text selection showing Average, Count, Numerical Count, Sum, Min, and Max footer statistics.",
                captureFullWindow: false));

            BeginFormulaBarFormulaEdit("=SUM(A1:A4)");
            captures.Add(await CaptureStatusFooterWindowStateAsync(
                outputDir,
                "formula-edit-mode",
                "freex_status_footer_formula_edit_mode",
                "Formula edit mode with footer mode text set to Edit and the formula bar showing the in-progress formula.",
                captureFullWindow: true));
            HideInlineEditor(commit: false);
            FocusSheetGridIfNeeded();

            SetWorksheetViewMode(WorksheetViewMode.PageLayout);
            RefreshStatusBar();
            captures.Add(await CaptureStatusFooterWindowStateAsync(
                outputDir,
                "view-shortcut-page-layout",
                "freex_status_footer_view_shortcut_page_layout",
                "Status bar view shortcut buttons with Page Layout selected.",
                captureFullWindow: false));

            SetWorksheetViewMode(WorksheetViewMode.PageBreakPreview);
            RefreshStatusBar();
            captures.Add(await CaptureStatusFooterWindowStateAsync(
                outputDir,
                "view-shortcut-page-break-preview",
                "freex_status_footer_view_shortcut_page_break_preview",
                "Status bar view shortcut buttons with Page Break Preview selected.",
                captureFullWindow: false));

            SetWorksheetViewMode(WorksheetViewMode.Normal);
            await SetStatusFooterTourZoomAsync(10);
            captures.Add(await CaptureStatusFooterWindowStateAsync(
                outputDir,
                "zoom-min-10-percent",
                "freex_status_footer_zoom_min_10",
                "Minimum representative zoom state with 10% footer text, slider at minimum, and visibly scaled grid.",
                captureFullWindow: true));

            await SetStatusFooterTourZoomAsync(100);
            captures.Add(await CaptureStatusFooterWindowStateAsync(
                outputDir,
                "zoom-baseline-100-percent",
                "freex_status_footer_zoom_baseline_100",
                "Baseline zoom state with 100% footer text, midpoint slider, and normal grid scale.",
                captureFullWindow: true));

            await SetStatusFooterTourZoomAsync(400);
            captures.Add(await CaptureStatusFooterWindowStateAsync(
                outputDir,
                "zoom-max-400-percent",
                "freex_status_footer_zoom_max_400",
                "Maximum representative zoom state with 400% footer text, slider at maximum, and visibly enlarged grid.",
                captureFullWindow: true));

            ValidateStatusFooterTourEvidence(outputDir, captures);
            await WriteStatusFooterTourManifestAsync(outputDir, captures);
        }
        catch
        {
            DeleteStatusFooterTourEvidence(outputDir);
            throw;
        }
    }

    private Sheet EnsureStatusFooterTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Status/footer tour requires an active worksheet.");

        _currentSheetId = sheet.Id;
        _options.StatusBarShowCellMode = true;
        _options.StatusBarShowAverage = true;
        _options.StatusBarShowCount = true;
        _options.StatusBarShowNumericalCount = true;
        _options.StatusBarShowSum = true;
        _options.StatusBarShowMinimum = true;
        _options.StatusBarShowMaximum = true;
        _options.StatusBarShowViewShortcuts = true;
        _options.StatusBarShowZoom = true;
        _options.StatusBarShowZoomSlider = true;

        var values = new (uint Row, uint Col, ScalarValue Value)[]
        {
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(20)),
            (3, 1, new NumberValue(30)),
            (4, 1, new NumberValue(40)),
            (1, 2, new NumberValue(5)),
            (2, 2, new NumberValue(15)),
            (3, 2, new NumberValue(25)),
            (4, 2, new NumberValue(35)),
            (1, 3, new TextValue("North")),
            (2, 3, new TextValue("South")),
            (3, 3, new TextValue("East")),
            (4, 3, new TextValue("West"))
        };

        for (uint row = 1; row <= 8; row++)
        {
            for (uint col = 1; col <= 5; col++)
                sheet.ClearCell(new CellAddress(sheet.Id, row, col));
        }

        foreach (var value in values)
            sheet.SetCell(new CellAddress(sheet.Id, value.Row, value.Col), value.Value);

        var activeCell = new CellAddress(sheet.Id, 1, 1);
        SelectStatusFooterTourRange(new GridRange(activeCell, activeCell));
        SyncZoomFromSheet(100);
        return sheet;
    }

    private void SelectStatusFooterTourRange(GridRange range)
    {
        SetActiveCell(range.Start);
        if (SheetGrid is not null)
        {
            SheetGrid.SelectedRange = range;
            SheetGrid.SelectedRanges = null;
            SheetGrid.Focus();
        }

        var cell = _workbook.GetSheet(range.Start.Sheet)?.GetCell(range.Start);
        SetFormulaBarSelectionText(FormatFormulaBarText(cell, range.Start));
        UpdateViewport();
        RefreshStatusBar();
    }

    private async Task SetStatusFooterTourZoomAsync(int zoomPercent)
    {
        ZoomSlider.Value = FreeX.App.UI.ZoomLevelMapper.ZoomPercentToSlider(zoomPercent);
        RefreshStatusBar();
        UpdateViewport();
        await Task.Delay(250);
    }

    private async Task<StatusFooterTourManifestCapture> CaptureStatusFooterWindowStateAsync(
        string outputDir,
        string state,
        string fileName,
        string evidencePurpose,
        bool captureFullWindow)
    {
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        if (captureFullWindow)
            await CaptureCurrentWindowAsync(outputDir, fileName, 760);
        else
            await CaptureElementAsync(StatusBarRoot, outputDir, fileName);

        return CreateStatusFooterTourCapture(state, fileName, evidencePurpose, captureFullWindow);
    }

    private StatusFooterTourManifestCapture CreateStatusFooterTourCapture(
        string state,
        string fileName,
        string evidencePurpose,
        bool captureFullWindow)
    {
        var activeRange = SheetGrid?.SelectedRange;
        var viewMode = _workbook.GetSheet(_currentSheetId)?.ViewMode ?? WorksheetViewMode.Normal;
        return new StatusFooterTourManifestCapture(
            CaptureKey: $"interactive:status-footer:{state}",
            PairKey: $"interactive:status-footer:{state}",
            ScenarioId: "status-footer:visual-evidence",
            State: state,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: captureFullWindow
                ? "RenderTargetBitmap-window-full"
                : "RenderTargetBitmap-status-footer-element",
            EvidencePurpose: evidencePurpose,
            CaptureLogicalWidth: captureFullWindow ? ActualWidth : StatusBarRoot.ActualWidth,
            CaptureLogicalHeight: captureFullWindow ? Math.Min(ActualHeight, 760) : StatusBarRoot.ActualHeight,
            ActiveRange: activeRange?.ToString() ?? string.Empty,
            StatusModeText: StatusReadyText.Text,
            StatusModeVisible: StatusReadyText.Visibility == Visibility.Visible,
            AverageText: StatusAvgText.Text,
            CountText: StatusCountText.Text,
            NumericalCountText: StatusNumericalCountText.Text,
            SumText: StatusSumText.Text,
            MinText: StatusMinText.Text,
            MaxText: StatusMaxText.Text,
            StatsVisible: StatusStatsPanel.Visibility == Visibility.Visible,
            ViewMode: viewMode.ToString(),
            NormalViewChecked: StatusNormalViewButton.IsChecked == true,
            PageLayoutViewChecked: StatusPageLayoutViewButton.IsChecked == true,
            PageBreakPreviewChecked: StatusPageBreakPreviewButton.IsChecked == true,
            ZoomText: StatusZoomText.Text,
            ZoomSliderValue: ZoomSlider.Value,
            ZoomOutButtonEnabled: StatusZoomOutButton.IsEnabled,
            ZoomInButtonEnabled: StatusZoomInButton.IsEnabled,
            FormulaBarText: FormulaBar.Text);
    }

    private static void DeleteStatusFooterTourEvidence(string outputDir)
    {
        foreach (var file in Directory.EnumerateFiles(outputDir, "freex_status_footer_*.png"))
            File.Delete(file);

        var manifestPath = Path.Combine(outputDir, StatusFooterTourManifestFileName);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);
    }

    private static void ValidateTitlebarWindowChromeTourEvidence(string outputDir, IReadOnlyList<TitlebarWindowChromeTourManifestCapture> captures)
    {
        foreach (var capture in captures)
        {
            var path = Path.Combine(outputDir, capture.OutputFileName);
            if (!File.Exists(path))
                throw new InvalidOperationException($"Titlebar/window chrome tour did not create planned capture '{capture.OutputFileName}'.");
        }
    }

    private static void ValidateFormulaBarNameBoxTourEvidence(string outputDir, IReadOnlyList<FormulaBarNameBoxTourManifestCapture> captures)
    {
        foreach (var capture in captures)
        {
            var path = Path.Combine(outputDir, capture.OutputFileName);
            if (!File.Exists(path))
                throw new InvalidOperationException($"Formula bar/name box tour did not create planned capture '{capture.OutputFileName}'.");
        }
    }

    private static void ValidateStatusFooterTourEvidence(string outputDir, IReadOnlyList<StatusFooterTourManifestCapture> captures)
    {
        foreach (var capture in captures)
        {
            var path = Path.Combine(outputDir, capture.OutputFileName);
            if (!File.Exists(path))
                throw new InvalidOperationException($"Status/footer tour did not create planned capture '{capture.OutputFileName}'.");
        }
    }

    private async Task CaptureFormulaDiagnosticsTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteFormulaDiagnosticsTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1180;
        Height = 760;
        await Task.Delay(700);

        var context = EnsureFormulaDiagnosticsTourContext();
        var captures = new List<FormulaDiagnosticsTourManifestCapture>();
        ErrorCheckingDialog? errorCheckingDialog = null;
        EvaluateFormulaDialog? evaluateFormulaDialog = null;
        AddWatchDialog? addWatchDialog = null;
        WatchWindowDialog? watchWindowDialog = null;

        try
        {
            SetFormulaDiagnosticsTourSelection(context.ResultCell);
            TracePrecedentsForCell(context.ResultCell, "Trace Precedents");
            captures.Add(await CaptureFormulaDiagnosticsWindowStateAsync(
                outputDir,
                "trace-precedents-visible",
                "freex_formula_diagnostics_trace_precedents",
                "window-full",
                "Trace Precedents draws visible formula auditing arrows from A2/A3 into B2."));

            SetFormulaDiagnosticsTourSelection(context.InputCell);
            TraceDependentsBtn_Click(this, new RoutedEventArgs());
            captures.Add(await CaptureFormulaDiagnosticsWindowStateAsync(
                outputDir,
                "trace-dependents-visible",
                "freex_formula_diagnostics_trace_dependents",
                "window-full",
                "Trace Dependents adds a visible auditing arrow from A2 toward B2 without clearing the existing precedent arrows."));

            SetFormulaDiagnosticsTourSelection(context.ResultCell);
            ShowFormulasBtn_Click(ShowFormulasButton, new RoutedEventArgs(ButtonBase.ClickEvent, ShowFormulasButton));
            captures.Add(await CaptureFormulaDiagnosticsWindowStateAsync(
                outputDir,
                "show-formulas-enabled",
                "freex_formula_diagnostics_show_formulas_enabled",
                "window-full",
                "Show Formulas toggles the active sheet to display formula text such as =A2+A3 and =B2/0 in the grid."));

            ShowFormulasBtn_Click(ShowFormulasButton, new RoutedEventArgs(ButtonBase.ClickEvent, ShowFormulasButton));
            RemoveTraceArrows(kind: null, "Remove Arrows");
            captures.Add(await CaptureFormulaDiagnosticsWindowStateAsync(
                outputDir,
                "remove-arrows-cleared",
                "freex_formula_diagnostics_remove_arrows_cleared",
                "window-full",
                "Remove Arrows clears the in-memory formula trace arrows and returns the sheet to value display mode."));

            var issues = FormulaAuditingService.FindFormulaErrorIssues(_workbook, _currentSheetId);
            if (issues.Count == 0)
                throw new InvalidOperationException("Formula diagnostics tour expected at least one formula error issue.");

            errorCheckingDialog = new ErrorCheckingDialog(
                issues,
                address =>
                {
                    NavigateToCell(address);
                    RefreshSheetTabs();
                    UpdateViewport();
                    RefreshStatusBar();
                },
                issue => true,
                issue => TracePrecedentsForCell(issue.Address, "Trace Error"),
                issue =>
                {
                    var summary = FormulaEvaluationSummaryService.GetSummary(_workbook, issue.Address)
                        ?? throw new InvalidOperationException("Formula diagnostics tour expected an evaluation summary for the selected error issue.");
                    var stepsDialog = new EvaluateFormulaDialog(summary) { Owner = this };
                    stepsDialog.Show();
                },
                openOptions: null)
            {
                Owner = this
            };
            errorCheckingDialog.Show();
            errorCheckingDialog.Activate();
            errorCheckingDialog.UpdateLayout();
            await Task.Delay(450);
            await CaptureWindowElementForScreenshotTourAsync(errorCheckingDialog, outputDir, "freex_formula_diagnostics_error_checking_dialog");
            captures.Add(CreateFormulaDiagnosticsCapture(
                "error-checking-dialog-list",
                "freex_formula_diagnostics_error_checking_dialog",
                "error-checking-dialog",
                "RenderTargetBitmap-error-checking-dialog",
                errorCheckingDialog.ActualWidth,
                errorCheckingDialog.ActualHeight,
                "Error Checking dialog opens with the issue list, selected first error, side actions, bottom navigation, Ignore, Trace Error, Options, and Close controls."));
            errorCheckingDialog.Close();
            errorCheckingDialog = null;

            var resultSummary = FormulaEvaluationSummaryService.GetSummary(_workbook, context.ResultCell)
                ?? throw new InvalidOperationException("Formula diagnostics tour expected an evaluation summary for the result cell.");
            evaluateFormulaDialog = new EvaluateFormulaDialog(resultSummary) { Owner = this };
            evaluateFormulaDialog.Show();
            evaluateFormulaDialog.Activate();
            evaluateFormulaDialog.UpdateLayout();
            await Task.Delay(450);
            await CaptureWindowElementForScreenshotTourAsync(evaluateFormulaDialog, outputDir, "freex_formula_diagnostics_evaluate_default");
            captures.Add(CreateFormulaDiagnosticsCapture(
                "evaluate-formula-default-button",
                "freex_formula_diagnostics_evaluate_default",
                "evaluate-formula-dialog",
                "RenderTargetBitmap-evaluate-formula-dialog",
                evaluateFormulaDialog.ActualWidth,
                evaluateFormulaDialog.ActualHeight,
                "Evaluate Formula dialog opens on B2 with the Evaluate command as the focused/default command and Close as the cancel command."));

            var evaluateButton = FindDescendantButtonByContent(evaluateFormulaDialog, UiText.Get("EvaluateFormula_EvaluateButton"))
                ?? throw new InvalidOperationException("Formula diagnostics tour could not find the Evaluate Formula default button.");
            evaluateButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, evaluateButton));
            await Task.Delay(250);
            evaluateFormulaDialog.UpdateLayout();
            await CaptureWindowElementForScreenshotTourAsync(evaluateFormulaDialog, outputDir, "freex_formula_diagnostics_evaluate_after_step");
            captures.Add(CreateFormulaDiagnosticsCapture(
                "evaluate-formula-after-step",
                "freex_formula_diagnostics_evaluate_after_step",
                "evaluate-formula-dialog",
                "RenderTargetBitmap-evaluate-formula-dialog",
                evaluateFormulaDialog.ActualWidth,
                evaluateFormulaDialog.ActualHeight,
                "Evaluate advances one deterministic calculation step while preserving the Evaluate/Step In/Step Out/Restart/Close/Help command row."));
            evaluateFormulaDialog.Close();
            evaluateFormulaDialog = null;

            SetFormulaDiagnosticsTourSelection(context.ResultCell);
            addWatchDialog = new AddWatchDialog(FormatRangeReference(context.ResultCell, context.ResultCell)) { Owner = this };
            addWatchDialog.Show();
            addWatchDialog.Activate();
            addWatchDialog.UpdateLayout();
            await Task.Delay(350);
            await CaptureWindowElementForScreenshotTourAsync(addWatchDialog, outputDir, "freex_formula_diagnostics_watch_add_dialog");
            captures.Add(CreateFormulaDiagnosticsCapture(
                "watch-window-add-dialog",
                "freex_formula_diagnostics_watch_add_dialog",
                "watch-window-add-dialog",
                "RenderTargetBitmap-add-watch-dialog",
                addWatchDialog.ActualWidth,
                addWatchDialog.ActualHeight,
                "Add Watch dialog shows the selected B2 range, Add default button, Cancel button, and stable AddWatch automation IDs."));
            addWatchDialog.Close();
            addWatchDialog = null;

            WatchWindowService.AddWatches(_workbook, new GridRange(context.ResultCell, context.ResultCell));
            WatchWindowService.AddWatches(_workbook, new GridRange(context.ErrorCell, context.ErrorCell));
            watchWindowDialog = CreateFormulaDiagnosticsWatchWindowDialog();
            watchWindowDialog.Show();
            watchWindowDialog.Activate();
            watchWindowDialog.UpdateLayout();
            await Task.Delay(450);
            await CaptureWindowElementForScreenshotTourAsync(watchWindowDialog, outputDir, "freex_formula_diagnostics_watch_window_list");
            captures.Add(CreateFormulaDiagnosticsCapture(
                "watch-window-list",
                "freex_formula_diagnostics_watch_window_list",
                "watch-window-dialog",
                "RenderTargetBitmap-watch-window-dialog",
                watchWindowDialog.ActualWidth,
                watchWindowDialog.ActualHeight,
                "Watch Window lists B2 and D2 with workbook, sheet, cell, value, and formula columns plus Add Watch, Refresh, Delete Watch, and Close controls."));

            var refreshButton = FindDescendantByAutomationId<Button>(watchWindowDialog, "WatchWindowRefreshButton")
                ?? throw new InvalidOperationException("Formula diagnostics tour could not find the Watch Window Refresh button.");
            refreshButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, refreshButton));
            await Task.Delay(250);
            await CaptureWindowElementForScreenshotTourAsync(watchWindowDialog, outputDir, "freex_formula_diagnostics_watch_window_after_refresh");
            captures.Add(CreateFormulaDiagnosticsCapture(
                "watch-window-after-refresh",
                "freex_formula_diagnostics_watch_window_after_refresh",
                "watch-window-dialog",
                "RenderTargetBitmap-watch-window-dialog",
                watchWindowDialog.ActualWidth,
                watchWindowDialog.ActualHeight,
                "Refresh rehydrates the watched rows while preserving the selected watched cell when possible."));

            var watchList = FindDescendantByAutomationId<ListView>(watchWindowDialog, "WatchWindowList")
                ?? throw new InvalidOperationException("Formula diagnostics tour could not find the Watch Window list.");
            if (watchList.Items.Count > 0)
                watchList.SelectedIndex = 0;
            var deleteButton = FindDescendantByAutomationId<Button>(watchWindowDialog, "WatchWindowDeleteButton")
                ?? throw new InvalidOperationException("Formula diagnostics tour could not find the Watch Window Delete Watch button.");
            deleteButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, deleteButton));
            await Task.Delay(250);
            await CaptureWindowElementForScreenshotTourAsync(watchWindowDialog, outputDir, "freex_formula_diagnostics_watch_window_after_delete");
            captures.Add(CreateFormulaDiagnosticsCapture(
                "watch-window-after-delete",
                "freex_formula_diagnostics_watch_window_after_delete",
                "watch-window-dialog",
                "RenderTargetBitmap-watch-window-dialog",
                watchWindowDialog.ActualWidth,
                watchWindowDialog.ActualHeight,
                "Delete Watch removes the selected watched row and leaves the remaining watched formula visible."));
            watchWindowDialog.Close();
            watchWindowDialog = null;

            ValidateFormulaDiagnosticsTourEvidence(outputDir, captures);
            await WriteFormulaDiagnosticsTourManifestAsync(outputDir, context, captures);
        }
        catch
        {
            DeleteFormulaDiagnosticsTourEvidence(outputDir);
            throw;
        }
        finally
        {
            if (errorCheckingDialog is { IsVisible: true })
                errorCheckingDialog.Close();
            if (evaluateFormulaDialog is { IsVisible: true })
                evaluateFormulaDialog.Close();
            if (addWatchDialog is { IsVisible: true })
                addWatchDialog.Close();
            if (watchWindowDialog is { IsVisible: true })
                watchWindowDialog.Close();

            _formulaTraceArrows.Clear();
            UpdateViewport();
        }
    }

    private FormulaDiagnosticsTourContext EnsureFormulaDiagnosticsTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Formula diagnostics tour requires an active worksheet.");

        _currentSheetId = sheet.Id;
        _formulaTraceArrows.Clear();
        WatchWindowService.RemoveWatches(
            _workbook,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 8, 6)));

        for (uint row = 1; row <= 8; row++)
        {
            for (uint col = 1; col <= 6; col++)
                sheet.ClearCell(new CellAddress(sheet.Id, row, col));
        }

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Input"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Result"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("Error"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(12));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(8));
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 2), "A2+A3");
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 4), "B2/0");
        sheet.SetFormula(new CellAddress(sheet.Id, 3, 4), "B2+A2");

        RecalculateWorkbook();
        var resultCell = new CellAddress(sheet.Id, 2, 2);
        SetFormulaDiagnosticsTourSelection(resultCell);
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
        UpdateLayout();

        return new FormulaDiagnosticsTourContext(
            SheetName: sheet.Name,
            InputCell: new CellAddress(sheet.Id, 2, 1),
            ResultCell: resultCell,
            ErrorCell: new CellAddress(sheet.Id, 2, 4),
            ResultFormula: sheet.GetCell(resultCell)?.FormulaText ?? "",
            ErrorFormula: sheet.GetCell(new CellAddress(sheet.Id, 2, 4))?.FormulaText ?? "");
    }

    private void SetFormulaDiagnosticsTourSelection(CellAddress address)
    {
        var range = new GridRange(address, address);
        SetSelectionRange(range, address);
        EnsureCellVisible(address);
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private WatchWindowDialog CreateFormulaDiagnosticsWatchWindowDialog() =>
        new(
            () =>
            {
                RecalculateWorkbook();
                return WatchWindowService.GetEntries(_workbook);
            },
            () => AddWatchFromSelection(showMessage: false),
            () => SheetGrid.SelectedRange is { } range
                ? FormatRangeReference(range.Start, range.End)
                : "",
            address =>
            {
                NavigateToCell(address);
                RefreshSheetTabs();
                UpdateViewport();
                RefreshStatusBar();
            },
            address =>
            {
                WatchWindowService.RemoveWatch(_workbook, address);
                UpdateViewport();
            })
        {
            Owner = this
        };

    private async Task<FormulaDiagnosticsTourManifestCapture> CaptureFormulaDiagnosticsWindowStateAsync(
        string outputDir,
        string state,
        string fileName,
        string surface,
        string evidenceSummary)
    {
        RefreshToolbar();
        RefreshStatusBar();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await Task.Delay(150);

        await CaptureCurrentWindowAsync(outputDir, fileName, 760);
        return CreateFormulaDiagnosticsCapture(
            state,
            fileName,
            surface,
            "RenderTargetBitmap-window-full",
            ActualWidth,
            Math.Min(ActualHeight, 760),
            evidenceSummary);
    }

    private FormulaDiagnosticsTourManifestCapture CreateFormulaDiagnosticsCapture(
        string state,
        string fileName,
        string surface,
        string captureMethod,
        double logicalWidth,
        double logicalHeight,
        string evidenceSummary)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var selectedRange = SheetGrid.SelectedRange;
        return new FormulaDiagnosticsTourManifestCapture(
            CaptureKey: $"formula-diagnostics:{state}",
            PairKey: $"interactive:formula-diagnostics:{state}",
            ScenarioId: "formula-diagnostics:visual-evidence",
            State: state,
            Surface: surface,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: captureMethod,
            CaptureLogicalWidth: logicalWidth,
            CaptureLogicalHeight: logicalHeight,
            SelectedRange: selectedRange?.ToString() ?? string.Empty,
            ShowFormulas: sheet?.ShowFormulas == true,
            FormulaTraceArrowCount: _formulaTraceArrows.Count,
            WatchCount: WatchWindowService.GetEntries(_workbook).Count,
            EvidenceSummary: evidenceSummary);
    }

    private static void DeleteFormulaDiagnosticsTourEvidence(string outputDir)
    {
        foreach (var file in Directory.EnumerateFiles(outputDir, "freex_formula_diagnostics_*.png"))
            File.Delete(file);

        var manifestPath = Path.Combine(outputDir, FormulaDiagnosticsTourManifestFileName);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);
    }

    private static void ValidateFormulaDiagnosticsTourEvidence(
        string outputDir,
        IReadOnlyList<FormulaDiagnosticsTourManifestCapture> captures)
    {
        var missing = captures
            .Select(capture => capture.OutputFileName)
            .Where(fileName => !File.Exists(Path.Combine(outputDir, fileName)))
            .ToArray();

        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"Formula diagnostics tour did not create {missing.Length} planned capture(s): {string.Join(", ", missing)}.");
    }

    private async Task CaptureKeyTipOverlayTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteKeyTipOverlayTourEvidence(outputDir);

        var captures = new List<KeyTipOverlayTourManifestCapture>();

        try
        {
            await ApplyScreenshotTourWidthAsync(new RibbonScreenshotTourWidth("1100", 1100));
            await CaptureKeyTipOverlayWindowStateAsync(
                outputDir,
                captures,
                "top-level-tabs-qat",
                "top-level",
                "Top-level Alt/F10 mode with top-level tab and QAT badges.",
                () => EnterRibbonKeyTipMode(RibbonKeyTipScope.TopLevel));

            await CaptureKeyTipOverlayWindowStateAsync(
                outputDir,
                captures,
                "home-visible-commands",
                "commands",
                "Home command scope with visible command badges, including combo box and dropdown-command placements.",
                () =>
                {
                    SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == "Home"));
                    EnterRibbonKeyTipMode(RibbonKeyTipScope.Commands);
                });

            await CaptureKeyTipOverlayMenuStateAsync(outputDir, captures);

            await ApplyScreenshotTourWidthAsync(new RibbonScreenshotTourWidth("750", 750));
            await CaptureKeyTipOverlayWindowStateAsync(
                outputDir,
                captures,
                "narrow-home-collapsed-commands",
                "commands",
                "Narrow Home command scope with generated collapsed-group keytip badges.",
                () =>
                {
                    SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == "Home"));
                    EnterRibbonKeyTipMode(RibbonKeyTipScope.Commands);
                },
                requireCollapsedGroupBadges: true);

            ValidateKeyTipOverlayTourEvidence(outputDir, captures);
            await WriteKeyTipOverlayTourManifestAsync(outputDir, captures);
        }
        catch
        {
            DeleteKeyTipOverlayTourEvidence(outputDir);
            throw;
        }
        finally
        {
            ExitRibbonKeyTipMode();
        }
    }

    private async Task CaptureKeyTipOverlayWindowStateAsync(
        string outputDir,
        List<KeyTipOverlayTourManifestCapture> captures,
        string fileName,
        string scope,
        string stateDescription,
        Action prepareState,
        bool requireCollapsedGroupBadges = false)
    {
        ExitRibbonKeyTipMode();
        prepareState();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await Task.Delay(350);
        UpdateLayout();

        var badgeCount = KeyTipOverlay.Children.OfType<Border>().Count();
        var collapsedBadgeCount = string.Equals(scope, "commands", StringComparison.Ordinal)
            ? GetVisibleKeyTipElements(RibbonKeyTipScope.Commands).Count(RibbonMetadata.IsCollapsedGroupButton)
            : 0;
        if (badgeCount == 0)
            throw new InvalidOperationException($"Keytip overlay tour state '{fileName}' produced no badges.");
        if (requireCollapsedGroupBadges && collapsedBadgeCount == 0)
            throw new InvalidOperationException($"Keytip overlay tour state '{fileName}' did not expose any collapsed-group badges.");

        await CaptureCurrentWindowAsync(outputDir, fileName, ScreenshotTourCaptureHeight);
        captures.Add(new KeyTipOverlayTourManifestCapture(
            CaptureKey: $"keytip-overlay:{scope}:{fileName}",
            State: fileName,
            Scope: scope,
            Description: stateDescription,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: "RenderTargetBitmap-window-top-band",
            CaptureLogicalWidth: ActualWidth,
            CaptureLogicalHeight: ScreenshotTourCaptureHeight,
            BadgeCount: badgeCount,
            CollapsedGroupBadgeCount: collapsedBadgeCount,
            MenuItemKeyTipCount: 0,
            IsInProcess: true,
            IsForegroundGuarded: !IsScreenshotTourBackgroundRenderAllowed()));
    }

    private async Task CaptureKeyTipOverlayMenuStateAsync(
        string outputDir,
        List<KeyTipOverlayTourManifestCapture> captures)
    {
        ExitRibbonKeyTipMode();
        await ApplyScreenshotTourWidthAsync(new RibbonScreenshotTourWidth("1100", 1100));
        SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == "Home"));
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();

        EnterRibbonKeyTipMode(RibbonKeyTipScope.TopLevel);
        HandleActiveRibbonKeyTip(Key.H);
        HandleActiveRibbonKeyTip(Key.B);
        await Task.Delay(350);
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();

        var menu = _activeRibbonKeyTipMenu
            ?? throw new InvalidOperationException("Keytip overlay tour could not open Home > Borders menu with Alt,H,B.");
        menu.UpdateLayout();
        var menuKeyTipCount = GetEnabledMenuItems(menu)
            .Count(item => !string.IsNullOrWhiteSpace(RibbonTooltip.GetKeyTip(item)));

        await CaptureElementAsync(menu, outputDir, "home-borders-menu-scope");
        captures.Add(new KeyTipOverlayTourManifestCapture(
            CaptureKey: "keytip-overlay:menu:home-borders-menu-scope",
            State: "home-borders-menu-scope",
            Scope: "menu",
            Description: "Home Borders dropdown opened through keytip routing; menu item keytips are rendered as scoped input gesture text.",
            FileName: "home-borders-menu-scope",
            OutputFileName: "home-borders-menu-scope.png",
            CaptureMethod: "RenderTargetBitmap-context-menu",
            CaptureLogicalWidth: menu.ActualWidth,
            CaptureLogicalHeight: menu.ActualHeight,
            BadgeCount: 0,
            CollapsedGroupBadgeCount: 0,
            MenuItemKeyTipCount: menuKeyTipCount,
            IsInProcess: true,
            IsForegroundGuarded: false));

        HandleActiveRibbonKeyTip(Key.C);
        await Task.Delay(350);
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();

        var submenuChild = FindOpenPopupChild(menu)
            ?? throw new InvalidOperationException("Keytip overlay tour could not locate the open Borders > Line Color submenu popup.");
        var activeItemsControl = _activeRibbonKeyTipItemsControl
            ?? throw new InvalidOperationException("Keytip overlay tour did not retain the nested menu keytip scope.");
        var nestedKeyTipCount = GetEnabledMenuItems(activeItemsControl)
            .Count(item => !string.IsNullOrWhiteSpace(RibbonTooltip.GetKeyTip(item)));

        await CaptureElementAsync(submenuChild, outputDir, "home-borders-line-color-submenu-scope");
        captures.Add(new KeyTipOverlayTourManifestCapture(
            CaptureKey: "keytip-overlay:menu:home-borders-line-color-submenu-scope",
            State: "home-borders-line-color-submenu-scope",
            Scope: "nested-menu",
            Description: "Home Borders > Line Color submenu opened through keytip routing after Alt,H,B,C.",
            FileName: "home-borders-line-color-submenu-scope",
            OutputFileName: "home-borders-line-color-submenu-scope.png",
            CaptureMethod: "RenderTargetBitmap-menu-popup-child",
            CaptureLogicalWidth: submenuChild.ActualWidth,
            CaptureLogicalHeight: submenuChild.ActualHeight,
            BadgeCount: 0,
            CollapsedGroupBadgeCount: 0,
            MenuItemKeyTipCount: nestedKeyTipCount,
            IsInProcess: true,
            IsForegroundGuarded: false));
    }

    private static void DeleteKeyTipOverlayTourEvidence(string outputDir)
    {
        foreach (var file in Directory.EnumerateFiles(outputDir, "*.png"))
            File.Delete(file);

        var manifestPath = Path.Combine(outputDir, KeyTipOverlayTourManifestFileName);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);
    }

    private static void ValidateKeyTipOverlayTourEvidence(
        string outputDir,
        IReadOnlyList<KeyTipOverlayTourManifestCapture> captures)
    {
        var missing = captures
            .Select(capture => capture.OutputFileName)
            .Where(fileName => !File.Exists(Path.Combine(outputDir, fileName)))
            .ToArray();

        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"Keytip overlay tour did not create {missing.Length} planned capture(s): {string.Join(", ", missing)}.");
    }

    private async Task CaptureRibbonTourAsync(string outputDir, RibbonScreenshotTourPlan plan)
    {
        DeleteRibbonScreenshotTourEvidence(outputDir, plan);

        try
        {
            await PrepareRibbonScreenshotTourContextAsync(plan.Context);

            if (plan.IsBurst)
            {
                await CaptureRibbonBurstTourAsync(outputDir, plan);
                ValidateRibbonScreenshotTourCaptures(outputDir, plan);
                await WriteRibbonScreenshotTourManifestAsync(outputDir, plan);
                return;
            }

            RibbonScreenshotTourWidth? activeWidth = null;
            foreach (var capture in plan.Captures)
            {
                if (!Equals(activeWidth, capture.Width))
                {
                    await ApplyScreenshotTourWidthAsync(capture.Width);
                    activeWidth = capture.Width;
                }

                await CaptureRibbonTabAsync(outputDir, capture);
            }

            ValidateRibbonScreenshotTourCaptures(outputDir, plan);
            await WriteRibbonScreenshotTourManifestAsync(outputDir, plan);
        }
        catch
        {
            DeleteRibbonScreenshotTourEvidence(outputDir, plan);
            throw;
        }
    }

    private static void DeleteStaleRibbonScreenshotTourCaptures(string outputDir, RibbonScreenshotTourPlan plan)
    {
        foreach (var capture in plan.Captures)
        {
            var path = Path.Combine(outputDir, $"{capture.FileName}.png");
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void DeleteRibbonScreenshotTourEvidence(string outputDir, RibbonScreenshotTourPlan plan)
    {
        DeleteStaleRibbonScreenshotTourCaptures(outputDir, plan);

        var manifestPath = Path.Combine(outputDir, RibbonScreenshotTourManifestFileName);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);
    }

    private static void ValidateRibbonScreenshotTourCaptures(string outputDir, RibbonScreenshotTourPlan plan)
    {
        var missing = plan.Captures
            .Select(capture => capture.OutputFileName)
            .Where(fileName => !File.Exists(Path.Combine(outputDir, fileName)))
            .ToArray();

        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"Ribbon screenshot tour did not create {missing.Length} planned capture(s): {string.Join(", ", missing)}.");
    }

    private async Task ApplyScreenshotTourWidthAsync(RibbonScreenshotTourWidth width)
    {
        ApplyScreenshotTourWidth(width);

        if (width.WindowWidth is not null)
        {
            await Task.Delay(600);
            return;
        }

        await Task.Delay(1200);
    }

    private async Task CaptureRibbonTabAsync(string outputDir, RibbonScreenshotTourCapture capture)
    {
        SelectRibbonTourTab(capture.Tab);
        UpdateLayout();
        await Task.Delay(350);
        UpdateLayout();

        await CaptureCurrentWindowAsync(outputDir, capture.FileName, ScreenshotTourCaptureHeight);
    }

    private async Task PrepareRibbonScreenshotTourContextAsync(string? context)
    {
        if (context is null)
            return;

        switch (context)
        {
            case "table":
                EnsureTableDesignScreenshotTourContext();
                break;
            case "pivot":
                EnsurePivotTableScreenshotTourContext();
                break;
            default:
                throw new InvalidOperationException($"Unknown ribbon screenshot tour context '{context}'.");
        }

        UpdateViewport();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
    }

    private void EnsureTableDesignScreenshotTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet();
        if (sheet is null)
            return;

        var headers = new[] { "Region", "Product", "Sales" };
        var rows = new[]
        {
            new object[] { "North", "Coffee", 1280d },
            new object[] { "South", "Tea", 960d },
            new object[] { "West", "Cocoa", 1140d }
        };

        for (var col = 0; col < headers.Length; col++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, (uint)(col + 1)), new TextValue(headers[col]));

        for (var row = 0; row < rows.Length; row++)
        {
            for (var col = 0; col < headers.Length; col++)
            {
                var address = new CellAddress(sheet.Id, (uint)(row + 2), (uint)(col + 1));
                if (rows[row][col] is double number)
                    sheet.SetCell(address, new NumberValue(number));
                else
                    sheet.SetCell(address, new TextValue(rows[row][col].ToString() ?? ""));
            }
        }

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3));
        var table = FindScreenshotTourTable(sheet);
        if (table is null)
        {
            table = new StructuredTableModel
            {
                Id = sheet.StructuredTables.Count == 0 ? 1 : sheet.StructuredTables.Max(candidate => candidate.Id) + 1,
                Name = ScreenshotTourTableName,
                DisplayName = ScreenshotTourTableName,
                Range = range,
                HasAutoFilter = true,
                HeaderRowCount = 1,
                StyleName = "TableStyleMedium2",
                ShowRowStripes = true
            };

            for (var index = 0; index < headers.Length; index++)
                table.Columns.Add(new StructuredTableColumnModel(index + 1, headers[index]));

            sheet.StructuredTables.Add(table);
        }

        if (SheetGrid is not null)
            SheetGrid.SelectedRange = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 2, 2));
    }

    private void EnsurePivotTableScreenshotTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet();
        if (sheet is null)
            return;

        _currentSheetId = sheet.Id;
        var headers = new[] { "Region", "Product", "Sales" };
        var rows = new[]
        {
            new object[] { "North", "Coffee", 1280d },
            new object[] { "North", "Tea", 760d },
            new object[] { "South", "Coffee", 960d },
            new object[] { "West", "Cocoa", 1140d }
        };

        for (var col = 0; col < headers.Length; col++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, (uint)(col + 1)), new TextValue(headers[col]));

        for (var row = 0; row < rows.Length; row++)
        {
            for (var col = 0; col < headers.Length; col++)
            {
                var address = new CellAddress(sheet.Id, (uint)(row + 2), (uint)(col + 1));
                if (rows[row][col] is double number)
                    sheet.SetCell(address, new NumberValue(number));
                else
                    sheet.SetCell(address, new TextValue(rows[row][col].ToString() ?? ""));
            }
        }

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3));
        var pivotTable = FindScreenshotTourPivotTable(sheet);
        if (pivotTable is null)
        {
            var targetRange = new GridRange(new CellAddress(sheet.Id, 2, 5), new CellAddress(sheet.Id, 8, 8));
            var command = new AddPivotTableCommand(
                sheet.Id,
                sourceRange,
                targetRange,
                ScreenshotTourPivotTableName,
                rowFieldIndexes: [0],
                dataFieldIndexes: [2]);

            if (!TryExecuteCommand(command, "Insert PivotTable", out var outcome))
                throw new InvalidOperationException(outcome.ErrorMessage ?? "PivotTable screenshot tour setup failed.");

            pivotTable = FindScreenshotTourPivotTable(sheet);
        }

        if (pivotTable is not null && SheetGrid is not null)
        {
            SheetGrid.SelectedRange = new GridRange(pivotTable.TargetRange.Start, pivotTable.TargetRange.Start);
            RefreshPivotFieldListPane();
        }
    }

    private Sheet? GetCurrentOrFirstScreenshotTourSheet()
    {
        var currentSheet = _workbook.GetSheet(_currentSheetId);
        if (currentSheet is not null)
            return currentSheet;

        foreach (var sheet in _workbook.Sheets)
            return sheet;

        return null;
    }

    private static StructuredTableModel? FindScreenshotTourTable(Sheet sheet)
    {
        foreach (var table in sheet.StructuredTables)
        {
            if (string.Equals(table.Name, ScreenshotTourTableName, StringComparison.OrdinalIgnoreCase))
                return table;
        }

        return null;
    }

    private static PivotTableModel? FindScreenshotTourPivotTable(Sheet sheet)
    {
        foreach (var pivotTable in sheet.PivotTables)
        {
            if (string.Equals(pivotTable.Name, ScreenshotTourPivotTableName, StringComparison.OrdinalIgnoreCase))
                return pivotTable;
        }

        return null;
    }

    private async Task CaptureRibbonBurstTourAsync(string outputDir, RibbonScreenshotTourPlan plan)
    {
        foreach (var width in plan.Widths)
        {
            ApplyScreenshotTourWidth(width);

            foreach (var tab in plan.Tabs)
            {
                SelectRibbonTourTab(tab);

                foreach (var phase in plan.Phases)
                {
                    await PrepareRibbonBurstCapturePhaseAsync(phase);
                    var capture = new RibbonScreenshotTourCapture(tab, width, phase);
                    await CaptureCurrentWindowAsync(outputDir, capture.FileName, ScreenshotTourCaptureHeight);
                }
            }
        }
    }

    private void ApplyScreenshotTourWidth(RibbonScreenshotTourWidth width)
    {
        if (width.WindowWidth is { } windowWidth)
        {
            WindowState = WindowState.Normal;
            Width = windowWidth;
            Height = 768;
            return;
        }

        WindowState = WindowState.Maximized;
    }

    private void SelectRibbonTourTab(RibbonScreenshotTourTab tab)
    {
        var tabItem = FindRibbonTourTab(tab);

        if (tabItem is null)
            throw new InvalidOperationException(
                $"Ribbon screenshot tour expected tab '{tab.Header}' ({tab.CatalogId}) but it was not found in the live ribbon.");

        RibbonTabs.SelectedItem = tabItem;
    }

    private TabItem? FindRibbonTourTab(RibbonScreenshotTourTab tab)
    {
        foreach (var item in RibbonTabs.Items)
        {
            if (item is TabItem tabItem &&
                RibbonMetadata.TryGetCatalogId(tabItem, out var catalogId) &&
                string.Equals(catalogId, tab.CatalogId, StringComparison.Ordinal))
                return tabItem;
        }

        foreach (var item in RibbonTabs.Items)
        {
            if (item is TabItem tabItem &&
                string.Equals(tabItem.Header?.ToString(), tab.Header, StringComparison.Ordinal))
                return tabItem;
        }

        return null;
    }

    private async Task PrepareRibbonBurstCapturePhaseAsync(RibbonScreenshotTourPhase phase)
    {
        switch (phase.Label)
        {
            case "immediate":
                UpdateLayout();
                return;
            case "first-render":
                await WaitForRibbonScreenshotRenderPassAsync();
                return;
            case "settled":
                await Task.Delay(350);
                UpdateLayout();
                await WaitForRibbonScreenshotRenderPassAsync();
                return;
            default:
                throw new InvalidOperationException($"Unknown ribbon screenshot tour burst phase '{phase.Label}'.");
        }
    }

    private async Task WaitForRibbonScreenshotRenderPassAsync()
    {
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

    private async Task CaptureCurrentWindowAsync(string outputDir, string fileName, double logicalHeight)
    {
        await EnsureWindowForegroundForScreenshotTourAsync($"capturing {fileName}.png");

        var source = PresentationSource.FromVisual(this);
        var dpiX = source?.CompositionTarget.TransformToDevice.M11 ?? 1.0;
        var dpiY = source?.CompositionTarget.TransformToDevice.M22 ?? 1.0;
        int pw = Math.Max(1, (int)(ActualWidth * dpiX));
        int ph = Math.Max(1, (int)(Math.Min(ActualHeight, logicalHeight) * dpiY));

        var rtb = new RenderTargetBitmap(pw, ph, 96 * dpiX, 96 * dpiY, PixelFormats.Pbgra32);
        AssertWindowForegroundForScreenshotTour($"rendering {fileName}.png");
        rtb.Render(this);
        AssertWindowForegroundForScreenshotTour($"saving {fileName}.png");
        var bitmap = new CroppedBitmap(rtb, new Int32Rect(0, 0, pw, ph));

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(outputDir, $"{fileName}.png");
        await using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private async Task EnsureWindowForegroundForScreenshotTourAsync(string operation)
    {
        if (IsScreenshotTourBackgroundRenderAllowed())
        {
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            return;
        }

        Activate();
        Focus();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        AssertWindowForegroundForScreenshotTour(operation);
    }

    private static async Task EnsureWindowForegroundForScreenshotTourAsync(Window window, string operation)
    {
        if (IsScreenshotTourBackgroundRenderAllowed())
        {
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            return;
        }

        window.Activate();
        window.Focus();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        AssertWindowForegroundForScreenshotTour(window, operation);
    }

    private void AssertWindowForegroundForScreenshotTour(string operation)
    {
        if (IsScreenshotTourBackgroundRenderAllowed())
            return;

        var expectedWindowHandle = new WindowInteropHelper(this).Handle;
        var foregroundWindowHandle = GetForegroundWindow();
        if (expectedWindowHandle == IntPtr.Zero ||
            foregroundWindowHandle != expectedWindowHandle ||
            !IsActive)
        {
            throw new InvalidOperationException(
                $"Screenshot tour blocked: FreeX main window must own foreground focus before {operation}; " +
                $"foreground handle 0x{foregroundWindowHandle.ToInt64():X}, expected 0x{expectedWindowHandle.ToInt64():X}.");
        }
    }

    private static void AssertWindowForegroundForScreenshotTour(Window window, string operation)
    {
        if (IsScreenshotTourBackgroundRenderAllowed())
            return;

        var expectedWindowHandle = new WindowInteropHelper(window).Handle;
        var foregroundWindowHandle = GetForegroundWindow();
        if (expectedWindowHandle == IntPtr.Zero ||
            foregroundWindowHandle != expectedWindowHandle ||
            !window.IsActive)
        {
            throw new InvalidOperationException(
                $"Screenshot tour blocked: expected WPF window must own foreground focus before {operation}; " +
                $"foreground handle 0x{foregroundWindowHandle.ToInt64():X}, expected 0x{expectedWindowHandle.ToInt64():X}.");
        }
    }

    private static bool IsScreenshotTourBackgroundRenderAllowed() =>
        Environment.GetEnvironmentVariable(ScreenshotTourAllowBackgroundRenderEnvVar) == "1";

    private static async Task WriteRibbonScreenshotTourManifestAsync(string outputDir, RibbonScreenshotTourPlan plan)
    {
        var manifest = new RibbonScreenshotTourManifest(
            Tool: "FREEX_SS_TOUR",
            EvidenceFamily: "ribbon",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            OutputDirectory: outputDir,
            OutputNaming: "<WidthLabel>_<RibbonTab>[_<Phase>].png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            Context: plan.Context,
            BurstMode: plan.IsBurst,
            CaptureLogicalHeight: ScreenshotTourCaptureHeight,
            PlannedCaptureCount: plan.Captures.Count,
            ActualCaptureCount: plan.Captures.Count,
            CaptureStatus: "complete",
            CaptureMethod: "RenderTargetBitmap-window-top-band",
            Pairing: new RibbonScreenshotTourManifestPairing(
                "ribbon:<WidthLabel>:<TabFileName>",
                "excel",
                "screenshot_excel.ps1",
                "excel_<WidthLabel>_<RibbonTab>.png"),
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed in-process RenderTargetBitmap capture without OS foreground ownership; no global mouse, keyboard, or screen capture input is used."
                    : "Abort and clear current PNG/manifest evidence unless the FreeX main window owns foreground focus immediately before render and file write."),
            Tabs: plan.Tabs.Select(tab => tab.Header).ToArray(),
            Widths: plan.Widths
                .Select(width => new RibbonScreenshotTourManifestWidth(
                    width.Label,
                    width.WindowWidth,
                    width.EvidencePurpose()))
                .ToArray(),
            Phases: plan.Phases
                .Select(phase => new RibbonScreenshotTourManifestPhase(phase.Label, phase.FileNameSuffix))
                .ToArray(),
            Captures: plan.Captures
                .Select(capture => new RibbonScreenshotTourManifestCapture(
                    capture.CaptureKey,
                    capture.PairKey,
                    capture.Tab.Header,
                    capture.Tab.FileName,
                    capture.Width.Label,
                    capture.Phase.Label,
                    capture.FileName,
                    capture.OutputFileName,
                    capture.CounterpartFileName))
                .ToArray(),
            Limitations:
            [
                "Ribbon captures cover the top window band only.",
                "Transient popups, dropdowns, native dialogs, and context menus require separate guarded captures.",
                "This in-app tour deletes only the currently requested plan's expected PNG files before capture.",
                IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 was used for in-process rendering; pair with foreground-guarded screen captures when validating OS compositing or input focus."
                    : "The in-app tour aborts before file write unless the FreeX main window owns foreground focus."
            ]);

        var path = Path.Combine(outputDir, RibbonScreenshotTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.RibbonScreenshotTourManifest);
    }

    private static async Task WriteAutoFilterFlyoutTourManifestAsync(
        string outputDir,
        AutoFilterDialog dialog,
        AutoFilterDropdownPlan plan)
    {
        var capture = new AutoFilterFlyoutTourManifestCapture(
            CaptureKey: "interactive:table-autofilter-dropdown:opened",
            PairKey: "interactive:table-autofilter-dropdown:opened",
            ScenarioId: "popup:table-autofilter-dropdown",
            State: "opened",
            FileName: AutoFilterFlyoutTourCaptureFileName,
            OutputFileName: $"{AutoFilterFlyoutTourCaptureFileName}.png",
            CounterpartFileName: "interactive_table_autofilter_dropdown_opened.png",
            CaptureLogicalWidth: dialog.ActualWidth,
            CaptureLogicalHeight: dialog.ActualHeight);

        var manifest = new AutoFilterFlyoutTourManifest(
            Tool: "FREEX_AUTOFILTER_FLYOUT_TOUR",
            EvidenceFamily: "popup",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "popup:table-autofilter-dropdown",
            OutputDirectory: outputDir,
            OutputNaming: "freex_table_autofilter_dropdown.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            HeaderCell: plan.Range.Start.ToA1(),
            HeaderText: "score",
            AutoFilterRange: plan.Range.ToString(),
            FilterColumnOffset: plan.FilterColumnOffset,
            CaptureStatus: "complete",
            CaptureMethod: "RenderTargetBitmap-autofilter-flyout-window",
            Pairing: new AutoFilterFlyoutTourManifestPairing(
                "interactive:table-autofilter-dropdown:<State>",
                "excel",
                "screenshot_excel.ps1",
                "interactive_table_autofilter_dropdown_opened.png"),
            Captures: [capture],
            Limitations:
            [
                "This in-app tour captures the actual FreeX AutoFilter flyout window without global mouse or keyboard input.",
                "The paired Microsoft Excel transient capture is declared by tools/screenshot_excel.ps1 and remains a separate foreground-guarded capture.",
                "The scenario opens the worksheet AutoFilter dropdown for the score header against numeric values 1-4 plus a blank row."
            ]);

        var path = Path.Combine(outputDir, AutoFilterFlyoutTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.AutoFilterFlyoutTourManifest);
    }

    private static async Task WriteHomeNumberFormatDropdownTourManifestAsync(string outputDir, FrameworkElement popupChild)
    {
        var capture = new HomeNumberFormatDropdownTourManifestCapture(
            CaptureKey: "interactive:home-number-format:opened",
            PairKey: "interactive:home-number-format:opened",
            ScenarioId: "dropdown:home-number-format",
            State: "opened",
            FileName: HomeNumberFormatDropdownTourCaptureFileName,
            OutputFileName: $"{HomeNumberFormatDropdownTourCaptureFileName}.png",
            CounterpartFileName: "interactive_home_number_format_opened.png",
            CaptureLogicalWidth: popupChild.ActualWidth,
            CaptureLogicalHeight: popupChild.ActualHeight);

        var manifest = new HomeNumberFormatDropdownTourManifest(
            Tool: "FREEX_HOME_NUMBER_FORMAT_DROPDOWN_TOUR",
            EvidenceFamily: "dropdown",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "dropdown:home-number-format",
            OutputDirectory: outputDir,
            OutputNaming: "freex_dropdown_home_number_format_opened.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            SelectedCell: "A1",
            SelectedFormat: HomeNumberFormatDropdownPlanner.Options[HomeNumberFormatDropdownPlanner.DefaultSelectionIndex].Label,
            OptionLabels: HomeNumberFormatDropdownPlanner.Options.Select(option => option.Label).ToArray(),
            CaptureStatus: "complete",
            CaptureMethod: "RenderTargetBitmap-combobox-popup-child",
            Pairing: new HomeNumberFormatDropdownTourManifestPairing(
                "interactive:home-number-format:<State>",
                "excel",
                "screenshot_excel.ps1",
                "interactive_home_number_format_opened.png"),
            Captures: [capture],
            Limitations:
            [
                "This in-app tour opens the production Home Number Format ComboBox and captures the open WPF popup child without global mouse or keyboard input.",
                "The paired Microsoft Excel transient capture is declared by tools/screenshot_excel.ps1 and remains a separate foreground-guarded capture.",
                "The scenario captures the opened dropdown with the default General format selected."
            ]);

        var path = Path.Combine(outputDir, HomeNumberFormatDropdownTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.HomeNumberFormatDropdownTourManifest);
    }

    private static async Task WriteHomeAlignmentNumberTourManifestAsync(
        string outputDir,
        HomeAlignmentNumberTourContext context,
        IReadOnlyList<HomeAlignmentNumberTourManifestCapture> captures)
    {
        var manifest = new HomeAlignmentNumberTourManifest(
            Tool: "FREEX_HOME_ALIGNMENT_NUMBER_TOUR",
            EvidenceFamily: "home-ribbon",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "home:alignment-number",
            OutputDirectory: outputDir,
            OutputNaming: "freex_home_alignment_*.png, freex_home_number_*.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            SheetName: context.SheetName,
            AlignmentRange: context.AlignmentRange.ToString(),
            NumberRange: context.NumberRange.ToString(),
            SampleFormats: context.SampleFormats,
            CaptureStatus: "complete",
            CaptureMethod: "RenderTargetBitmap-main-window-context-menu-and-dialogs",
            Pairing: new HomeAlignmentNumberTourManifestPairing(
                "interactive:home-alignment-number:<State>",
                "excel",
                "screenshot_excel.ps1",
                "interactive_home_alignment_number_<state>.png"),
            Captures: captures,
            Limitations:
            [
                "This in-app tour seeds worksheet cells, executes the production FreeX style command path, and captures WPF output with RenderTargetBitmap.",
                "The paired Microsoft Excel transient captures remain a separate foreground-guarded capture set.",
                "The tour covers visible Home Alignment and Number group command rendering, Orientation menu shape, and Format Cells Alignment/Number entry states; save/reload and locale-specific number-format fidelity remain follow-up verification."
            ]);

        var path = Path.Combine(outputDir, HomeAlignmentNumberTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.HomeAlignmentNumberTourManifest);
    }

    private static async Task WriteWorksheetContextMenuTourManifestAsync(
        string outputDir,
        ContextMenu menu,
        CellAddress address)
    {
        var menuHeaders = menu.Items
            .OfType<MenuItem>()
            .Select(item => item.Header?.ToString() ?? string.Empty)
            .Where(header => !string.IsNullOrWhiteSpace(header))
            .ToArray();

        var capture = new WorksheetContextMenuTourManifestCapture(
            CaptureKey: "interactive:worksheet-cell-context-menu:opened",
            PairKey: "interactive:worksheet-cell-context-menu:opened",
            ScenarioId: "context-menu:worksheet-cell",
            State: "opened",
            FileName: WorksheetContextMenuTourCaptureFileName,
            OutputFileName: $"{WorksheetContextMenuTourCaptureFileName}.png",
            CounterpartFileName: "interactive_worksheet_cell_context_menu_opened.png",
            CaptureLogicalWidth: menu.ActualWidth,
            CaptureLogicalHeight: menu.ActualHeight);

        var manifest = new WorksheetContextMenuTourManifest(
            Tool: "FREEX_WORKSHEET_CONTEXT_MENU_TOUR",
            EvidenceFamily: "context-menu",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "context-menu:worksheet-cell",
            OutputDirectory: outputDir,
            OutputNaming: "freex_context_menu_worksheet_cell_opened.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            SelectedCell: address.ToA1(),
            EntryPath: "keyboard-context-menu-point",
            MenuHeaders: menuHeaders,
            CaptureStatus: "complete",
            CaptureMethod: "RenderTargetBitmap-worksheet-context-menu",
            Pairing: new WorksheetContextMenuTourManifestPairing(
                "interactive:worksheet-cell-context-menu:<State>",
                "excel",
                "screenshot_excel.ps1",
                "interactive_worksheet_cell_context_menu_opened.png"),
            Captures: [capture],
            Limitations:
            [
                "This in-app tour opens the production worksheet-cell ContextMenu and captures the live WPF menu without global mouse or keyboard input.",
                "The paired Microsoft Excel transient capture remains a separate foreground-guarded capture.",
                "The scenario captures the default worksheet-cell context menu for A1."
            ]);

        var path = Path.Combine(outputDir, WorksheetContextMenuTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.WorksheetContextMenuTourManifest);
    }

    private static async Task WriteHomeBordersDropdownTourManifestAsync(string outputDir, ContextMenu menu)
    {
        var menuHeaders = menu.Items
            .OfType<MenuItem>()
            .Select(item => item.Header?.ToString() ?? string.Empty)
            .Where(header => !string.IsNullOrWhiteSpace(header))
            .ToArray();

        var capture = new HomeBordersDropdownTourManifestCapture(
            CaptureKey: "interactive:home-borders:opened",
            PairKey: "interactive:home-borders:opened",
            ScenarioId: "dropdown:home-borders",
            State: "opened",
            FileName: HomeBordersDropdownTourCaptureFileName,
            OutputFileName: $"{HomeBordersDropdownTourCaptureFileName}.png",
            CounterpartFileName: "interactive_home_borders_opened.png",
            CaptureLogicalWidth: menu.ActualWidth,
            CaptureLogicalHeight: menu.ActualHeight);

        var manifest = new HomeBordersDropdownTourManifest(
            Tool: "FREEX_HOME_BORDERS_DROPDOWN_TOUR",
            EvidenceFamily: "dropdown",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "dropdown:home-borders",
            OutputDirectory: outputDir,
            OutputNaming: "freex_dropdown_home_borders_opened.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            EntryPath: "Home > Borders",
            MenuHeaders: menuHeaders,
            CaptureStatus: "complete",
            CaptureMethod: "RenderTargetBitmap-context-menu",
            Pairing: new HomeBordersDropdownTourManifestPairing(
                "interactive:home-borders:<State>",
                "excel",
                "screenshot_excel.ps1",
                "interactive_home_borders_opened.png"),
            Captures: [capture],
            Limitations:
            [
                "This in-app tour opens the production Home Borders menu and captures the live WPF ContextMenu without global mouse or keyboard input.",
                "The paired Microsoft Excel transient capture remains a separate foreground-guarded capture.",
                "The scenario captures the top-level Borders menu; nested Line Color and Line Style submenus are separate future captures."
            ]);

        var path = Path.Combine(outputDir, HomeBordersDropdownTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.HomeBordersDropdownTourManifest);
    }

    private static async Task WriteQatUndoRedoTourManifestAsync(
        string outputDir,
        CellAddress address,
        IReadOnlyList<QatUndoRedoTourManifestCapture> captures)
    {
        var manifest = new QatUndoRedoTourManifest(
            Tool: "FREEX_QAT_UNDO_REDO_TOUR",
            EvidenceFamily: "qat",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "qat:undo-redo",
            OutputDirectory: outputDir,
            OutputNaming: "freex_qat_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            SelectedCell: address.ToA1(),
            CaptureStatus: "complete",
            CaptureMethod: "RenderTargetBitmap-window-full-and-qat-history-context-menu",
            Pairing: new QatUndoRedoTourManifestPairing(
                "interactive:qat-undo-redo:<State>",
                "excel",
                "not-yet-wired",
                "not-yet-captured"),
            Captures: captures,
            Limitations:
            [
                "This in-app tour drives the real FreeX command bus and Quick Access Toolbar controls, then captures WPF output with RenderTargetBitmap.",
                "The tour does not use global mouse or keyboard input; foreground/live OS-input validation remains separate unless the capture is run without the background-render override.",
                "The edit and style mutation are created by the in-app harness through the same command stack used by routed UI commands, not by physical keyboard text entry.",
                "No Microsoft Excel counterpart capture is produced by this tool."
            ]);

        var path = Path.Combine(outputDir, QatUndoRedoTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.QatUndoRedoTourManifest);
    }

    private static async Task WriteTitlebarWindowChromeTourManifestAsync(
        string outputDir,
        IReadOnlyList<TitlebarWindowChromeTourManifestCapture> captures,
        string savedWorkbookPath)
    {
        var manifest = new TitlebarWindowChromeTourManifest(
            Tool: "FREEX_TITLEBAR_WINDOW_CHROME_TOUR",
            EvidenceFamily: "window-chrome",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "window-chrome:titlebar",
            OutputDirectory: outputDir,
            OutputNaming: "freex_titlebar_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            PlannedCaptureCount: 5,
            ActualCaptureCount: captures.Count,
            CaptureStatus: "complete",
            CaptureMethod: "RenderTargetBitmap-window-top-band",
            SavedWorkbookOutputFileName: Path.GetFileName(savedWorkbookPath),
            SavedWorkbookRetained: File.Exists(savedWorkbookPath),
            Pairing: new TitlebarWindowChromeTourManifestPairing(
                "interactive:titlebar-window-chrome:<State>",
                "excel",
                "not-yet-wired",
                "not-yet-captured"),
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed in-process RenderTargetBitmap capture; no global mouse, keyboard, close, minimize, or drag input was used."
                    : "Abort and clear titlebar/window-chrome tour evidence unless the FreeX main window owns foreground focus immediately before render and file write."),
            Captures: captures,
            Limitations:
            [
                "This tour captures real FreeX WPF titlebar/window chrome visuals and changes WindowState directly instead of using global mouse input.",
                "Minimize and Close are not clicked; evidence is limited to visible button/UIA state so the tour cannot lose unsaved work.",
                "Alt+Space/system menu, native titlebar drag, hover styling, and live mouse clicks remain foreground-runner gaps.",
                "The saved/renamed title state is produced through SaveWorkbookToTargetAsync against an XLSX target without opening the native Save As dialog.",
                "No Microsoft Excel counterpart capture is produced by this tool."
            ]);

        var path = Path.Combine(outputDir, TitlebarWindowChromeTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.TitlebarWindowChromeTourManifest);
    }

    private static async Task WriteFormulaBarNameBoxTourManifestAsync(
        string outputDir,
        FormulaBarNameBoxTourContext context,
        IReadOnlyList<FormulaBarNameBoxTourManifestCapture> captures)
    {
        var manifest = new FormulaBarNameBoxTourManifest(
            Tool: "FREEX_FORMULA_BAR_NAME_BOX_TOUR",
            EvidenceFamily: "formula-bar-name-box",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "formula-bar-name-box:visual-evidence",
            OutputDirectory: outputDir,
            OutputNaming: "freex_formula_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            SheetName: context.SheetName,
            NamedRangeName: context.NamedRangeName,
            NamedRangeAddress: context.NamedRangeAddress,
            StartCell: context.StartCell,
            CaptureStatus: "complete",
            CaptureMethod: "RenderTargetBitmap-window-full-top-band-dropdown-and-dialog",
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed in-process RenderTargetBitmap capture; no global mouse, keyboard, or screen capture input was used."
                    : "Abort before file write unless the expected FreeX window owns foreground focus for each window/dialog capture."),
            Pairing: new FormulaBarNameBoxTourManifestPairing(
                "interactive:formula-bar-name-box:<State>",
                "excel",
                "not-yet-wired",
                "not-yet-captured"),
            Captures: captures,
            CoveredStates:
            [
                "Name Box displays exact selected defined name",
                "Name Box dropdown opens and lists workbook defined names",
                "Name Box dropdown selection navigates to the named range",
                "Formula bar edit mode with Cancel and Enter controls",
                "Cancel restores formula bar text and worksheet focus",
                "Enter commits formula bar edit and returns worksheet focus",
                "Formula bar fx button focus and Insert Function dialog surface",
                "Expanded/collapsed formula bar visual state",
                "Formula bar focus",
                "Top-level keytips while focus starts in the Name Box"
            ],
            Limitations:
            [
                "This tour drives FreeX in process and captures WPF output with RenderTargetBitmap rather than OS CopyFromScreen.",
                "The Name Box dropdown is opened through the production ComboBox state, and the Sales dropdown navigation uses the production SelectionChanged path without global mouse input.",
                "The formula-bar Enter and Cancel evidence uses the production button handlers, but button activation is invoked in process rather than by physical mouse input.",
                "The Insert Function dialog capture uses the production InsertFunctionDialog shown by the tour because invoking the fx button's modal handler would block deterministic screenshot capture.",
                "The keytip capture enters the production top-level keytip mode while focus starts in the Name Box; it is not a physical Alt-key foreground input capture.",
                "No paired Microsoft Excel screenshots are produced by this tool."
            ]);

        var path = Path.Combine(outputDir, FormulaBarNameBoxTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.FormulaBarNameBoxTourManifest);
    }

    private static async Task WriteStatusFooterTourManifestAsync(
        string outputDir,
        IReadOnlyList<StatusFooterTourManifestCapture> captures)
    {
        var manifest = new StatusFooterTourManifest(
            Tool: "FREEX_STATUS_FOOTER_TOUR",
            EvidenceFamily: "status-footer",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "status-footer:visual-evidence",
            OutputDirectory: outputDir,
            OutputNaming: "freex_status_footer_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            CaptureStatus: "complete",
            CaptureMode: IsScreenshotTourBackgroundRenderAllowed()
                ? "background-render-opt-in"
                : "foreground-guarded-render",
            PlannedCaptureCount: captures.Count,
            ActualCaptureCount: captures.Count,
            Pairing: new StatusFooterTourManifestPairing(
                "interactive:status-footer:<State>",
                "manual-or-excel",
                "not-yet-wired",
                "not-yet-captured"),
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? "FREEX_SS_TOUR_ALLOW_BACKGROUND_RENDER=1 was set; no global mouse, keyboard, or screen capture input is used."
                    : "FreeX main window must own foreground focus before each RenderTargetBitmap window capture."),
            Captures: captures,
            Limitations:
            [
                "RenderTargetBitmap evidence only; it is not foreground CopyFromScreen proof.",
                "Zoom slider min/baseline/max are set programmatically through the in-app slider model; live mouse drag remains open.",
                "Ctrl+wheel, foreground mouse, native UIA RangeValue interaction, filtered selections, and multi-range visual stats remain open.",
                "Formula edit visual evidence covers Edit mode text; modal-dialog return and error status transitions remain open."
            ]);

        var path = Path.Combine(outputDir, StatusFooterTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.StatusFooterTourManifest);
    }

    private static async Task WriteFormulaDiagnosticsTourManifestAsync(
        string outputDir,
        FormulaDiagnosticsTourContext context,
        IReadOnlyList<FormulaDiagnosticsTourManifestCapture> captures)
    {
        var manifest = new FormulaDiagnosticsTourManifest(
            Tool: "FREEX_FORMULA_DIAGNOSTICS_TOUR",
            EvidenceFamily: "formula-diagnostics",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "formula-diagnostics:visual-evidence",
            OutputDirectory: outputDir,
            OutputNaming: "freex_formula_diagnostics_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            CatalogIds: ["UI-CAT-FORMULAS-002", "UI-CMD-FORM-003", "UI-CMD-FORM-005"],
            SheetName: context.SheetName,
            InputCell: context.InputCell.ToA1(),
            ResultCell: context.ResultCell.ToA1(),
            ErrorCell: context.ErrorCell.ToA1(),
            ResultFormula: context.ResultFormula,
            ErrorFormula: context.ErrorFormula,
            CaptureStatus: "complete",
            CaptureMode: IsScreenshotTourBackgroundRenderAllowed()
                ? "background-render-opt-in"
                : "foreground-guarded-render",
            PlannedCaptureCount: captures.Count,
            ActualCaptureCount: captures.Count,
            Pairing: new FormulaDiagnosticsTourManifestPairing(
                "interactive:formula-diagnostics:<State>",
                "excel",
                "not-yet-wired",
                "not-yet-captured"),
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed in-process RenderTargetBitmap capture; no foreground mouse, keyboard, or screen capture input was used."
                    : "Abort before file write unless the expected FreeX window/dialog owns foreground focus for each capture."),
            Captures: captures,
            CoveredStates:
            [
                "Trace Precedents visible arrows",
                "Trace Dependents visible arrows",
                "Remove Arrows cleared state",
                "Show Formulas enabled sheet state",
                "Error Checking dialog/list",
                "Evaluate Formula default button and one-step advance",
                "Add Watch dialog",
                "Watch Window list, refresh, and delete states"
            ],
            Limitations:
            [
                "This tour drives FreeX in process and captures WPF windows with RenderTargetBitmap; it is not foreground CopyFromScreen proof.",
                "No global mouse or keyboard input is synthesized; command handlers and WPF button events are invoked in process for deterministic capture.",
                "The Add Watch surface is captured by showing the production AddWatchDialog directly; the actual watch insertion then uses the same AddWatchFromSelection/WatchWindowService path as the command.",
                "The Evaluate Formula dialog is shown modeless so the tour can capture the default command and a stepped state without blocking on ShowDialog.",
                "The trace-arrow and show-formulas captures are FreeX-only visual states; no paired Microsoft Excel evidence is produced by this tool."
            ]);

        var path = Path.Combine(outputDir, FormulaDiagnosticsTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.FormulaDiagnosticsTourManifest);
    }

    private static async Task WritePrintPreviewTourManifestAsync(
        string outputDir,
        Sheet sheet,
        int totalPages,
        bool closedViaEscapeEquivalent,
        bool focusReturned)
    {
        var includeLastPage = totalPages > 1;
        var captures = new List<PrintPreviewTourManifestCapture>
        {
            new(
                CaptureKey: "print-preview:file-print-entry:opened",
                PairKey: "interactive:print-preview:file-print-entry:opened",
                ScenarioId: "print-preview:file-print-entry",
                State: "opened",
                EntryPath: "File > Print",
                FileName: "freex_print_backstage_file_print_entry",
                OutputFileName: "freex_print_backstage_file_print_entry.png",
                EvidenceSummary: "Backstage Print view shows the Print Preview command and active sheet settings summary."),
            new(
                CaptureKey: "print-preview:ctrl-p-entry:opened",
                PairKey: "interactive:print-preview:ctrl-p-entry:opened",
                ScenarioId: "print-preview:ctrl-p-entry",
                State: "opened",
                EntryPath: "Ctrl+P routed to File > Print, then Print Preview",
                FileName: "freex_print_preview_ctrlp_entry_opened",
                OutputFileName: "freex_print_preview_ctrlp_entry_opened.png",
                EvidenceSummary: "Print Preview dialog opens with the production toolbar, preview surface, settings panel, and Print as the initial keyboard target."),
            new(
                CaptureKey: "print-preview:toolbar:first-page",
                PairKey: "interactive:print-preview:toolbar:first-page",
                ScenarioId: "print-preview:toolbar-navigation",
                State: "first-page",
                EntryPath: "File > Print > Print Preview",
                FileName: "freex_print_preview_toolbar_first_page",
                OutputFileName: "freex_print_preview_toolbar_first_page.png",
                EvidenceSummary: "Toolbar shows first-page navigation state, page count label, print controls, zoom, margins, page setup, close, and settings summary.")
        };

        if (includeLastPage)
        {
            captures.Add(new PrintPreviewTourManifestCapture(
                CaptureKey: "print-preview:toolbar:last-page",
                PairKey: "interactive:print-preview:toolbar:last-page",
                ScenarioId: "print-preview:toolbar-navigation",
                State: "last-page",
                EntryPath: "File > Print > Print Preview, page number box to final page",
                FileName: "freex_print_preview_toolbar_last_page",
                OutputFileName: "freex_print_preview_toolbar_last_page.png",
                EvidenceSummary: "Toolbar shows the final-page page-count label after keyboard-equivalent page-number navigation."));
        }

        captures.AddRange(
        [
            new PrintPreviewTourManifestCapture(
                CaptureKey: "print-preview:zoom-settings-summary:page-width",
                PairKey: "interactive:print-preview:zoom-settings-summary:page-width",
                ScenarioId: "print-preview:zoom-settings-summary",
                State: "page-width-zoom",
                EntryPath: "Print Preview > Zoom > Page Width",
                FileName: "freex_print_preview_zoom_settings_summary",
                OutputFileName: "freex_print_preview_zoom_settings_summary.png",
                EvidenceSummary: "Zoom combo is changed to Page Width while the print settings summary remains visible."),
            new PrintPreviewTourManifestCapture(
                CaptureKey: "print-preview:closed:focus-return",
                PairKey: "interactive:print-preview:closed:focus-return",
                ScenarioId: "print-preview:close-focus-return",
                State: "closed-focus-return",
                EntryPath: "Print Preview close via IsCancel Close button route",
                FileName: "freex_print_preview_closed_focus_return",
                OutputFileName: "freex_print_preview_closed_focus_return.png",
                EvidenceSummary: "Preview is closed and the workbook window is visible again with focus explicitly returned to the backstage Print Preview command.")
        ]);

        var manifest = new PrintPreviewTourManifest(
            Tool: "FREEX_PRINT_PREVIEW_TOUR",
            EvidenceFamily: "print-preview",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "print-preview:foreground-focus-return",
            OutputDirectory: outputDir,
            OutputNaming: "freex_print_preview_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            EntryPaths: ["Ctrl+P", "File > Print > Print Preview"],
            SheetName: sheet.Name,
            TotalPages: totalPages,
            SettingsSummary: PrintSettingsPlanner.Build(sheet).Summary,
            CaptureStatus: "complete",
            CaptureMethod: "RenderTargetBitmap-print-preview-dialog-and-main-window",
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed in-process RenderTargetBitmap capture; no global mouse, keyboard, or screen capture input was used."
                    : "Abort before file write unless the expected FreeX main window or Print Preview dialog owns foreground focus for each capture."),
            ClosedViaEscapeEquivalent: closedViaEscapeEquivalent,
            FocusReturnedToBackstagePrintPreviewCommand: focusReturned,
            Captures: captures,
            Limitations:
            [
                "This in-app tour renders real FreeX WPF windows using RenderTargetBitmap rather than OS CopyFromScreen.",
                "The Ctrl+P route is represented by FreeX's existing source-proven Ctrl+P-to-File-Print path plus a live Print Preview dialog opened from that backstage entry point; no global Ctrl+P keystroke is synthesized.",
                "The close capture uses the PrintPreviewCloseButton IsCancel route as the Escape-equivalent path, then explicitly returns focus to the backstage Print Preview command before the final screenshot.",
                "The native Windows print dialog is not opened during this tour to avoid sending output to a real printer or blocking on system print UI."
            ]);

        var path = Path.Combine(outputDir, PrintPreviewTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.PrintPreviewTourManifest);
    }

    private static async Task WriteOptionsAccountTourManifestAsync(
        string outputDir,
        LocalAccountPlan accountPlan,
        OptionsAccountTourManifestCapture accountMessageCapture,
        IReadOnlyList<OptionsAccountTourManifestCapture> optionCaptures,
        bool categoryListFocusedByDefault,
        bool closedViaCancelEquivalent,
        bool focusReturned)
    {
        var captures = new List<OptionsAccountTourManifestCapture>
        {
            new(
                CaptureKey: "account:backstage-entry:focused",
                PairKey: "interactive:options-account:account-backstage-entry-focused",
                ScenarioId: "options-account:account-backstage-entry",
                State: "account-entry-focused",
                Surface: "Backstage Account entry",
                FileName: "freex_account_backstage_entry_focused",
                OutputFileName: "freex_account_backstage_entry_focused.png",
                CaptureMethod: "RenderTargetBitmap-main-window",
                EvidenceSummary: "Backstage is open with the Account navigation command focused beside the Options command.",
                CategoryName: null,
                CategoryIndex: null,
                FocusedElementAutomationId: "BackstageAccountButton",
                CaptureLogicalWidth: 1120,
                CaptureLogicalHeight: 760),
            accountMessageCapture,
            new(
                CaptureKey: "account:closed:focus-return",
                PairKey: "interactive:options-account:account-focus-return",
                ScenarioId: "options-account:account-focus-return",
                State: "account-focus-return",
                Surface: "Backstage Account entry",
                FileName: "freex_account_backstage_focus_return",
                OutputFileName: "freex_account_backstage_focus_return.png",
                CaptureMethod: "RenderTargetBitmap-main-window",
                EvidenceSummary: "After the Account message closes, focus is restored to the Backstage Account command.",
                CategoryName: null,
                CategoryIndex: null,
                FocusedElementAutomationId: "BackstageAccountButton",
                CaptureLogicalWidth: 1120,
                CaptureLogicalHeight: 760)
        };
        captures.AddRange(optionCaptures);
        captures.Add(new OptionsAccountTourManifestCapture(
            CaptureKey: "options:closed:cancel-focus-return",
            PairKey: "interactive:options-account:options-cancel-focus-return",
            ScenarioId: "options-account:options-focus-return",
            State: "options-cancel-focus-return",
            Surface: "Backstage Options entry",
            FileName: "freex_options_cancel_focus_return",
            OutputFileName: "freex_options_cancel_focus_return.png",
            CaptureMethod: "RenderTargetBitmap-main-window",
            EvidenceSummary: "After verifying the OptionsCancelButton IsCancel metadata and closing the tour dialog, focus is restored to the Backstage Options command.",
            CategoryName: null,
            CategoryIndex: null,
            FocusedElementAutomationId: "BackstageOptionsButton",
            CaptureLogicalWidth: 1120,
            CaptureLogicalHeight: 760));

        var manifest = new OptionsAccountTourManifest(
            Tool: "FREEX_OPTIONS_ACCOUNT_TOUR",
            EvidenceFamily: "backstage-options-account",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "options-account:visual-evidence",
            OutputDirectory: outputDir,
            OutputNaming: "freex_<Surface>_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md#UI-CMD-FILE-005",
            EntryPaths: ["File > Account", "File > Options"],
            CaptureStatus: "complete",
            CaptureMethod: "RenderTargetBitmap-WPF-windows-and-PrintWindow-owned-native-dialog",
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed deterministic in-process WPF RenderTargetBitmap captures plus owned native Account dialog PrintWindow capture; no global mouse or keyboard input is used."
                    : "Abort before WPF window file writes unless the expected FreeX window owns foreground focus; owned native Account dialog is captured by HWND ownership and caption."),
            AccountTitle: accountPlan.Title,
            AccountDetailLabels: accountPlan.Details.Select(detail => detail.Label).ToArray(),
            AccountMicrosoft365Exclusion: accountPlan.Details
                .FirstOrDefault(detail => string.Equals(detail.Label, "Microsoft 365 services", StringComparison.OrdinalIgnoreCase))
                ?.Value ?? string.Empty,
            CategoryListFocusedByDefault: categoryListFocusedByDefault,
            OptionsClosedViaCancelEquivalent: closedViaCancelEquivalent,
            FocusReturnedToBackstageOptionsCommand: focusReturned,
            PlannedCaptureCount: OptionsAccountTourExpectedFileNames().Count,
            ActualCaptureCount: captures.Count,
            Captures: captures,
            Limitations:
            [
                "This in-app tour captures real FreeX Backstage and Options WPF surfaces with RenderTargetBitmap and the real owned Account MessageBox with PrintWindow.",
                "The tour does not synthesize global mouse/keytip/UIA input; those interaction paths remain separate from this visual evidence.",
                "The Options close proof verifies the OptionsCancelButton IsCancel metadata before closing the modeless tour dialog directly; modal Escape/Cancel event routing remains separate.",
                "The tour does not persist option changes through OK.",
                "The Account command is a local-account information message, not a Microsoft account sign-in surface; the manifest records the explicit Microsoft 365 services exclusion text."
            ]);

        var path = Path.Combine(outputDir, OptionsAccountTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.OptionsAccountTourManifest);
    }

    private static async Task WriteKeyTipOverlayTourManifestAsync(
        string outputDir,
        IReadOnlyList<KeyTipOverlayTourManifestCapture> captures)
    {
        var manifest = new KeyTipOverlayTourManifest(
            Tool: "FREEX_KEYTIP_OVERLAY_TOUR",
            EvidenceFamily: "keytip-overlay",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "ribbon-keytip-overlay-pixel-placement",
            OutputDirectory: outputDir,
            OutputNaming: "<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            CaptureStatus: "complete",
            CaptureMode: IsScreenshotTourBackgroundRenderAllowed()
                ? "in-process-background-render-allowed"
                : "foreground-guarded-in-process-render",
            FocusGuard: new KeyTipOverlayTourManifestFocusGuard(
                RequiredForWindowCaptures: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed deterministic in-process RenderTargetBitmap captures; no global mouse, keyboard, or screen capture input is used."
                    : "Window-band captures abort unless the FreeX main window owns foreground focus immediately before render and file write. Popup element captures are in-process element renders."),
            PlannedCaptureCount: captures.Count,
            ActualCaptureCount: captures.Count,
            Captures: captures,
            CoveredStates:
            [
                "Top-level Alt/F10 tab badges",
                "QAT badges in top-level keytip mode",
                "Home visible command-scope badges",
                "Home Borders dropdown menu keytip scope",
                "Home Borders > Line Color nested submenu keytip scope",
                "Narrow Home command-scope collapsed-group badges"
            ],
            Limitations:
            [
                "Window-band captures cover the top 300 logical pixels of the FreeX window.",
                "Top-level, QAT, visible command, and narrow collapsed cases capture the production KeyTipOverlay badges.",
                "Dropdown and nested submenu states are captured as live WPF popup elements; their scoped keytips are rendered as menu input gesture text rather than overlay badges because the production keytip mode intentionally clears the owner-window badge overlay while menu scope is active.",
                "This evidence proves FreeX pixel placement for the captured states only; broader Excel pair captures remain separate foreground-guarded work."
            ]);

        var path = Path.Combine(outputDir, KeyTipOverlayTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.KeyTipOverlayTourManifest);
    }

    private sealed record RibbonScreenshotTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        string? Context,
        bool BurstMode,
        double CaptureLogicalHeight,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        string CaptureStatus,
        string CaptureMethod,
        RibbonScreenshotTourManifestPairing Pairing,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        IReadOnlyList<string> Tabs,
        IReadOnlyList<RibbonScreenshotTourManifestWidth> Widths,
        IReadOnlyList<RibbonScreenshotTourManifestPhase> Phases,
        IReadOnlyList<RibbonScreenshotTourManifestCapture> Captures,
        IReadOnlyList<string> Limitations);

    private sealed record RibbonScreenshotTourManifestPairing(
        string PairKeyPattern,
        string CounterpartSubject,
        string CounterpartTool,
        string CounterpartOutputNaming);

    private sealed record RibbonScreenshotTourManifestFocusGuard(bool Required, string Policy);

    private sealed record RibbonScreenshotTourManifestWidth(string Label, double? WindowWidth, string EvidencePurpose);

    private sealed record RibbonScreenshotTourManifestPhase(string Label, string? FileNameSuffix);

    private sealed record RibbonScreenshotTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string Tab,
        string TabFileName,
        string Width,
        string Phase,
        string FileName,
        string OutputFileName,
        string CounterpartFileName);

    private sealed record AutoFilterFlyoutTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        string HeaderCell,
        string HeaderText,
        string AutoFilterRange,
        uint FilterColumnOffset,
        string CaptureStatus,
        string CaptureMethod,
        AutoFilterFlyoutTourManifestPairing Pairing,
        IReadOnlyList<AutoFilterFlyoutTourManifestCapture> Captures,
        IReadOnlyList<string> Limitations);

    private sealed record AutoFilterFlyoutTourManifestPairing(
        string PairKeyPattern,
        string CounterpartSubject,
        string CounterpartTool,
        string CounterpartOutputNaming);

    private sealed record AutoFilterFlyoutTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string FileName,
        string OutputFileName,
        string CounterpartFileName,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight);

    private sealed record HomeNumberFormatDropdownTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        string SelectedCell,
        string SelectedFormat,
        IReadOnlyList<string> OptionLabels,
        string CaptureStatus,
        string CaptureMethod,
        HomeNumberFormatDropdownTourManifestPairing Pairing,
        IReadOnlyList<HomeNumberFormatDropdownTourManifestCapture> Captures,
        IReadOnlyList<string> Limitations);

    private sealed record HomeNumberFormatDropdownTourManifestPairing(
        string PairKeyPattern,
        string CounterpartSubject,
        string CounterpartTool,
        string CounterpartOutputNaming);

    private sealed record HomeNumberFormatDropdownTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string FileName,
        string OutputFileName,
        string CounterpartFileName,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight);

    private sealed record HomeAlignmentNumberTourContext(
        string SheetName,
        GridRange AlignmentRange,
        GridRange NumberRange,
        IReadOnlyList<string> SampleFormats);

    private sealed record HomeAlignmentNumberTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        string SheetName,
        string AlignmentRange,
        string NumberRange,
        IReadOnlyList<string> SampleFormats,
        string CaptureStatus,
        string CaptureMethod,
        HomeAlignmentNumberTourManifestPairing Pairing,
        IReadOnlyList<HomeAlignmentNumberTourManifestCapture> Captures,
        IReadOnlyList<string> Limitations);

    private sealed record HomeAlignmentNumberTourManifestPairing(
        string PairKeyPattern,
        string CounterpartSubject,
        string CounterpartTool,
        string CounterpartOutputNaming);

    private sealed record HomeAlignmentNumberTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string Surface,
        string FileName,
        string OutputFileName,
        string CounterpartFileName,
        string CaptureMethod,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight,
        string EvidencePurpose);

    private sealed record HomeBordersDropdownTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        string EntryPath,
        IReadOnlyList<string> MenuHeaders,
        string CaptureStatus,
        string CaptureMethod,
        HomeBordersDropdownTourManifestPairing Pairing,
        IReadOnlyList<HomeBordersDropdownTourManifestCapture> Captures,
        IReadOnlyList<string> Limitations);

    private sealed record HomeBordersDropdownTourManifestPairing(
        string PairKeyPattern,
        string CounterpartSubject,
        string CounterpartTool,
        string CounterpartOutputNaming);

    private sealed record HomeBordersDropdownTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string FileName,
        string OutputFileName,
        string CounterpartFileName,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight);

    private sealed record WorksheetContextMenuTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        string SelectedCell,
        string EntryPath,
        IReadOnlyList<string> MenuHeaders,
        string CaptureStatus,
        string CaptureMethod,
        WorksheetContextMenuTourManifestPairing Pairing,
        IReadOnlyList<WorksheetContextMenuTourManifestCapture> Captures,
        IReadOnlyList<string> Limitations);

    private sealed record WorksheetContextMenuTourManifestPairing(
        string PairKeyPattern,
        string CounterpartSubject,
        string CounterpartTool,
        string CounterpartOutputNaming);

    private sealed record WorksheetContextMenuTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string FileName,
        string OutputFileName,
        string CounterpartFileName,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight);

    private sealed record PrintPreviewTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        IReadOnlyList<string> EntryPaths,
        string SheetName,
        int TotalPages,
        string SettingsSummary,
        string CaptureStatus,
        string CaptureMethod,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        bool ClosedViaEscapeEquivalent,
        bool FocusReturnedToBackstagePrintPreviewCommand,
        IReadOnlyList<PrintPreviewTourManifestCapture> Captures,
        IReadOnlyList<string> Limitations);

    private sealed record PrintPreviewTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string EntryPath,
        string FileName,
        string OutputFileName,
        string EvidenceSummary);

    private sealed record OptionsAccountTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        IReadOnlyList<string> EntryPaths,
        string CaptureStatus,
        string CaptureMethod,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        string AccountTitle,
        IReadOnlyList<string> AccountDetailLabels,
        string AccountMicrosoft365Exclusion,
        bool CategoryListFocusedByDefault,
        bool OptionsClosedViaCancelEquivalent,
        bool FocusReturnedToBackstageOptionsCommand,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        IReadOnlyList<OptionsAccountTourManifestCapture> Captures,
        IReadOnlyList<string> Limitations);

    private sealed record OptionsAccountTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string Surface,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        string EvidenceSummary,
        string? CategoryName,
        int? CategoryIndex,
        string? FocusedElementAutomationId,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight);

    private sealed record OptionsAccountTourNativeCaptureSize(int Width, int Height);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeRect
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;
    }

    private sealed record KeyTipOverlayTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        string CaptureStatus,
        string CaptureMode,
        KeyTipOverlayTourManifestFocusGuard FocusGuard,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        IReadOnlyList<KeyTipOverlayTourManifestCapture> Captures,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record KeyTipOverlayTourManifestFocusGuard(
        bool RequiredForWindowCaptures,
        string Policy);

    private sealed record KeyTipOverlayTourManifestCapture(
        string CaptureKey,
        string State,
        string Scope,
        string Description,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight,
        int BadgeCount,
        int CollapsedGroupBadgeCount,
        int MenuItemKeyTipCount,
        bool IsInProcess,
        bool IsForegroundGuarded);

    private sealed record QatUndoRedoTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        string SelectedCell,
        string CaptureStatus,
        string CaptureMethod,
        QatUndoRedoTourManifestPairing Pairing,
        IReadOnlyList<QatUndoRedoTourManifestCapture> Captures,
        IReadOnlyList<string> Limitations);

    private sealed record QatUndoRedoTourManifestPairing(
        string PairKeyPattern,
        string CounterpartSubject,
        string CounterpartTool,
        string CounterpartOutputNaming);

    private sealed record QatUndoRedoTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string Surface,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight,
        bool UndoButtonEnabled,
        bool UndoHistoryButtonEnabled,
        bool RedoButtonEnabled,
        bool RedoHistoryButtonEnabled,
        bool CanUndo,
        bool CanRedo,
        string ActiveCell,
        string ActiveCellText,
        bool ActiveCellBold,
        string? ActiveCellFillColor,
        string StatusText,
        IReadOnlyList<string> UndoHistoryLabels,
        IReadOnlyList<string> RedoHistoryLabels,
        IReadOnlyList<string> MenuHeaders);

    private sealed record SheetTabTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        string CaptureStatus,
        string CaptureMethod,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        IReadOnlyList<SheetTabTourManifestCapture> Captures,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record SheetTabTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string Surface,
        string FileName,
        string OutputFileName,
        string EvidenceSummary);
    private sealed record TitlebarWindowChromeTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        string CaptureStatus,
        string CaptureMethod,
        string SavedWorkbookOutputFileName,
        bool SavedWorkbookRetained,
        TitlebarWindowChromeTourManifestPairing Pairing,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        IReadOnlyList<TitlebarWindowChromeTourManifestCapture> Captures,
        IReadOnlyList<string> Limitations);

    private sealed record TitlebarWindowChromeTourManifestPairing(
        string PairKeyPattern,
        string CounterpartSubject,
        string CounterpartTool,
        string CounterpartOutputNaming);

    private sealed record TitlebarWindowChromeTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight,
        string EvidenceSummary,
        string WindowState,
        string WindowTitle,
        string WorkbookNameText,
        string WorkbookName,
        bool WorkbookDirty,
        string? CurrentFileName,
        bool TitleBarQatVisible,
        IReadOnlyList<string> TitleBarQatCommandIds,
        TitlebarWindowChromeTourManifestButtonState MinimizeButton,
        TitlebarWindowChromeTourManifestButtonState MaxRestoreButton,
        TitlebarWindowChromeTourManifestButtonState CloseButton,
        string MaxRestoreIconKind);

    private sealed record TitlebarWindowChromeTourManifestButtonState(
        string AutomationId,
        string AutomationName,
        string HelpText,
        bool IsVisible,
        bool IsEnabled,
        double ActualWidth,
        double ActualHeight);

    private sealed record FormulaBarNameBoxTourContext(
        string SheetName,
        string NamedRangeName,
        string NamedRangeAddress,
        string StartCell);

    private sealed record FormulaDiagnosticsTourContext(
        string SheetName,
        CellAddress InputCell,
        CellAddress ResultCell,
        CellAddress ErrorCell,
        string ResultFormula,
        string ErrorFormula);

    private sealed record FormulaBarNameBoxTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        string SheetName,
        string NamedRangeName,
        string NamedRangeAddress,
        string StartCell,
        string CaptureStatus,
        string CaptureMethod,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        FormulaBarNameBoxTourManifestPairing Pairing,
        IReadOnlyList<FormulaBarNameBoxTourManifestCapture> Captures,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record FormulaBarNameBoxTourManifestPairing(
        string PairKeyPattern,
        string CounterpartSubject,
        string CounterpartTool,
        string CounterpartOutputNaming);

    private sealed record FormulaBarNameBoxTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string Surface,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight,
        string NameBoxText,
        bool NameBoxDropDownOpen,
        string FormulaBarText,
        bool FormulaBarAcceptsReturn,
        bool FormulaBarExpanded,
        string SelectedRange,
        string ActiveCellText,
        string FocusedAutomationId,
        int KeyTipBadgeCount,
        string EvidenceSummary);

    private sealed record StatusFooterTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        string CaptureStatus,
        string CaptureMode,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        StatusFooterTourManifestPairing Pairing,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        IReadOnlyList<StatusFooterTourManifestCapture> Captures,
        IReadOnlyList<string> Limitations);

    private sealed record StatusFooterTourManifestPairing(
        string PairKeyPattern,
        string CounterpartSubject,
        string CounterpartTool,
        string CounterpartOutputNaming);

    private sealed record StatusFooterTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        string EvidencePurpose,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight,
        string ActiveRange,
        string StatusModeText,
        bool StatusModeVisible,
        string AverageText,
        string CountText,
        string NumericalCountText,
        string SumText,
        string MinText,
        string MaxText,
        bool StatsVisible,
        string ViewMode,
        bool NormalViewChecked,
        bool PageLayoutViewChecked,
        bool PageBreakPreviewChecked,
        string ZoomText,
        double ZoomSliderValue,
        bool ZoomOutButtonEnabled,
        bool ZoomInButtonEnabled,
        string FormulaBarText);

    private sealed record FormulaDiagnosticsTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        IReadOnlyList<string> CatalogIds,
        string SheetName,
        string InputCell,
        string ResultCell,
        string ErrorCell,
        string ResultFormula,
        string ErrorFormula,
        string CaptureStatus,
        string CaptureMode,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        FormulaDiagnosticsTourManifestPairing Pairing,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        IReadOnlyList<FormulaDiagnosticsTourManifestCapture> Captures,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record FormulaDiagnosticsTourManifestPairing(
        string PairKeyPattern,
        string CounterpartSubject,
        string CounterpartTool,
        string CounterpartOutputNaming);

    private sealed record FormulaDiagnosticsTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string Surface,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight,
        string SelectedRange,
        bool ShowFormulas,
        int FormulaTraceArrowCount,
        int WatchCount,
        string EvidenceSummary);

    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(RibbonScreenshotTourManifest))]
    [JsonSerializable(typeof(AutoFilterFlyoutTourManifest))]
    [JsonSerializable(typeof(HomeNumberFormatDropdownTourManifest))]
    [JsonSerializable(typeof(HomeAlignmentNumberTourManifest))]
    [JsonSerializable(typeof(HomeBordersDropdownTourManifest))]
    [JsonSerializable(typeof(WorksheetContextMenuTourManifest))]
    [JsonSerializable(typeof(PrintPreviewTourManifest))]
    [JsonSerializable(typeof(OptionsAccountTourManifest))]
    [JsonSerializable(typeof(KeyTipOverlayTourManifest))]
    [JsonSerializable(typeof(QatUndoRedoTourManifest))]
    [JsonSerializable(typeof(SheetTabTourManifest))]
    [JsonSerializable(typeof(TitlebarWindowChromeTourManifest))]
    [JsonSerializable(typeof(FormulaBarNameBoxTourManifest))]
    [JsonSerializable(typeof(StatusFooterTourManifest))]
    [JsonSerializable(typeof(FormulaDiagnosticsTourManifest))]
    private sealed partial class RibbonScreenshotTourManifestJsonContext : JsonSerializerContext;

    // Activated by FREEX_ACCENT_BAR_TOUR=1 env var. Output lands in <repo-root>/screenshots/accent-bars-tour/.
    private void TryStartAccentBarVisualTour()
    {
        if (Environment.GetEnvironmentVariable("FREEX_ACCENT_BAR_TOUR") != "1")
            return;

        var outputDir = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "screenshots", "accent-bars-tour"));
        Directory.CreateDirectory(outputDir);
        _ = RunAccentBarVisualTourAsync(outputDir);
    }

    private async Task RunAccentBarVisualTourAsync(string outputDir)
    {
        foreach (var file in Directory.EnumerateFiles(outputDir, "*.png"))
            File.Delete(file);

        WindowState = WindowState.Normal;
        Width = 1280;
        Height = 760;
        await Task.Delay(900);

        await CaptureElementAsync(TitleBarRoot, outputDir, "title-normal");
        await CaptureElementAsync(StatusBarRoot, outputDir, "status-normal");

        if (GetQuickAccessToolbarButton(QuickAccessToolbarCommandIds.Save) is { } saveQatButton)
            await HoverAndCaptureElementAsync(saveQatButton, TitleBarRoot, outputDir, "title-save-hover");
        await HoverAndCaptureElementAsync(MaxRestoreBtn, TitleBarRoot, outputDir, "title-system-hover");
        await HoverAndCaptureElementAsync(StatusZoomOutButton, StatusBarRoot, outputDir, "status-minus-hover");
        await HoverAndCaptureElementAsync(StatusZoomInButton, StatusBarRoot, outputDir, "status-plus-hover");
        await HoverAndCaptureElementAsync(CloseSysBtn, TitleBarRoot, outputDir, "title-close-hover");

        Application.Current.Shutdown();
    }

    private async Task HoverAndCaptureElementAsync(
        FrameworkElement hoverTarget,
        FrameworkElement captureTarget,
        string outputDir,
        string fileName)
    {
        UpdateLayout();
        var center = hoverTarget.PointToScreen(new Point(hoverTarget.ActualWidth / 2, hoverTarget.ActualHeight / 2));
        SetCursorPos((int)Math.Round(center.X), (int)Math.Round(center.Y));
        await Task.Delay(220);
        await CaptureElementAsync(captureTarget, outputDir, fileName);
    }

    private static async Task CaptureElementAsync(FrameworkElement element, string outputDir, string fileName)
    {
        element.UpdateLayout();

        var source = PresentationSource.FromVisual(element);
        var dpiX = source?.CompositionTarget.TransformToDevice.M11 ?? 1.0;
        var dpiY = source?.CompositionTarget.TransformToDevice.M22 ?? 1.0;
        int pw = Math.Max(1, (int)(element.ActualWidth * dpiX));
        int ph = Math.Max(1, (int)(element.ActualHeight * dpiY));

        var rtb = new RenderTargetBitmap(pw, ph, 96 * dpiX, 96 * dpiY, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var brush = new VisualBrush(element) { Stretch = Stretch.Fill };
            context.DrawRectangle(brush, null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        }
        rtb.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        var path = Path.Combine(outputDir, $"{fileName}.png");
        await using var stream = File.Create(path);
        encoder.Save(stream);
    }

    // Activated by FREEX_SHEET_TAB_TOUR=1 env var. Output lands in <repo-root>/screenshots/sheet-tabs-tour/.
    private void TryStartSheetTabVisualTour()
    {
        if (Environment.GetEnvironmentVariable("FREEX_SHEET_TAB_TOUR") != "1")
            return;

        var outputDir = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "screenshots", SheetTabTourOutputDirectoryName));
        Directory.CreateDirectory(outputDir);
        _ = RunSheetTabVisualTourAsync(outputDir);
    }

    private async Task RunSheetTabVisualTourAsync(string outputDir)
    {
        DeleteSheetTabTourEvidence(outputDir);
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        WindowState = WindowState.Normal;
        Width = 1180;
        Height = 760;
        await Task.Delay(700);

        var captures = new List<SheetTabTourManifestCapture>();
        await CaptureSheetTabsForTourAsync(
            outputDir,
            captures,
            "freex_sheet_tabs_single_sheet",
            "single-sheet",
            "Fresh workbook tab strip shows the selected Sheet1 tab and the plus add-sheet affordance.");

        InsertNewSheet();
        await Task.Delay(300);
        await CaptureSheetTabsForTourAsync(
            outputDir,
            captures,
            "freex_sheet_tabs_after_add_sheet",
            "after-add-sheet",
            "Production Insert Sheet route added Sheet2, selected it, and left the plus affordance visible.");

        PrepareSheetTabVisualTourWorkbook();
        await Task.Delay(400);
        var visibleSheets = _workbook.Sheets.Where(sheet => !sheet.IsHidden).Take(20).ToList();

        _currentSheetId = visibleSheets[3].Id;
        _groupedSheetIds.Clear();
        foreach (var sheet in visibleSheets.Skip(1).Take(5))
            _groupedSheetIds.Add(sheet.Id);
        _sheetGroupAnchor = visibleSheets[1].Id;
        RefreshSheetTabs();
        await Task.Delay(300);
        await CaptureSheetTabsForTourAsync(
            outputDir,
            captures,
            "freex_sheet_tabs_grouped_colored",
            "grouped-colored-tabs",
            "Grouped tabs 2-6 show active/grouped styling while tab colors render on colored sheets.");

        await CaptureSheetTabContextMenuForTourAsync(outputDir, captures, visibleSheets[3]);
        await CaptureSheetNameDialogForTourAsync(outputDir, captures, visibleSheets[3].Name);

        var hiddenSheet = visibleSheets[6];
        hiddenSheet.IsHidden = true;
        _currentSheetId = visibleSheets[3].Id;
        RefreshSheetTabs();
        await Task.Delay(300);
        await CaptureSheetTabsForTourAsync(
            outputDir,
            captures,
            "freex_sheet_tabs_hidden_sheet_excluded",
            "hidden-sheet-excluded",
            "Hidden sheet is absent from the visible tab strip while adjacent visible tabs remain selectable.");
        await CaptureUnhideSheetDialogForTourAsync(outputDir, captures, hiddenSheet.Name);
        hiddenSheet.IsHidden = false;
        RefreshSheetTabs();

        Width = 760;
        await Task.Delay(450);
        await CaptureSheetTabStateForTourAsync(
            outputDir,
            captures,
            visibleSheets,
            0,
            "freex_sheet_tabs_overflow_start",
            "overflow-start",
            "Narrow tab strip at the first visible sheet shows overflow navigation affordances.");
        await CaptureSheetTabStateForTourAsync(
            outputDir,
            captures,
            visibleSheets,
            10,
            "freex_sheet_tabs_overflow_middle",
            "overflow-middle",
            "Narrow tab strip scrolls the active middle sheet into view with left/right navigation affordances.");
        await CaptureSheetTabStateForTourAsync(
            outputDir,
            captures,
            visibleSheets,
            19,
            "freex_sheet_tabs_overflow_end",
            "overflow-end",
            "Narrow tab strip scrolls to the final sheet and shows the right edge overflow state.");

        ValidateSheetTabTourEvidence(outputDir, captures);
        await WriteSheetTabTourManifestAsync(outputDir, captures);

        _suppressClosePrompt = true;
        Application.Current.Shutdown();
    }

    private async Task CaptureSheetTabStateForTourAsync(
        string outputDir,
        List<SheetTabTourManifestCapture> captures,
        IReadOnlyList<Sheet> visibleSheets,
        int activeIndex,
        string fileName,
        string state,
        string evidenceSummary)
    {
        var sheet = visibleSheets[activeIndex];
        _currentSheetId = sheet.Id;
        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(sheet.Id);
        _sheetGroupAnchor = sheet.Id;
        RefreshSheetTabs();
        await Task.Delay(260);
        await CaptureSheetTabsForTourAsync(outputDir, captures, fileName, state, evidenceSummary);
    }

    private void PrepareSheetTabVisualTourWorkbook()
    {
        while (_workbook.Sheets.Count < 20)
            _workbook.AddSheet(SheetTabListPlanner.GenerateUniqueSheetName(_workbook));

        var names = new[]
        {
            "Overview",
            "Inputs",
            "Assumptions",
            "Forecast",
            "Actuals",
            "Charts",
            "Audit",
            "Archive",
            "Region East",
            "Region West",
            "Region North",
            "Region South",
            "Ops",
            "People",
            "Capital",
            "Cash Flow",
            "Notes",
            "Review",
            "Signoff",
            "2026 Plan"
        };
        for (var index = 0; index < names.Length && index < _workbook.Sheets.Count; index++)
            _workbook.Sheets[index].Name = names[index];

        var colors = new CellColor?[]
        {
            null,
            new(232, 121, 65),
            new(83, 141, 213),
            new(112, 173, 71),
            new(165, 105, 189),
            null,
            new(243, 156, 18),
            new(75, 172, 198)
        };

        for (var index = 0; index < colors.Length && index < _workbook.Sheets.Count; index++)
            _workbook.Sheets[index].TabColor = colors[index];

        _currentSheetId = _workbook.Sheets[0].Id;
        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(_currentSheetId);
        _sheetGroupAnchor = _currentSheetId;
        RefreshSheetTabs();
    }

    private async Task CaptureSheetTabsForTourAsync(
        string outputDir,
        List<SheetTabTourManifestCapture> captures,
        string fileName,
        string state,
        string evidenceSummary,
        bool revealCurrentSheet = true)
    {
        UpdateLayout();
        SheetTabsRowGrid.UpdateLayout();
        if (revealCurrentSheet)
            BringCurrentSheetTabIntoView();
        UpdateSheetTabNavigation();
        UpdateLayout();
        SheetTabsRowGrid.UpdateLayout();
        if (revealCurrentSheet)
            BringCurrentSheetTabIntoView();
        UpdateSheetTabNavigation();
        UpdateLayout();
        SheetTabsRowGrid.UpdateLayout();

        var source = PresentationSource.FromVisual(SheetTabsRowGrid);
        var dpiX = source?.CompositionTarget.TransformToDevice.M11 ?? 1.0;
        var dpiY = source?.CompositionTarget.TransformToDevice.M22 ?? 1.0;
        int pw = Math.Max(1, (int)(SheetTabsRowGrid.ActualWidth * dpiX));
        int ph = Math.Max(1, (int)(SheetTabsRowGrid.ActualHeight * dpiY));

        var rtb = new RenderTargetBitmap(pw, ph, 96 * dpiX, 96 * dpiY, PixelFormats.Pbgra32);
        rtb.Render(SheetTabsRowGrid);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        var path = Path.Combine(outputDir, $"{fileName}.png");
        await using var stream = File.Create(path);
        encoder.Save(stream);

        captures.Add(new SheetTabTourManifestCapture(
            CaptureKey: $"sheet-tabs:{state}",
            PairKey: $"interactive:sheet-tabs:{state}",
            ScenarioId: "sheet-tabs:visual-parity",
            State: state,
            Surface: "sheet-tab-strip",
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            EvidenceSummary: evidenceSummary));
    }

    private async Task CaptureSheetTabContextMenuForTourAsync(
        string outputDir,
        List<SheetTabTourManifestCapture> captures,
        Sheet sheet)
    {
        _currentSheetId = sheet.Id;
        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(sheet.Id);
        _sheetGroupAnchor = sheet.Id;
        RefreshSheetTabs();
        await Task.Delay(300);

        var tab = FindSheetTab(sheet.Id)
            ?? throw new InvalidOperationException("Sheet-tab tour could not locate the context-menu target tab.");
        var target = FindSheetTabContextMenuTarget(tab)
            ?? throw new InvalidOperationException("Sheet-tab tour could not locate the tab ContextMenu visual.");
        var menu = target.ContextMenu
            ?? throw new InvalidOperationException("Sheet-tab tour could not locate the tab ContextMenu.");

        try
        {
            MenuKeyTipAssigner.AssignUniqueKeyTips(menu.Items.OfType<MenuItem>());
            menu.PlacementTarget = target;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
            await Task.Delay(350);
            menu.UpdateLayout();
            await CaptureElementAsync(menu, outputDir, "freex_sheet_tabs_context_menu_opened");
            captures.Add(new SheetTabTourManifestCapture(
                CaptureKey: "sheet-tabs:context-menu-opened",
                PairKey: "interactive:sheet-tabs:context-menu-opened",
                ScenarioId: "sheet-tabs:context-menu",
                State: "context-menu-opened",
                Surface: "sheet-tab-context-menu",
                FileName: "freex_sheet_tabs_context_menu_opened",
                OutputFileName: "freex_sheet_tabs_context_menu_opened.png",
                EvidenceSummary: "Production sheet-tab ContextMenu is open for the active tab, including Insert, Delete, Rename, Move or Copy, Tab Color, Hide, Unhide, Select All Sheets, and Ungroup Sheets entries."));
        }
        finally
        {
            menu.IsOpen = false;
        }
    }

    private async Task CaptureSheetNameDialogForTourAsync(
        string outputDir,
        List<SheetTabTourManifestCapture> captures,
        string currentName)
    {
        var dialog = new SheetNameDialog(currentName) { Owner = this };
        try
        {
            dialog.Show();
            dialog.Activate();
            dialog.UpdateLayout();
            await Task.Delay(350);
            await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, "freex_sheet_tabs_rename_dialog_opened");
            captures.Add(new SheetTabTourManifestCapture(
                CaptureKey: "sheet-tabs:rename-dialog-opened",
                PairKey: "interactive:sheet-tabs:rename-dialog-opened",
                ScenarioId: "sheet-tabs:rename-dialog",
                State: "rename-dialog-opened",
                Surface: "rename-sheet-dialog",
                FileName: "freex_sheet_tabs_rename_dialog_opened",
                OutputFileName: "freex_sheet_tabs_rename_dialog_opened.png",
                EvidenceSummary: "Rename Sheet dialog is open through the same SheetNameDialog used by sheet-tab double-click and context Rename, with the name box focused and selected on load."));
        }
        finally
        {
            dialog.Close();
        }
    }

    private async Task CaptureUnhideSheetDialogForTourAsync(
        string outputDir,
        List<SheetTabTourManifestCapture> captures,
        string hiddenSheetName)
    {
        var dialog = new UnhideSheetDialog([hiddenSheetName]) { Owner = this };
        try
        {
            dialog.Show();
            dialog.Activate();
            dialog.UpdateLayout();
            await Task.Delay(350);
            await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, "freex_sheet_tabs_unhide_dialog_opened");
            captures.Add(new SheetTabTourManifestCapture(
                CaptureKey: "sheet-tabs:unhide-dialog-opened",
                PairKey: "interactive:sheet-tabs:unhide-dialog-opened",
                ScenarioId: "sheet-tabs:unhide-dialog",
                State: "unhide-dialog-opened",
                Surface: "unhide-sheet-dialog",
                FileName: "freex_sheet_tabs_unhide_dialog_opened",
                OutputFileName: "freex_sheet_tabs_unhide_dialog_opened.png",
                EvidenceSummary: $"Unhide Sheet dialog lists the hidden worksheet '{hiddenSheetName}' and focuses the hidden-sheet list."));
        }
        finally
        {
            dialog.Close();
        }
    }

    private static void DeleteSheetTabTourEvidence(string outputDir)
    {
        foreach (var file in Directory.EnumerateFiles(outputDir, "freex_sheet_tabs_*.png"))
            File.Delete(file);

        var manifestPath = Path.Combine(outputDir, SheetTabTourManifestFileName);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);
    }

    private static void ValidateSheetTabTourEvidence(string outputDir, IReadOnlyList<SheetTabTourManifestCapture> captures)
    {
        foreach (var capture in captures)
        {
            var path = Path.Combine(outputDir, capture.OutputFileName);
            if (!File.Exists(path))
                throw new InvalidOperationException($"Sheet-tab tour did not create planned capture {capture.OutputFileName}.");
        }
    }

    private static async Task WriteSheetTabTourManifestAsync(
        string outputDir,
        IReadOnlyList<SheetTabTourManifestCapture> captures)
    {
        var manifest = new SheetTabTourManifest(
            Tool: "FREEX_SHEET_TAB_TOUR",
            EvidenceFamily: "sheet-tabs",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "sheet-tabs:visual-evidence",
            OutputDirectory: outputDir,
            OutputNaming: "freex_sheet_tabs_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            CaptureStatus: "complete",
            CaptureMethod: "RenderTargetBitmap-sheet-tab-strip-context-menu-and-dialogs",
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed deterministic in-process RenderTargetBitmap captures; no global mouse, keyboard, or screen capture input is used."
                    : "Dialog captures abort unless the expected FreeX WPF window owns foreground focus immediately before render and file write."),
            PlannedCaptureCount: captures.Count,
            ActualCaptureCount: captures.Count,
            Captures: captures,
            CoveredStates:
            [
                "Selected single-sheet tab and plus add-sheet affordance",
                "Add Sheet route selecting the newly created sheet",
                "Grouped sheet-tab styling and tab color rendering",
                "Production sheet-tab context menu",
                "Rename Sheet dialog focus/select-all affordance",
                "Hidden sheet excluded from the tab strip",
                "Unhide Sheet dialog with hidden-sheet list",
                "Narrow tab-strip overflow navigation at start, middle, and end positions"
            ],
            Limitations:
            [
                "This tour renders FreeX WPF surfaces in-process; it does not synthesize physical mouse clicks, Ctrl/Shift modifiers, drag reorder, or double-click input.",
                "The context menu capture is opened from the production tab ContextMenu object rather than by OS right-click, so live placement/focus evidence remains separate.",
                "The rename and unhide dialog captures show the production dialogs and initial focus targets, but they do not submit dialog changes.",
                "No Microsoft Excel counterpart or macOS/native-host capture is produced by this tool."
            ]);

        var path = Path.Combine(outputDir, SheetTabTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.SheetTabTourManifest);
    }
}
