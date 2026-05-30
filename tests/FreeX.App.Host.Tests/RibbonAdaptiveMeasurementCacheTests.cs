using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

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

    [Fact]
    public void AdaptiveCompaction_ReusesMeasuredGroupsAndThresholdsAcrossResizeWidths()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = RibbonAdaptiveDiagnosticsHarness.Create();

            harness.SelectRibbonTab("Home", 1280);
            harness.UpdateCompact(force: true);
            var warm = harness.Diagnostics;
            warm.MeasurementCacheKey.Should().NotBeNullOrWhiteSpace();
            warm.ResizeThresholdCacheKey.Should().NotBeNullOrWhiteSpace();
            warm.CompactSnapshotCacheKey.Should().NotBeNullOrWhiteSpace();

            harness.ResetDiagnostics();
            harness.SetWidth(1100);
            harness.UpdateCompact();

            var resized = harness.Diagnostics;
            resized.MeasurementInvalidationCount.Should().Be(0);
            resized.GroupMeasurementCount.Should().Be(0);
            resized.CompactSnapshotCaptureCount.Should().Be(0);
            resized.ResizeThresholdRebuildCount.Should().Be(0);
            resized.MeasurementCacheKey.Should().Be(warm.MeasurementCacheKey);
            resized.ResizeThresholdCacheKey.Should().Be(warm.ResizeThresholdCacheKey);
            resized.CompactSnapshotCacheKey.Should().Be(warm.CompactSnapshotCacheKey);

            harness.ResetDiagnostics();
            harness.UpdateCompact();

            var sameWidth = harness.Diagnostics;
            sameWidth.GroupMeasurementCount.Should().Be(0);
            sameWidth.ResizeThresholdRebuildCount.Should().Be(0);
            sameWidth.LayoutPlanComputeCount.Should().Be(0, "the pure layout plan should be reused when the tab metrics and width are unchanged");
            sameWidth.LayoutPlanCacheHitCount.Should().Be(1);
            sameWidth.AppliedStateSkipCount.Should().Be(1, "a second pass at the same width should hit the applied-state guard instead of reapplying the ribbon tree");
        });
    }

    [Fact]
    public void AdaptiveCompaction_CachesPureLayoutPlansPerMeasuredTabAndWidth()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = RibbonAdaptiveDiagnosticsHarness.Create();

            harness.SelectRibbonTab("Data", 1280);
            harness.UpdateCompact(force: true);

            harness.ResetDiagnostics();
            harness.SetWidth(900);
            harness.UpdateCompact(force: true);

            var firstPassAtWidth = harness.Diagnostics;
            firstPassAtWidth.GroupMeasurementCount.Should().Be(0);
            firstPassAtWidth.ResizeThresholdRebuildCount.Should().Be(0);
            (firstPassAtWidth.LayoutPlanComputeCount + firstPassAtWidth.LayoutPlanCacheHitCount)
                .Should()
                .BeGreaterThan(0, "resizing or forcing compaction should produce a deterministic layout plan without remeasuring groups");

            harness.ResetDiagnostics();
            harness.UpdateCompact(force: true);

            var repeatedWidth = harness.Diagnostics;
            repeatedWidth.GroupMeasurementCount.Should().Be(0);
            repeatedWidth.ResizeThresholdRebuildCount.Should().Be(0);
            repeatedWidth.LayoutPlanComputeCount.Should().Be(0);
            repeatedWidth.LayoutPlanCacheHitCount.Should().Be(1);
        });
    }

    [Fact]
    public void AdaptiveCompaction_ReusesLayoutPlansWhenReturningToPreviouslySeenWidths()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = RibbonAdaptiveDiagnosticsHarness.Create();
            var warmedWidths = new[] { 1180d, 980d, 760d };

            harness.SelectRibbonTab("Data", 1280);
            harness.UpdateCompact(force: true);
            foreach (var width in warmedWidths)
            {
                harness.SetWidth(width);
                harness.UpdateCompact(force: true);
            }

            foreach (var width in warmedWidths.Reverse().Concat(warmedWidths))
            {
                harness.SetWidth(width);
                harness.ResetDiagnostics();
                harness.UpdateCompact(force: true);

                var reused = harness.Diagnostics;
                reused.MeasurementInvalidationCount.Should().Be(0, $"{width} is a width-only revisit");
                reused.GroupMeasurementCount.Should().Be(0, $"{width} should reuse measured groups");
                reused.CompactSnapshotCaptureCount.Should().Be(0, $"{width} should reuse compact snapshots");
                reused.ResizeThresholdRebuildCount.Should().Be(0, $"{width} should reuse resize thresholds");
                reused.LayoutPlanComputeCount.Should().Be(0, $"{width} should reuse the cached pure layout plan");
                reused.LayoutPlanCacheHitCount.Should().Be(1, $"{width} should require exactly one cached layout lookup");
            }
        });
    }

    [Fact]
    public void AdaptiveCompaction_ReusesMeasurementsAcrossResizeWidthsForEveryMainRibbonTab()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = RibbonAdaptiveDiagnosticsHarness.Create();

            foreach (var tab in new[] { "Home", "Insert", "Draw", "Page Layout", "Formulas", "Data", "Review", "View", "Help" })
            {
                harness.SelectRibbonTab(tab, 1280);
                harness.UpdateCompact(force: true);

                var warm = harness.Diagnostics;
                warm.MeasurementCacheKey.Should().NotBeNullOrWhiteSpace($"{tab} should warm a stable group measurement cache key");
                warm.ResizeThresholdCacheKey.Should().NotBeNullOrWhiteSpace($"{tab} should warm resize thresholds");
                warm.CompactSnapshotCacheKey.Should().NotBeNullOrWhiteSpace($"{tab} should warm compact snapshots");

                harness.ResetDiagnostics();
                harness.SetWidth(1100);
                harness.UpdateCompact();

                var resized = harness.Diagnostics;
                resized.MeasurementInvalidationCount.Should().Be(0, $"{tab} width-only resize should not invalidate adaptive caches");
                resized.GroupMeasurementCount.Should().Be(0, $"{tab} width-only resize should reuse measured group widths");
                resized.CompactSnapshotCaptureCount.Should().Be(0, $"{tab} width-only resize should reuse compact snapshots");
                resized.ResizeThresholdRebuildCount.Should().Be(0, $"{tab} width-only resize should reuse resize thresholds");
                (resized.LayoutPlanComputeCount + resized.LayoutPlanCacheHitCount)
                    .Should()
                    .BeGreaterThan(0, $"{tab} should plan or reuse a deterministic layout without rebuilding measured metrics");
                resized.MeasurementCacheKey.Should().Be(warm.MeasurementCacheKey, $"{tab} should keep the same measured group cache across width-only resize");
                resized.ResizeThresholdCacheKey.Should().Be(warm.ResizeThresholdCacheKey, $"{tab} should keep the same threshold cache across width-only resize");
                resized.CompactSnapshotCacheKey.Should().Be(warm.CompactSnapshotCacheKey, $"{tab} should keep the same snapshot cache across width-only resize");
            }
        });
    }

    [Fact]
    public void WindowResize_UsesCachedBreakpointsBeforeCompactingRibbon()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = RibbonAdaptiveDiagnosticsHarness.Create();

            harness.SelectRibbonTab("Home", 1280);
            harness.UpdateCompact(force: true);
            harness.ResetDiagnostics();
            harness.ResetFallbackDiagnostics();

            harness.SetWidth(1279);

            harness.FallbackDiagnostics.RequestCount.Should().Be(0);
            harness.Diagnostics.GroupMeasurementCount.Should().Be(0);
            harness.Diagnostics.ResizeThresholdRebuildCount.Should().Be(0);
            harness.Diagnostics.AppliedStateSkipCount.Should().Be(0);

            harness.ResetDiagnostics();
            harness.ResetFallbackDiagnostics();

            harness.SetWidth(700);

            harness.FallbackDiagnostics.RequestCount.Should().Be(1);
            harness.FallbackDiagnostics.LastRequestedWork.Should().Be("CompactOnly");
            harness.Diagnostics.GroupMeasurementCount.Should().Be(0);
            harness.Diagnostics.ResizeThresholdRebuildCount.Should().Be(0);
        });
    }

    [Fact]
    public void AdaptiveResizeHotPath_UsesValueTypeKeysForWidthAndStateCaches()
    {
        var fieldsSource = System.IO.File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml.cs"));
        fieldsSource.Should().Contain("Dictionary<RibbonAdaptiveLayoutPlanCacheEntryKey, RibbonAdaptiveLayoutResult>");
        fieldsSource.Should().Contain("Dictionary<RibbonCorrectionCacheKey, IReadOnlyList<RibbonAdaptiveGroupState>>");
        fieldsSource.Should().Contain("Dictionary<RibbonMeasuredOverflowCacheKey, bool>");
        fieldsSource.Should().Contain("RibbonAppliedStateKey? _lastRibbonAdaptiveAppliedStateKey");

        var source = System.IO.File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.RibbonAdaptive.cs"));
        var hotPathKeyHelpers = source.Substring(
            source.IndexOf("private static RibbonAdaptiveLayoutPlanCacheEntryKey CreateRibbonAdaptiveLayoutPlanCacheEntryKey", StringComparison.Ordinal),
            source.IndexOf("private static string CreateRibbonAdaptiveMeasurementCacheKey", StringComparison.Ordinal) -
            source.IndexOf("private static RibbonAdaptiveLayoutPlanCacheEntryKey CreateRibbonAdaptiveLayoutPlanCacheEntryKey", StringComparison.Ordinal));

        hotPathKeyHelpers.Should().Contain("CreateRibbonStateSignature(");
        hotPathKeyHelpers.Should().Contain("RoundRibbonWidthToTenths(");
        hotPathKeyHelpers.Should().NotContain("string.Join(");
        hotPathKeyHelpers.Should().NotContain(".Select(state");
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

        public RibbonFallbackDiagnosticsSnapshot FallbackDiagnostics => _window.GetRibbonFallbackDiagnosticsForTests();

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

        public void SetWidth(double width)
        {
            _window.WindowState = WindowState.Normal;
            _window.Width = width;
            _window.UpdateLayout();
            PumpDispatcher();
        }

        public void ForceCompact()
        {
            UpdateCompact(force: true);
        }

        public void UpdateCompact(bool force = false)
        {
            _updateRibbonCompactMode.Invoke(_window, [force]);
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

        public void ResetFallbackDiagnostics() =>
            _window.ResetRibbonFallbackDiagnosticsForTests();

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
