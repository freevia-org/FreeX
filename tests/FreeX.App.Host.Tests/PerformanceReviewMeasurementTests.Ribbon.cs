using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
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
    public void Benchmark_RibbonForcedCompactSkipPath_ReportsTimingAndAllocatedBytes()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = RibbonResizeHarness.Create();
            const double width = 1280d;
            const int iterations = 300;

            harness.SelectRibbonTab("Home", width);
            harness.MeasureRepeatedForcedCompact(width, iterations: 10);
            harness.ResetAdaptiveDiagnostics();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var result = harness.MeasureRepeatedForcedCompact(width, iterations);
            var diagnostics = harness.AdaptiveDiagnostics;
            Console.WriteLine(
                "PERF RIBBON_FORCE_COMPACT_SKIP " +
                $"steps={result.StepCount} total_ms={result.TotalMilliseconds:F2} " +
                $"mean_ms={result.MeanMilliseconds:F4} p95_ms={result.P95Milliseconds:F4} " +
                $"max_ms={result.MaxMilliseconds:F4} allocated_bytes={result.AllocatedBytes:N0} " +
                $"applied_state_skips={diagnostics.AppliedStateSkipCount:N0} " +
                $"state_applies={diagnostics.StateApplyCount:N0}");

            result.StepCount.Should().Be(iterations);
            diagnostics.AppliedStateSkipCount.Should().Be(iterations);
            diagnostics.StateApplyCount.Should().Be(0);
        });
    }

    [Fact]
    public void RibbonMeasuredCorrections_ApplySingleGroupWithoutStateSnapshots()
    {
        var source = System.IO.File.ReadAllText(WorkspaceFileLocator.Find(
            "src",
            "FreeX.App.Host",
            "MainWindow.RibbonAdaptive.cs"));

        source.Should().Contain("ApplyRibbonAdaptiveStateAt(");
        source.Should().Contain("out var changedIndex");
        source.Should().NotContain("var appliedStates = plannedStates.ToArray();");
        source.Should().NotContain("var previousStates = plannedStates.ToArray();");
        source.Should().NotContain("previousStates = plannedStates.ToArray();");
        source.Should().NotContain("var expandedStates = plannedStates.ToArray();");
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

        public MeasurementResult MeasureRepeatedForcedCompact(double width, int iterations)
        {
            _window.Width = width;
            _window.UpdateLayout();
            PumpDispatcher();

            var timings = new List<double>(iterations);
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var total = Stopwatch.StartNew();
            for (var iteration = 0; iteration < iterations; iteration++)
            {
                var step = Stopwatch.StartNew();
                _updateRibbonCompactMode.Invoke(_window, [true]);
                PumpDispatcher();
                step.Stop();
                timings.Add(step.Elapsed.TotalMilliseconds);
            }

            total.Stop();
            return MeasurementResult.From(timings, total.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
        }

        public RibbonFallbackDiagnosticsSnapshot FallbackDiagnostics => _window.GetRibbonFallbackDiagnosticsForTests();

        public RibbonAdaptiveDiagnosticsSnapshot AdaptiveDiagnostics => _window.GetRibbonAdaptiveDiagnosticsForTests();

        public void ResetAdaptiveDiagnostics() => _window.ResetRibbonAdaptiveDiagnosticsForTests();

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
}
