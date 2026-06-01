using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

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
            result.ViewportCalls.Should().BeLessThanOrEqualTo(1);
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
            var fallbackDiagnostics = harness.FallbackDiagnostics;
            var adaptiveDiagnostics = harness.AdaptiveDiagnostics;

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
            Console.WriteLine(
                "PERF RIBBON_DIAGNOSTICS " +
                $"fallback_requests={fallbackDiagnostics.RequestCount:N0} " +
                $"fallback_posts={fallbackDiagnostics.PostedCount:N0} " +
                $"fallback_executed={fallbackDiagnostics.ExecutedCount:N0} " +
                $"first_frame_layouts={fallbackDiagnostics.FirstFrameLayoutUpdateCount:N0} " +
                $"group_measurements={adaptiveDiagnostics.GroupMeasurementCount:N0} " +
                $"snapshot_captures={adaptiveDiagnostics.CompactSnapshotCaptureCount:N0} " +
                $"threshold_rebuilds={adaptiveDiagnostics.ResizeThresholdRebuildCount:N0} " +
                $"layout_plan_computes={adaptiveDiagnostics.LayoutPlanComputeCount:N0} " +
                $"layout_plan_hits={adaptiveDiagnostics.LayoutPlanCacheHitCount:N0} " +
                $"measured_overflow_checks={adaptiveDiagnostics.MeasuredOverflowMeasurementCount:N0} " +
                $"corrected_state_hits={adaptiveDiagnostics.CorrectedStateCacheHitCount:N0} " +
                $"applied_state_skips={adaptiveDiagnostics.AppliedStateSkipCount:N0} " +
                $"state_applies={adaptiveDiagnostics.StateApplyCount:N0} " +
                $"state_changed_groups={adaptiveDiagnostics.StateChangedGroupCount:N0} " +
                $"collapsed_footprint_applies={adaptiveDiagnostics.CollapsedFootprintApplyCount:N0}");

            resize.StepCount.Should().Be(widths.Length * 3);
            forcedCompact.StepCount.Should().Be(widths.Length * 3);
            resize.TotalMilliseconds.Should().BeGreaterThan(0);
            forcedCompact.TotalMilliseconds.Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public void Benchmark_RibbonCollapsedButtonFootprint_ReportsTimingAndAllocatedBytes()
    {
        StaTestRunner.Run(() =>
        {
            var buttons = CreateCollapsedFootprintButtons(count: 12);
            var widths = new[] { 1500d, 920d, 900d, 820d, 700d, 640d, 760d, 1000d };

            foreach (var width in widths)
                RibbonAdaptiveStateApplicator.SetCollapsedButtonFootprint(buttons, width);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var timings = new List<double>(widths.Length * 3000);
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var total = Stopwatch.StartNew();
            for (var iteration = 0; iteration < 3000; iteration++)
            {
                foreach (var width in widths)
                {
                    var step = Stopwatch.StartNew();
                    RibbonAdaptiveStateApplicator.SetCollapsedButtonFootprint(buttons, width);
                    step.Stop();
                    timings.Add(step.Elapsed.TotalMilliseconds);
                }
            }

            total.Stop();
            var result = MeasurementResult.From(
                timings,
                total.Elapsed.TotalMilliseconds,
                GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
            Console.WriteLine(
                "PERF RIBBON_COLLAPSED_BUTTON_FOOTPRINT " +
                $"steps={result.StepCount} buttons={buttons.Count:N0} " +
                $"total_ms={result.TotalMilliseconds:F2} mean_ms={result.MeanMilliseconds:F4} " +
                $"p95_ms={result.P95Milliseconds:F4} max_ms={result.MaxMilliseconds:F4} " +
                $"allocated_bytes={result.AllocatedBytes:N0}");

            result.StepCount.Should().Be(widths.Length * 3000);
            result.TotalMilliseconds.Should().BeGreaterThan(0);
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

    [Fact]
    public void Benchmark_AdditionalSelectionDragToolbarRefresh_ReportsTiming()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = SelectionDragHarness.Create();
            harness.MeasureAdditionalSelectionDrag(iterations: 10);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var result = harness.MeasureAdditionalSelectionDrag(iterations: 80);
            Console.WriteLine(
                "PERF ADDITIONAL_SELECTION_DRAG_TOOLBAR " +
                $"steps={result.StepCount} total_ms={result.TotalMilliseconds:F2} " +
                $"mean_ms={result.MeanMilliseconds:F2} p95_ms={result.P95Milliseconds:F2} " +
                $"max_ms={result.MaxMilliseconds:F2} allocated_bytes={result.AllocatedBytes:N0}");

            result.StepCount.Should().Be(80);
            result.TotalMilliseconds.Should().BeGreaterThan(0);
        });
    }

    [Fact]
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
            result.ViewportCalls.Should().Be(80);
            result.TotalMilliseconds.Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public void Benchmark_RepeatedSelectionDragTargetNoOps_ReportsTiming()
    {
        StaTestRunner.Run(() =>
        {
            using var dragHarness = SelectionDragHarness.Create();
            dragHarness.MeasureRepeatedDragSelectionTarget(iterations: 10);

            using var additionalHarness = SelectionDragHarness.Create();
            additionalHarness.MeasureRepeatedAdditionalSelectionTarget(iterations: 10);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var dragResult = dragHarness.MeasureRepeatedDragSelectionTarget(iterations: 2_000);
            var additionalResult = additionalHarness.MeasureRepeatedAdditionalSelectionTarget(iterations: 2_000);
            Console.WriteLine(
                "PERF SELECTION_DRAG_REPEATED_TARGET " +
                $"steps={dragResult.StepCount} total_ms={dragResult.TotalMilliseconds:F2} " +
                $"mean_ms={dragResult.MeanMilliseconds:F2} p95_ms={dragResult.P95Milliseconds:F2} " +
                $"max_ms={dragResult.MaxMilliseconds:F2} allocated_bytes={dragResult.AllocatedBytes:N0}");
            Console.WriteLine(
                "PERF ADDITIONAL_SELECTION_DRAG_REPEATED_TARGET " +
                $"steps={additionalResult.StepCount} total_ms={additionalResult.TotalMilliseconds:F2} " +
                $"mean_ms={additionalResult.MeanMilliseconds:F2} p95_ms={additionalResult.P95Milliseconds:F2} " +
                $"max_ms={additionalResult.MaxMilliseconds:F2} allocated_bytes={additionalResult.AllocatedBytes:N0}");

            dragResult.StepCount.Should().Be(2_000);
            additionalResult.StepCount.Should().Be(2_000);
            dragResult.TotalMilliseconds.Should().BeGreaterThan(0);
            additionalResult.TotalMilliseconds.Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public void RepeatedSelectionDragTargetNoOps_DoNotQueueDeferredRefresh()
    {
        StaTestRunner.Run(() =>
        {
            using var dragHarness = SelectionDragHarness.Create();
            dragHarness.RepeatedDragSelectionTargetLeavesStatusPendingClear().Should().BeTrue();

            using var additionalHarness = SelectionDragHarness.Create();
            additionalHarness.RepeatedAdditionalSelectionTargetLeavesPendingRefreshClear().Should().BeTrue();
        });
    }

    [Fact]
    public void AdditionalSelectionDragWithUnchangedStyleSource_DoesNotQueueToolbarOrProbeQat()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = SelectionDragHarness.Create();

            var result = harness.MeasureUnchangedStyleAdditionalSelectionToolbarChurn();

            result.ToolbarRefreshQueued.Should().BeFalse();
            result.CanUndoProbeCount.Should().Be(0);
            result.CanRedoProbeCount.Should().Be(0);
            result.ToolbarWriteCount.Should().Be(0);
        });
    }

    [Fact]
    public void Benchmark_NonDragSelectionToolbarRefresh_ReportsTimingAndQatProbes()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = SelectionDragHarness.Create();
            harness.MeasureNonDragSelectionToolbarRefresh(iterations: 10);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var result = harness.MeasureNonDragSelectionToolbarRefresh(iterations: 160);
            Console.WriteLine(
                "PERF NON_DRAG_SELECTION_TOOLBAR " +
                $"steps={result.StepCount} total_ms={result.TotalMilliseconds:F2} " +
                $"mean_ms={result.MeanMilliseconds:F2} p95_ms={result.P95Milliseconds:F2} " +
                $"max_ms={result.MaxMilliseconds:F2} allocated_bytes={result.AllocatedBytes:N0} " +
                $"can_undo_probes={result.CanUndoProbeCount:N0} " +
                $"can_redo_probes={result.CanRedoProbeCount:N0} " +
                $"toolbar_writes={result.ToolbarWriteCount:N0}");

            result.StepCount.Should().Be(160);
            result.TotalMilliseconds.Should().BeGreaterThan(0);
            result.CanUndoProbeCount.Should().Be(0);
            result.CanRedoProbeCount.Should().Be(0);
        });
    }

    [Fact]
    public void NonDragSelectionWithUnchangedStyleSource_DoesNotProbeQat()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = SelectionDragHarness.Create();

            var result = harness.MeasureNonDragSelectionToolbarRefresh(iterations: 20);

            result.CanUndoProbeCount.Should().Be(0);
            result.CanRedoProbeCount.Should().Be(0);
            result.ToolbarWriteCount.Should().Be(0);
        });
    }

    [Fact]
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

    [Fact]
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

        public RibbonFallbackDiagnosticsSnapshot FallbackDiagnostics => _window.GetRibbonFallbackDiagnosticsForTests();

        public RibbonAdaptiveDiagnosticsSnapshot AdaptiveDiagnostics => _window.GetRibbonAdaptiveDiagnosticsForTests();

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
                workbook,
                NullUserMessageService.Instance);

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

    private static IReadOnlyList<Button> CreateCollapsedFootprintButtons(int count)
    {
        var buttons = new List<Button>(count);
        for (var index = 0; index < count; index++)
        {
            var content = new StackPanel();
            var caption = new TextBlock { Text = $"Group {index}" };
            RibbonMetadata.SetRole(caption, RibbonMetadataRole.CommandLabel);
            content.Children.Add(caption);

            if (index % 2 == 0)
            {
                var icon = new TextBlock { Text = "\uE8A5" };
                RibbonMetadata.SetRole(icon, RibbonMetadataRole.CommandIcon);
                content.Children.Add(icon);
            }
            else
            {
                var icon = new TextBlock { Text = "\uE8A5" };
                RibbonMetadata.SetRole(icon, RibbonMetadataRole.CommandIcon);
                content.Children.Add(new Border { Child = icon });
            }

            buttons.Add(new Button { Content = content });
        }

        return buttons;
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

    private sealed class SelectionDragHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly CountingCommandBus _commandBus;
        private readonly Action<CellAddress> _setActiveCell;
        private readonly Action<CellAddress, CellAddress> _extendSelection;
        private readonly Action<CellAddress, bool> _addOrMoveAdditionalSelection;
        private readonly Action _completeDragSelectionStatusRefresh;
        private readonly Action? _completeDragSelectionToolbarRefresh;
        private readonly FieldInfo _dragSelectActive;
        private readonly FieldInfo _dragSelectStatusRefreshPending;
        private readonly FieldInfo _dragSelectToolbarRefreshPending;
        private readonly CellAddress _anchor;

        private SelectionDragHarness(MainWindow window, SheetId sheetId, CountingCommandBus commandBus)
        {
            _window = window;
            _commandBus = commandBus;
            _anchor = new CellAddress(sheetId, 1, 1);
            var setActiveCell = typeof(MainWindow)
                .GetMethod("SetActiveCell", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "SetActiveCell");
            _setActiveCell = setActiveCell.CreateDelegate<Action<CellAddress>>(window);

            var extendSelection = typeof(MainWindow)
                .GetMethod("ExtendSelection", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "ExtendSelection");
            _extendSelection = extendSelection.CreateDelegate<Action<CellAddress, CellAddress>>(window);

            var addOrMoveAdditionalSelection = typeof(MainWindow)
                .GetMethod("AddOrMoveAdditionalSelection", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "AddOrMoveAdditionalSelection");
            _addOrMoveAdditionalSelection = addOrMoveAdditionalSelection.CreateDelegate<Action<CellAddress, bool>>(window);

            var completeDragSelectionStatusRefresh = typeof(MainWindow)
                .GetMethod("CompleteDragSelectionStatusRefresh", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "CompleteDragSelectionStatusRefresh");
            _completeDragSelectionStatusRefresh = completeDragSelectionStatusRefresh.CreateDelegate<Action>(window);

            var completeDragSelectionToolbarRefresh = typeof(MainWindow)
                .GetMethod("CompleteDragSelectionToolbarRefresh", BindingFlags.Instance | BindingFlags.NonPublic);
            _completeDragSelectionToolbarRefresh =
                completeDragSelectionToolbarRefresh?.CreateDelegate<Action>(window);
            _dragSelectActive = typeof(MainWindow)
                .GetField("_dragSelectActive", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_dragSelectActive");
            _dragSelectStatusRefreshPending = typeof(MainWindow)
                .GetField("_dragSelectStatusRefreshPending", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_dragSelectStatusRefreshPending");
            _dragSelectToolbarRefreshPending = typeof(MainWindow)
                .GetField("_dragSelectToolbarRefreshPending", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_dragSelectToolbarRefreshPending");
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
                    _extendSelection(_anchor, new CellAddress(_anchor.Sheet, row, 40));
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

        public MeasurementResult MeasureAdditionalSelectionDrag(int iterations)
        {
            var timings = new List<double>(iterations);
            _addOrMoveAdditionalSelection(_anchor, false);
            PumpDispatcher();
            _dragSelectActive.SetValue(_window, true);
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var total = Stopwatch.StartNew();
            try
            {
                for (var i = 0; i < iterations; i++)
                {
                    var row = (uint)(20 + i * 6);
                    var step = Stopwatch.StartNew();
                    _addOrMoveAdditionalSelection(new CellAddress(_anchor.Sheet, row, 40), true);
                    PumpDispatcher();
                    step.Stop();
                    timings.Add(step.Elapsed.TotalMilliseconds);
                }
            }
            finally
            {
                _dragSelectActive.SetValue(_window, false);
                _completeDragSelectionToolbarRefresh?.Invoke();
                _completeDragSelectionStatusRefresh();
            }

            total.Stop();
            return MeasurementResult.From(timings, total.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
        }

        public MeasurementResult MeasureRepeatedDragSelectionTarget(int iterations)
        {
            var target = new CellAddress(_anchor.Sheet, 120, 40);
            _extendSelection(_anchor, target);
            PumpDispatcher();

            var timings = new List<double>(iterations);
            _dragSelectActive.SetValue(_window, true);
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var total = Stopwatch.StartNew();
            try
            {
                for (var i = 0; i < iterations; i++)
                {
                    var stepStarted = Stopwatch.GetTimestamp();
                    _extendSelection(_anchor, target);
                    timings.Add(Stopwatch.GetElapsedTime(stepStarted).TotalMilliseconds);
                }
            }
            finally
            {
                _dragSelectActive.SetValue(_window, false);
                _completeDragSelectionStatusRefresh();
            }

            total.Stop();
            return MeasurementResult.From(timings, total.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
        }

        public MeasurementResult MeasureRepeatedAdditionalSelectionTarget(int iterations)
        {
            var target = new CellAddress(_anchor.Sheet, 120, 40);
            _addOrMoveAdditionalSelection(_anchor, false);
            PumpDispatcher();
            _dragSelectActive.SetValue(_window, true);
            _addOrMoveAdditionalSelection(target, true);
            PumpDispatcher();

            var timings = new List<double>(iterations);
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var total = Stopwatch.StartNew();
            try
            {
                for (var i = 0; i < iterations; i++)
                {
                    var stepStarted = Stopwatch.GetTimestamp();
                    _addOrMoveAdditionalSelection(target, true);
                    timings.Add(Stopwatch.GetElapsedTime(stepStarted).TotalMilliseconds);
                }
            }
            finally
            {
                _dragSelectActive.SetValue(_window, false);
                _completeDragSelectionToolbarRefresh?.Invoke();
                _completeDragSelectionStatusRefresh();
            }

            total.Stop();
            return MeasurementResult.From(timings, total.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
        }

        public bool RepeatedDragSelectionTargetLeavesStatusPendingClear()
        {
            var target = new CellAddress(_anchor.Sheet, 120, 40);
            _extendSelection(_anchor, target);
            PumpDispatcher();
            _dragSelectActive.SetValue(_window, true);
            _dragSelectStatusRefreshPending.SetValue(_window, false);

            try
            {
                _extendSelection(_anchor, target);
                return IsStatusRefreshPending() is false;
            }
            finally
            {
                _dragSelectActive.SetValue(_window, false);
                _completeDragSelectionStatusRefresh();
            }
        }

        public bool RepeatedAdditionalSelectionTargetLeavesPendingRefreshClear()
        {
            var target = new CellAddress(_anchor.Sheet, 120, 40);
            _addOrMoveAdditionalSelection(_anchor, false);
            PumpDispatcher();
            _dragSelectActive.SetValue(_window, true);
            _addOrMoveAdditionalSelection(target, true);
            PumpDispatcher();
            _dragSelectStatusRefreshPending.SetValue(_window, false);
            _dragSelectToolbarRefreshPending.SetValue(_window, false);

            try
            {
                _addOrMoveAdditionalSelection(target, true);
                return IsStatusRefreshPending() is false && IsToolbarRefreshPending() is false;
            }
            finally
            {
                _dragSelectActive.SetValue(_window, false);
                _completeDragSelectionToolbarRefresh?.Invoke();
                _completeDragSelectionStatusRefresh();
            }
        }

        public ToolbarDragRefreshProbeResult MeasureUnchangedStyleAdditionalSelectionToolbarChurn()
        {
            _addOrMoveAdditionalSelection(_anchor, false);
            PumpDispatcher();
            var toolbarWrites = AttachToolbarWriteCounter();
            _commandBus.ResetQuickAccessProbeCounts();

            _dragSelectActive.SetValue(_window, true);
            try
            {
                _addOrMoveAdditionalSelection(new CellAddress(_anchor.Sheet, 20, 40), true);
                PumpDispatcher();
                var queuedDuringDrag = IsToolbarRefreshPending();
                _dragSelectActive.SetValue(_window, false);
                _completeDragSelectionToolbarRefresh?.Invoke();
                PumpDispatcher();

                return new ToolbarDragRefreshProbeResult(
                    queuedDuringDrag,
                    _commandBus.CanUndoProbeCount,
                    _commandBus.CanRedoProbeCount,
                    toolbarWrites.Count);
            }
            finally
            {
                _dragSelectActive.SetValue(_window, false);
                _completeDragSelectionToolbarRefresh?.Invoke();
                _completeDragSelectionStatusRefresh();
            }
        }

        public ToolbarSelectionRefreshMeasurement MeasureNonDragSelectionToolbarRefresh(int iterations)
        {
            var timings = new List<double>(iterations);
            var toolbarWrites = AttachToolbarWriteCounter();
            _commandBus.ResetQuickAccessProbeCounts();

            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var total = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
            {
                var row = (uint)(2 + i % 500);
                var step = Stopwatch.StartNew();
                _setActiveCell(new CellAddress(_anchor.Sheet, row, 2));
                PumpDispatcher();
                step.Stop();
                timings.Add(step.Elapsed.TotalMilliseconds);
            }

            total.Stop();
            var measurement = MeasurementResult.From(
                timings,
                total.Elapsed.TotalMilliseconds,
                GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
            return new ToolbarSelectionRefreshMeasurement(
                measurement,
                _commandBus.CanUndoProbeCount,
                _commandBus.CanRedoProbeCount,
                toolbarWrites.Count);
        }

        private bool IsStatusRefreshPending() =>
            _dragSelectStatusRefreshPending.GetValue(_window) is true;

        private bool IsToolbarRefreshPending() =>
            _dragSelectToolbarRefreshPending.GetValue(_window) is true;

        private ToolbarWriteCounter AttachToolbarWriteCounter()
        {
            var counter = new ToolbarWriteCounter();
            foreach (var name in new[]
            {
                "BoldButton",
                "ItalicButton",
                "UnderlineButton",
                "StrikeButton",
                "AlignTopBtn",
                "AlignMiddleBtn",
                "AlignBottomBtn",
                "AlignLeftBtn",
                "AlignCenterBtn",
                "AlignRightBtn",
                "WrapTextBtn"
            })
            {
                if (_window.FindName(name) is ToggleButton toggle)
                    counter.Attach(toggle);
            }

            foreach (var name in new[] { "FontNameBox", "FontSizeBox" })
            {
                if (_window.FindName(name) is ComboBox comboBox)
                    counter.Attach(comboBox);
            }

            return counter;
        }

        public static SelectionDragHarness Create()
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");

            var workbookRef = new WorkbookRef { Current = workbook };
            var graph = new DependencyGraph();
            var evaluator = new FormulaEvaluator();
            var commandBus = new CountingCommandBus(new CommandBus(_ => new TestCommandContext(workbookRef.Current)));
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                commandBus,
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
            var sheet = workbookRef.Current.Sheets[0];
            for (uint row = 1; row <= 600; row++)
            {
                for (uint col = 1; col <= 40; col++)
                    sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(row * col));
            }

            window.UpdateLayout();
            PumpDispatcher();
            return new SelectionDragHarness(window, sheet.Id, commandBus);
        }

        public void Dispose()
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
        }
    }

    private sealed class ToolbarWriteCounter
    {
        public int Count { get; private set; }

        public void Attach(ToggleButton toggleButton)
        {
            toggleButton.Checked += (_, _) => Count++;
            toggleButton.Unchecked += (_, _) => Count++;
            toggleButton.Indeterminate += (_, _) => Count++;
        }

        public void Attach(ComboBox comboBox)
        {
            comboBox.SelectionChanged += (_, _) => Count++;
        }
    }

    private sealed class CountingCommandBus(ICommandBus inner) : ICommandBus
    {
        public int CanUndoProbeCount { get; private set; }

        public int CanRedoProbeCount { get; private set; }

        public CommandOutcome Execute(WorkbookId workbookId, IWorkbookCommand command) =>
            inner.Execute(workbookId, command);

        public CommandOutcome ExecuteRepeatable(WorkbookId workbookId, Func<IWorkbookCommand> commandFactory) =>
            inner.ExecuteRepeatable(workbookId, commandFactory);

        public CommandOutcome Undo(WorkbookId workbookId) => inner.Undo(workbookId);

        public CommandOutcome Redo(WorkbookId workbookId) => inner.Redo(workbookId);

        public bool CanUndo(WorkbookId workbookId)
        {
            CanUndoProbeCount++;
            return inner.CanUndo(workbookId);
        }

        public bool CanRedo(WorkbookId workbookId)
        {
            CanRedoProbeCount++;
            return inner.CanRedo(workbookId);
        }

        public CommandOutcome RepeatLast(WorkbookId workbookId) => inner.RepeatLast(workbookId);

        public bool CanRepeat(WorkbookId workbookId) => inner.CanRepeat(workbookId);

        public void ResetQuickAccessProbeCounts()
        {
            CanUndoProbeCount = 0;
            CanRedoProbeCount = 0;
        }
    }

    private sealed record ToolbarDragRefreshProbeResult(
        bool ToolbarRefreshQueued,
        int CanUndoProbeCount,
        int CanRedoProbeCount,
        int ToolbarWriteCount);

    private sealed record ToolbarSelectionRefreshMeasurement(
        MeasurementResult Measurement,
        int CanUndoProbeCount,
        int CanRedoProbeCount,
        int ToolbarWriteCount)
    {
        public int StepCount => Measurement.StepCount;

        public double TotalMilliseconds => Measurement.TotalMilliseconds;

        public double MeanMilliseconds => Measurement.MeanMilliseconds;

        public double P95Milliseconds => Measurement.P95Milliseconds;

        public double MaxMilliseconds => Measurement.MaxMilliseconds;

        public long AllocatedBytes => Measurement.AllocatedBytes;
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
