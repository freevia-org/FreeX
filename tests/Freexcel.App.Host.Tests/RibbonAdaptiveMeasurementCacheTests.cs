using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using FluentAssertions;
using Freexcel.Core.Calc;
using Freexcel.Core.Commands;
using Freexcel.Core.Formula;
using Freexcel.Core.IO;
using Freexcel.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace Freexcel.App.Host.Tests;

public sealed class RibbonAdaptiveMeasurementCacheTests
{
    [Fact]
    public void StaticRibbonNormalization_InvalidatesThenReusesWidthSensitiveAdaptiveCaches()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = RibbonAdaptiveDiagnosticsHarness.Create();

            harness.SelectRibbonTab("Home", 1280);
            harness.ForceCompact();

            harness.ResetDiagnostics();
            harness.ForceCompact();

            var warmReuse = harness.Diagnostics;
            warmReuse.MeasurementInvalidationCount.Should().Be(0);
            warmReuse.GroupMeasurementCount.Should().Be(0);
            warmReuse.CompactSnapshotCaptureCount.Should().Be(0);
            warmReuse.ResizeThresholdRebuildCount.Should().Be(0);
            warmReuse.MeasurementCacheKey.Should().NotBeNullOrWhiteSpace();
            warmReuse.CompactSnapshotCacheKey.Should().NotBeNullOrWhiteSpace();
            warmReuse.ResizeThresholdCacheKey.Should().NotBeNullOrWhiteSpace();

            harness.ResetDiagnostics(resetSelectedStaticNormalization: true);
            harness.NormalizeRibbonSurface();

            var rebuilt = harness.Diagnostics;
            rebuilt.MeasurementInvalidationCount.Should().Be(1);
            rebuilt.GroupMeasurementCount.Should().BeGreaterThan(0);
            rebuilt.CompactSnapshotCaptureCount.Should().BeGreaterThan(0);
            rebuilt.ResizeThresholdRebuildCount.Should().Be(1);
            rebuilt.MeasurementCacheKey.Should().NotBeNullOrWhiteSpace();
            rebuilt.CompactSnapshotCacheKey.Should().NotBeNullOrWhiteSpace();
            rebuilt.ResizeThresholdCacheKey.Should().NotBeNullOrWhiteSpace();
            harness.VisibleRibbonGroupNames.Should().NotContain(string.Empty);

            harness.ResetDiagnostics();
            harness.ForceCompact();

            var postRebuildReuse = harness.Diagnostics;
            postRebuildReuse.MeasurementInvalidationCount.Should().Be(0);
            postRebuildReuse.GroupMeasurementCount.Should().Be(0);
            postRebuildReuse.CompactSnapshotCaptureCount.Should().Be(0);
            postRebuildReuse.ResizeThresholdRebuildCount.Should().Be(0);
        });
    }

    private sealed class RibbonAdaptiveDiagnosticsHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly MethodInfo _updateRibbonCompactMode;
        private readonly MethodInfo _normalizeRibbonSurface;

        private RibbonAdaptiveDiagnosticsHarness(MainWindow window)
        {
            _window = window;
            _updateRibbonCompactMode = typeof(MainWindow)
                .GetMethod("UpdateRibbonCompactMode", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "UpdateRibbonCompactMode");
            _normalizeRibbonSurface = typeof(MainWindow)
                .GetMethod("NormalizeRibbonSurface", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "NormalizeRibbonSurface");
        }

        public RibbonAdaptiveDiagnosticsSnapshot Diagnostics => _window.GetRibbonAdaptiveDiagnosticsForTests();

        public IReadOnlyList<string> VisibleRibbonGroupNames =>
            (_window.FindName("RibbonTabs") as TabControl)?.SelectedItem is TabItem selectedTab
                ? EnumerateVisualDescendants(selectedTab.Content as DependencyObject ?? selectedTab)
                    .OfType<FrameworkElement>()
                    .Where(element => element.Visibility == Visibility.Visible &&
                                      RibbonMetadata.TryGetGroupName(element, out _))
                    .Select(GetGroupName)
                    .Where(name => name is not null)
                    .Select(name => name!)
                    .ToList()
                : [];

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
            _window.UpdateLayout();
            PumpDispatcher();
        }

        public void ForceCompact()
        {
            _updateRibbonCompactMode.Invoke(_window, [true]);
            _window.UpdateLayout();
            PumpDispatcher();
        }

        public void NormalizeRibbonSurface()
        {
            _normalizeRibbonSurface.Invoke(_window, [true]);
            _window.UpdateLayout();
            PumpDispatcher();
        }

        public void ResetDiagnostics(bool resetSelectedStaticNormalization = false) =>
            _window.ResetRibbonAdaptiveDiagnosticsForTests(resetSelectedStaticNormalization);

        public static RibbonAdaptiveDiagnosticsHarness Create()
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
            PumpDispatcher();
            return new RibbonAdaptiveDiagnosticsHarness(window);
        }

        public void Dispose()
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
        }

        private static string? GetGroupName(FrameworkElement element) =>
            RibbonMetadata.TryGetGroupName(element, out var name) ? name : null;

        private static IEnumerable<DependencyObject> EnumerateVisualDescendants(DependencyObject root)
        {
            var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                yield return child;

                foreach (var descendant in EnumerateVisualDescendants(child))
                    yield return descendant;
            }
        }

        private static void PumpDispatcher()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
    }

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new InvalidOperationException($"Sheet {sheetId} not found");
    }
}
