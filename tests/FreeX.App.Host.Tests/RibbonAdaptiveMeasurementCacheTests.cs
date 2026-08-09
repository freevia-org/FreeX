using System.Reflection;
using System.Diagnostics;
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
    // The legacy width-sensitive adaptive caches (measurement / snapshot / resize-threshold keys) are
    // dormant under the declarative ribbon. The live equivalent invariant is per-group: the
    // RibbonAdaptivePanel records each group's natural full width once (grow-only) and the collapse set
    // is a pure function of width — re-running the panel measure pass at the same width reuses those
    // recorded widths and produces the identical collapse decision, with no clipping.
    [Fact]
    public void StaticRibbonNormalization_InvalidatesThenReusesWidthSensitiveAdaptiveCaches()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = RibbonLiveAdaptivePanelHarness.Create();

            harness.SelectRibbonTab("Home", 1280);

            harness.Hosts.Should().NotBeEmpty("the Home tab renders its groups into the live RibbonAdaptivePanel");
            var warmFullWidths = harness.FullWidths;
            warmFullWidths.Values.Should().OnlyContain(width => width > 0, "every group records a natural full width once measured");
            var warmCollapsed = harness.CollapsedGroupNames;

            // A redundant measure pass at the same width must reuse the recorded full widths verbatim and
            // reach the same collapse decision (no re-measure drift, deterministic).
            harness.RemeasurePanel();
            harness.FullWidths.Should().Equal(warmFullWidths, "a redundant measure pass reuses the grow-only full-width cache");
            harness.CollapsedGroupNames.Should().Equal(warmCollapsed, "the collapse decision is a deterministic function of the unchanged width");
            harness.RightOverflowPx.Should().BeLessThanOrEqualTo(2.0, "the live ribbon collapses groups to fit, never clipping its right edge");

            // Re-selecting the tab rebuilds the panel's hosts; it must re-measure to a positive full width
            // for every group and converge to the same width-driven collapse set.
            harness.SelectRibbonTab("Home", 1280);
            harness.FullWidths.Values.Should().OnlyContain(width => width > 0, "the rebuilt panel re-measures each group's full width");
            harness.CollapsedGroupNames.Should().Equal(warmCollapsed, "the rebuilt panel reaches the same deterministic collapse set at the same width");
            harness.RightOverflowPx.Should().BeLessThanOrEqualTo(2.0);
        });
    }

    // Live equivalent of "reuse measured groups + thresholds across resize widths": the grow-only
    // per-group full-width cache is recorded once at the widest width and carried unchanged across a
    // shrink to a narrower band (a collapsed group keeps the accurate width it had while expanded), so a
    // narrower width only flips collapse states — it never shrinks the recorded full widths — and a
    // redundant pass at that width flips nothing.
    [Fact]
    public void AdaptiveCompaction_ReusesMeasuredGroupsAndThresholdsAcrossResizeWidths()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = RibbonLiveAdaptivePanelHarness.Create();

            harness.SelectRibbonTab("Home", 1280);
            var warmFullWidths = harness.FullWidths;
            warmFullWidths.Should().NotBeEmpty();
            warmFullWidths.Values.Should().OnlyContain(width => width > 0);
            var warmCollapsed = harness.CollapsedGroupNames;

            harness.SetWidth(1100);

            var resizedFullWidths = harness.FullWidths;
            resizedFullWidths.Should().Equal(warmFullWidths,
                "a width-only resize reuses the grow-only full-width cache instead of re-measuring groups smaller");
            var resizedCollapsed = harness.CollapsedGroupNames;
            warmCollapsed.Should().BeSubsetOf(resizedCollapsed,
                "shrinking the ribbon may collapse more groups but never re-expands a group that was already collapsed");
            harness.RightOverflowPx.Should().BeLessThanOrEqualTo(2.0, "the narrower band still fits without clipping");

            // A second pass at the same width must flip nothing: no host's Content is swapped and the
            // collapse set is identical (the live "applied-state guard skips the tree mutation").
            var contentBefore = harness.ContentIdentities;
            harness.RemeasurePanel();
            harness.ContentIdentities.Should().Equal(contentBefore,
                "a redundant pass at the same width re-uses every host's current content (no tree mutation)");
            harness.CollapsedGroupNames.Should().Equal(resizedCollapsed);
            harness.FullWidths.Should().Equal(resizedFullWidths);
        });
    }

    // Live equivalent of "a pure layout plan is cached per measured tab + width": the collapse decision
    // the RibbonAdaptivePanel reaches at a given width is a deterministic, repeatable function of the
    // recorded full widths. The first pass at a width produces a collapse plan; a repeated pass at the
    // SAME width reproduces it exactly and mutates nothing.
    [Fact]
    public void AdaptiveCompaction_CachesPureLayoutPlansPerMeasuredTabAndWidth()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = RibbonLiveAdaptivePanelHarness.Create();

            harness.SelectRibbonTab("Data", 1280);
            harness.SetWidth(900);

            var firstPassFullWidths = harness.FullWidths;
            firstPassFullWidths.Should().NotBeEmpty();
            var firstPassCollapsed = harness.CollapsedGroupNames;
            firstPassCollapsed.Should().NotBeEmpty("the Data tab cannot fit every group at 900px and folds its lowest-priority groups");
            harness.RightOverflowPx.Should().BeLessThanOrEqualTo(2.0);

            var contentBefore = harness.ContentIdentities;
            harness.RemeasurePanel();

            harness.FullWidths.Should().Equal(firstPassFullWidths, "the repeated pass reuses the recorded full widths");
            harness.CollapsedGroupNames.Should().Equal(firstPassCollapsed, "the repeated pass at the same width reproduces the identical collapse plan");
            harness.ContentIdentities.Should().Equal(contentBefore, "the repeated pass mutates no host content");
        });
    }

    [Fact]
    public void AdaptiveCompaction_DataTabCutoffIsIndependentOfWarmupSequence()
    {
        StaTestRunner.Run(() =>
        {
            static (IReadOnlyList<string> Collapsed, IReadOnlyDictionary<string, double> FullWidths) CaptureDataAt1120(
                Action<RibbonLiveAdaptivePanelHarness> warmup)
            {
                using var harness = RibbonLiveAdaptivePanelHarness.Create();
                warmup(harness);
                harness.RemeasurePanel();
                return (harness.CollapsedGroupNames, RoundFullWidths(harness.FullWidths));
            }

            static IReadOnlyDictionary<string, double> RoundFullWidths(IReadOnlyDictionary<string, double> widths) =>
                widths.ToDictionary(pair => pair.Key, pair => Math.Round(pair.Value, 1));

            var direct = CaptureDataAt1120(harness => harness.SelectRibbonTab("Data", 1120));
            var wideFirst = CaptureDataAt1120(harness =>
            {
                harness.SelectRibbonTab("Data", 1280);
                harness.SetWidth(1120);
            });
            var compactFirst = CaptureDataAt1120(harness =>
            {
                harness.SelectRibbonTab("Data", 900);
                harness.SetWidth(1120);
            });

            wideFirst.FullWidths.Should().Equal(
                direct.FullWidths,
                "the Data tab full-width budget at 1120px must not depend on a wider warm-up pass");
            compactFirst.FullWidths.Should().Equal(
                direct.FullWidths,
                "the Data tab full-width budget at 1120px must not depend on a compact warm-up pass");
            wideFirst.Collapsed.Should().Equal(
                direct.Collapsed,
                "Data at 1120px should collapse the same groups after a wider warm-up pass");
            compactFirst.Collapsed.Should().Equal(
                direct.Collapsed,
                "Data at 1120px should collapse the same groups after a compact warm-up pass");
        });
    }

    [BenchmarkFact]
    public void Benchmark_DataTabRepeatedCompact_ReusesCachedOverrideLayout()
    {
        StaTestRunner.Run(() =>
        {
            const int iterations = 120;
            using var harness = RibbonAdaptiveDiagnosticsHarness.Create();

            harness.SelectRibbonTab("Data", 1280);
            harness.SetWidth(1290);
            harness.UpdateCompact(force: true);
            harness.ResetDiagnostics();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var stopwatch = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
                harness.UpdateCompact(force: true);
            stopwatch.Stop();

            var diagnostics = harness.Diagnostics;
            var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Console.WriteLine(
                "PERF RIBBON_DATA_REPEATED_COMPACT " +
                $"steps={iterations} total_ms={stopwatch.Elapsed.TotalMilliseconds:F2} " +
                $"allocated_bytes={allocatedBytes:N0} " +
                $"layout_compute={diagnostics.LayoutPlanComputeCount:N0} " +
                $"layout_cache_hits={diagnostics.LayoutPlanCacheHitCount:N0} " +
                $"applied_state_skips={diagnostics.AppliedStateSkipCount:N0}");

            diagnostics.LayoutPlanComputeCount.Should().Be(0);
            diagnostics.LayoutPlanCacheHitCount.Should().Be(iterations);
            diagnostics.AppliedStateSkipCount.Should().Be(iterations);
            diagnostics.StateApplyCount.Should().Be(0);
            diagnostics.StateChangedGroupCount.Should().Be(0);
            diagnostics.CollapsedFootprintApplyCount.Should().Be(0);
        });
    }

    // Live equivalent of "skip the tree mutation when the applied state is already current": the
    // RibbonAdaptivePanel only swaps a host's Content for the groups whose collapsed state actually
    // flips, so a redundant measure pass at an unchanged width leaves every host's Content reference
    // untouched.
    [Fact]
    public void ForcedCompact_SkipsTreeMutationWhenAppliedStateIsAlreadyCurrent()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = RibbonLiveAdaptivePanelHarness.Create();

            harness.SelectRibbonTab("Home", 1280);
            harness.Hosts.Should().NotBeEmpty();
            var collapsedBefore = harness.CollapsedGroupNames;
            var contentBefore = harness.ContentIdentities;

            harness.RemeasurePanel();

            harness.ContentIdentities.Should().Equal(contentBefore,
                "a forced re-measure at an unchanged width must not swap any host's content");
            harness.CollapsedGroupNames.Should().Equal(collapsedBefore, "the collapse state is unchanged");
        });
    }

    // Live equivalent of "a one-pixel width drift keeps the same applied ribbon visuals": shrinking by a
    // single pixel does not cross any group's collapse boundary, so no host flips state and no host's
    // Content is swapped.
    [Fact]
    public void ForcedCompact_SkipsTreeMutationAcrossWidthsWithSameVisualStateMode()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = RibbonLiveAdaptivePanelHarness.Create();

            harness.SelectRibbonTab("Draw", 1280);
            harness.Hosts.Should().NotBeEmpty();
            var collapsedBefore = harness.CollapsedGroupNames;
            var contentBefore = harness.ContentIdentities;

            harness.SetWidth(1279);

            harness.CollapsedGroupNames.Should().Equal(collapsedBefore,
                "a one-pixel width drift keeps the same collapse set");
            harness.ContentIdentities.Should().Equal(contentBefore,
                "a one-pixel width drift swaps no host content");
            harness.RightOverflowPx.Should().BeLessThanOrEqualTo(2.0);
        });
    }

    // Live equivalent of "returning to a previously-seen width reuses its cached layout plan": the
    // collapse decision is a pure deterministic function of width, so revisiting a width band — in
    // either direction across a sweep — always reproduces exactly the collapse set first seen at that
    // width, with the grow-only full-width cache unchanged and no clipping.
    [Fact]
    public void AdaptiveCompaction_ReusesLayoutPlansWhenReturningToPreviouslySeenWidths()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = RibbonLiveAdaptivePanelHarness.Create();
            var warmedWidths = new[] { 1180d, 980d, 760d };

            harness.SelectRibbonTab("Data", 1280);
            var stableFullWidths = harness.FullWidths;
            stableFullWidths.Should().NotBeEmpty();

            // Warm each width once and record the collapse plan the panel settles on there.
            var collapsedByWidth = new Dictionary<double, IReadOnlyList<string>>();
            foreach (var width in warmedWidths)
            {
                harness.SetWidth(width);
                collapsedByWidth[width] = harness.CollapsedGroupNames;
                harness.FullWidths.Should().Equal(stableFullWidths, $"{width} reuses the grow-only full-width cache");
            }

            // Revisiting each width (sweeping back up, then back down) must reproduce the identical plan.
            foreach (var width in warmedWidths.Reverse().Concat(warmedWidths))
            {
                harness.SetWidth(width);
                harness.CollapsedGroupNames.Should().Equal(collapsedByWidth[width],
                    $"{width} is a width-only revisit and reproduces its first-seen collapse plan");
                harness.FullWidths.Should().Equal(stableFullWidths,
                    $"{width} revisit does not re-measure groups (grow-only full-width cache reused)");
                harness.RightOverflowPx.Should().BeLessThanOrEqualTo(2.0, $"{width} fits without clipping");
            }
        });
    }

    // Live equivalent of "measured-overflow correction is applied once then reused": the legacy
    // measured-correction loop is dormant; the declarative RibbonAdaptivePanel instead folds the
    // lowest-priority groups into overflow buttons directly so the row fits. At a compact width the
    // Insert tab must collapse at least one group (overflow), and a repeated pass at the same width must
    // reuse that decision verbatim without flipping any host.
    [Fact]
    public void AdaptiveCompaction_ReusesMeasuredOverflowDecisionsForRepeatedCorrectionStates()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = RibbonLiveAdaptivePanelHarness.Create();

            harness.SelectRibbonTab("Insert", 1280);
            harness.SetWidth(900);

            var firstPassCollapsed = harness.CollapsedGroupNames;
            firstPassCollapsed.Should()
                .NotBeEmpty("the Insert tab overflows at 900px and folds its lowest-priority groups into overflow buttons");
            harness.RightOverflowPx.Should().BeLessThanOrEqualTo(2.0, "the overflow decision keeps the row within the viewport (no clipping)");
            var contentBefore = harness.ContentIdentities;

            harness.RemeasurePanel();

            harness.CollapsedGroupNames.Should().Equal(firstPassCollapsed,
                "the same surface + width reuses the cached overflow decision verbatim");
            harness.ContentIdentities.Should().Equal(contentBefore,
                "the repeated pass flips no host (no redundant overflow correction)");
        });
    }

    // Live equivalent of "every main tab reuses measured groups across a width-only resize": for each
    // tab, the panel records each group's natural full width at the widest width, and a shrink to a
    // narrower band reuses those recorded widths unchanged (grow-only) — it may collapse more groups but
    // never re-measures a group smaller, and never clips.
    [Fact]
    public void AdaptiveCompaction_ReusesMeasurementsAcrossResizeWidthsForEveryMainRibbonTab()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = RibbonLiveAdaptivePanelHarness.Create();

            foreach (var tab in new[] { "Home", "Insert", "Draw", "Page Layout", "Formulas", "Data", "Review", "View", "Help" })
            {
                harness.SelectRibbonTab(tab, 1280);

                var warmFullWidths = harness.FullWidths;
                warmFullWidths.Should().NotBeEmpty($"{tab} renders its groups into the live RibbonAdaptivePanel");
                warmFullWidths.Values.Should().OnlyContain(width => width > 0, $"{tab} measures every group's natural full width");
                var warmCollapsed = harness.CollapsedGroupNames;

                harness.SetWidth(1100);

                harness.FullWidths.Should().Equal(warmFullWidths,
                    $"{tab} width-only resize reuses the grow-only full-width cache (no group re-measured smaller)");
                warmCollapsed.Should().BeSubsetOf(harness.CollapsedGroupNames,
                    $"{tab} narrower width only ever collapses more groups, never re-expands one");
                harness.RightOverflowPx.Should().BeLessThanOrEqualTo(2.0, $"{tab} fits at 1100px without clipping");

                // Width-only revisit back to the wide width reproduces the original wide collapse set.
                harness.SetWidth(1280);
                harness.CollapsedGroupNames.Should().Equal(warmCollapsed,
                    $"{tab} returning to the starting width reproduces the same deterministic collapse set");
                harness.FullWidths.Should().Equal(warmFullWidths, $"{tab} keeps the same recorded full widths across the resize sweep");
            }
        });
    }

    // The legacy resize-threshold breakpoint gate (suppress compaction inside the same width band) is
    // dormant: GetActiveRibbonPanel finds no legacy panel, so _ribbonResizeThresholds is never built and
    // the gate always passes through to the live panel. The live equivalent invariant is what the user
    // sees: a one-pixel width change does not cross any group's collapse boundary (same collapse set),
    // while a large shrink to 700px folds strictly more groups — both without clipping.
    [Fact]
    public void WindowResize_UsesCachedBreakpointsBeforeCompactingRibbon()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = RibbonLiveAdaptivePanelHarness.Create();

            harness.SelectRibbonTab("Home", 1280);
            harness.Hosts.Should().NotBeEmpty();
            var wideCollapsed = harness.CollapsedGroupNames;
            var wideFullWidths = harness.FullWidths;

            harness.SetWidth(1279);

            harness.CollapsedGroupNames.Should().Equal(wideCollapsed,
                "a one-pixel resize stays inside the same collapse band (no breakpoint crossed)");
            harness.FullWidths.Should().Equal(wideFullWidths, "no group is re-measured by the tiny resize");
            harness.RightOverflowPx.Should().BeLessThanOrEqualTo(2.0);

            harness.SetWidth(700);

            wideCollapsed.Should().BeSubsetOf(harness.CollapsedGroupNames,
                "shrinking far below the wide layout collapses strictly more (lower-priority) groups");
            harness.CollapsedGroupNames.Count.Should().BeGreaterThan(wideCollapsed.Count,
                "700px cannot fit the Home groups that fit at 1280px, so more fold into overflow buttons");
            harness.FullWidths.Should().Equal(wideFullWidths, "the grow-only full-width cache is reused, not rebuilt smaller");
            harness.RightOverflowPx.Should().BeLessThanOrEqualTo(2.0, "even at 700px the ribbon collapses to fit, never clipping");
        });
    }

    [Fact]
    public void AdaptiveResizeHotPath_UsesValueTypeKeysForWidthAndStateCaches()
    {
        var fieldsSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        fieldsSource.Should().Contain("Dictionary<RibbonAdaptiveLayoutPlanCacheEntryKey, RibbonAdaptiveLayoutResult>");
        fieldsSource.Should().Contain("Dictionary<RibbonCorrectionCacheKey, IReadOnlyList<RibbonAdaptiveGroupState>>");
        fieldsSource.Should().Contain("Dictionary<RibbonMeasuredOverflowCacheKey, bool>");
        fieldsSource.Should().Contain("RibbonAppliedStateKey? _lastRibbonAdaptiveAppliedStateKey");

        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.RibbonAdaptive.cs");
        const string keyHelperStart = "private static RibbonAdaptiveLayoutPlanCacheEntryKey CreateRibbonAdaptiveLayoutPlanCacheEntryKey";
        const string keyHelperEnd = "private string CreateRibbonAdaptiveMeasurementCacheKey";
        var hotPathKeyHelpers = source.Substring(
            source.IndexOf(keyHelperStart, StringComparison.Ordinal),
            source.IndexOf(keyHelperEnd, StringComparison.Ordinal) -
            source.IndexOf(keyHelperStart, StringComparison.Ordinal));

        hotPathKeyHelpers.Should().Contain("RibbonAdaptiveWpfSurface.CreateLayoutPlanKey(");
        hotPathKeyHelpers.Should().Contain("RibbonAdaptiveWpfSurface.CreateAppliedStateKey(");
        hotPathKeyHelpers.Should().Contain("RibbonAdaptiveWpfSurface.CreateCorrectionKey(");
        hotPathKeyHelpers.Should().Contain("RibbonAdaptiveWpfSurface.CreateMeasuredOverflowKey(");
        hotPathKeyHelpers.Should().NotContain("string.Join(");
        hotPathKeyHelpers.Should().NotContain(".Select(state");

        var sharedSource = DialogSourceTestSupport.ReadSharedRibbonWpfSource("RibbonAdaptiveWpfSurface.cs");
        sharedSource.Should().Contain("public static int RoundWidthToTenths(");
        sharedSource.Should().Contain("public static RibbonAdaptiveWpfStateSignature CreateStateSignature(");
        sharedSource.Should().Contain("public readonly record struct RibbonAdaptiveWpfLayoutPlanKey(");
        sharedSource.Should().Contain("public readonly record struct RibbonAdaptiveWpfMeasuredOverflowKey(");
    }

    [Fact]
    public void AppliedStateCacheKey_TracksPortableIconOnlyLabelModeBoundary()
    {
        var createAppliedStateKey = typeof(MainWindow).GetMethod(
            "CreateRibbonAppliedStateKey",
            BindingFlags.Static | BindingFlags.NonPublic);
        createAppliedStateKey.Should().NotBeNull();

        var states = new[] { RibbonAdaptiveGroupState.IconOnly };
        object CreateKey(double availableWidth) =>
            createAppliedStateKey!.Invoke(null, [availableWidth, states])!;

        CreateKey(819).Should().Be(
            CreateKey(820),
            "widths on the same side of the portable label boundary reuse the applied visual state");
        CreateKey(821).Should().NotBe(
            CreateKey(820),
            "crossing the portable label boundary must invalidate the cached applied visual state");
        CreateKey(900).Should().Be(
            CreateKey(821),
            "wide widths in the same collapsed-footprint band reuse the applied visual state");
    }

    // Live declarative-ribbon harness. The legacy MainWindow adaptive engine
    // (UpdateRibbonCompactMode -> GetActiveRibbonPanel -> measurement/threshold/snapshot caches) is
    // DORMANT for the declarative ribbon: GetActiveRibbonPanel looks for the old horizontal
    // StackPanel-of-grids and finds none, so the per-group caching + 2-state collapse is done by
    // RibbonAdaptivePanel.MeasureOverride instead. This harness inspects that live panel:
    // RibbonAdaptivePanel -> RibbonGroupHost (.GroupName / .FullWidth (grow-only width cache) /
    // .Collapsed (2-state: full grid OR one overflow button) / .Priority).
    private sealed class RibbonLiveAdaptivePanelHarness : IDisposable
    {
        private readonly MainWindow _window;

        private RibbonLiveAdaptivePanelHarness(MainWindow window) => _window = window;

        public RibbonAdaptivePanel? Panel
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

        public IReadOnlyList<RibbonGroupHost> Hosts =>
            Panel is { } panel ? panel.Children.OfType<RibbonGroupHost>().ToList() : [];

        // The group names currently folded into a single overflow button (the live 2-state collapse).
        public IReadOnlyList<string> CollapsedGroupNames =>
            Hosts.Where(host => host.Collapsed).Select(host => host.GroupName).ToList();

        public IReadOnlyList<string> ExpandedGroupNames =>
            Hosts.Where(host => !host.Collapsed).Select(host => host.GroupName).ToList();

        // Per-host grow-only full-width cache snapshot: the value the collapse decision budgets per group.
        public IReadOnlyDictionary<string, double> FullWidths =>
            Hosts.ToDictionary(host => host.GroupName, host => host.FullWidth);

        // Identity of each host's currently-shown Content. Re-measuring at an unchanged width must not
        // swap any host's Content (the panel only flips the groups whose collapsed state changes), so a
        // steady-state pass leaves these references untouched — the live analogue of the old
        // "applied-state guard skips the tree mutation".
        public IReadOnlyDictionary<string, int> ContentIdentities =>
            Hosts.ToDictionary(
                host => host.GroupName,
                host => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(host.Content));

        // Pixels by which the live arranged ribbon content overflows the panel's right edge. <= ~0 means
        // every group fits (or folded into an overflow button): no clipping, no horizontal scrollbar.
        public double RightOverflowPx
        {
            get
            {
                if (Panel is not { } panel || panel.ActualWidth <= 0)
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

            SetWidth(width);
        }

        public void SetWidth(double width)
        {
            _window.WindowState = WindowState.Normal;
            _window.Width = width;
            _window.UpdateLayout();
            PumpDispatcher();
        }

        // Force the live panel to re-run its measure/collapse pass at the current width without changing
        // anything else (mirrors a redundant resize tick).
        public void RemeasurePanel()
        {
            if (Panel is { } panel)
            {
                panel.InvalidateMeasure();
                _window.UpdateLayout();
            }

            PumpDispatcher();
        }

        public static RibbonLiveAdaptivePanelHarness Create()
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
            return new RibbonLiveAdaptivePanelHarness(window);
        }

        public void Dispose() => MainWindowTestCleanup.CloseWithoutSavePrompt(_window);

        private static void PumpDispatcher()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
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
                ? WpfTestTree.FindVisualDescendants<DependencyObject>(selectedTab.Content as DependencyObject ?? selectedTab)
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

        private static void PumpDispatcher()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
    }
}
