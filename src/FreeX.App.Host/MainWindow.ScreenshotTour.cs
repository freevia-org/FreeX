using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        if (!ribbonTour && !backstageTour && !autoFilterFlyoutTour && !homeNumberFormatDropdownTour)
            return;

        var ribbonPlan = ribbonTour
            ? RibbonScreenshotTourPlanner.CreatePlan(
                Environment.GetEnvironmentVariable("FREEX_SS_TOUR_TABS"),
                Environment.GetEnvironmentVariable("FREEX_SS_TOUR_WIDTHS"),
                ribbonBurstTour,
                Environment.GetEnvironmentVariable("FREEX_SS_TOUR_CONTEXT"))
            : null;

        var outputDir = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "screenshots"));
        Directory.CreateDirectory(outputDir);
        await RunScreenshotTourAsync(outputDir, ribbonPlan, backstageTour, autoFilterFlyoutTour, homeNumberFormatDropdownTour);
    }

    private async Task RunScreenshotTourAsync(
        string outputDir,
        RibbonScreenshotTourPlan? ribbonPlan,
        bool backstageTour,
        bool autoFilterFlyoutTour,
        bool homeNumberFormatDropdownTour)
    {
        if (ribbonPlan is not null)
            await CaptureRibbonTourAsync(outputDir, ribbonPlan);

        if (backstageTour)
            await CaptureBackstageAsync(outputDir);

        if (autoFilterFlyoutTour)
            await CaptureAutoFilterFlyoutTourAsync(Path.Combine(outputDir, "autofilter-flyout-tour"));

        if (homeNumberFormatDropdownTour)
            await CaptureHomeNumberFormatDropdownTourAsync(Path.Combine(outputDir, "home-number-format-dropdown-tour"));

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
        Activate();
        Focus();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        AssertWindowForegroundForScreenshotTour(operation);
    }

    private void AssertWindowForegroundForScreenshotTour(string operation)
    {
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
                Required: true,
                Policy: "Abort and clear current PNG/manifest evidence unless the FreeX main window owns foreground focus immediately before render and file write."),
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
                "The in-app tour aborts before file write unless the FreeX main window owns foreground focus."
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

    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(RibbonScreenshotTourManifest))]
    [JsonSerializable(typeof(AutoFilterFlyoutTourManifest))]
    [JsonSerializable(typeof(HomeNumberFormatDropdownTourManifest))]
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
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "screenshots", "sheet-tabs-tour"));
        Directory.CreateDirectory(outputDir);
        _ = RunSheetTabVisualTourAsync(outputDir);
    }

    private async Task RunSheetTabVisualTourAsync(string outputDir)
    {
        foreach (var file in Directory.EnumerateFiles(outputDir, "*.png"))
            File.Delete(file);

        WindowState = WindowState.Normal;
        Width = 1180;
        Height = 760;
        await Task.Delay(700);

        await CaptureSheetTabsAsync(outputDir, "single-sheet");

        while (_workbook.Sheets.Count < 6)
            _workbook.AddSheet(SheetTabListPlanner.GenerateUniqueSheetName(_workbook));
        _currentSheetId = _workbook.Sheets[5].Id;
        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(_currentSheetId);
        _sheetGroupAnchor = _currentSheetId;
        RefreshSheetTabs();
        await Task.Delay(300);
        await CaptureSheetTabsAsync(outputDir, "six-sheets-active-06");

        PrepareSheetTabVisualTourWorkbook();
        await Task.Delay(400);

        var visibleSheets = _workbook.Sheets.Where(sheet => !sheet.IsHidden).Take(20).ToList();
        for (var index = 0; index < visibleSheets.Count; index++)
            await CaptureSheetTabStateAsync(outputDir, visibleSheets, index, $"active-{index + 1:00}-{visibleSheets[index].Name}");

        _currentSheetId = visibleSheets[2].Id;
        _groupedSheetIds.Clear();
        foreach (var sheet in visibleSheets.Skip(1).Take(4))
            _groupedSheetIds.Add(sheet.Id);
        _sheetGroupAnchor = visibleSheets[1].Id;
        RefreshSheetTabs();
        await Task.Delay(300);
        await CaptureSheetTabsAsync(outputDir, "grouped-sheets-2-through-5");

        _currentSheetId = visibleSheets[11].Id;
        _groupedSheetIds.Clear();
        foreach (var sheet in visibleSheets.Skip(9).Take(4))
            _groupedSheetIds.Add(sheet.Id);
        _sheetGroupAnchor = visibleSheets[9].Id;
        RefreshSheetTabs();
        await Task.Delay(300);
        await CaptureSheetTabsAsync(outputDir, "grouped-sheets-10-through-13");

        Width = 900;
        await Task.Delay(450);
        await CaptureSheetTabStateAsync(outputDir, visibleSheets, 0, "narrow-active-01");
        await CaptureSheetTabStateAsync(outputDir, visibleSheets, 7, "narrow-active-08");
        await CaptureSheetTabStateAsync(outputDir, visibleSheets, 15, "narrow-active-16");
        await CaptureSheetTabStateAsync(outputDir, visibleSheets, 19, "narrow-active-20");

        _currentSheetId = visibleSheets[19].Id;
        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(_currentSheetId);
        _sheetGroupAnchor = _currentSheetId;
        RefreshSheetTabs();
        await Task.Delay(260);
        SheetTabsScroller.ScrollToHorizontalOffset(0);
        await Task.Delay(200);
        await CaptureSheetTabsAsync(outputDir, "resize-preserve-before", revealCurrentSheet: false);
        Width = 760;
        await Task.Delay(450);
        await CaptureSheetTabsAsync(outputDir, "resize-preserve-after", revealCurrentSheet: false);

        Application.Current.Shutdown();
    }

    private async Task CaptureSheetTabStateAsync(
        string outputDir,
        IReadOnlyList<Sheet> visibleSheets,
        int activeIndex,
        string fileName)
    {
        var sheet = visibleSheets[activeIndex];
        _currentSheetId = sheet.Id;
        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(sheet.Id);
        _sheetGroupAnchor = sheet.Id;
        RefreshSheetTabs();
        await Task.Delay(260);
        await CaptureSheetTabsAsync(outputDir, fileName);
    }

    private void PrepareSheetTabVisualTourWorkbook()
    {
        while (_workbook.Sheets.Count < 20)
            _workbook.AddSheet(SheetTabListPlanner.GenerateUniqueSheetName(_workbook));

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
    }

    private async Task CaptureSheetTabsAsync(string outputDir, string fileName, bool revealCurrentSheet = true)
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
    }
}
