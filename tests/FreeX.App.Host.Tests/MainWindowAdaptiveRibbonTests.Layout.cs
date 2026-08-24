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

            // The declarative ribbon lays the Page Setup commands out directly (no legacy dense UniformGrid),
            // but the meaningful invariant survives: the commands render in Excel left-to-right reading order.
            harness.ActiveRibbonGroupVisibleCommandLabels("Page Setup")
                .Should()
                .ContainInOrder(
                    "Margins",
                    "Page Orientation",
                    "Paper Size",
                    "Print Area",
                    "Breaks",
                    "Background",
                    "Print Titles",
                    "Page Setup");
        });
    }

    [Fact]
    public void RibbonTabs_CollapseLowerPriorityGroupsInExcelOrderAcrossCommonWidths()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            // Live group identities (declarative metadata) and the 2-state collapse priority: each tab keeps
            // its highest-priority group expanded and folds its lowest-priority group(s) into overflow
            // buttons first. Expectations assert the reliable ends of that order (highest expanded, lowest
            // collapsed) so they hold across the live adaptive cutoffs.
            var expectations = new[]
            {
                new RibbonFallbackExpectation("Insert", 900, Expanded: ["Tables"], Collapsed: ["Symbols"]),
                new RibbonFallbackExpectation("Data", 1120, Expanded: ["Get Transform", "Sort Filter"], Collapsed: ["Outline"]),
                new RibbonFallbackExpectation("Page Layout", 1120, Expanded: ["Page Setup"], Collapsed: ["Sheet Options"]),
                new RibbonFallbackExpectation("View", 900, Expanded: ["Workbook Views", "Show", "Zoom"], Collapsed: ["Window"]),
                new RibbonFallbackExpectation("View", 750, Expanded: ["Workbook Views", "Show"], Collapsed: ["Window"])
            };

            foreach (var expectation in expectations)
            {
                harness.SelectRibbonTab(expectation.Tab, expectation.Width);
                if (!harness.CanUseRequestedRibbonWidth(expectation.Width))
                    continue;

                if (expectation.Expanded.Count > 0)
                {
                    harness.CollapsedActiveRibbonGroupNames.Should().NotContain(
                        expectation.Expanded,
                        $"{expectation.Tab} at {expectation.Width:0}px should keep requested primary groups expanded before lower-priority groups; {harness.DebugActiveRibbonChildren}");
                }
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
                ["Illustrations", "Format"],
                $"the Draw tab should keep its higher-priority object-creation and format groups expanded at 900px; {harness.DebugActiveRibbonChildren}");
            harness.ActiveRibbonGroupVisibleCommandLabels("Illustrations").Should().NotBeEmpty(
                $"the expanded Illustrations group should show its real commands; {harness.DebugActiveRibbonChildren}");
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

            // 2-state truth: Page Setup stays fully expanded with its commands laid inside the group row;
            // none of its command rows spill below the group-label strip (no clip behind the label).
            harness.ActiveRibbonGroupCommandOverflow("Page Setup").Should().BeLessThanOrEqualTo(
                0.5,
                "Excel lays out Page Setup as compact command rows instead of letting the command stack clip behind the group label");
            harness.VisibleRibbonCommandLabels.Should().Contain(
                ["Margins", "Page Orientation", "Paper Size", "Print Area", "Breaks", "Background", "Print Titles", "Page Setup"]);
        });
    }

    [Theory]
    [InlineData(1100)]
    public void PageLayoutRibbon_KeepsPageSetupExpandedAtNormalNarrowWidths(double width)
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("Page Layout", width);

            // At 1100px the live ribbon keeps the primary Page Setup group expanded and folds only
            // lower-priority groups (Themes, Scale To Fit, Sheet Options) into overflow buttons. The live
            // command captions are the full Excel names ("Page Orientation"/"Paper Size").
            harness.CollapsedActiveRibbonGroupNames.Should().NotContain(
                "Page Setup",
                "Excel keeps the primary Page Setup commands directly reachable at 1100px");
            harness.VisibleRibbonCommandLabels.Should().Contain(
                ["Margins", "Page Orientation", "Paper Size", "Page Setup"],
                "Page Layout should collapse lower-priority groups before the primary Page Setup group");
            harness.ActiveRibbonGroupCommandOverflow("Page Setup").Should().BeLessThanOrEqualTo(
                0.5,
                "Page Setup should keep all command rows above the group-label strip at 1100px");
        });
    }

    [Fact]
    public void PageLayoutRibbon_KeepsPageSetupExpandedAt900()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("Page Layout", 900);
            if (!harness.CanUseRequestedRibbonWidth(900))
                return;

            harness.CollapsedActiveRibbonGroupNames.Should().NotContain(
                "Page Setup",
                $"Page Layout should spend available 900px row width on the primary Page Setup group before collapsing it; {harness.DebugActiveRibbonChildren}");
            harness.VisibleRibbonCommandLabels.Should().Contain(
                ["Margins", "Page Orientation", "Paper Size", "Page Setup"],
                $"Page Setup commands should remain directly reachable at 900px; {harness.DebugActiveRibbonChildren}");
            harness.ActiveRibbonPanelOverflow.Should().BeLessThanOrEqualTo(
                0.5,
                $"Page Layout at 900px must not clip its right edge; {harness.DebugActiveRibbonChildren}");
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
                    stack => stack.Offsets.Max() - stack.Offsets.Min() <= 2.0,
                    $"{tab} vertical command stacks should put small command icons directly above one another");

                harness.DirectVerticalButtonStackIconOffsets.Should().OnlyContain(
                    stack => stack.Offsets.Max() - stack.Offsets.Min() <= 2.0,
                    $"{tab} direct XAML vertical button stacks should align small command icons in a fixed column");

                harness.StackedRibbonRowColumnIconOffsets.Should().OnlyContain(
                    stack => stack.Offsets.Max() - stack.Offsets.Min() <= 2.0,
                    $"{tab} stacked small-command rows should align each icon column across rows");

                harness.GridRibbonColumnIconOffsets.Should().OnlyContain(
                    stack => stack.Offsets.Max() - stack.Offsets.Min() <= 2.0,
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
    public void ViewRibbon_WorkbookViewButtonsAreMutuallyExclusive()
    {
        // The three workbook views stay mutually exclusive because all three checked flags come
        // from one WorksheetViewModeUiState. That still holds; the pieces just moved. The status
        // buttons are still set together here, the planner call now happens at the viewport that
        // owns the per-window view mode, and the ribbon toggles are published by the shared
        // WorkbookViewRibbonStatePlanner both renderers consume.
        var source = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Host/MainWindow.ViewCommands.cs");
        var viewport = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Host/MainWindow.Viewport.cs");
        var planner = WorkspaceFileLocator.ReadAllText(
            "src",
            "FreeX.App.Presentation/Ribbon/WorkbookViewRibbonStatePlanner.cs");

        source.Should().Contain("StatusNormalViewButton.IsChecked = state.NormalChecked;");
        source.Should().Contain("StatusPageLayoutViewButton.IsChecked = state.PageLayoutChecked;");
        source.Should().Contain("StatusPageBreakPreviewButton.IsChecked = state.PageBreakPreviewChecked;");
        viewport.Should().Contain(
            "SyncStatusViewShortcutState(WorksheetViewModeUiStatePlanner.Build(viewState.ViewMode));");
        planner.Should().Contain("\"Normal\" => new RibbonCommandState(IsChecked: NormalChecked),");
        planner.Should().Contain("\"Page Layout\" => new RibbonCommandState(IsChecked: PageLayoutChecked),");
        planner.Should().Contain(
            "\"Page Break Preview\" => new RibbonCommandState(IsChecked: PageBreakPreviewChecked),");
    }

    [Fact]
    public void ViewRibbon_WorkbookViewButtonsRefreshFromStatusViewShortcuts()
    {
        var source = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Host/MainWindow.ViewCommands.cs");
        var xaml = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Host/MainWindow.xaml");

        source.Should().Contain("private void NormalViewBtn_Click(object sender, RoutedEventArgs e) =>");
        source.Should().Contain("SetWorksheetViewMode(WorksheetViewMode.Normal);");
        source.Should().Contain("private void PageBreakPreviewBtn_Click(object sender, RoutedEventArgs e) =>");
        source.Should().Contain("SetWorksheetViewMode(WorksheetViewMode.PageBreakPreview);");
        source.Should().Contain("private void PageLayoutViewBtn_Click(object sender, RoutedEventArgs e) =>");
        source.Should().Contain("SetWorksheetViewMode(WorksheetViewMode.PageLayout);");
        xaml.Should().Contain("x:Name=\"StatusNormalViewButton\"");
        xaml.Should().Contain("Click=\"NormalViewBtn_Click\"");
        xaml.Should().Contain("x:Name=\"StatusPageLayoutViewButton\"");
        xaml.Should().Contain("Click=\"PageLayoutViewBtn_Click\"");
        xaml.Should().Contain("x:Name=\"StatusPageBreakPreviewButton\"");
        xaml.Should().Contain("Click=\"PageBreakPreviewBtn_Click\"");
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
                ["Help Online", "Feedback", "Copy Diagnostics", "Test Crash Reporting", "Check for Updates", "About FreeX", "Legal Notices"],
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
            collapsedGroups.Should().Contain(
                ["Defined Names", "Formula Auditing", "Calculation"],
                $"Formulas should preserve its primary surface before collapsing the lower-priority groups at 750px; {harness.DebugActiveRibbonChildren}");
            harness.CollapsedActiveRibbonGroupsWithoutOverflowMenu.Should().BeEmpty(
                $"every collapsed Formulas group must still open its commands from its overflow button; {harness.DebugActiveRibbonChildren}");
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
