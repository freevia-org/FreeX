using System.Diagnostics;
using System.Reflection;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

public sealed partial class PerformanceReviewMeasurementTests
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
            result.ViewportCalls.Should().BeLessThanOrEqualTo(1);
            result.TotalMilliseconds.Should().BeGreaterThan(0);
        });
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
                workbook,
                NullUserMessageService.Instance)
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
}
