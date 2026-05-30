using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private const double ScreenshotTourCaptureHeight = 300;

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    // Activated by FREEX_SS_TOUR=1 env var.  Output lands in <repo-root>/screenshots/.
    private async void TryStartScreenshotTour()
    {
        var ribbonBurstTour = Environment.GetEnvironmentVariable("FREEX_SS_TOUR_BURST") == "1";
        var ribbonTour = ribbonBurstTour || Environment.GetEnvironmentVariable("FREEX_SS_TOUR") == "1";
        var backstageTour = Environment.GetEnvironmentVariable("FREEX_BACKSTAGE_TOUR") == "1";
        if (!ribbonTour && !backstageTour)
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
        await RunScreenshotTourAsync(outputDir, ribbonPlan, backstageTour);
    }

    private async Task RunScreenshotTourAsync(string outputDir, RibbonScreenshotTourPlan? ribbonPlan, bool backstageTour)
    {
        if (ribbonPlan is not null)
            await CaptureRibbonTourAsync(outputDir, ribbonPlan);

        if (backstageTour)
            await CaptureBackstageAsync(outputDir);

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

    private async Task CaptureRibbonTourAsync(string outputDir, RibbonScreenshotTourPlan plan)
    {
        await PrepareRibbonScreenshotTourContextAsync(plan.Context);
        DeleteStaleRibbonScreenshotTourCaptures(outputDir, plan);

        if (plan.IsBurst)
        {
            await CaptureRibbonBurstTourAsync(outputDir, plan);
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

        await WriteRibbonScreenshotTourManifestAsync(outputDir, plan);
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
            default:
                throw new InvalidOperationException($"Unknown ribbon screenshot tour context '{context}'.");
        }

        UpdateViewport();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
    }

    private void EnsureTableDesignScreenshotTourContext()
    {
        var sheet = _workbook.GetSheet(_currentSheetId) ?? _workbook.Sheets.FirstOrDefault();
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
        var table = sheet.StructuredTables
            .FirstOrDefault(candidate => string.Equals(candidate.Name, "TourTable", StringComparison.OrdinalIgnoreCase));
        if (table is null)
        {
            table = new StructuredTableModel
            {
                Id = sheet.StructuredTables.Count == 0 ? 1 : sheet.StructuredTables.Max(candidate => candidate.Id) + 1,
                Name = "TourTable",
                DisplayName = "TourTable",
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
        var tabItem = RibbonTabs.Items
            .OfType<System.Windows.Controls.TabItem>()
            .FirstOrDefault(item => string.Equals(item.Header?.ToString(), tab.Header, StringComparison.Ordinal));

        if (tabItem is null)
            throw new InvalidOperationException(
                $"Ribbon screenshot tour expected tab '{tab.Header}' but it was not found in the live ribbon.");

        RibbonTabs.SelectedItem = tabItem;
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
        var source = PresentationSource.FromVisual(this);
        var dpiX = source?.CompositionTarget.TransformToDevice.M11 ?? 1.0;
        var dpiY = source?.CompositionTarget.TransformToDevice.M22 ?? 1.0;
        int pw = Math.Max(1, (int)(ActualWidth * dpiX));
        int ph = Math.Max(1, (int)(Math.Min(ActualHeight, logicalHeight) * dpiY));

        var rtb = new RenderTargetBitmap(pw, ph, 96 * dpiX, 96 * dpiY, PixelFormats.Pbgra32);
        rtb.Render(this);
        var bitmap = new CroppedBitmap(rtb, new Int32Rect(0, 0, pw, ph));

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(outputDir, $"{fileName}.png");
        await using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static async Task WriteRibbonScreenshotTourManifestAsync(string outputDir, RibbonScreenshotTourPlan plan)
    {
        var manifest = new RibbonScreenshotTourManifest(
            Tool: "FREEX_SS_TOUR",
            OutputDirectory: outputDir,
            CatalogEvidenceTarget: "docs/UI_TEST_CATALOG.md",
            Context: plan.Context,
            BurstMode: plan.IsBurst,
            CaptureLogicalHeight: ScreenshotTourCaptureHeight,
            PlannedCaptureCount: plan.Captures.Count,
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
                    capture.Tab.Header,
                    capture.Width.Label,
                    capture.Phase.Label,
                    $"{capture.FileName}.png"))
                .ToArray(),
            Limitations:
            [
                "Ribbon captures cover the top window band only.",
                "Transient popups, dropdowns, native dialogs, and context menus require separate guarded captures.",
                "This in-app tour deletes only the currently requested plan's expected PNG files before capture."
            ]);

        var path = Path.Combine(outputDir, "ribbon_screenshot_tour_manifest.json");
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.RibbonScreenshotTourManifest);
    }

    private sealed record RibbonScreenshotTourManifest(
        string Tool,
        string OutputDirectory,
        string CatalogEvidenceTarget,
        string? Context,
        bool BurstMode,
        double CaptureLogicalHeight,
        int PlannedCaptureCount,
        IReadOnlyList<string> Tabs,
        IReadOnlyList<RibbonScreenshotTourManifestWidth> Widths,
        IReadOnlyList<RibbonScreenshotTourManifestPhase> Phases,
        IReadOnlyList<RibbonScreenshotTourManifestCapture> Captures,
        IReadOnlyList<string> Limitations);

    private sealed record RibbonScreenshotTourManifestWidth(string Label, double? WindowWidth, string EvidencePurpose);

    private sealed record RibbonScreenshotTourManifestPhase(string Label, string? FileNameSuffix);

    private sealed record RibbonScreenshotTourManifestCapture(string Tab, string Width, string Phase, string FileName);

    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(RibbonScreenshotTourManifest))]
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

        await HoverAndCaptureElementAsync(SaveQatBtn, TitleBarRoot, outputDir, "title-save-hover");
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
