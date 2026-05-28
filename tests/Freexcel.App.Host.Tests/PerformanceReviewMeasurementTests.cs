using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Calc;
using Freexcel.Core.Commands;
using Freexcel.Core.Formula;
using Freexcel.Core.IO;
using Freexcel.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Freexcel.App.Host.Tests;

public sealed class PerformanceReviewMeasurementTests
{
    [Fact]
    public void Benchmark_ColumnResizePreview_ReportsTiming()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = ColumnResizePreviewHarness.Create();
            harness.MeasurePreview(iterations: 10);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var result = harness.MeasurePreview(iterations: 100);
            Console.WriteLine(
                "PERF COLUMN_RESIZE_PREVIEW " +
                $"steps={result.StepCount} total_ms={result.TotalMilliseconds:F2} " +
                $"mean_ms={result.MeanMilliseconds:F2} p95_ms={result.P95Milliseconds:F2} " +
                $"max_ms={result.MaxMilliseconds:F2} allocated_bytes={result.AllocatedBytes:N0} " +
                $"viewport_gets={result.ViewportCalls:N0}");

            result.StepCount.Should().Be(100);
            result.ViewportCalls.Should().Be(0);
            result.TotalMilliseconds.Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public void Benchmark_RibbonResizeSequence_ReportsTiming()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = RibbonResizeHarness.Create();
            var widths = new[]
            {
                1500d, 1465d, 1400d, 1366d, 1320d, 1280d, 1200d, 1120d,
                1000d, 920d, 900d, 820d, 760d, 700d, 640d, 760d,
                900d, 1120d, 1280d, 1366d, 1465d, 1500d
            };

            harness.SelectRibbonTab("Home", 1500);
            harness.MeasureWindowResizeSequence(widths, iterations: 1);
            harness.MeasureForcedCompactSequence(widths, iterations: 1);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var resize = harness.MeasureWindowResizeSequence(widths, iterations: 3);
            var forcedCompact = harness.MeasureForcedCompactSequence(widths, iterations: 3);

            Console.WriteLine(
                "PERF RIBBON_RESIZE " +
                $"steps={resize.StepCount} total_ms={resize.TotalMilliseconds:F2} " +
                $"mean_ms={resize.MeanMilliseconds:F2} p95_ms={resize.P95Milliseconds:F2} " +
                $"max_ms={resize.MaxMilliseconds:F2} allocated_bytes={resize.AllocatedBytes:N0}");
            Console.WriteLine(
                "PERF RIBBON_FORCE_COMPACT " +
                $"steps={forcedCompact.StepCount} total_ms={forcedCompact.TotalMilliseconds:F2} " +
                $"mean_ms={forcedCompact.MeanMilliseconds:F2} p95_ms={forcedCompact.P95Milliseconds:F2} " +
                $"max_ms={forcedCompact.MaxMilliseconds:F2} allocated_bytes={forcedCompact.AllocatedBytes:N0}");

            resize.StepCount.Should().Be(widths.Length * 3);
            forcedCompact.StepCount.Should().Be(widths.Length * 3);
            resize.TotalMilliseconds.Should().BeGreaterThan(0);
            forcedCompact.TotalMilliseconds.Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public void Benchmark_SelectionDragStatusRefresh_ReportsTiming()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = SelectionDragHarness.Create();
            harness.MeasureDragSelection(iterations: 10);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var result = harness.MeasureDragSelection(iterations: 80);
            Console.WriteLine(
                "PERF SELECTION_DRAG_STATUS " +
                $"steps={result.StepCount} total_ms={result.TotalMilliseconds:F2} " +
                $"mean_ms={result.MeanMilliseconds:F2} p95_ms={result.P95Milliseconds:F2} " +
                $"max_ms={result.MaxMilliseconds:F2} allocated_bytes={result.AllocatedBytes:N0}");

            result.StepCount.Should().Be(80);
            result.TotalMilliseconds.Should().BeGreaterThan(0);
        });
    }

    private sealed class RibbonResizeHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly MethodInfo _updateRibbonCompactMode;

        private RibbonResizeHarness(MainWindow window)
        {
            _window = window;
            _updateRibbonCompactMode = typeof(MainWindow)
                .GetMethod("UpdateRibbonCompactMode", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "UpdateRibbonCompactMode");
        }

        public void SelectRibbonTab(string header, double width)
        {
            if (_window.FindName("RibbonTabs") is TabControl tabs)
            {
                tabs.SelectedItem = tabs.Items
                    .OfType<TabItem>()
                    .First(item => string.Equals(item.Header?.ToString(), header, StringComparison.Ordinal));
            }

            _window.WindowState = WindowState.Normal;
            _window.Width = width;
            _window.Height = 720;
            _window.UpdateLayout();
            PumpDispatcher();
            PumpDispatcher();
            _updateRibbonCompactMode.Invoke(_window, [true]);
            PumpDispatcher();
        }

        public MeasurementResult MeasureWindowResizeSequence(IReadOnlyList<double> widths, int iterations)
        {
            var timings = new List<double>(widths.Count * iterations);
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var total = Stopwatch.StartNew();
            for (var iteration = 0; iteration < iterations; iteration++)
            {
                foreach (var width in widths)
                {
                    var step = Stopwatch.StartNew();
                    _window.Width = width;
                    _window.UpdateLayout();
                    PumpDispatcher();
                    step.Stop();
                    timings.Add(step.Elapsed.TotalMilliseconds);
                }
            }

            total.Stop();
            return MeasurementResult.From(timings, total.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
        }

        public MeasurementResult MeasureForcedCompactSequence(IReadOnlyList<double> widths, int iterations)
        {
            var timings = new List<double>(widths.Count * iterations);
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var total = Stopwatch.StartNew();
            for (var iteration = 0; iteration < iterations; iteration++)
            {
                foreach (var width in widths)
                {
                    _window.Width = width;
                    _window.UpdateLayout();

                    var step = Stopwatch.StartNew();
                    _updateRibbonCompactMode.Invoke(_window, [true]);
                    PumpDispatcher();
                    step.Stop();
                    timings.Add(step.Elapsed.TotalMilliseconds);
                }
            }

            total.Stop();
            return MeasurementResult.From(timings, total.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
        }

        public static RibbonResizeHarness Create()
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = workbook };
            var graph = new DependencyGraph();
            var evaluator = new FormulaEvaluator();
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(graph, evaluator),
                Array.Empty<IFileAdapter>(),
                workbookRef,
                workbook);

            window.Width = 1500;
            window.Height = 720;
            window.Show();
            PumpDispatcher();
            return new RibbonResizeHarness(window);
        }

        public void Dispose()
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
        }
    }

    private sealed class ColumnResizePreviewHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly CountingViewportService _viewportService;
        private readonly MethodInfo _onColumnResizing;

        private ColumnResizePreviewHarness(MainWindow window, CountingViewportService viewportService)
        {
            _window = window;
            _viewportService = viewportService;
            _onColumnResizing = typeof(MainWindow)
                .GetMethod("OnColumnResizing", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "OnColumnResizing");
        }

        public MeasurementResult MeasurePreview(int iterations)
        {
            var timings = new List<double>(iterations);
            _viewportService.Reset();
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var total = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
            {
                var width = 72d + i % 40;
                var step = Stopwatch.StartNew();
                _onColumnResizing.Invoke(_window, [3u, width]);
                PumpDispatcher();
                step.Stop();
                timings.Add(step.Elapsed.TotalMilliseconds);
            }

            total.Stop();
            return MeasurementResult.From(
                timings,
                total.Elapsed.TotalMilliseconds,
                GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
                _viewportService.GetViewportCallCount);
        }

        public static ColumnResizePreviewHarness Create()
        {
            var workbook = new Workbook("Book1");
            var sheet = workbook.AddSheet("Sheet1");
            for (uint row = 1; row <= 200; row++)
            {
                for (uint col = 1; col <= 20; col++)
                    sheet.SetCell(new CellAddress(sheet.Id, row, col), new TextValue($"R{row}C{col}"));
            }

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
                workbook)
            {
                Width = 1280,
                Height = 720
            };

            window.Show();
            window.UpdateLayout();
            PumpDispatcher();
            viewportService.Reset();
            return new ColumnResizePreviewHarness(window, viewportService);
        }

        public void Dispose()
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
        }
    }

    private sealed class SelectionDragHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly MethodInfo _extendSelection;
        private readonly FieldInfo _dragSelectActive;
        private readonly CellAddress _anchor;

        private SelectionDragHarness(MainWindow window, SheetId sheetId)
        {
            _window = window;
            _anchor = new CellAddress(sheetId, 1, 1);
            _extendSelection = typeof(MainWindow)
                .GetMethod("ExtendSelection", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "ExtendSelection");
            _dragSelectActive = typeof(MainWindow)
                .GetField("_dragSelectActive", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_dragSelectActive");
        }

        public MeasurementResult MeasureDragSelection(int iterations)
        {
            var timings = new List<double>(iterations);
            _dragSelectActive.SetValue(_window, true);
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var total = Stopwatch.StartNew();
            try
            {
                for (var i = 0; i < iterations; i++)
                {
                    var row = (uint)(20 + i * 6);
                    var step = Stopwatch.StartNew();
                    _extendSelection.Invoke(_window, [_anchor, new CellAddress(_anchor.Sheet, row, 40)]);
                    PumpDispatcher();
                    step.Stop();
                    timings.Add(step.Elapsed.TotalMilliseconds);
                }
            }
            finally
            {
                _dragSelectActive.SetValue(_window, false);
            }

            total.Stop();
            return MeasurementResult.From(timings, total.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
        }

        public static SelectionDragHarness Create()
        {
            var workbook = new Workbook("Book1");
            var sheet = workbook.AddSheet("Sheet1");
            for (uint row = 1; row <= 600; row++)
            {
                for (uint col = 1; col <= 40; col++)
                    sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(row * col));
            }

            var workbookRef = new WorkbookRef { Current = workbook };
            var graph = new DependencyGraph();
            var evaluator = new FormulaEvaluator();
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(graph, evaluator),
                Array.Empty<IFileAdapter>(),
                workbookRef,
                workbook)
            {
                Width = 1280,
                Height = 720
            };

            window.Show();
            window.UpdateLayout();
            PumpDispatcher();
            return new SelectionDragHarness(window, sheet.Id);
        }

        public void Dispose()
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
        }
    }

    private sealed record MeasurementResult(
        int StepCount,
        double TotalMilliseconds,
        double MeanMilliseconds,
        double P95Milliseconds,
        double MaxMilliseconds,
        long AllocatedBytes,
        int ViewportCalls = 0)
    {
        public static MeasurementResult From(
            IReadOnlyList<double> timings,
            double totalMilliseconds,
            long allocatedBytes,
            int viewportCalls = 0)
        {
            var ordered = timings.OrderBy(value => value).ToArray();
            var p95Index = Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1);
            return new MeasurementResult(
                timings.Count,
                totalMilliseconds,
                timings.Average(),
                ordered[p95Index],
                ordered[^1],
                allocatedBytes,
                viewportCalls);
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

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
}
