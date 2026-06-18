using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace FreeX.App.Host.Tests;

// Ribbon-focused UI test lane: extra harness helpers shared by the RibbonLane.* test files.
// These reuse the offscreen MainWindow + ribbon-tree queries already defined on MainWindowHarness,
// adding tab enumeration, adaptive/fallback diagnostics passthrough, a faithful live-resize-drag
// simulation, and per-step resize timing.
public sealed partial class MainWindowAdaptiveRibbonTests
{
    // The non-contextual ribbon tabs the declarative model renders content for. "File" is the backstage
    // (opens the start screen, no ribbon body) and is excluded.
    private static readonly string[] MainRibbonTabHeaders =
    {
        "Home", "Insert", "Draw", "Page Layout", "Formulas", "Data", "Review", "View", "Help"
    };

    // A representative ladder of window widths, widest first, covering the adaptive range from a
    // full-screen ribbon down to a heavily-collapsed one. Capped at 1500 because forcing an offscreen
    // headless window wider than the virtual desktop makes WPF's auto-sizing ScrollViewer loop
    // ("cross-dependent views"); callers still guard with CanUseRequestedWidth.
    private static readonly double[] RibbonResolutionWidths =
    {
        1500d, 1366d, 1280d, 1100d, 960d, 820d, 700d
    };

    private sealed partial class MainWindowHarness
    {
        private static readonly FieldInfo InWindowResizeMoveLoopField = typeof(MainWindow)
            .GetField("_isInWindowResizeMoveLoop", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(MainWindow), "_isInWindowResizeMoveLoop");

        private static readonly MethodInfo NormalizeRibbonSurfaceAfterResizeMethod = typeof(MainWindow)
            .GetMethod("NormalizeRibbonSurfaceAfterResize", BindingFlags.Instance | BindingFlags.NonPublic, [])
            ?? throw new MissingMethodException(nameof(MainWindow), "NormalizeRibbonSurfaceAfterResize");

        private static readonly MethodInfo CompleteRibbonResizeCompactionMethod = typeof(MainWindow)
            .GetMethod("CompleteRibbonResizeCompaction", BindingFlags.Instance | BindingFlags.NonPublic, [])
            ?? throw new MissingMethodException(nameof(MainWindow), "CompleteRibbonResizeCompaction");

        // Headers of every selectable, currently-visible ribbon tab except the File backstage.
        public IReadOnlyList<string> SelectableRibbonTabHeaders =>
            _window.FindName("RibbonTabs") is TabControl tabs
                ? tabs.Items
                    .OfType<TabItem>()
                    .Where(item => item.Visibility == Visibility.Visible)
                    .Select(item => item.Header?.ToString() ?? string.Empty)
                    .Where(header => !string.IsNullOrWhiteSpace(header) &&
                                     !string.Equals(header, "File", StringComparison.Ordinal))
                    .ToList()
                : [];

        public bool TabExists(string header) =>
            _window.FindName("RibbonTabs") is TabControl tabs &&
            tabs.Items.OfType<TabItem>().Any(item =>
                item.Visibility == Visibility.Visible &&
                string.Equals(item.Header?.ToString(), header, StringComparison.Ordinal));

        public int ActiveRibbonGroupCount => ActiveRibbonGroupNames.Count;

        // Counts the rendered group containers in the selected tab. Unlike ActiveRibbonGroupNames (which
        // keys off the legacy StackPanel surface), this walks for RibbonGroupHost — the declarative
        // renderer's group container — so it works for every tab, including non-Home and contextual tabs.
        public int SelectedTabGroupHostCount =>
            WpfTestTree.FindVisualSelfAndDescendants<RibbonGroupHost>(SelectedRibbonContentRoot).Count();

        // Counts the command controls currently shown (expanded) in the selected tab — visible buttons
        // that are not the single collapsed-group overflow button. Robust for the declarative ribbon,
        // unlike VisibleRibbonCommandLabels which depends on the legacy label-extraction path.
        public int SelectedTabVisibleCommandControlCount =>
            WpfTestTree.FindVisualSelfAndDescendants<System.Windows.Controls.Primitives.ButtonBase>(SelectedRibbonContentRoot)
                .Where(IsEffectivelyVisible)
                .Count(button => !RibbonMetadata.IsCollapsedGroupButton(button));

        public RibbonAdaptiveDiagnosticsSnapshot AdaptiveDiagnostics =>
            _window.GetRibbonAdaptiveDiagnosticsForTests();

        public RibbonFallbackDiagnosticsSnapshot FallbackDiagnostics =>
            _window.GetRibbonFallbackDiagnosticsForTests();

        public void ResetRibbonDiagnostics()
        {
            _window.ResetRibbonAdaptiveDiagnosticsForTests();
            _window.ResetRibbonFallbackDiagnosticsForTests();
        }

        // Sets the window width WITHOUT forcing a compaction pass — the layout is left to react through
        // the real SizeChanged -> NormalizeRibbonSurfaceAfterResize path, exactly as it does at runtime.
        // (SetRibbonWidth, by contrast, force-compacts and so would mask realtime/deferral behavior.)
        public void SetWindowWidthThroughResizePath(double width)
        {
            _window.WindowState = WindowState.Normal;
            _window.Width = width;
            _window.UpdateLayout();
            PumpDispatcher();
        }

        // Faithful simulation of a user dragging the window edge: WM_ENTERSIZEMOVE sets the move-loop
        // flag, each mouse move resizes the window (the OS raises SizeChanged), WM_EXITSIZEMOVE clears
        // the flag and completes any deferred compaction. Width/UpdateLayout raises SizeChanged
        // synchronously, so the same NormalizeRibbonSurfaceAfterResize handler runs as in production.
        public void BeginSimulatedResizeDrag() => InWindowResizeMoveLoopField.SetValue(_window, true);

        public void DragResizeTo(double width)
        {
            _window.WindowState = WindowState.Normal;
            _window.Width = width;
            _window.UpdateLayout();
            NormalizeRibbonSurfaceAfterResizeMethod.Invoke(_window, null);
            PumpDispatcher();
        }

        public void EndSimulatedResizeDrag()
        {
            InWindowResizeMoveLoopField.SetValue(_window, false);
            CompleteRibbonResizeCompactionMethod.Invoke(_window, null);
            _window.UpdateLayout();
            PumpDispatcher();
            PumpDispatcher();
        }

        public bool CanUseRequestedWidth(double width) => _window.ActualWidth >= width - 1;

        // Forces one adaptive-compaction pass at the current width (same call the resize path makes),
        // used by perf guards to count how much layout work a redundant resize tick does.
        public void ForceRibbonCompaction()
        {
            _updateRibbonCompactMode.Invoke(_window, [true]);
            PumpDispatcher();
        }

        // Drives the window across widths through the real resize path (no forced compaction), as a live
        // drag would, letting the threshold gate and measurement caches behave exactly as in production.
        public void ResizeThroughResizePath(IReadOnlyList<double> widths)
        {
            foreach (var width in widths)
                SetWindowWidthThroughResizePath(width);
        }

        // Drives window resizes across the given widths and returns per-step elapsed milliseconds,
        // mirroring how a real drag streams width changes through the ribbon layout.
        public IReadOnlyList<double> MeasureResizeStepMilliseconds(IReadOnlyList<double> widths, int iterations)
        {
            var timings = new List<double>(widths.Count * iterations);
            for (var iteration = 0; iteration < iterations; iteration++)
            {
                foreach (var width in widths)
                {
                    var step = Stopwatch.StartNew();
                    _window.WindowState = WindowState.Normal;
                    _window.Width = width;
                    _window.UpdateLayout();
                    PumpDispatcher();
                    step.Stop();
                    timings.Add(step.Elapsed.TotalMilliseconds);
                }
            }

            return timings;
        }
    }
}
