using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
    private const string HomeBordersDropdownTourManifestFileName = "home_borders_dropdown_tour_manifest.json";
    private const string HomeBordersDropdownTourCaptureFileName = "freex_dropdown_home_borders_opened";
    private const string WorksheetContextMenuTourManifestFileName = "worksheet_context_menu_tour_manifest.json";
    private const string WorksheetContextMenuTourCaptureFileName = "freex_context_menu_worksheet_cell_opened";
    private const string KeyTipOverlayTourManifestFileName = "keytip_overlay_tour_manifest.json";
    private const string PrintPreviewTourManifestFileName = "print_preview_tour_manifest.json";
    private const string QatUndoRedoTourManifestFileName = "qat_undo_redo_tour_manifest.json";
    private const string QatUndoRedoTourOutputDirectoryName = "qat-undo-redo-tour";
    private const string SheetTabTourManifestFileName = "sheet_tabs_tour_manifest.json";
    private const string SheetTabTourOutputDirectoryName = "sheet-tabs-tour";
    private const string ScreenshotTourAllowBackgroundRenderEnvVar = "FREEX_SS_TOUR_ALLOW_BACKGROUND_RENDER";
    private const string ScreenshotTourOutputSubdirectoryEnvVar = "FREEX_SS_TOUR_OUTPUT_SUBDIR";

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    // Activated by FREEX_SS_TOUR=1 env var.  Output lands in <repo-root>/screenshots/.
    private async void TryStartScreenshotTour()
    {
        var ribbonBurstTour = Environment.GetEnvironmentVariable("FREEX_SS_TOUR_BURST") == "1";
        var ribbonTour = ribbonBurstTour || Environment.GetEnvironmentVariable("FREEX_SS_TOUR") == "1";
        var backstageTour = Environment.GetEnvironmentVariable("FREEX_BACKSTAGE_TOUR") == "1";
        var autoFilterFlyoutTour = Environment.GetEnvironmentVariable("FREEX_AUTOFILTER_FLYOUT_TOUR") == "1";
        var homeNumberFormatDropdownTour = Environment.GetEnvironmentVariable("FREEX_HOME_NUMBER_FORMAT_DROPDOWN_TOUR") == "1";
        var homeBordersDropdownTour = Environment.GetEnvironmentVariable("FREEX_HOME_BORDERS_DROPDOWN_TOUR") == "1";
        var worksheetContextMenuTour = Environment.GetEnvironmentVariable("FREEX_WORKSHEET_CONTEXT_MENU_TOUR") == "1";
        var keyTipOverlayTour = Environment.GetEnvironmentVariable("FREEX_KEYTIP_OVERLAY_TOUR") == "1";
        var printPreviewTour = Environment.GetEnvironmentVariable("FREEX_PRINT_PREVIEW_TOUR") == "1";
        var qatUndoRedoTour = Environment.GetEnvironmentVariable("FREEX_QAT_UNDO_REDO_TOUR") == "1";
        if (!ribbonTour && !backstageTour && !autoFilterFlyoutTour && !homeNumberFormatDropdownTour && !homeBordersDropdownTour && !worksheetContextMenuTour && !keyTipOverlayTour && !printPreviewTour && !qatUndoRedoTour)
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
        await RunScreenshotTourAsync(outputDir, ribbonPlan, backstageTour, autoFilterFlyoutTour, homeNumberFormatDropdownTour, homeBordersDropdownTour, worksheetContextMenuTour, keyTipOverlayTour, printPreviewTour, qatUndoRedoTour);
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
        bool homeBordersDropdownTour,
        bool worksheetContextMenuTour,
        bool keyTipOverlayTour,
        bool printPreviewTour,
        bool qatUndoRedoTour)
    {
        if (ribbonPlan is not null)
            await CaptureRibbonTourAsync(outputDir, ribbonPlan);

        if (backstageTour)
            await CaptureBackstageAsync(outputDir);

        if (autoFilterFlyoutTour)
            await CaptureAutoFilterFlyoutTourAsync(Path.Combine(outputDir, "autofilter-flyout-tour"));

        if (homeNumberFormatDropdownTour)
            await CaptureHomeNumberFormatDropdownTourAsync(Path.Combine(outputDir, "home-number-format-dropdown-tour"));

        if (homeBordersDropdownTour)
            await CaptureHomeBordersDropdownTourAsync(Path.Combine(outputDir, "home-borders-dropdown-tour"));

        if (worksheetContextMenuTour)
            await CaptureWorksheetContextMenuTourAsync(Path.Combine(outputDir, "worksheet-context-menu-tour"));

        if (keyTipOverlayTour)
            await CaptureKeyTipOverlayTourAsync(Path.Combine(outputDir, "keytip-overlay-tour"));

        if (printPreviewTour)
            await CapturePrintPreviewTourAsync(Path.Combine(outputDir, "print-preview-tour"));

        if (qatUndoRedoTour)
            await CaptureQatUndoRedoTourAsync(Path.Combine(outputDir, QatUndoRedoTourOutputDirectoryName));

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

    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(RibbonScreenshotTourManifest))]
    [JsonSerializable(typeof(AutoFilterFlyoutTourManifest))]
    [JsonSerializable(typeof(HomeNumberFormatDropdownTourManifest))]
    [JsonSerializable(typeof(HomeBordersDropdownTourManifest))]
    [JsonSerializable(typeof(WorksheetContextMenuTourManifest))]
    [JsonSerializable(typeof(PrintPreviewTourManifest))]
    [JsonSerializable(typeof(KeyTipOverlayTourManifest))]
    [JsonSerializable(typeof(QatUndoRedoTourManifest))]
    [JsonSerializable(typeof(SheetTabTourManifest))]
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
