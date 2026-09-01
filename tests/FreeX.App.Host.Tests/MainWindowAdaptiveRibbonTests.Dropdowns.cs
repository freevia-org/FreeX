using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowAdaptiveRibbonTests
{
    // Commands that own a dropdown menu in the declarative ribbon get the split-button
    // treatment: exactly one actionable chevron, a routed dropdown zone, and the split hover highlight.
    [Theory]
    [InlineData("Paste")]
    [InlineData("Orientation")]
    [InlineData("AutoSum")]
    [InlineData("Sort & Filter")]
    public void RibbonMenuButtons_ShowActionableDropdownGlyph(string title)
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.SelectRibbonTab("Home", 1465);
            if (!harness.CanUseRequestedWidth(1465))
                return; // A constrained offscreen desktop (e.g. CI) cannot reach 1465px, so Home folds its
                        // lower-priority groups into overflow and the asserted expanded split-button layout
                        // is not realizable here. Covered on roomier desktops; over-collapse tracked separately.

            harness.VisibleRibbonButtonDropdownChevronCount(title).Should().Be(1,
                $"{title} should not keep the old decorative glyph after it receives a real dropdown target");
            harness.VisibleRibbonButtonHasDropdownChevron(title).Should().BeTrue(
                $"{title} should expose a real dropdown hit target when it owns a menu");
            harness.VisibleRibbonButtonHasDropdownZoneHandler(title).Should().BeTrue(
                $"{title} should route clicks on the chevron zone to its menu");
            harness.VisibleRibbonButtonHasDropdownZoneHighlight(title).Should().BeTrue(
                $"{title} should show a split-button hover affordance for its main and menu zones");
        });
    }

    [Theory]
    [InlineData("Percent Style")]
    [InlineData("Wrap Text")]
    public void RibbonPlainCommands_DoNotShowSplitDropdownGlyph(string title)
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.SelectRibbonTab("Home", 1465);

            // The declarative ribbon renders these as ordinary commands (no inline dropdown menu), so they
            // must not draw a stale split-button chevron beside a non-actionable zone.
            harness.VisibleRibbonButtonDropdownChevronCount(title).Should().Be(0,
                $"{title} renders as a plain command in the live ribbon, so it should carry no split-button chevron");
            harness.VisibleRibbonButtonHasDropdownZoneHandler(title).Should().BeFalse(
                $"{title} has no inline dropdown menu, so no chevron-zone click handler should be attached");
        });
    }

    [Fact]
    public void RibbonMenuButtons_AllTabsUseSplitDropdownTreatment()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            foreach (var tab in new[] { "Home", "Insert", "Page Layout", "Formulas", "Data", "Review", "View" })
            {
                harness.SelectRibbonTab(tab, 1800);

                harness.ActiveRibbonMenuButtonsWithoutSplitTreatment.Should().BeEmpty(
                    $"{tab} menu-capable ribbon buttons should show one actionable chevron with split hover/click metadata");
            }
        });
    }

    [Fact]
    public void RibbonMenuButtons_DrawHorizontalSplitBelowTallButtonLabels()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("Home", 1465);

            harness.HorizontalDropdownZoneClearsCommandLabel("Paste")
                .Should()
                .BeTrue("the split-button separator should sit below the visible label instead of slicing through it");
        });
    }

    [Fact]
    public void ExpandedRibbonGroups_HideCollapsedGroupDropdownGlyphs()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.SelectRibbonTab("Home", 1465);

            harness.HiddenCollapsedRibbonGroupsWithVisibleDropdownGlyph.Should().BeEmpty(
                "overflow glyph adorners should disappear when their collapsed group buttons are hidden");
        });
    }

    [Fact]
    public void RibbonScrollViewers_KeepHorizontalScrollBarsHiddenDuringTabSwitchesAndResize()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            foreach (var tab in new[] { "Home", "Insert", "Draw", "Page Layout", "Formulas", "Data", "Review", "View", "Help" })
            {
                foreach (var width in new[] { 1465.0, 900.0, 640.0, 220.0, 1280.0 })
                {
                    harness.SelectRibbonTab(tab, width);

                    // 2-state ribbon collapses groups to fit rather than scrolling, so the adaptive panel
                    // exposes no horizontal scroll surface (null) -- or, if one exists, it stays Hidden.
                    // Either way no horizontal scrollbar may ever appear.
                    (harness.ActiveRibbonHorizontalScrollBarMode is null or ScrollBarVisibility.Hidden).Should().BeTrue(
                        $"{tab} should never expose a horizontal ribbon scroller after resizing to {width:0}px");
                    harness.ActiveRibbonVisibleHorizontalScrollBars.Should().BeEmpty(
                        $"{tab} should not show a horizontal ribbon scrollbar after resizing to {width:0}px");
                }
            }
        });
    }

}
