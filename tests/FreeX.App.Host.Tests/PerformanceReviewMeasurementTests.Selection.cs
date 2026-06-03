using System.Diagnostics;
using System.Reflection;
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

public sealed partial class PerformanceReviewMeasurementTests
{
    [BenchmarkFact]
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

    [BenchmarkFact]
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

    [BenchmarkFact]
    public void Benchmark_RepeatedHeaderSelectionTarget_ReportsTiming()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = SelectionDragHarness.Create();
            harness.MeasureRepeatedHeaderSelectionTarget(iterations: 10);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var result = harness.MeasureRepeatedHeaderSelectionTarget(iterations: 2_000);
            Console.WriteLine(
                "PERF HEADER_SELECTION_REPEATED_TARGET " +
                $"steps={result.StepCount} total_ms={result.TotalMilliseconds:F2} " +
                $"mean_ms={result.MeanMilliseconds:F4} p95_ms={result.P95Milliseconds:F4} " +
                $"max_ms={result.MaxMilliseconds:F4} allocated_bytes={result.AllocatedBytes:N0}");

            result.StepCount.Should().Be(2_000);
            result.TotalMilliseconds.Should().BeGreaterThan(0);
        });
    }

    [BenchmarkFact]
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

    [BenchmarkFact]
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

    private sealed class SelectionDragHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly CountingCommandBus _commandBus;
        private readonly Action<CellAddress> _setActiveCell;
        private readonly Action<CellAddress, CellAddress> _extendSelection;
        private readonly Action<CellAddress, bool> _addOrMoveAdditionalSelection;
        private readonly Action<FreeX.App.UI.GridHeaderContextMenuTarget, uint, uint> _extendHeaderSelection;
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

            var extendHeaderSelection = typeof(MainWindow)
                .GetMethod("ExtendHeaderSelection", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "ExtendHeaderSelection");
            _extendHeaderSelection = extendHeaderSelection
                .CreateDelegate<Action<FreeX.App.UI.GridHeaderContextMenuTarget, uint, uint>>(window);

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

        public MeasurementResult MeasureRepeatedHeaderSelectionTarget(int iterations)
        {
            _extendHeaderSelection(FreeX.App.UI.GridHeaderContextMenuTarget.Column, 3, 8);
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
                    _extendHeaderSelection(FreeX.App.UI.GridHeaderContextMenuTarget.Column, 3, 8);
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
}
