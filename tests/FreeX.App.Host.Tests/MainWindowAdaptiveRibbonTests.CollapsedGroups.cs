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
    [Fact]
    public void CollapsedRibbonGroupKeyTips_AreUniqueWithinSelectedTab()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            foreach (var tab in new[] { "Home", "Insert", "Draw", "Page Layout", "Formulas", "Data", "Review", "View", "Help" })
            {
                harness.SelectRibbonTab(tab, 220);

                harness.CollapsedActiveRibbonGroupKeyTips
                    .GroupBy(pair => pair.KeyTip, StringComparer.OrdinalIgnoreCase)
                    .Where(group => group.Count() > 1)
                    .Should()
                    .BeEmpty($"{tab} collapsed group keytips should remain routable without duplicate generated group badges");
            }
        });
    }

    [Fact]
    public void CollapsedRibbonGroupButtons_KeepKeyTipsWithinSelectedTab()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            foreach (var tab in new[] { "Home", "Insert", "Draw", "Page Layout", "Formulas", "Data", "Review", "View", "Help" })
            {
                harness.SelectRibbonTab(tab, 220);

                harness.CollapsedActiveRibbonGroupsWithoutKeyTips.Should().BeEmpty(
                    $"{tab} collapsed group buttons should remain reachable through command-scope keytips after adaptive layout changes");
            }
        });
    }

    [Fact]
    public void CollapsedRibbonGroups_ShowGroupCaptionsAtNormalNarrowWidths()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("Review", 900);

            harness.CollapsedActiveRibbonGroupNames.Should().Contain(["Notes", "Protect"], harness.DebugActiveRibbonChildren);
            harness.CollapsedActiveRibbonGroupVisibleLabels.Should().Contain(
                ["Notes", "Protect"],
                "Excel keeps collapsed group captions visible at common 900px workbook widths so icon-only fallbacks remain identifiable");
        });
    }

    [Fact]
    public void CollapsedRibbonGroups_TrimCompactCaptionsInsteadOfWrappingMidWord()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("Home", 900);

            // 2-state truth: each lower-priority Home group that folds to an overflow button shows its full
            // group name as a single-line caption rather than breaking the word across two uneven lines.
            // (Asserting the displayed caption equals the complete group name proves it was neither wrapped
            // mid-word nor truncated.)
            var collapsed = harness.CollapsedActiveRibbonGroupNames;
            collapsed.Should().NotBeEmpty(
                "the Home tab should collapse at least one lower-priority group at 900px to exercise caption trimming");
            harness.CollapsedActiveRibbonGroupVisibleLabels.Should().Contain(
                collapsed,
                $"every collapsed Home group should show its full group-name caption on one line; {harness.DebugActiveRibbonChildren}");
        });
    }

    [Fact]
    public void RibbonGroupMetadata_IsSeededForEveryVisibleRibbonTab()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            foreach (var tab in new[] { "Home", "Insert", "Draw", "Page Layout", "Formulas", "Data", "Review", "View", "Help" })
            {
                harness.SelectRibbonTab(tab, 1100);

                harness.ActiveRibbonGroupNames.Should().NotBeEmpty($"{tab} should expose metadata-backed ribbon groups");
                harness.ActiveRibbonGroupNames.Should().OnlyContain(
                    name => !string.IsNullOrWhiteSpace(name) && !string.Equals(name, "Commands", StringComparison.Ordinal),
                    $"{tab} group names should be seeded from the existing group captions before adaptive layout runs");
            }
        });
    }

    [Fact]
    public void ContextualRibbonTabs_SeedGroupMetadataAndCollapsedKeyTips()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.ShowDrawingObjectContextualTabs();
            harness.ShowChartContextualTabs();
            harness.ShowPivotContextualTabs();

            foreach (var tab in new[] { "Shape Format", "Picture Format", "Chart Design", "Format", "PivotTable Analyze", "Design" })
            {
                harness.SelectRibbonTab(tab, 1100);

                harness.ActiveRibbonGroupNames.Should().NotBeEmpty($"{tab} should participate in static ribbon normalization when contextual tabs become visible");
                harness.ActiveRibbonGroupNames.Should().OnlyContain(
                    name => !string.IsNullOrWhiteSpace(name) && !string.Equals(name, "Commands", StringComparison.Ordinal),
                    $"{tab} group names should be seeded from contextual group captions before adaptive layout runs");

                harness.SelectRibbonTab(tab, 220);

                harness.CollapsedActiveRibbonGroupsWithoutKeyTips.Should().BeEmpty(
                    $"{tab} collapsed contextual groups should remain reachable through command-scope keytips");
                harness.CollapsedActiveRibbonGroupKeyTips
                    .GroupBy(pair => pair.KeyTip, StringComparer.OrdinalIgnoreCase)
                    .Where(group => group.Count() > 1)
                    .Should()
                    .BeEmpty($"{tab} collapsed contextual group keytips should remain unique within the selected tab");
            }
        });
    }

    // Contextual ribbon tabs participate in the same adaptive group-collapse engine as the main tabs:
    // narrow screenshot-tour widths should fold groups into overflow buttons instead of clipping the
    // right edge or surfacing a horizontal scrollbar.
    [Fact]
    public void ContextualRibbonTabs_FitWithoutVisibleScrollBarsAtScreenshotTourWidths()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.ShowDrawingObjectContextualTabs();
            harness.ShowChartContextualTabs();
            harness.ShowTableDesignContextualTab();
            harness.ShowPivotContextualTabs();

            foreach (var tab in new[] { "Shape Format", "Picture Format", "Chart Design", "Format", "Table Design", "PivotTable Analyze", "Design" })
            {
                foreach (var width in new[] { 1100.0, 900.0, 750.0 })
                {
                    harness.SelectRibbonTab(tab, width);
                    if (!harness.CanUseRequestedRibbonWidth(width))
                        continue;

                    // The 2-state ribbon collapses non-contextual groups to fit instead of scrolling, so the
                    // adaptive panel has no horizontal scroll surface (mode is null) -- and certainly never a
                    // visible horizontal scrollbar. This invariant holds at every contextual width.
                    (harness.ActiveRibbonHorizontalScrollBarMode is null or ScrollBarVisibility.Hidden).Should().BeTrue(
                        $"{tab} should never expose a horizontal ribbon scroller at the screenshot-tour width {width:0}px");
                    harness.ActiveRibbonVisibleHorizontalScrollBars.Should().BeEmpty(
                        $"{tab} should not expose a horizontal scrollbar at the screenshot-tour width {width:0}px");

                    harness.ActiveRibbonPanelOverflow.Should().BeLessThanOrEqualTo(
                        0.5,
                        $"{tab} at {width:0}px should fit its contextual commands without clipping; {harness.DebugActiveRibbonChildren}");
                }
            }
        });
    }

    [Fact]
    public void CollapsedRibbonGroupButtons_ShowDropdownGlyph()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            foreach (var tab in new[] { "Home", "Insert", "Draw", "Page Layout", "Formulas", "Data", "Review", "View", "Help" })
            {
                harness.SelectRibbonTab(tab, 220);

                // 2-state truth: every group folds to a single overflow button that advertises and opens its
                // commands. The dropdown affordance is the button's lazily-built overflow ContextMenu; this
                // asserts each collapsed group can actually be expanded from its overflow button.
                harness.CollapsedActiveRibbonGroupsWithoutOverflowMenu.Should().BeEmpty(
                    $"{tab} collapsed group buttons should open an overflow menu of their commands like Excel");
            }
        });
    }

}
