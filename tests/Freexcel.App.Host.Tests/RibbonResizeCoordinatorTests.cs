using FluentAssertions;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Freexcel.Core.Calc;
using Freexcel.Core.Commands;
using Freexcel.Core.Formula;
using Freexcel.Core.IO;
using Freexcel.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace Freexcel.App.Host.Tests;

public sealed class RibbonResizeCoordinatorTests
{
    private const int WmEnterSizeMove = 0x0231;
    private const int WmExitSizeMove = 0x0232;

    [Fact]
    public void FallbackScheduler_CoalescesLayoutAndResizeFallbacks()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = RibbonCoordinatorHarness.Create();

            harness.QueueLayoutNormalizeThenResizeCompact();

            var queued = harness.Diagnostics;
            queued.RequestCount.Should().Be(2);
            queued.PostedCount.Should().Be(1);
            queued.ExecutedCount.Should().Be(0);
            queued.IsPending.Should().BeTrue();
            queued.LastRequestedWork.Should().Be("CompactOnly");
            queued.LastMergedWork.Should().Be("NormalizeSurface");

            harness.PumpDispatcher();

            var executed = harness.Diagnostics;
            executed.ExecutedCount.Should().Be(1);
            executed.ForcedNormalizeCount.Should().Be(1);
            executed.ForcedCompactCount.Should().Be(0);
            executed.LastExecutedWork.Should().Be("NormalizeSurface");
            executed.IsPending.Should().BeFalse();
        });
    }

    [Fact]
    public void WindowResize_DebouncesViewportRefreshUntilResizeIdle()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = RibbonCoordinatorHarness.Create();

            harness.ResetDiagnostics();

            harness.ResizeWindow(1180, pumpDispatcher: false);

            harness.ViewportCallCount.Should().Be(0);
            harness.IsLiveResizing.Should().BeTrue();

            harness.PumpDispatcher();

            harness.ViewportCallCount.Should().Be(0);
            harness.IsLiveResizing.Should().BeTrue();

            harness.PumpUntil(() => harness.ViewportCallCount > 0 && !harness.IsLiveResizing);

            harness.ViewportCallCount.Should().BeGreaterThan(0);
            harness.IsLiveResizing.Should().BeFalse();
        });
    }

    [Fact]
    public void NativeResizeLoop_DefersViewportRefreshAndRibbonCompactionUntilExit()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = RibbonCoordinatorHarness.Create();

            harness.SelectRibbonTab("Home", width: 1500);
            harness.PrimeResizeGate();
            harness.ResetDiagnostics();

            harness.EnterNativeResizeLoop();
            harness.ResizeWindow(700);

            harness.IsLiveResizing.Should().BeTrue();
            harness.ViewportCallCount.Should().Be(0);
            var deferred = harness.Diagnostics;
            deferred.ResizeCompactionPendingOnExit.Should().BeTrue();
            deferred.RequestCount.Should().Be(0);
            deferred.PostedCount.Should().Be(0);

            harness.ExitNativeResizeLoop();

            harness.IsLiveResizing.Should().BeFalse();
            harness.ViewportCallCount.Should().BeGreaterThan(0);

            var queued = harness.Diagnostics;
            queued.ResizeCompactionPendingOnExit.Should().BeFalse();
            queued.RequestCount.Should().Be(1);
            queued.PostedCount.Should().Be(1);
            queued.LastMergedWork.Should().Be("CompactOnly");

            harness.PumpDispatcher();

            var executed = harness.Diagnostics;
            executed.ExecutedCount.Should().Be(1);
            executed.ForcedCompactCount.Should().Be(1);
            executed.LastExecutedWork.Should().Be("CompactOnly");
        });
    }

    [Fact]
    public void NativeResizeExit_OnlySchedulesFallbackWhenResizeLoopDeferredCompaction()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = RibbonCoordinatorHarness.Create();

            harness.CompleteResizeCompaction();
            harness.PumpDispatcher();
            harness.Diagnostics.RequestCount.Should().Be(0);
            harness.Diagnostics.PostedCount.Should().Be(0);

            harness.DeferResizeCompactionUntilExit();
            harness.Diagnostics.ResizeCompactionPendingOnExit.Should().BeTrue();
            harness.Diagnostics.RequestCount.Should().Be(0);

            harness.CompleteResizeCompaction();
            var queued = harness.Diagnostics;
            queued.ResizeCompactionPendingOnExit.Should().BeFalse();
            queued.RequestCount.Should().Be(1);
            queued.PostedCount.Should().Be(1);
            queued.LastMergedWork.Should().Be("CompactOnly");

            harness.PumpDispatcher();

            var executed = harness.Diagnostics;
            executed.ExecutedCount.Should().Be(1);
            executed.ForcedNormalizeCount.Should().Be(0);
            executed.ForcedCompactCount.Should().Be(1);
            executed.LastExecutedWork.Should().Be("CompactOnly");
        });
    }

    private sealed class RibbonCoordinatorHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly CountingViewportService _viewportService;
        private readonly MethodInfo _mainWindowWndProc;
        private readonly MethodInfo _updateRibbonCompactMode;
        private readonly MethodInfo _normalizeRibbonSurfaceAfterLayoutChange;
        private readonly MethodInfo _compactRibbonSurfaceAfterResize;
        private readonly MethodInfo _completeRibbonResizeCompaction;

        private RibbonCoordinatorHarness(MainWindow window, CountingViewportService viewportService)
        {
            _window = window;
            _viewportService = viewportService;
            _mainWindowWndProc = typeof(MainWindow).GetMethod(
                    "MainWindow_WndProc",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "MainWindow_WndProc");
            _updateRibbonCompactMode = typeof(MainWindow).GetMethod(
                    "UpdateRibbonCompactMode",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "UpdateRibbonCompactMode");
            _normalizeRibbonSurfaceAfterLayoutChange = typeof(MainWindow).GetMethod(
                    "NormalizeRibbonSurfaceAfterLayoutChange",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    [typeof(bool), typeof(bool)])
                ?? throw new MissingMethodException(nameof(MainWindow), "NormalizeRibbonSurfaceAfterLayoutChange");
            _compactRibbonSurfaceAfterResize = typeof(MainWindow).GetMethod(
                    "CompactRibbonSurfaceAfterResize",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "CompactRibbonSurfaceAfterResize");
            _completeRibbonResizeCompaction = typeof(MainWindow).GetMethod(
                    "CompleteRibbonResizeCompaction",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "CompleteRibbonResizeCompaction");
        }

        public RibbonFallbackDiagnosticsSnapshot Diagnostics => _window.GetRibbonFallbackDiagnosticsForTests();
        public int ViewportCallCount => _viewportService.GetViewportCallCount;
        public bool IsLiveResizing => SheetGrid.IsLiveResizing;

        private Freexcel.App.UI.GridView SheetGrid =>
            (Freexcel.App.UI.GridView)_window.FindName("SheetGrid");

        public void SelectRibbonTab(string header, double width)
        {
            if (_window.FindName("RibbonTabs") is TabControl tabs)
            {
                tabs.SelectedItem = tabs.Items
                    .OfType<TabItem>()
                    .First(item => string.Equals(item.Header?.ToString(), header, StringComparison.Ordinal));
            }

            ResizeWindow(width);
            _updateRibbonCompactMode.Invoke(_window, [true]);
            _window.UpdateLayout();
            PumpDispatcher();
        }

        public void PrimeResizeGate()
        {
            ResizeWindow(_window.Width - 1);
            PumpDispatcher();
        }

        public void ResizeWindow(double width, bool pumpDispatcher = true)
        {
            _window.WindowState = WindowState.Normal;
            _window.Width = width;
            _window.UpdateLayout();
            if (pumpDispatcher)
                PumpDispatcher();
        }

        public void EnterNativeResizeLoop() => InvokeWindowProcedure(WmEnterSizeMove);

        public void ExitNativeResizeLoop() => InvokeWindowProcedure(WmExitSizeMove);

        private void InvokeWindowProcedure(int message)
        {
            object?[] args = [IntPtr.Zero, message, IntPtr.Zero, IntPtr.Zero, false];
            _mainWindowWndProc.Invoke(_window, args);
        }

        public void QueueLayoutNormalizeThenResizeCompact()
        {
            _window.ResetRibbonFallbackDiagnosticsForTests();
            _normalizeRibbonSurfaceAfterLayoutChange.Invoke(_window, [false, true]);
            _compactRibbonSurfaceAfterResize.Invoke(_window, [true]);
        }

        public void DeferResizeCompactionUntilExit()
        {
            _window.ResetRibbonFallbackDiagnosticsForTests();
            _compactRibbonSurfaceAfterResize.Invoke(_window, [false]);
        }

        public void CompleteResizeCompaction()
        {
            _completeRibbonResizeCompaction.Invoke(_window, null);
        }

        public void ResetDiagnostics()
        {
            _window.ResetRibbonFallbackDiagnosticsForTests();
            _viewportService.Reset();
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

        public static RibbonCoordinatorHarness Create()
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = workbook };
            var graph = new DependencyGraph();
            var evaluator = new FormulaEvaluator();
            var viewportService = new CountingViewportService(new ViewportService());
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                viewportService,
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(graph, evaluator),
                Array.Empty<IFileAdapter>(),
                workbookRef,
                workbook,
                NullUserMessageService.Instance);

            window.Width = 1280;
            window.Height = 720;
            window.Show();
            var harness = new RibbonCoordinatorHarness(window, viewportService);
            harness.PumpDispatcher();
            harness.ResetDiagnostics();
            return harness;
        }

        public void Dispose()
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
        }
    }

    private sealed class CountingViewportService(IViewportService inner) : IViewportService
    {
        public int GetViewportCallCount { get; private set; }

        public ViewportModel GetViewport(Workbook workbook, SheetId sheetId, ViewportRequest request)
        {
            GetViewportCallCount++;
            return inner.GetViewport(workbook, sheetId, request);
        }

        public CellAddress? HitTest(Workbook workbook, SheetId sheetId, double x, double y, double zoom) =>
            inner.HitTest(workbook, sheetId, x, y, zoom);

        public void Reset() => GetViewportCallCount = 0;
    }

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new InvalidOperationException($"Sheet {sheetId} not found");
    }
}
