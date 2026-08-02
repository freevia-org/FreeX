using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using Free.Shared.AppServices;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R116-chartsheet-dpi-raster-refresh: <c>RenderActiveChartsheet</c>
/// (MainWindow.Chartsheet.cs) reads <c>VisualTreeHelper.GetDpi(ChartsheetView)</c> once and bakes that
/// scale into the chartsheet's raster bitmap, and was only ever re-invoked from
/// <c>ChartsheetView_SizeChanged</c>. A per-monitor DPI change that crosses monitors WITHOUT the
/// window's DIP size changing (the common docking-station scenario: drag a maximized/fixed-size
/// window from a 96-DPI monitor to a 200%-scaled one) never raises SizeChanged, so the bitmap stayed
/// baked at the stale DPI and rendered blurry/pixelated until a manual resize or a sheet-switch away
/// and back. The fix extends the existing WM_DPICHANGED WndProc hook in MainWindow.AltKeyTips.cs
/// (previously used only to re-pin the taskbar icon) to also call the new
/// <c>RefreshChartsheetForDpiChange</c>, which re-runs the SAME <c>RenderActiveChartsheet</c> choke
/// point <c>ChartsheetView_SizeChanged</c> uses. Drives the real <c>MainWindow_WndProc</c> instance
/// method via reflection -- the same hook every genuine WM_DPICHANGED window message reaches -- rather
/// than calling the renderer directly.
/// </summary>
public sealed class R116_ChartsheetDpiChangeRefreshTests
{
    private const int WmDpiChanged = 0x02E0;

    /// <summary>Placeholder registered ahead of time so the real window adopts our seeded workbook
    /// instead of MainWindow_Loaded replacing it with a fresh one.</summary>
    private sealed class DocumentPlaceholderWindow(WorkbookId documentId) : IWorkbookWindow
    {
        public WorkbookId DocumentId { get; } = documentId;
        public void ApplyWindowTitleSuffix(string suffix) { }
        public void RefreshFromSharedWorkbook() { }
        public void RefreshTitleBar() { }
        public void ActivateWindow() { }
        public void SetWindowVisible(bool visible) { }
        public WorkbookScrollOffset GetScrollOffset() => default;
        public void SetScrollOffset(WorkbookScrollOffset offset) { }
        public void TileToWorkArea(Rect bounds) { }
        public void ApplyFormulaBarVisibility(bool visible) { }
        public void ApplySaveInProgress(bool inProgress) { }
    }

    private static (MainWindow Window, Workbook Workbook, Sheet DataSheet, Sheet Chartsheet) CreateWindowWithChartsheet()
    {
        var workbook = new Workbook("Book1");

        // A worksheet holding the chart's series data (chartsheets have no cell grid of their own --
        // RenderActiveChartsheet resolves the data sheet from chart.DataRange.Start.Sheet).
        var dataSheet = workbook.AddSheet("Sheet1");
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 1, 1), Cell.FromValue(new NumberValue(1)));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 2, 1), Cell.FromValue(new NumberValue(2)));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 3, 1), Cell.FromValue(new NumberValue(3)));

        var chartsheet = workbook.AddSheet("Chart1");
        chartsheet.Kind = SheetKind.Chartsheet;
        chartsheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Line,
            Title = "Value",
            DataRange = new GridRange(
                new CellAddress(dataSheet.Id, 1, 1),
                new CellAddress(dataSheet.Id, 3, 1))
        });

        var workbookRef = new WorkbookRef { Current = workbook };
        var registry = new WorkbookWindowRegistry();
        registry.Register(new DocumentPlaceholderWindow(workbook.Id));

        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var commandBus = new CommandBus(_ => new TestCommandContext(workbookRef.Current));
        var window = new MainWindow(
            NullLogger<MainWindow>.Instance,
            new ViewportService(),
            commandBus,
            new RecalcEngine(graph, evaluator),
            [],
            workbookRef,
            workbookRef.Current,
            NullUserMessageService.Instance,
            new WorkbookDocumentState(),
            windowRegistry: registry)
        {
            WindowState = WindowState.Normal,
            Width = 1280,
            Height = 720
        };

        window.Show();
        window.Activate();
        window.UpdateLayout();
        PumpDispatcher();

        return (window, workbook, dataSheet, chartsheet);
    }

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    /// <summary>Switches the window's active sheet to the chartsheet through the same
    /// _currentSheetId + UpdateViewport pair every real sheet-tab click drives, via reflection since
    /// both are private.</summary>
    private static void SwitchToSheet(MainWindow window, SheetId sheetId)
    {
        var currentSheetIdField = typeof(MainWindow).GetField(
            "_currentSheetId", BindingFlags.Instance | BindingFlags.NonPublic);
        currentSheetIdField.Should().NotBeNull();
        currentSheetIdField!.SetValue(window, sheetId);

        var updateViewport = typeof(MainWindow).GetMethod(
            "UpdateViewport", BindingFlags.Instance | BindingFlags.NonPublic);
        updateViewport.Should().NotBeNull();
        updateViewport!.Invoke(window, null);
    }

    private static Image GetChartsheetView(MainWindow window) =>
        (Image)window.FindName("ChartsheetView")!;

    /// <summary>
    /// Establishes the realistic precondition this defect assumes: a chartsheet that has ALREADY
    /// been rendered and is currently on screen (as opposed to the moment it is first activated).
    /// WPF's <see cref="Image"/> control reports an ActualWidth/ActualHeight of zero whenever its
    /// Source is null -- regardless of Stretch mode, alignment, or even an explicit Width/Height --
    /// so a never-yet-rendered ChartsheetView cannot be measured by layout alone; seeding a
    /// placeholder bitmap here only works around that WPF-level precondition to reach the "already
    /// showing" state, and is not itself part of the fix under test. Once ChartsheetView has a real
    /// size, the private RenderActiveChartsheet choke point is invoked directly (the same one
    /// ChartsheetView_SizeChanged and the new WM_DPICHANGED hook both call) to produce a genuine,
    /// chart-accurate initial bitmap.
    /// </summary>
    private static BitmapSource SeedInitialChartsheetRender(MainWindow window, Sheet chartsheet)
    {
        var chartsheetView = GetChartsheetView(window);
        chartsheetView.Source = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
        window.UpdateLayout();
        chartsheetView.ActualWidth.Should().BeGreaterThan(0, "the placeholder bitmap must have given ChartsheetView a real measured size");

        var renderMethod = typeof(MainWindow).GetMethod(
            "RenderActiveChartsheet", BindingFlags.Instance | BindingFlags.NonPublic);
        renderMethod.Should().NotBeNull();
        renderMethod!.Invoke(window, [chartsheet]);

        var initialSource = chartsheetView.Source as BitmapSource;
        initialSource.Should().NotBeNull("RenderActiveChartsheet must have produced a real chart bitmap once ChartsheetView had a size");
        return initialSource!;
    }

    /// <summary>Drives the exact same private WndProc choke point every real WM_DPICHANGED (or
    /// WM_ENTERSIZEMOVE/WM_EXITSIZEMOVE) window message reaches (MainWindow.AltKeyTips.cs).</summary>
    private static void InvokeWndProc(MainWindow window, int message)
    {
        var method = typeof(MainWindow).GetMethod(
            "MainWindow_WndProc", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        object?[] args = [IntPtr.Zero, message, IntPtr.Zero, IntPtr.Zero, false];
        method!.Invoke(window, args);
    }

    [Fact]
    public void WmDpiChanged_WhileChartsheetIsActive_RerendersTheChartBitmap() =>
        StaTestRunner.Run(() =>
    {
        var (window, _, _, chartsheet) = CreateWindowWithChartsheet();
        try
        {
            SwitchToSheet(window, chartsheet.Id);
            window.UpdateLayout();
            PumpDispatcher();

            var chartsheetView = GetChartsheetView(window);
            chartsheetView.Visibility.Should().Be(
                Visibility.Visible, "the active sheet is a chartsheet");

            var initialSource = SeedInitialChartsheetRender(window, chartsheet);

            // Simulate the window crossing to a differently-scaled monitor without its DIP size
            // changing: no SizeChanged fires, only WM_DPICHANGED.
            InvokeWndProc(window, WmDpiChanged);
            // The fix defers the refresh to DispatcherPriority.Background so it runs after WPF's own
            // per-monitor-DPI layout pass; pump the dispatcher to let it run.
            PumpDispatcher();

            chartsheetView.Source.Should().NotBeSameAs(initialSource,
                "WM_DPICHANGED must trigger a fresh RenderActiveChartsheet call so the chart is " +
                "re-rasterized at the new monitor's DPI, even though the window's DIP size (and " +
                "therefore SizeChanged) never changed");
        }
        finally
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(window);
            PumpDispatcher();
        }
    });

    /// <summary>No-regression sibling: a WM_DPICHANGED notification while a plain worksheet (not a
    /// chartsheet) is active must not touch the (hidden) ChartsheetView at all -- it must stay
    /// collapsed with no bitmap, exactly as ChartsheetView_SizeChanged already guards for resizes.</summary>
    [Fact]
    public void WmDpiChanged_WhileANormalWorksheetIsActive_LeavesTheChartsheetViewUntouched() =>
        StaTestRunner.Run(() =>
    {
        var (window, _, dataSheet, _) = CreateWindowWithChartsheet();
        try
        {
            // _currentSheetId defaults to the workbook's first sheet (the plain data worksheet); the
            // chartsheet is never activated in this test.
            SwitchToSheet(window, dataSheet.Id);
            window.UpdateLayout();
            PumpDispatcher();

            var chartsheetView = GetChartsheetView(window);
            chartsheetView.Visibility.Should().Be(
                Visibility.Collapsed, "the active sheet is a normal worksheet, not the chartsheet");
            chartsheetView.Source.Should().BeNull();

            Action act = () => InvokeWndProc(window, WmDpiChanged);
            act.Should().NotThrow();
            PumpDispatcher();

            chartsheetView.Visibility.Should().Be(Visibility.Collapsed,
                "a DPI-change refresh must not reveal the chartsheet view for a plain worksheet");
            chartsheetView.Source.Should().BeNull(
                "a DPI-change refresh must not render a chart bitmap when no chartsheet is active");
        }
        finally
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(window);
            PumpDispatcher();
        }
    });
}
