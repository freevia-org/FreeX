using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace FreeX.App.Host.Tests;

// Ribbon-focused UI test lane: extra harness helpers shared by the RibbonLane.* test files.
// These reuse the offscreen MainWindow + ribbon-tree queries already defined on MainWindowHarness,
// adding tab enumeration, a faithful live-resize-drag simulation, and per-step resize timing.
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

        private static readonly MethodInfo CompleteRibbonResizeLayoutMethod = typeof(MainWindow)
            .GetMethod("CompleteRibbonResizeLayout", BindingFlags.Instance | BindingFlags.NonPublic, [])
            ?? throw new MissingMethodException(nameof(MainWindow), "CompleteRibbonResizeLayout");

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

        // Counts the rendered group containers in the selected tab. Unlike ActiveRibbonGroupNames, this
        // walks RibbonGroupHost directly, so it works for every tab, including contextual tabs.
        public int SelectedTabGroupHostCount =>
            WpfTestTree.FindVisualSelfAndDescendants<RibbonGroupHost>(SelectedRibbonContentRoot).Count();

        // Pixels by which the selected tab's ribbon content overflows the right edge of its adaptive panel
        // — i.e. how much is clipped. <= ~0 means every group fits (or folded into an overflow button).
        public double SelectedTabRibbonRightOverflowPx
        {
            get
            {
                var panel = WpfTestTree.FindVisualSelfAndDescendants<RibbonAdaptivePanel>(SelectedRibbonContentRoot).FirstOrDefault();
                if (panel is null || panel.ActualWidth <= 0)
                    return 0;

                double maxRight = 0;
                foreach (var child in panel.Children.OfType<System.Windows.FrameworkElement>())
                {
                    if (!IsEffectivelyVisible(child))
                        continue;
                    var x = child.TransformToAncestor(panel).Transform(new System.Windows.Point(0, 0)).X;
                    maxRight = System.Math.Max(maxRight, x + child.ActualWidth);
                }

                return maxRight - panel.ActualWidth;
            }
        }

        // Counts the command controls currently shown (expanded) in the selected tab: visible buttons
        // that are not the single collapsed-group overflow button.
        // Count of visible command buttons in the selected tab that are disabled (greyed). Used to verify
        // the Help tab's commands bind to live handlers instead of rendering disabled.
        public int SelectedTabDisabledCommandButtonCount =>
            WpfTestTree.FindVisualSelfAndDescendants<System.Windows.Controls.Primitives.ButtonBase>(SelectedRibbonContentRoot)
                .Where(IsEffectivelyVisible)
                .Where(button => !RibbonMetadata.IsCollapsedGroupButton(button))
                .Count(button => !button.IsEnabled);

        public IReadOnlyList<string> SelectedTabDisabledCommandTitles =>
            WpfTestTree.FindVisualSelfAndDescendants<System.Windows.Controls.Primitives.ButtonBase>(SelectedRibbonContentRoot)
                .Where(IsEffectivelyVisible)
                .Where(button => !RibbonMetadata.IsCollapsedGroupButton(button))
                .Where(button => !button.IsEnabled)
                .Select(button => RibbonTooltip.GetTitle(button) ?? button.Name)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();

        public int SelectedTabVisibleCommandControlCount =>
            WpfTestTree.FindVisualSelfAndDescendants<System.Windows.Controls.Primitives.ButtonBase>(SelectedRibbonContentRoot)
                .Where(IsEffectivelyVisible)
                .Count(button => !RibbonMetadata.IsCollapsedGroupButton(button));

        // Sets the window width without forcing a synchronous pass. The shared panel reacts through the
        // real SizeChanged -> NormalizeRibbonSurfaceAfterResize path, exactly as it does at runtime.
        public void SetWindowWidthThroughResizePath(double width)
        {
            _window.WindowState = WindowState.Normal;
            _window.Width = width;
            _window.UpdateLayout();
            PumpDispatcher();
        }

        // Faithful simulation of a user dragging the window edge: WM_ENTERSIZEMOVE sets the move-loop
        // flag, each mouse move resizes the window (the OS raises SizeChanged), WM_EXITSIZEMOVE clears
        // the flag and completes the shared panel layout. Width/UpdateLayout raises SizeChanged
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
            CompleteRibbonResizeLayoutMethod.Invoke(_window, null);
            _window.UpdateLayout();
            PumpDispatcher();
            PumpDispatcher();
        }

        public bool CanUseRequestedWidth(double width) => _window.ActualWidth >= width - 1;

        // Drives the window across widths through the real resize path, as a live drag would, letting the
        // shared adaptive panel remeasure exactly as it does in production.
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
