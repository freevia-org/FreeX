using System.Diagnostics;
using System.Reflection;
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
    [BenchmarkFact]
    public void Benchmark_FullQuickAccessToolbarCommandStateApply_ReportsTimingAndAllocatedBytes()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = FullQuickAccessToolbarStateHarness.Create();
            const int iterations = 100_000;

            harness.MeasureLegacyMetadataApply(iterations: 100);
            harness.MeasureCachedApply(iterations: 100);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var legacy = harness.MeasureLegacyMetadataApply(iterations);
            var cached = harness.MeasureCachedApply(iterations);
            Console.WriteLine(
                "PERF FULL_QAT_COMMAND_STATE_APPLY_LEGACY " +
                $"steps={legacy.StepCount} qat_buttons={harness.ButtonCount:N0} " +
                $"total_ms={legacy.TotalMilliseconds:F2} mean_ms={legacy.MeanMilliseconds:F6} " +
                $"p95_ms={legacy.P95Milliseconds:F6} max_ms={legacy.MaxMilliseconds:F6} " +
                $"allocated_bytes={legacy.AllocatedBytes:N0}");
            Console.WriteLine(
                "PERF FULL_QAT_COMMAND_STATE_APPLY_CACHED " +
                $"steps={cached.StepCount} qat_buttons={harness.ButtonCount:N0} " +
                $"total_ms={cached.TotalMilliseconds:F2} mean_ms={cached.MeanMilliseconds:F6} " +
                $"p95_ms={cached.P95Milliseconds:F6} max_ms={cached.MaxMilliseconds:F6} " +
                $"allocated_bytes={cached.AllocatedBytes:N0}");

            legacy.StepCount.Should().Be(iterations);
            cached.StepCount.Should().Be(iterations);
            harness.ButtonCount.Should().Be(QuickAccessToolbarCatalog.Commands.Count);
            legacy.TotalMilliseconds.Should().BeGreaterThan(0);
            cached.TotalMilliseconds.Should().BeGreaterThan(0);
        });
    }

    private sealed class FullQuickAccessToolbarStateHarness : IDisposable
    {
        private static readonly QuickAccessCommandState EnabledState = new(
            CanUndo: true,
            CanRedo: true,
            HasActiveWorksheet: true,
            HasSelection: true);

        private readonly MainWindow _window;
        private readonly ApplyQuickAccessToolbarCommandStateDelegate _applyState;
        private readonly List<Button> _quickAccessToolbarButtons;

        private FullQuickAccessToolbarStateHarness(MainWindow window)
        {
            _window = window;
            var applyState = typeof(MainWindow)
                .GetMethod("ApplyQuickAccessToolbarCommandState", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "ApplyQuickAccessToolbarCommandState");
            _applyState = applyState.CreateDelegate<ApplyQuickAccessToolbarCommandStateDelegate>(window);
            _quickAccessToolbarButtons = QuickAccessToolbarCatalog.Commands
                .Select(command => window.FindName(command.AutomationId))
                .OfType<Button>()
                .ToList();
        }

        private delegate void ApplyQuickAccessToolbarCommandStateDelegate(
            QuickAccessCommandState state,
            bool force);

        public int ButtonCount => _quickAccessToolbarButtons.Count;

        public MeasurementResult MeasureLegacyMetadataApply(int iterations)
        {
            _applyState(EnabledState, force: true);
            return MeasureApplyLoop(
                iterations,
                () =>
                {
                    foreach (var button in _quickAccessToolbarButtons)
                    {
                        if (!RibbonMetadata.TryGetCatalogId(button, out var commandId))
                            continue;

                        var isEnabled = QuickAccessCommandStateResolver.CanExecute(commandId, EnabledState);
                        if (button.IsEnabled != isEnabled)
                            button.IsEnabled = isEnabled;
                    }
                });
        }

        public MeasurementResult MeasureCachedApply(int iterations)
        {
            _applyState(EnabledState, force: true);
            return MeasureApplyLoop(iterations, () => _applyState(EnabledState, force: true));
        }

        private static MeasurementResult MeasureApplyLoop(int iterations, Action apply)
        {
            var timings = new List<double>(iterations);
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var total = Stopwatch.StartNew();
            for (var iteration = 0; iteration < iterations; iteration++)
            {
                var stepStarted = Stopwatch.GetTimestamp();
                apply();
                timings.Add(Stopwatch.GetElapsedTime(stepStarted).TotalMilliseconds);
            }

            total.Stop();
            return MeasurementResult.From(
                timings,
                total.Elapsed.TotalMilliseconds,
                GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
        }

        public static FullQuickAccessToolbarStateHarness Create()
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");

            var workbookRef = new WorkbookRef { Current = workbook };
            var graph = new DependencyGraph();
            var evaluator = new FormulaEvaluator();
            var options = new AppOptions
            {
                QuickAccessToolbarCommands = QuickAccessToolbarCatalog.Commands
                    .Select(command => command.Id)
                    .ToList()
            };
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(graph, evaluator),
                Array.Empty<IFileAdapter>(),
                workbookRef,
                workbook,
                NullUserMessageService.Instance,
                options: options)
            {
                Width = 1280,
                Height = 720
            };

            window.Show();
            PumpDispatcher();
            return new FullQuickAccessToolbarStateHarness(window);
        }

        public void Dispose()
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
        }
    }
}
