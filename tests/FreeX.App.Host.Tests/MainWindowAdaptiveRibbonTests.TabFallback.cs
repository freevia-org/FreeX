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
    public void InsertRibbon_HidesChartFormattingCommands()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("Insert", 800);

            harness.VisibleRibbonCommandLabels.Should().NotContain("Label Border", harness.DebugActiveRibbonChildren);
            harness.VisibleRibbonCommandLabels.Should().NotContain("Y Bounds", harness.DebugActiveRibbonChildren);
            harness.CollapsedActiveRibbonGroupNames.Should().Contain("Charts", harness.DebugActiveRibbonChildren);
            harness.CollapsedActiveMenuHeaders("Charts").Should().Contain("Column Chart", harness.DebugActiveRibbonChildren);
            harness.CollapsedActiveMenuHeaders("Charts").Should().NotContain("Data Label Border", harness.DebugActiveRibbonChildren);
        });
    }

    [Fact]
    public void InsertRibbon_KeepsTablesExpandedAtNormalNarrowWidths()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("Insert", 900);

            harness.CollapsedActiveRibbonGroupNames.Should().NotContain("Tables", harness.DebugActiveRibbonChildren);
            harness.VisibleRibbonCommandLabels.Should().Contain(
                ["PivotTable", "Recommended PivotTables", "Table"],
                "Excel keeps the first Insert groups expanded at normal narrow widths before collapsing gallery-heavy groups");
        });
    }

    [Theory]
    [InlineData("Formulas", "Function Library")]
    [InlineData("Data", "Get & Transform Data")]
    [InlineData("Review", "Proofing")]
    [InlineData("View", "Workbook Views")]
    public void RibbonTabs_KeepPrimaryGroupExpandedAtNormalNarrowWidths(string tab, string primaryGroup)
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab(tab, 900);

            harness.CollapsedActiveRibbonGroupNames.Should().NotContain(
                primaryGroup,
                $"{tab} should collapse lower-priority groups before the first Excel-style primary group at normal narrow widths");
        });
    }

    [Fact]
    public void DataRibbon_CollapsesSortFilterBeforePromotedDataToolsAtMediumWidths()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("Data", 1120);

            harness.CollapsedActiveRibbonGroupNames.Should().Contain(
                "Sort & Filter",
                "Data should keep promoted standalone Data Tools and Forecast commands readable before the dense Sort & Filter cluster at medium widths");
            harness.CollapsedActiveRibbonGroupNames.Should().NotContain(
                ["Data Tools", "Forecast"],
                $"Data should keep promoted standalone command groups expanded at medium widths; {harness.DebugActiveRibbonChildren}");
            harness.ActiveRibbonPanelOverflow.Should().BeLessThanOrEqualTo(
                0.5,
                $"Data should collapse Sort & Filter before the ribbon clips; {harness.DebugActiveRibbonChildren}");
        });
    }

    [Fact]
    public void DataRibbon_KeepsDataToolsAndForecastVisibleAtMediumWidths()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("Data", 1120);

            harness.CollapsedActiveRibbonGroupNames.Should().NotContain("Data Tools", harness.DebugActiveRibbonChildren);
            harness.CollapsedActiveRibbonGroupNames.Should().NotContain("Forecast", harness.DebugActiveRibbonChildren);
            harness.VisibleRibbonCommandLabels.Should().Contain(
                ["Data Validation", "What-If Analysis", "Forecast Sheet"],
                "Excel keeps the medium-priority Data Tools and Forecast affordances visible around 1120px");
        });
    }

    [Theory]
    [InlineData(900)]
    [InlineData(1100)]
    public void DataRibbon_DataToolsCommandLabelsDoNotClipAtNormalNarrowWidths(double width)
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("Data", width);

            harness.ActiveRibbonGroupClippedCommandLabels("Data Tools").Should().BeEmpty(
                "Excel keeps the visible Data Tools command labels readable instead of clipping names such as Remove Duplicates");
        });
    }

    [Fact]
    public void DataRibbon_DataToolsCommandsUseIconLabelRows()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("Data", 1465);

            harness.ActiveRibbonGroupCommandLabelsWithoutIconSlots("Data Tools").Should().BeEmpty(
                "Excel presents Data Tools as compact icon-and-label commands, not plain text-only buttons");
        });
    }

    [Fact]
    public void DataAndHelpRibbon_PromoteStandaloneCommandsToTallIconLabelsWhenSpaceAllows()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("Data", 1465);
            if (harness.CanUseRequestedRibbonWidth(1465))
            {
                harness.TallLargeRibbonCommandLabels.Should().Contain(
                    ["Get Data", "Refresh All", "Text to Columns", "Flash Fill", "Remove Duplicates", "Data Validation", "Consolidate", "What-If Analysis", "Forecast Sheet"],
                    $"Data should use large icon-label tiles for standalone commands when there is room; {harness.DebugActiveRibbonChildren}");
                harness.ActiveRibbonPanelOverflow.Should().BeLessThanOrEqualTo(
                    0.5,
                    $"Data should promote standalone commands without hidden overflow; {harness.DebugActiveRibbonChildren}");
            }

            harness.SelectRibbonTab("Data", 1100);
            if (harness.CanUseRequestedRibbonWidth(1100))
            {
                harness.TallLargeRibbonCommandLabels.Should().Contain(
                    ["Text to Columns", "Flash Fill", "Remove Duplicates", "Data Validation", "Consolidate"],
                    $"Data Tools should keep tall icon-label tiles at medium desktop widths instead of compact horizontal rows; {harness.DebugActiveRibbonChildren}");
                harness.ActiveRibbonPanelOverflow.Should().BeLessThanOrEqualTo(
                    0.5,
                    $"Data should keep Data Tools expanded without hidden overflow at 1100px; {harness.DebugActiveRibbonChildren}");
            }

            harness.SelectRibbonTab("Help", 900);
            if (harness.CanUseRequestedRibbonWidth(900))
            {
                harness.TallLargeRibbonCommandLabels.Should().Contain(
                    ["Help Online", "Feedback", "Copy Diagnostics", "Check for Updates", "About FreeX", "Legal Notices"],
                    $"Help should use large icon-label tiles instead of stacked small rows when the row has room; {harness.DebugActiveRibbonChildren}");
                harness.ActiveRibbonPanelOverflow.Should().BeLessThanOrEqualTo(
                    0.5,
                    $"Help should promote standalone commands without hidden overflow; {harness.DebugActiveRibbonChildren}");
            }
        });
    }

    [Fact]
    public void ViewRibbon_KeepsShowWithZoomAndWindowAtMediumWidths()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("View", 1366);

            harness.CollapsedActiveRibbonGroupNames.Should().NotContain("Show", harness.DebugActiveRibbonChildren);
            harness.CollapsedActiveRibbonGroupNames.Should().NotContain("Zoom", harness.DebugActiveRibbonChildren);
            harness.VisibleViewShowCheckBoxLabels.Should().Contain(
                ["Gridlines", "Headings", "Formula Bar"],
                "Excel keeps the Show checkbox group visible at medium workbook widths before collapsing lower-priority view groups");
        });
    }

    [Theory]
    [InlineData(900)]
    [InlineData(1100)]
    public void ViewRibbon_WorkbookViewsCommandLabelsDoNotClipAtMediumWidths(double width)
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("View", width);

            harness.ActiveRibbonGroupClippedCommandLabels("Workbook Views").Should().BeEmpty(
                "Excel keeps the primary View commands readable instead of truncating short labels such as Normal");
        });
    }

    [Fact]
    public void ReviewAndViewRibbon_PromotePrimaryCommandsToTallIconLabelsAtWideTourWidth()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("Review", 1100);
            if (harness.CanUseRequestedRibbonWidth(1100))
            {
                harness.TallLargeRibbonCommandLabels.Should().Contain(
                    ["Spelling", "Workbook Statistics", "Accessibility"],
                    $"Review at 1100px should spend available width on primary Excel-style tall commands; {harness.DebugActiveRibbonChildren}");
                harness.ActiveRibbonPanelOverflow.Should().BeLessThanOrEqualTo(
                    0.5,
                    $"Review at 1100px should promote primary commands without hidden overflow; {harness.DebugActiveRibbonChildren}");
            }

            harness.SelectRibbonTab("View", 1100);
            if (harness.CanUseRequestedRibbonWidth(1100))
            {
                harness.TallLargeRibbonCommandLabels.Should().Contain(
                    ["Normal", "Page Break Preview", "Page Layout", "Custom Views", "Zoom", "100%", "Zoom to Selection"],
                    $"View at 1100px should use tall icon-label commands when the row has room; {harness.DebugActiveRibbonChildren}");
                harness.ActiveRibbonPanelOverflow.Should().BeLessThanOrEqualTo(
                    0.5,
                    $"View at 1100px should promote commands without hidden overflow; {harness.DebugActiveRibbonChildren}");
            }
        });
    }

    [Fact]
    public void PageLayoutAndFormulasRibbon_PromoteStandaloneCommandsToTallIconLabelsAtWideWidth()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("Page Layout", 1465);
            if (harness.CanUseRequestedRibbonWidth(1465))
            {
                harness.TallLargeRibbonCommandLabels.Should().Contain(
                    ["Themes", "Colors", "Fonts", "Effects", "Scale", "Bring Forward", "Send Backward", "Selection Pane", "Rotate", "Size"],
                    $"Page Layout should spend wide ribbon space on standalone large commands while keeping stacked Page Setup compact; {harness.DebugActiveRibbonChildren}");
                harness.ActiveRibbonPanelOverflow.Should().BeLessThanOrEqualTo(
                    0.5,
                    $"Page Layout should promote standalone commands without hidden overflow; {harness.DebugActiveRibbonChildren}");
            }

            harness.SelectRibbonTab("Formulas", 1465);
            if (harness.CanUseRequestedRibbonWidth(1465))
            {
                harness.TallLargeRibbonCommandLabels.Should().Contain(
                    ["Insert Function", "AutoSum", "Name Manager", "Define Name", "Use in Formula", "Create from Selection", "Calculate Now", "Calculate Sheet", "Calc Options"],
                    $"Formulas should use large icon-label commands for standalone ribbon actions before compacting dense stacks; {harness.DebugActiveRibbonChildren}");
                harness.ActiveRibbonPanelOverflow.Should().BeLessThanOrEqualTo(
                    0.5,
                    $"Formulas should promote standalone commands without hidden overflow; {harness.DebugActiveRibbonChildren}");
            }
        });
    }

    [Fact]
    public void RibbonTabs_RemainSingleRowAtNarrowWidths()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetRibbonWidth(640);

            harness.VisibleRibbonTabHeaderRows.Should().HaveCount(1, "Excel keeps the main ribbon tabs on one row while the command groups collapse");
        });
    }

    [Fact]
    public void RibbonStaticNormalization_DoesNotRecreateCommandContentAfterTabIsPrepared()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            foreach (var tab in new[] { "Home", "Insert", "Draw", "Page Layout", "Formulas", "Data", "Review", "View", "Help" })
            {
                harness.SelectRibbonTab(tab, 1100);
                var before = harness.VisibleRibbonButtonContentIdentityHashCodes;

                before.Should().NotBeEmpty($"{tab} should expose normalized ribbon command content");

                harness.NormalizeRibbonSurface();

                harness.VisibleRibbonButtonContentIdentityHashCodes.Should().Equal(
                    before,
                    $"{tab} static ribbon normalization should be a one-shot pass; resize and tab fallback compaction should reuse command content");
            }
        });
    }

    [Fact]
    public void RibbonGroupDiscovery_UsesMetadataRatherThanVisualShapeDuringResize()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            foreach (var tab in new[] { "Home", "Insert", "Draw", "Page Layout", "Formulas", "Data", "Review", "View", "Help" })
            {
                harness.SelectRibbonTab(tab, 1465);
                var expectedGroupOrder = harness.ActiveRibbonGroupNames;

                expectedGroupOrder.Should().NotBeEmpty($"{tab} should expose metadata-backed ribbon groups before resize");
                expectedGroupOrder.Should().OnlyContain(
                    name => !string.IsNullOrWhiteSpace(name) && !string.Equals(name, "Commands", StringComparison.Ordinal),
                    $"{tab} should resolve group identity from ribbon metadata, not from generic visual shape");

                var sawCollapsedGroups = false;
                foreach (var width in new[] { 900.0, 640.0, 220.0, 1280.0, 1465.0 })
                {
                    harness.SelectRibbonTab(tab, width);
                    sawCollapsedGroups |= harness.CollapsedActiveRibbonGroupNames.Count > 0;

                    harness.ActiveRibbonGroupNames.Should().Equal(
                        expectedGroupOrder,
                        $"{tab} metadata group order should stay stable after switching tabs and resizing to {width:0}px");
                    harness.ActiveRibbonPresentationGroupNames.Should().Equal(
                        expectedGroupOrder,
                        $"{tab} should present one visible group affordance per metadata group after resizing to {width:0}px");
                }

                sawCollapsedGroups.Should().BeTrue($"{tab} should exercise collapsed group discovery during the resize sweep");
            }
        });
    }

}
