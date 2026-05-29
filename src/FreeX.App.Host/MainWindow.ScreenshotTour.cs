using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private const double ScreenshotTourCaptureHeight = 300;

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    private static readonly (string Header, string FileName)[] TourTabs =
    [
        ("Home", "Home"),
        ("Insert", "Insert"),
        ("Draw", "Draw"),
        ("Page Layout", "Page_Layout"),
        ("Formulas", "Formulas"),
        ("Data", "Data"),
        ("Review", "Review"),
        ("View", "View"),
        ("Help", "Help"),
    ];

    // Activated by FREEX_SS_TOUR=1 env var.  Output lands in <repo-root>/screenshots/.
    private void TryStartScreenshotTour()
    {
        var ribbonTour = Environment.GetEnvironmentVariable("FREEX_SS_TOUR") == "1";
        var backstageTour = Environment.GetEnvironmentVariable("FREEX_BACKSTAGE_TOUR") == "1";
        if (!ribbonTour && !backstageTour)
            return;

        var outputDir = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "screenshots"));
        Directory.CreateDirectory(outputDir);
        _ = RunScreenshotTourAsync(outputDir, ribbonTour, backstageTour);
    }

    private async Task RunScreenshotTourAsync(string outputDir, bool ribbonTour, bool backstageTour)
    {
        if (ribbonTour)
        {
            await Task.Delay(1200);
            await CaptureAllTabsAsync(outputDir, "max");

            WindowState = WindowState.Normal;
            Width = 1100; Height = 768;
            await Task.Delay(600);
            await CaptureAllTabsAsync(outputDir, "1100");

            Width = 900;
            await Task.Delay(600);
            await CaptureAllTabsAsync(outputDir, "900");

            Width = 750;
            await Task.Delay(600);
            await CaptureAllTabsAsync(outputDir, "750");
        }

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

    private async Task CaptureAllTabsAsync(string outputDir, string label)
    {
        foreach (var (header, fileName) in TourTabs)
        {
            var tab = RibbonTabs.Items
                .OfType<System.Windows.Controls.TabItem>()
                .FirstOrDefault(item => string.Equals(item.Header?.ToString(), header, StringComparison.Ordinal));

            if (tab is null)
                continue;

            RibbonTabs.SelectedItem = tab;
            UpdateLayout();
            await Task.Delay(350);
            UpdateLayout();

            await CaptureCurrentWindowAsync(outputDir, $"{label}_{fileName}", ScreenshotTourCaptureHeight);
        }
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

    // Activated by FREEX_GREEN_BAR_TOUR=1 env var. Output lands in <repo-root>/screenshots/green-bars-tour/.
    private void TryStartGreenBarVisualTour()
    {
        if (Environment.GetEnvironmentVariable("FREEX_GREEN_BAR_TOUR") != "1")
            return;

        var outputDir = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "screenshots", "green-bars-tour"));
        Directory.CreateDirectory(outputDir);
        _ = RunGreenBarVisualTourAsync(outputDir);
    }

    private async Task RunGreenBarVisualTourAsync(string outputDir)
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
