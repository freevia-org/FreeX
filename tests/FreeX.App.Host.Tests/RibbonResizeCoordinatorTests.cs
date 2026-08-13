using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Windows.Threading;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

public sealed class RibbonResizeCoordinatorTests
{
    private const int WmEnterSizeMove = 0x0231;
    private const int WmExitSizeMove = 0x0232;

    [Fact]
    public void WindowResize_DelegatesAdaptiveLayoutToSharedWpfPanel()
    {
        var resizeSource = DialogSourceTestSupport.ReadHostSources("MainWindow.WorkbookUiState.cs");
        var ribbonSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Ribbon.cs");

        resizeSource.Should().Contain("NormalizeRibbonSurfaceAfterResize();");
        ribbonSource.Should().Contain("RefreshActiveDeclarativeRibbonLayout(forceLayout: false);");
        ribbonSource.Should().Contain("panel.InvalidateMeasure();");
        ribbonSource.Should().NotContain("RibbonResizeThresholdGate");
        ribbonSource.Should().NotContain("RibbonAdaptiveLayoutEngine");
    }

    [Fact]
    public void WindowResize_DebouncesViewportRefreshUntilResizeIdle()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = ResizeHarness.Create();
            harness.ResizeWindow(1180, pumpDispatcher: false);

            harness.ViewportCallCount.Should().Be(0);
            harness.IsLiveResizing.Should().BeTrue();

            harness.PumpUntil(() => harness.ViewportCallCount > 0 && !harness.IsLiveResizing);
        });
    }

    [Fact]
    public void NativeMoveLoop_DoesNotEnterLiveResizeUntilWidthChanges()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = ResizeHarness.Create();
            harness.PumpUntil(() => !harness.IsLiveResizing);
            harness.ResetViewportCalls();

            harness.EnterNativeResizeLoop();
            harness.IsLiveResizing.Should().BeFalse();

            harness.ExitNativeResizeLoop();
            harness.IsLiveResizing.Should().BeFalse();
            harness.ViewportCallCount.Should().Be(0);
        });
    }

    private sealed class ResizeHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly CountingViewportService _viewportService;
        private readonly MethodInfo _mainWindowWndProc;

        private ResizeHarness(MainWindow window, CountingViewportService viewportService)
        {
            _window = window;
            _viewportService = viewportService;
            _mainWindowWndProc = typeof(MainWindow).GetMethod(
                    "MainWindow_WndProc",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "MainWindow_WndProc");
        }

        public int ViewportCallCount => _viewportService.GetViewportCallCount;
        public bool IsLiveResizing => SheetGrid.IsLiveResizing;

        private FreeX.App.UI.GridView SheetGrid =>
            (FreeX.App.UI.GridView)_window.FindName("SheetGrid");

        public void ResizeWindow(double width, bool pumpDispatcher = true)
        {
            _window.WindowState = System.Windows.WindowState.Normal;
            _window.Width = width;
            _window.UpdateLayout();
            if (pumpDispatcher)
                PumpDispatcher();
        }

        public void EnterNativeResizeLoop() => InvokeWindowProcedure(WmEnterSizeMove);

        public void ExitNativeResizeLoop() => InvokeWindowProcedure(WmExitSizeMove);

        public void ResetViewportCalls() => _viewportService.Reset();

        private void InvokeWindowProcedure(int message)
        {
            object?[] args = [IntPtr.Zero, message, IntPtr.Zero, IntPtr.Zero, false];
            _mainWindowWndProc.Invoke(_window, args);
        }

        public void PumpDispatcher()
        {
            _window.UpdateLayout();
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        public void PumpUntil(Func<bool> condition, int timeoutMilliseconds = 2000)
        {
            var stopwatch = Stopwatch.StartNew();
            while (!condition())
            {
                if (stopwatch.ElapsedMilliseconds > timeoutMilliseconds)
                    throw new TimeoutException("The dispatcher condition was not reached before the timeout.");

                Thread.Sleep(10);
                PumpDispatcher();
            }
        }

        public static ResizeHarness Create()
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = workbook };
            var viewportService = new CountingViewportService(new ViewportService());
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                viewportService,
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                Array.Empty<IFileAdapter>(),
                workbookRef,
                workbook,
                NullUserMessageService.Instance)
            {
                Width = 1280,
                Height = 720
            };

            window.Show();
            var harness = new ResizeHarness(window, viewportService);
            harness.PumpDispatcher();
            viewportService.Reset();
            return harness;
        }

        public void Dispose() => MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
    }

    private sealed class CountingViewportService(IViewportService inner) : IViewportService
    {
        public int GetViewportCallCount { get; private set; }

        public ViewportModel GetViewport(Workbook workbook, SheetId sheetId, ViewportRequest request)
        {
            GetViewportCallCount++;
            return inner.GetViewport(workbook, sheetId, request);
        }

        public (uint LastVisibleRow, IReadOnlyList<OutlineGroupRange> RowOutlineGroups)
            ComputeRowMetricsSummary(Workbook workbook, SheetId sheetId, ViewportRequest request) =>
            inner.ComputeRowMetricsSummary(workbook, sheetId, request);

        public CellAddress? HitTest(Workbook workbook, SheetId sheetId, double x, double y, double zoom) =>
            inner.HitTest(workbook, sheetId, x, y, zoom);

        public void Reset() => GetViewportCallCount = 0;
    }
}
