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
                    harness.SelectedTabVisibleCommandControlCount.Should().BeGreaterThan(0,
                        $"the '{header}' tab must render expanded command controls at its widest width {width:0}");
                    widestReachable = false;
                }
            }

            resolutionsExercised.Should().BeGreaterThan(0,
                $"the '{header}' tab should have been verified at at least one reachable resolution");
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
