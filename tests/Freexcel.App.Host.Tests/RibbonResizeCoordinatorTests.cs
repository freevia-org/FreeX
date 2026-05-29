using FluentAssertions;
using System.Reflection;
using System.Windows;
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
        private readonly MethodInfo _normalizeRibbonSurfaceAfterLayoutChange;
        private readonly MethodInfo _compactRibbonSurfaceAfterResize;
        private readonly MethodInfo _completeRibbonResizeCompaction;

        private RibbonCoordinatorHarness(MainWindow window)
        {
            _window = window;
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

        public void PumpDispatcher()
        {
            _window.UpdateLayout();
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        public static RibbonCoordinatorHarness Create()
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

            window.Width = 1280;
            window.Height = 720;
            window.Show();
            var harness = new RibbonCoordinatorHarness(window);
            harness.PumpDispatcher();
            window.ResetRibbonFallbackDiagnosticsForTests();
            return harness;
        }

        public void Dispose()
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
        }
    }

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new InvalidOperationException($"Sheet {sheetId} not found");
    }
}
