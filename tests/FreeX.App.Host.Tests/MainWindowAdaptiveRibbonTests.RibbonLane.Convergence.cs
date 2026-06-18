using System.Windows;
using System.Windows.Controls;
using Free.Shared.Ribbon;
using FluentAssertions;
using Xunit;

namespace FreeX.App.Host.Tests;

// Ribbon UI lane — adaptive-layout convergence.
// Regression guard for the "cross-dependent views" infinite loop that froze/blanked the ribbon and
// crashed the app during resize: the adaptive panel used to swap every group's Content on every
// measure pass, so a narrow-width measure never stabilized. These tests drive the REAL renderer's
// adaptive panel through a full shrink/grow resize sweep, offscreen (fast, no live HWND), and assert
// the layout pass converges (UpdateLayout never throws) and is deterministic. If the panel ever
// regresses to a non-converging measure, UpdateLayout throws synchronously and these fail loudly
// instead of crashing the whole test host.
public sealed partial class MainWindowAdaptiveRibbonTests
{
    // Widest -> narrowest -> widest again. The narrow end is well below any group's natural width so
    // collapsing is forced; returning to the start width checks the layout settles back identically.
    private static readonly double[] ConvergenceSweepWidths =
        { 1500d, 1200d, 1000d, 820d, 700d, 560d, 460d, 380d, 460d, 700d, 1000d, 1500d };

    [Fact]
    [Trait("Category", "RibbonUiLane")]
    public void RibbonLane_EveryTab_AdaptivePanelConvergesAndIsStableAcrossResizeSweep()
    {
        StaTestRunner.Run(() =>
        {
            foreach (var tab in ResolveAllRibbonLaneTabs())
            {
                var host = CreateRibbonResourceHost();
                host.Child = RibbonWpfRenderer.BuildTabContent(tab, host);

                var collapsedByWidth = new List<(double Width, int Collapsed)>();
                foreach (var width in ConvergenceSweepWidths)
                {
                    host.Width = width;
                    host.Measure(new Size(width, 200));
                    host.Arrange(new Rect(0, 0, width, 200));
                    // Throws InvalidOperationException("...cross-dependent views") if the adaptive measure
                    // pass fails to converge — exactly the regression this guards.
                    host.UpdateLayout();
                    collapsedByWidth.Add((width, CountCollapsedGroupHosts(host)));
                }

                var narrowest = collapsedByWidth.OrderBy(s => s.Width).First();
                var widest = collapsedByWidth.OrderByDescending(s => s.Width).First();
                narrowest.Collapsed.Should().BeGreaterThanOrEqualTo(widest.Collapsed,
                    $"the '{tab.Header}' tab should collapse at least as many groups when narrow ({narrowest.Width:0}) " +
                    $"as when wide ({widest.Width:0})");

                var firstWide = collapsedByWidth.First();
                var lastWide = collapsedByWidth.Last();
                lastWide.Collapsed.Should().Be(firstWide.Collapsed,
                    $"the '{tab.Header}' tab layout should settle back to the same collapse state when the window " +
                    "returns to its starting width (deterministic, no drift)");
            }
        });
    }

    [Fact]
    [Trait("Category", "RibbonUiLane")]
    public void RibbonLane_HomeTab_CollapsesGroupsWhenForcedNarrow()
    {
        StaTestRunner.Run(() =>
        {
            var home = ResolveAllRibbonLaneTabs()
                .First(tab => string.Equals(tab.Header, "Home", StringComparison.Ordinal));

            var host = CreateRibbonResourceHost();
            host.Child = RibbonWpfRenderer.BuildTabContent(home, host);

            host.Width = 360;
            host.Measure(new Size(360, 200));
            host.Arrange(new Rect(0, 0, 360, 200));
            host.UpdateLayout();

            CountCollapsedGroupHosts(host).Should().BeGreaterThan(0,
                "the Home tab cannot fit all 7 groups at 360px and must collapse the lowest-priority ones");
        });
    }

    private static IReadOnlyList<RibbonTab> ResolveAllRibbonLaneTabs()
    {
        var definition = FreeXRibbon.Build();
        var allActive = RibbonContextState.None
            .With("chart.selected")
            .With("picture.selected")
            .With("shape.selected")
            .With("table.active")
            .With("pivot.active");
        return RibbonContextResolver.Resolve(definition, allActive);
    }

    private static Border CreateRibbonResourceHost()
    {
        var host = new Border();
        host.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/FreeX.App.Host;component/Resources/MainWindowResources.xaml")
        });
        return host;
    }

    private static int CountCollapsedGroupHosts(DependencyObject root) =>
        WpfTestTree.FindVisualSelfAndDescendants<RibbonGroupHost>(root).Count(h => h.Collapsed);
}
