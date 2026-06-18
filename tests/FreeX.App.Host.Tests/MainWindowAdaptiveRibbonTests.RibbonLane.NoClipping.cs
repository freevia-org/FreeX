using FluentAssertions;
using Xunit;

namespace FreeX.App.Host.Tests;

// Ribbon UI lane — no-clipping guard.
// Regression test for the reported "resizing the app clips some of the ribbon": the adaptive panel
// seeded each group's full width from its first (pre-icon-realization) measure and trusted that stale
// value, so it under-collapsed and the right-hand groups were cut off instead of folding into overflow
// buttons. At every width and on every tab the live arranged content must fit within the panel (groups
// collapse to fit), never overflow its right edge.
public sealed partial class MainWindowAdaptiveRibbonTests
{
    [Theory]
    [Trait("Category", "RibbonUiLane")]
    [MemberData(nameof(MainTabHeaderCases))]
    public void RibbonLane_MainTab_NeverClipsContentAtAnyResolution(string header)
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            var checkedAtLeastOnce = false;
            foreach (var width in RibbonResolutionWidths)
            {
                harness.SelectRibbonTab(header, width);
                if (!harness.CanUseRequestedWidth(width))
                    continue;

                checkedAtLeastOnce = true;
                // A couple of pixels of slack absorbs layout rounding; real clipping (a whole group hanging
                // off the edge) is tens to hundreds of pixels, which this catches.
                harness.SelectedTabRibbonRightOverflowPx.Should().BeLessThanOrEqualTo(2.0,
                    $"the '{header}' tab must collapse groups to fit width {width:0}, not clip its right edge");
            }

            checkedAtLeastOnce.Should().BeTrue($"the '{header}' tab should be checked at a reachable width");
        });
    }
}
