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
    [BenchmarkFact]
    public void Benchmark_ViewportSidePaneRefresh_ReportsTiming()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = ViewportSidePaneRefreshHarness.Create();
            harness.MeasureUpdateViewport(iterations: 10);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var result = harness.MeasureUpdateViewport(iterations: 80);
            Console.WriteLine(
                "PERF VIEWPORT_SIDE_PANE_REFRESH " +
                $"steps={result.StepCount} total_ms={result.TotalMilliseconds:F2} " +
                $"mean_ms={result.MeanMilliseconds:F2} p95_ms={result.P95Milliseconds:F2} " +
                $"max_ms={result.MaxMilliseconds:F2} allocated_bytes={result.AllocatedBytes:N0} " +
                $"viewport_gets={result.ViewportCalls:N0}");

            result.StepCount.Should().Be(80);
            result.ViewportCalls.Should().BeInRange(80, 81);
            result.TotalMilliseconds.Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public void ViewportScrollableMetricCounts_AvoidCapturedLinqPredicates()
    {
        var source = System.IO.File.ReadAllText(WorkspaceFileLocator.Find(
            "src",
            "FreeX.App.Host",
            "MainWindow.Viewport.cs"));

        source.Should().Contain("foreach (var row in viewport.RowMetrics)");
        source.Should().Contain("foreach (var column in viewport.ColMetrics)");
        source.Should().NotContain("viewport.RowMetrics.Count(row =>");
        source.Should().NotContain("viewport.ColMetrics.Count(column =>");
    }

    [BenchmarkFact]
    public void Benchmark_ViewportNoCommentsFastPath_ReportsTiming()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        for (uint row = 1; row <= 120; row++)
        {
            for (uint col = 1; col <= 40; col++)
                sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(row * col));
        }

        var service = new ViewportService();
        var request = new ViewportRequest(1, 1, 2_600, 3_000, IncludeObjects: false);
        for (var i = 0; i < 5; i++)
            service.GetViewport(workbook, sheet.Id, request);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var timings = new List<double>(80);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        ViewportModel? viewport = null;
        for (var i = 0; i < 80; i++)
        {
            var step = Stopwatch.StartNew();
            viewport = service.GetViewport(workbook, sheet.Id, request);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
        }

        total.Stop();
        var result = MeasurementResult.From(
            timings,
            total.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);

        Console.WriteLine(
            "PERF VIEWPORT_NO_COMMENTS_FAST_PATH " +
            $"steps={result.StepCount} cells={viewport!.Cells.Count:N0} " +
            $"total_ms={result.TotalMilliseconds:F2} mean_ms={result.MeanMilliseconds:F2} " +
            $"p95_ms={result.P95Milliseconds:F2} max_ms={result.MaxMilliseconds:F2} " +
            $"allocated_bytes={result.AllocatedBytes:N0}");

        result.StepCount.Should().Be(80);
        viewport.Cells.Should().HaveCount(4_800);
        viewport.Cells.Should().OnlyContain(cell => !cell.HasComment);
        result.TotalMilliseconds.Should().BeGreaterThan(0);
    }

    [BenchmarkFact]
    public void Benchmark_SparseViewportEmptyCellFastPath_ReportsTiming()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 60, 20), new NumberValue(1_200));
        sheet.SetCell(new CellAddress(sheet.Id, 120, 40), new NumberValue(4_800));

        var service = new ViewportService();
        var request = new ViewportRequest(1, 1, 2_600, 3_000, IncludeObjects: false);
        for (var i = 0; i < 5; i++)
            service.GetViewport(workbook, sheet.Id, request);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var timings = new List<double>(80);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        ViewportModel? viewport = null;
        for (var i = 0; i < 80; i++)
        {
            var step = Stopwatch.StartNew();
            viewport = service.GetViewport(workbook, sheet.Id, request);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
        }

        total.Stop();
        var result = MeasurementResult.From(
            timings,
            total.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);

        Console.WriteLine(
            "PERF SPARSE_VIEWPORT_EMPTY_CELL_FAST_PATH " +
            $"steps={result.StepCount} cells={viewport!.Cells.Count:N0} " +
            $"total_ms={result.TotalMilliseconds:F2} mean_ms={result.MeanMilliseconds:F2} " +
            $"p95_ms={result.P95Milliseconds:F2} max_ms={result.MaxMilliseconds:F2} " +
            $"allocated_bytes={result.AllocatedBytes:N0}");

        result.StepCount.Should().Be(80);
        viewport.Cells.Should().HaveCount(3);
        result.TotalMilliseconds.Should().BeGreaterThan(0);
    }

    private sealed class ViewportSidePaneRefreshHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly CountingViewportService _viewportService;
        private readonly MethodInfo _updateViewport;

        private ViewportSidePaneRefreshHarness(MainWindow window, CountingViewportService viewportService)
        {
            _window = window;
            _viewportService = viewportService;
            _updateViewport = typeof(MainWindow)
                .GetMethod("UpdateViewport", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "UpdateViewport");
        }

        public MeasurementResult MeasureUpdateViewport(int iterations)
        {
            var timings = new List<double>(iterations);
            _viewportService.Reset();
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var total = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
            {
                var step = Stopwatch.StartNew();
                _updateViewport.Invoke(_window, []);
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

        public static ViewportSidePaneRefreshHarness Create()
        {
            var workbook = new Workbook("Book1");
            var sheet = workbook.AddSheet("Sheet1");
            var headers = new[] { "Region", "Product", "Sales" };
            for (uint col = 1; col <= headers.Length; col++)
                sheet.SetCell(new CellAddress(sheet.Id, 1, col), new TextValue(headers[col - 1]));

            for (uint row = 2; row <= 180; row++)
            {
                sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"Region {row % 12}"));
                sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue($"Product {row % 16}"));
                sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(row * 13));
            }

            var pivotTable = new PivotTableModel
            {
                Name = "SalesPivot",
                CacheId = 1,
                SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 180, 3)),
                TargetRange = new GridRange(new CellAddress(sheet.Id, 2, 6), new CellAddress(sheet.Id, 16, 9))
            };
            pivotTable.RowFields.Add(new PivotFieldModel(0));
            pivotTable.DataFields.Add(new PivotDataFieldModel(2, "Sum of Sales", "sum"));
            sheet.PivotTables.Add(pivotTable);

            sheet.StructuredTables.Add(new StructuredTableModel
            {
                Id = 1,
                Name = "SalesTable",
                DisplayName = "SalesTable",
                Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 180, 3)),
                HeaderRowCount = 1,
                HasAutoFilter = true,
                ShowRowStripes = true,
                StyleName = "TableStyleMedium2"
            });

            for (var i = 0; i < 6; i++)
            {
                workbook.Slicers.Add(new SlicerModel
                {
                    Name = $"RegionSlicer{i}",
                    CacheName = $"RegionSlicerCache{i}",
                    SourcePivotTableName = pivotTable.Name,
                    SourceFieldName = "Region"
                });
            }

            workbook.Timelines.Add(new TimelineModel
            {
                Name = "SalesTimeline",
                CacheName = "SalesTimelineCache",
                SourcePivotTableName = pivotTable.Name,
                SourceFieldName = "Sales"
            });

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
            if (window.FindName("SheetGrid") is FreeX.App.UI.GridView grid)
            {
                grid.SelectedRange = new GridRange(
                    new CellAddress(sheet.Id, 3, 6),
                    new CellAddress(sheet.Id, 3, 6));
            }

            PumpDispatcher();
            viewportService.Reset();
            return new ViewportSidePaneRefreshHarness(window, viewportService);
        }

        public void Dispose()
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
        }
    }
}
