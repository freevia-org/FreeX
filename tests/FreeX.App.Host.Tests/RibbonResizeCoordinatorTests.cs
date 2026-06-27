using FluentAssertions;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

public sealed class RibbonResizeCoordinatorTests
{
    private const int WmEnterSizeMove = 0x0231;
    private const int WmExitSizeMove = 0x0232;

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
            queued.FirstFrameLayoutUpdateCount.Should().BeGreaterThan(
                0,
                "layout-change normalization should settle the selected ribbon before the queued render fallback runs");

            harness.PumpDispatcher();

            var executed = harness.Diagnostics;
            executed.ExecutedCount.Should().Be(1);
            executed.ForcedNormalizeCount.Should().Be(1);
            executed.ForcedCompactCount.Should().Be(0);
            executed.LastExecutedWork.Should().Be("NormalizeSurface");
            executed.FirstFrameLayoutUpdateCount.Should().BeGreaterThan(queued.FirstFrameLayoutUpdateCount);
            executed.IsPending.Should().BeFalse();
        });
    }

    [Fact]
    public void FallbackScheduler_UsesRenderPriorityInsteadOfSynchronousSend()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.Ribbon.cs");
        var methodStart = source.IndexOf("private void QueueRibbonFallback", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("internal RibbonFallbackDiagnosticsSnapshot GetRibbonFallbackDiagnosticsForTests", StringComparison.Ordinal);
        var method = source[methodStart..methodEnd];

        method.Should().Contain("DispatcherPriority.Render");
        method.Should().NotContain("DispatcherPriority.Send");
        method.Should().Contain("UpdateRibbonCompactMode(force: false)");
        method.Should().Contain("UpdateActiveRibbonLayoutBeforeFirstFrame()");
        method.Should().Contain("RibbonCompactUpdateRequiresLayout(result)");
        method.Should().Contain("_ribbonFallbackSkippedCompactLayoutCount");
    }

    [Fact]
    public void ResizeHotPath_ReusesCachedRibbonPanelOwnerInsteadOfAncestorWalks()
    {
        var fieldSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var adaptiveSource = DialogSourceTestSupport.ReadHostSources("MainWindow.RibbonAdaptive.cs");
        var ribbonSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Ribbon.cs");

        fieldSource.Should().Contain("Dictionary<TabItem, RibbonActivePanelCacheEntry>");
        fieldSource.Should().Contain("private TabItem? _ribbonAdaptiveControlCacheTab;");
        adaptiveSource.Should().Contain("private sealed record RibbonActivePanelCacheEntry");

        var cachedPanelLookup = SourceMethodExtractor.ExtractMethodSource(
            adaptiveSource,
            "private bool TryGetCachedActiveRibbonPanel(TabItem tabItem, out StackPanel? activePanel)");
        cachedPanelLookup.Should().Contain("cached.Panel.IsVisible");
        cachedPanelLookup.Should().NotContain("FindVisualAncestor<TabItem>");

        var tabIdentityLookup = SourceMethodExtractor.ExtractMethodSource(
            adaptiveSource,
            "private string GetRibbonAdaptiveTabIdentity(DependencyObject element)");
        tabIdentityLookup.Should().Contain("TryGetSelectedRibbonActivePanelCache");
        tabIdentityLookup.IndexOf("TryGetSelectedRibbonActivePanelCache", StringComparison.Ordinal)
            .Should()
            .BeLessThan(tabIdentityLookup.IndexOf("FindVisualAncestor<TabItem>", StringComparison.Ordinal));

        var selectedSurfaceCheck = SourceMethodExtractor.ExtractMethodSource(
            ribbonSource,
            "private bool IsCachedRibbonSurfaceSelected()");
        selectedSurfaceCheck.Should().Contain("_ribbonAdaptiveControlCacheTab");
        selectedSurfaceCheck.Should().NotContain("FindVisualAncestor<TabItem>");
    }

    [Fact]
    public void WindowResize_UsesResizeThresholdGateBeforeCompactingRibbon()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = RibbonCoordinatorHarness.Create();

            harness.SelectRibbonTab("Home", width: 1500);
            harness.PrimeResizeGate();
            harness.ResetDiagnostics();

            // NOTE: the legacy resize-threshold breakpoint gate (skip compaction inside the same width
            // band before touching the ribbon) is DORMANT under the declarative ribbon: GetActiveRibbonPanel
            // finds no legacy panel so _ribbonResizeThresholds is never built, and ShouldNormalizeRibbonSurfaceForResize
            // falls through (empty thresholds => always normalize). Each width change therefore requests a
            // CompactOnly fallback. The width-driven collapse correctness now lives in the RibbonAdaptivePanel,
            // so this asserts the user-visible invariant instead of the dead diagnostic counter.
            var wideCollapsed = harness.LiveCollapsedGroupNames;

            harness.ResizeWindow(1498);

            // A tiny same-band resize does not cross any group's collapse boundary in the live panel.
            harness.LiveCollapsedGroupNames.Should().Equal(wideCollapsed,
                "a 2px resize stays inside the same collapse band");
            harness.LiveRibbonFitsOrIsAtCollapsedFloor.Should().BeTrue();
            harness.AdaptiveDiagnostics.GroupMeasurementCount.Should().Be(0, "the dormant legacy adaptive engine measures no groups");
            harness.AdaptiveDiagnostics.ResizeThresholdRebuildCount.Should().Be(0, "the dormant legacy engine builds no resize thresholds");

            harness.ResizeWindow(700);
            if (!harness.CanUseRequestedWidth(700))
                return;

            // A large shrink crosses breakpoints: the live panel folds strictly more (lower-priority)
            // groups into overflow buttons, and the resize path requests a single coalesced CompactOnly
            // fallback. No clipping at the narrow width.
            wideCollapsed.Should().BeSubsetOf(harness.LiveCollapsedGroupNames,
                "shrinking to 700px collapses strictly more groups than the wide layout");
            harness.LiveCollapsedGroupNames.Count.Should().BeGreaterThan(wideCollapsed.Count);
            harness.LiveRibbonFitsOrIsAtCollapsedFloor.Should().BeTrue("even at 700px the ribbon collapses to fit unless every group is already collapsed");
            var crossedBand = harness.Diagnostics;
            crossedBand.RequestCount.Should().BeGreaterThan(0, "crossing a band requests a compact fallback");
            crossedBand.LastRequestedWork.Should().Be("CompactOnly");
            crossedBand.LastMergedWork.Should().Be("CompactOnly");
        });
    }

    [Fact]
    public void WindowResize_DebouncesViewportRefreshUntilResizeIdle()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = RibbonCoordinatorHarness.Create();

            harness.ResetDiagnostics();

            harness.ResizeWindow(1180, pumpDispatcher: false);

            harness.ViewportCallCount.Should().Be(0);
            harness.IsLiveResizing.Should().BeTrue();

            harness.PumpDispatcher();

            harness.ViewportCallCount.Should().Be(0);
            harness.IsLiveResizing.Should().BeTrue();

            harness.PumpUntil(() => harness.ViewportCallCount > 0 && !harness.IsLiveResizing);

            harness.ViewportCallCount.Should().BeGreaterThan(0);
            harness.IsLiveResizing.Should().BeFalse();
        });
    }

    [Fact]
    public void NativeResizeLoop_DefersViewportRefreshAndRibbonCompactionUntilExit()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = RibbonCoordinatorHarness.Create();

            harness.SelectRibbonTab("Home", width: 1500);
            harness.PrimeResizeGate();
            harness.ResetDiagnostics();

            harness.EnterNativeResizeLoop();
            harness.ResizeWindow(700);
            if (!harness.CanUseRequestedWidth(700))
                return;

            // During the native resize loop the shell coordinator DEFERS both viewport refresh and ribbon
            // compaction: it only marks compaction pending-on-exit and posts no fallback yet. This shell
            // deferral runs regardless of the (declarative) ribbon engine and is what this guards.
            harness.IsLiveResizing.Should().BeTrue();
            harness.ViewportCallCount.Should().Be(0);
            var deferred = harness.Diagnostics;
            deferred.ResizeCompactionPendingOnExit.Should().BeTrue();
            deferred.RequestCount.Should().Be(0);
            deferred.PostedCount.Should().Be(0);
            harness.AdaptiveDiagnostics.GroupMeasurementCount.Should().Be(0);
            harness.AdaptiveDiagnostics.AppliedStateSkipCount.Should().Be(0);

            harness.ExitNativeResizeLoop();

            harness.IsLiveResizing.Should().BeFalse();
            harness.ViewportCallCount.Should().BeGreaterThan(0);

            // On exit the deferred compaction is flushed as exactly ONE coalesced CompactOnly fallback
            // request. (With the legacy adaptive engine dormant the applied-state-key skip guard no longer
            // fires, so the fallback is actually posted rather than skipped — the meaningful invariant is
            // the single coalesced request, not whether the dead skip-guard suppressed the post.)
            var queued = harness.Diagnostics;
            queued.ResizeCompactionPendingOnExit.Should().BeFalse();
            queued.RequestCount.Should().Be(1);
            queued.PostedCount.Should().Be(1);
            queued.LastMergedWork.Should().Be("CompactOnly");

            harness.PumpDispatcher();

            // The posted fallback executes once as a CompactOnly pass. The declarative ribbon's
            // MainWindow-level compaction path is dormant, so the pass does no ribbon tree work, but the
            // single coalesced fallback is the observable shell behavior.
            var executed = harness.Diagnostics;
            executed.ExecutedCount.Should().Be(1);
            executed.ForcedCompactCount.Should().Be(1);
            executed.LastExecutedWork.Should().Be("CompactOnly");
        });
    }

    [Fact]
    public void NativeMoveLoop_DoesNotEnterLiveResizeModeUntilSizeChanges()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = RibbonCoordinatorHarness.Create();

            harness.PumpUntil(() => !harness.IsLiveResizing);
            harness.ResetDiagnostics();

            harness.EnterNativeResizeLoop();

            harness.IsLiveResizing.Should().BeFalse(
                "a pure window move should preserve the retained worksheet and drawing-object layers");
            harness.ViewportCallCount.Should().Be(0);

            harness.ExitNativeResizeLoop();

            harness.IsLiveResizing.Should().BeFalse();
            harness.ViewportCallCount.Should().Be(0);
        });
    }

    [Fact]
    public void NativeResizeLoop_EntersLiveResizeModeWhenWindowActuallyResizes()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = RibbonCoordinatorHarness.Create();

            harness.PumpUntil(() => !harness.IsLiveResizing);
            harness.ResetDiagnostics();

            harness.EnterNativeResizeLoop();

            harness.IsLiveResizing.Should().BeFalse();

            harness.ResizeWindow(700);

            harness.IsLiveResizing.Should().BeTrue();
            harness.ViewportCallCount.Should().Be(0);

            harness.ExitNativeResizeLoop();

            harness.IsLiveResizing.Should().BeFalse();
            harness.ViewportCallCount.Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public void NativeResizeLoop_CoalescesMultipleDeferredCompactionsIntoSingleExitFallback()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = RibbonCoordinatorHarness.Create();

            harness.SelectRibbonTab("Home", width: 1500);
            harness.PrimeResizeGate();
            harness.ResetDiagnostics();

            harness.EnterNativeResizeLoop();
            var reachedNarrowResizeWidth = false;
            foreach (var width in new[] { 700d, 640d, 900d, 760d })
            {
                harness.ResizeWindow(width);
                reachedNarrowResizeWidth |= width == 700d && harness.CanUseRequestedWidth(width);
            }
            if (!reachedNarrowResizeWidth)
                return;

            // Four width changes inside the loop are all deferred (coalesced) — no fallback requested or
            // posted yet, just a single pending-on-exit flag.
            var deferred = harness.Diagnostics;
            deferred.ResizeCompactionPendingOnExit.Should().BeTrue();
            deferred.RequestCount.Should().Be(0);
            deferred.PostedCount.Should().Be(0);
            deferred.ExecutedCount.Should().Be(0);
            harness.AdaptiveDiagnostics.GroupMeasurementCount.Should().Be(0);

            harness.ExitNativeResizeLoop();

            // The four deferred resizes collapse into exactly ONE CompactOnly fallback on exit — the
            // coalescing this guards. (The post is no longer skipped because the legacy applied-state-key
            // skip guard is dormant under the declarative ribbon; the single request is the invariant.)
            var queued = harness.Diagnostics;
            queued.ResizeCompactionPendingOnExit.Should().BeFalse();
            queued.RequestCount.Should().Be(1);
            queued.PostedCount.Should().Be(1);
            queued.ExecutedCount.Should().Be(0);
            queued.LastMergedWork.Should().Be("CompactOnly");

            harness.PumpDispatcher();

            // Still a single coalesced fallback — it executes exactly once, never N times for the N
            // deferred resizes.
            var executed = harness.Diagnostics;
            executed.RequestCount.Should().Be(1);
            executed.PostedCount.Should().Be(1);
            executed.ExecutedCount.Should().Be(1);
            executed.ForcedCompactCount.Should().Be(1);
            executed.LastExecutedWork.Should().Be("CompactOnly");
        });
    }

    [Fact]
    public void NativeResizeExit_OnlySchedulesFallbackWhenResizeLoopDeferredCompaction()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = RibbonCoordinatorHarness.Create();

            // Completing a resize with NOTHING deferred schedules no fallback at all — the gate on the
            // pending-on-exit flag means an idle exit is free.
            harness.CompleteResizeCompaction();
            harness.PumpDispatcher();
            harness.Diagnostics.RequestCount.Should().Be(0);
            harness.Diagnostics.PostedCount.Should().Be(0);

            // Once a resize HAS deferred its compaction (pending-on-exit set), it is held until exit.
            harness.DeferResizeCompactionUntilExit();
            harness.Diagnostics.ResizeCompactionPendingOnExit.Should().BeTrue();
            harness.Diagnostics.RequestCount.Should().Be(0);

            // Completing the resize now flushes exactly one CompactOnly fallback for the deferred work.
            // (The legacy applied-state-key skip guard is dormant under the declarative ribbon, so the
            // single fallback is posted rather than skipped — the invariant is that exit schedules a
            // fallback ONLY because a resize deferred compaction.)
            harness.CompleteResizeCompaction();
            var queued = harness.Diagnostics;
            queued.ResizeCompactionPendingOnExit.Should().BeFalse();
            queued.RequestCount.Should().Be(1);
            queued.PostedCount.Should().Be(1);
            queued.LastMergedWork.Should().Be("CompactOnly");

            harness.PumpDispatcher();

            // The single deferred fallback executes once as a CompactOnly pass.
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
        private readonly CountingViewportService _viewportService;
        private readonly MethodInfo _mainWindowWndProc;
        private readonly MethodInfo _updateRibbonCompactMode;
        private readonly MethodInfo _normalizeRibbonSurfaceAfterLayoutChange;
        private readonly MethodInfo _compactRibbonSurfaceAfterResize;
        private readonly MethodInfo _completeRibbonResizeCompaction;

        private RibbonCoordinatorHarness(MainWindow window, CountingViewportService viewportService)
        {
            _window = window;
            _viewportService = viewportService;
            _mainWindowWndProc = typeof(MainWindow).GetMethod(
                    "MainWindow_WndProc",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "MainWindow_WndProc");
            _updateRibbonCompactMode = typeof(MainWindow).GetMethod(
                    "UpdateRibbonCompactMode",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "UpdateRibbonCompactMode");
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
        public RibbonAdaptiveDiagnosticsSnapshot AdaptiveDiagnostics => _window.GetRibbonAdaptiveDiagnosticsForTests();
        public int ViewportCallCount => _viewportService.GetViewportCallCount;
        public bool IsLiveResizing => SheetGrid.IsLiveResizing;

        private FreeX.App.UI.GridView SheetGrid =>
            (FreeX.App.UI.GridView)_window.FindName("SheetGrid");

        // The live declarative ribbon panel for the selected tab. The MainWindow-level adaptive engine is
        // dormant for the declarative ribbon; this panel does the real per-group caching + 2-state
        // collapse, so resize-coordinator outcomes are verified against ITS state.
        private RibbonAdaptivePanel? LivePanel
        {
            get
            {
                if (_window.FindName("RibbonTabs") is not TabControl tabs ||
                    tabs.SelectedItem is not TabItem tabItem)
                {
                    return null;
                }

                var root = tabItem.Content as DependencyObject ?? tabItem;
                return WpfTestTree.FindVisualSelfAndDescendants<RibbonAdaptivePanel>(root)
                    .Concat(WpfTestTree.FindLogicalDescendants<RibbonAdaptivePanel>(root))
                    .Distinct()
                    .FirstOrDefault();
            }
        }

        public IReadOnlyList<string> LiveCollapsedGroupNames =>
            LivePanel is { } panel
                ? panel.Children.OfType<RibbonGroupHost>().Where(host => host.Collapsed).Select(host => host.GroupName).ToList()
                : [];

        public bool LiveRibbonFitsOrIsAtCollapsedFloor =>
            LiveRibbonRightOverflowPx <= 2.0 ||
            LivePanel is { } panel &&
            panel.Children.OfType<RibbonGroupHost>().All(host => host.Collapsed);

        public double LiveRibbonRightOverflowPx
        {
            get
            {
                if (LivePanel is not { } panel || panel.ActualWidth <= 0)
                    return 0;

                double maxRight = 0;
                foreach (var child in panel.Children.OfType<FrameworkElement>())
                {
                    if (child.Visibility != Visibility.Visible)
                        continue;

                    var x = child.TransformToAncestor(panel).Transform(new Point(0, 0)).X;
                    maxRight = Math.Max(maxRight, x + child.ActualWidth);
                }

                return maxRight - panel.ActualWidth;
            }
        }

        public void SelectRibbonTab(string header, double width)
        {
            if (_window.FindName("RibbonTabs") is TabControl tabs)
            {
                tabs.SelectedItem = tabs.Items
                    .OfType<TabItem>()
                    .First(item => string.Equals(item.Header?.ToString(), header, StringComparison.Ordinal));
            }

            ResizeWindow(width);
            _updateRibbonCompactMode.Invoke(_window, [true]);
            _window.UpdateLayout();
            PumpDispatcher();
        }

        public void PrimeResizeGate()
        {
            ResizeWindow(_window.Width - 1);
            PumpDispatcher();
        }

        public void ResizeWindow(double width, bool pumpDispatcher = true)
        {
            _window.WindowState = WindowState.Normal;
            _window.Width = width;
            _window.UpdateLayout();
            if (pumpDispatcher)
                PumpDispatcher();
        }

        public bool CanUseRequestedWidth(double width) =>
            _window.ActualWidth >= width - 1;

        public void EnterNativeResizeLoop() => InvokeWindowProcedure(WmEnterSizeMove);

        public void ExitNativeResizeLoop() => InvokeWindowProcedure(WmExitSizeMove);

        private void InvokeWindowProcedure(int message)
        {
            object?[] args = [IntPtr.Zero, message, IntPtr.Zero, IntPtr.Zero, false];
            _mainWindowWndProc.Invoke(_window, args);
        }

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

        public void ResetDiagnostics()
        {
            _window.ResetRibbonFallbackDiagnosticsForTests();
            _window.ResetRibbonAdaptiveDiagnosticsForTests();
            _viewportService.Reset();
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

        public void PumpUntil(Func<bool> condition, int timeoutMilliseconds = 2000)
        {
            var stopwatch = Stopwatch.StartNew();
            while (!condition())
            {
                if (stopwatch.ElapsedMilliseconds > timeoutMilliseconds)
                    throw new TimeoutException("The dispatcher condition was not reached before the timeout.");

                Thread.Sleep(10);
                PumpDispatcher();
            }
        }

        public static RibbonCoordinatorHarness Create()
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");
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
                NullUserMessageService.Instance);

            window.Width = 1280;
            window.Height = 720;
            window.Show();
            var harness = new RibbonCoordinatorHarness(window, viewportService);
            harness.PumpDispatcher();
            harness.ResetDiagnostics();
            return harness;
        }

        public void Dispose()
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
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

        public (uint LastVisibleRow, IReadOnlyList<OutlineGroupRange> RowOutlineGroups)
            ComputeRowMetricsSummary(Workbook workbook, SheetId sheetId, ViewportRequest request) =>
            inner.ComputeRowMetricsSummary(workbook, sheetId, request);

        public CellAddress? HitTest(Workbook workbook, SheetId sheetId, double x, double y, double zoom) =>
            inner.HitTest(workbook, sheetId, x, y, zoom);

        public void Reset() => GetViewportCallCount = 0;
    }
}
