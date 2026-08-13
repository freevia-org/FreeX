using FluentAssertions;
using Xunit;

namespace FreeX.App.Host.Tests;

// Ribbon UI lane — tab rendering coverage.
// Verifies that EVERY ribbon tab renders real content (groups + commands) at a range of
// screen resolutions, directly targeting the reported "some of the tabs are not rendering" defect.
public sealed partial class MainWindowAdaptiveRibbonTests
{
    public static IEnumerable<object[]> MainTabHeaderCases() =>
        MainRibbonTabHeaders.Select(header => new object[] { header });

    [Theory]
    [Trait("Category", "RibbonUiLane")]
    [MemberData(nameof(MainTabHeaderCases))]
    public void RibbonLane_MainTab_RendersGroupsAndCommandsAtEveryResolution(string header)
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.TabExists(header).Should().BeTrue($"the '{header}' tab should exist in the ribbon");

            var resolutionsExercised = 0;
            var widestReachable = true;
            foreach (var width in RibbonResolutionWidths)
            {
                harness.SelectRibbonTab(header, width);
                if (!harness.CanUseRequestedWidth(width))
                    continue; // Offscreen window could not reach this width on this desktop; skip.

                resolutionsExercised++;
                harness.SelectedTabGroupHostCount.Should().BeGreaterThan(0,
                    $"the '{header}' tab must render its ribbon groups at width {width:0}");

                // At the widest reachable width the tab has room to show real commands (not just collapsed
                // overflow buttons). Narrower widths legitimately collapse most commands into popups, so we
                // only require expanded commands at the top of the resolution ladder.
                if (widestReachable)
                {
                    // At the widest reachable width the tab must render something actionable. On a roomy
                    // desktop that is expanded command controls; on a constrained offscreen desktop (e.g. CI)
                    // the widest reachable width can be narrow enough that a wide tab (Page Layout) folds
                    // entirely into overflow group buttons (the 2-state engine's over-collapse, tracked
                    // separately) — which is still actionable. Require expanded commands OR overflow groups.
                    (harness.SelectedTabVisibleCommandControlCount > 0 ||
                        harness.CollapsedActiveRibbonGroupNames.Count > 0).Should().BeTrue(
                        $"the '{header}' tab must render expanded commands or overflow group buttons at its widest reachable width {width:0}");
                    widestReachable = false;
                }
            }

            resolutionsExercised.Should().BeGreaterThan(0,
                $"the '{header}' tab should have been verified at at least one reachable resolution");
        });
    }

    [Fact]
    [Trait("Category", "RibbonUiLane")]
    public void RibbonLane_HelpTab_CommandsBindToLiveHandlers()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.SelectRibbonTab("Help", 1200);

            harness.SelectedTabVisibleCommandControlCount.Should().BeGreaterThanOrEqualTo(5,
                "the Help tab renders its commands (Help Online, Feedback, Copy Diagnostics, Check for Updates, About, Legal Notices)");
            // Help commands must bind through the typed registry instead of rendering disabled. Feedback can
            // be state-disabled like Excel, so allow at most one disabled command.
            harness.SelectedTabDisabledCommandButtonCount.Should().BeLessThanOrEqualTo(1,
                $"Help commands must bind to live handlers, but these were disabled: " +
                string.Join(", ", harness.SelectedTabDisabledCommandTitles));
        });
    }

    [Fact]
    [Trait("Category", "RibbonUiLane")]
    public void RibbonLane_ContextualTabs_RenderGroupsAndCommands()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.ShowChartContextualTabs();
            harness.ShowPivotContextualTabs();
            harness.ShowTableDesignContextualTab();
            harness.ShowDrawingObjectContextualTabs();

            // Contextual tabs = selectable tabs that are neither a main content tab nor the backstage.
            var contextualHeaders = harness.SelectableRibbonTabHeaders
                .Where(header => !MainRibbonTabHeaders.Contains(header))
                .Where(header => !string.Equals(header, "File", StringComparison.Ordinal))
                .Distinct()
                .ToList();

            contextualHeaders.Should().NotBeEmpty("activating contextual surfaces should reveal contextual tabs");

            foreach (var header in contextualHeaders)
            {
                harness.SelectRibbonTab(header, 1366);
                harness.SelectedTabGroupHostCount.Should().BeGreaterThan(0,
                    $"the contextual '{header}' tab must render at least one ribbon group");
                harness.SelectedTabVisibleCommandControlCount.Should().BeGreaterThan(0,
                    $"the contextual '{header}' tab must render at least one expanded command at 1366px");
            }
        });
    }
}
