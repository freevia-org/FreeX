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
    public void DenseRibbonCommandColumns_UseShortRowButtons()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            foreach (var tab in new[] { "Page Layout", "Formulas", "Data", "Review", "View", "Help" })
            {
                harness.SelectRibbonTab(tab, 1465);

                harness.DenseColumnButtonHeights.Should().OnlyContain(
                    height => height <= 24,
                    $"{tab} dense ribbon columns should use Excel-like short row commands instead of tall large-button footprints");
            }
        });
    }

    [Fact]
    public void DenseRibbonCommandColumns_PreserveExcelReadingOrderWithinRows()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("Page Layout", 1465);
            harness.ActiveRibbonGroupDenseCommandPlacements("Page Setup")
                .Should()
                .ContainInOrder(
                    new DenseCommandPlacement("Margins", 0, 0),
                    new DenseCommandPlacement("Orientation", 0, 1),
                    new DenseCommandPlacement("Size", 0, 2),
                    new DenseCommandPlacement("Print Area", 1, 0),
                    new DenseCommandPlacement("Breaks", 1, 1),
                    new DenseCommandPlacement("Background", 1, 2),
                    new DenseCommandPlacement("Print Titles", 2, 0));
        });
    }

    [Fact]
    public void RibbonTabs_CollapseLowerPriorityGroupsInExcelOrderAcrossCommonWidths()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            var expectations = new[]
            {
                new RibbonFallbackExpectation("Insert", 900, Expanded: ["Tables"], Collapsed: ["Charts"]),
                new RibbonFallbackExpectation("Data", 1120, Expanded: ["Data Tools", "Forecast"], Collapsed: ["Sort & Filter"]),
                new RibbonFallbackExpectation("Page Layout", 1120, Expanded: ["Themes", "Page Setup", "Arrange"], Collapsed: []),
                new RibbonFallbackExpectation("View", 900, Expanded: ["Workbook Views", "Show", "Zoom"], Collapsed: ["Window"]),
                new RibbonFallbackExpectation("View", 750, Expanded: ["Workbook Views", "Show", "Zoom"], Collapsed: ["Window"])
            };

            foreach (var expectation in expectations)
            {
                harness.SelectRibbonTab(expectation.Tab, expectation.Width);
                if (!harness.CanUseRequestedRibbonWidth(expectation.Width))
                    continue;

                harness.CollapsedActiveRibbonGroupNames.Should().NotContain(
                    expectation.Expanded,
                    $"{expectation.Tab} at {expectation.Width:0}px should keep Excel-style primary groups expanded before lower-priority groups; {harness.DebugActiveRibbonChildren}");
                if (expectation.Collapsed.Count > 0)
                {
                    harness.CollapsedActiveRibbonGroupNames.Should().Contain(
                        expectation.Collapsed,
                        $"{expectation.Tab} at {expectation.Width:0}px should collapse lower-priority groups first; {harness.DebugActiveRibbonChildren}");
                }
                harness.ActiveRibbonPanelOverflow.Should().BeLessThanOrEqualTo(
                    0.5,
                    $"{expectation.Tab} at {expectation.Width:0}px should fit without a hidden-scroll overflow after fallback ordering; {harness.DebugActiveRibbonChildren}");
            }
        });
    }

    [Fact]
    public void DrawRibbon_KeepsCurrentGroupsExpandedWhenTheyFit()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("Draw", 900);

            harness.CollapsedActiveRibbonGroupNames.Should().NotContain(
                ["Arrange", "Format"],
                $"the current Draw tab has only arrange/format command groups, so normal widths should spend available space on the real commands; {harness.DebugActiveRibbonChildren}");
            harness.ActiveRibbonPanelOverflow.Should().BeLessThanOrEqualTo(
                0.5,
                $"Draw at 900px should fit without hidden-scroll overflow; {harness.DebugActiveRibbonChildren}");
        });
    }

    [Fact]
    public void PageLayoutPageSetup_KeepsCommandsInsideRibbonRow()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("Page Layout", 1465);

            harness.ActiveRibbonGroupCommandOverflow("Page Setup").Should().BeLessThanOrEqualTo(
                0.5,
                "Excel lays out Page Setup as compact command rows instead of letting the command stack clip behind the group label");
            harness.ActiveRibbonGroupDenseCommandRows("Page Setup").Should().Contain(
                3,
                "Excel-like Page Setup commands should use three short rows, not one tall vertical stack that clips");
            harness.VisibleRibbonCommandLabels.Should().Contain(
                ["Margins", "Orientation", "Size", "Print Area", "Breaks", "Background", "Print Titles"]);
        });
    }

    [Theory]
    [InlineData(900)]
    [InlineData(1100)]
    public void PageLayoutRibbon_KeepsPageSetupExpandedAtNormalNarrowWidths(double width)
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("Page Layout", width);

            harness.CollapsedActiveRibbonGroupNames.Should().NotContain(
                "Page Setup",
                "Excel keeps the primary Page Setup commands directly reachable at normal narrow widths");
            harness.VisibleRibbonCommandLabels.Should().Contain(
                ["Margins", "Orientation", "Size"],
                "Page Layout should collapse lower-priority groups before the primary Page Setup group");
            harness.ActiveRibbonGroupCommandOverflow("Page Setup").Should().BeLessThanOrEqualTo(
                0.5,
                "Page Setup should keep all command rows above the group-label strip at normal narrow widths");
        });
    }

    [Fact]
    public void VerticallyStackedRibbonCommands_AlignIconSlots()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            foreach (var tab in new[] { "Home", "Insert", "Page Layout", "Formulas", "Data", "Review", "View", "Help" })
            {
                harness.SelectRibbonTab(tab, 1465);

                harness.VerticallyStackedRibbonIconOffsets.Should().OnlyContain(
                    stack => stack.Offsets.Max() - stack.Offsets.Min() <= 1.0,
                    $"{tab} vertical command stacks should put small command icons directly above one another");

                harness.DirectVerticalButtonStackIconOffsets.Should().OnlyContain(
                    stack => stack.Offsets.Max() - stack.Offsets.Min() <= 1.0,
                    $"{tab} direct XAML vertical button stacks should align small command icons in a fixed column");

                harness.StackedRibbonRowColumnIconOffsets.Should().OnlyContain(
                    stack => stack.Offsets.Max() - stack.Offsets.Min() <= 1.0,
                    $"{tab} stacked small-command rows should align each icon column across rows");

                harness.GridRibbonColumnIconOffsets.Should().OnlyContain(
                    stack => stack.Offsets.Max() - stack.Offsets.Min() <= 1.0,
                    $"{tab} grid-based small-command columns should align icons vertically inside each column");
            }
        });
    }

    [Fact]
    public void ViewRibbon_ShowCheckBoxLabelsShareLeftEdge()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("View", 2200);

            harness.ViewShowCheckBoxLabelOffsets
                .Select(offset => Math.Round(offset.Offset, 1))
                .Distinct()
                .Should()
                .HaveCount(1, "Excel keeps View tab checkbox labels in one tidy left-aligned column after the checkbox glyphs");

            harness.ViewShowCheckBoxContentAlignments.Should().OnlyContain(
                alignment => alignment == System.Windows.HorizontalAlignment.Left,
                "ribbon checkbox rows should not center short labels inside the widest checkbox row");
        });
    }

    [Fact]
    public void ViewRibbon_RulerCheckBoxIsEnabledOnlyInPageLayoutView()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("View", 1465);

            harness.ViewRulerCheckBoxIsEnabled.Should().BeFalse("Excel disables Ruler outside Page Layout view");

            harness.ClickActiveRibbonButton("Page Layout");

            harness.ViewRulerCheckBoxIsEnabled.Should().BeTrue("Excel enables Ruler in Page Layout view");

            harness.ClickActiveRibbonButton("Normal");

            harness.ViewRulerCheckBoxIsEnabled.Should().BeFalse("returning to Normal view should disable Ruler again");
        });
    }

    [Fact]
    public void RibbonScrollViewers_HideHorizontalScrollBarsWithoutDisablingFallbackScroll()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            foreach (var tab in new[] { "Home", "Insert", "Draw", "Page Layout", "Formulas", "Data", "Review", "View", "Help" })
            {
                harness.SelectRibbonTab(tab, 640);

                harness.RibbonHorizontalScrollBarModes.Should().OnlyContain(
                    mode => mode == ScrollBarVisibility.Hidden,
                    $"{tab} should keep the ribbon face clean while preserving hidden horizontal fallback scrolling");
            }
        });
    }

    [Theory]
    [InlineData(750)]
    [InlineData(900)]
    [InlineData(1100)]
    public void HelpRibbon_DoesNotClipAtExcelWidths(double width)
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("Help", width);

            harness.ActiveRibbonPanelOverflow.Should().BeLessThanOrEqualTo(
                0.5,
                $"Help at {width}px should fit without exposing hidden horizontal scroll; {harness.DebugActiveRibbonChildren}");
            harness.VisibleRibbonCommandLabels.Should().Contain(
                ["Help Online", "Feedback", "Copy Diagnostics", "Check for Updates", "About FreeX", "Legal Notices"],
                "the enabled Help commands should remain directly usable at common Excel widths");
            harness.CollapsedActiveRibbonGroupNames.Should().NotContain("Help", harness.DebugActiveRibbonChildren);
        });
    }

    [Fact]
    public void FormulasRibbon_SpendsAvailableSpaceAtNarrowExcelWidth()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("Formulas", 750);
            if (!harness.CanUseRequestedRibbonWidth(750))
                return;

            var collapsedGroups = harness.CollapsedActiveRibbonGroupNames;
            collapsedGroups.Count.Should().BeLessThan(
                4,
                $"Formulas at 750px should reopen at least one group instead of showing only group buttons beside empty space; {harness.DebugActiveRibbonChildren}");
            if (collapsedGroups.Count > 0)
            {
                harness.ActiveRibbonPanelUnusedWidth.Should().BeLessThan(
                    120,
                    $"Formulas at 750px should spend the row width when collapsed group buttons still remain; {harness.DebugActiveRibbonChildren}");
            }
            harness.ActiveRibbonPanelOverflow.Should().BeLessThanOrEqualTo(
                0.5,
                $"Formulas at 750px should still fit without hidden horizontal overflow; {harness.DebugActiveRibbonChildren}");
        });
    }

    [Theory]
    [InlineData(900)]
    [InlineData(1120)]
    [InlineData(1280)]
    [InlineData(1366)]
    [InlineData(1465)]
    public void RibbonTabs_DoNotClipActiveCommandRowAtCommonExcelWidths(double width)
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            foreach (var tab in new[] { "Home", "Insert", "Draw", "Page Layout", "Formulas", "Data", "Review", "View", "Help" })
            {
                harness.SelectRibbonTab(tab, width);
                if (!harness.CanUseRequestedRibbonWidth(width))
                    continue;

                harness.ActiveRibbonPanelOverflow.Should().BeLessThanOrEqualTo(
                    0.5,
                    $"{tab} at {width}px should collapse groups before the hidden ribbon scroll surface clips visible commands; {harness.DebugActiveRibbonChildren}");
            }
        });
    }

}
