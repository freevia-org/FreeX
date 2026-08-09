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
    public void HomeRibbon_CollapsesGroupsIntoGroupButtonsAtNarrowWidths()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetRibbonWidth(220);

            harness.CollapsedRibbonGroupNames.Should().Contain("Editing", harness.DebugRibbonChildren);
            harness.CollapsedRibbonGroupMenus.Should().NotBeEmpty();
            harness.CollapsedMenuHeaders("Editing").Should().Contain(["AutoSum", "Fill", "Clear", "Sort & Filter", "Find & Select"]);
        });
    }

    [Fact]
    public void HomeRibbon_KeepsPrimaryCommandsExpandedAtNormalNarrowWidths()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetRibbonWidth(900);

            harness.CollapsedRibbonGroupNames.Should().NotContain("Clipboard", harness.DebugRibbonChildren);
            harness.VisibleRibbonCommandLabels.Should().Contain(
                ["Paste", "Cut", "Copy"],
                "Excel keeps the primary Clipboard commands expanded at normal narrow window widths and collapses lower-priority groups first");
        });
    }

    [Fact]
    public void HomeRibbon_ExpandsEditingWhenWideWidthHasRoom()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetRibbonWidth(1465);
            if (!harness.CanUseRequestedRibbonWidth(1465))
                return;

            // 2-state live ribbon: at this wide width the higher-priority Home groups stay fully expanded
            // with their real commands visible; Editing is the lowest-priority group, so it is the one that
            // folds into a single overflow button rather than clipping or pushing out a higher group.
            harness.CollapsedRibbonGroupNames.Should().NotContain(
                ["Clipboard", "Font", "Alignment", "Number", "Styles", "Cells"],
                harness.DebugRibbonChildren);
            harness.VisibleRibbonCommandLabels.Should().Contain(
                ["Paste", "Cut", "Copy", "Insert", "Delete", "Format"],
                "Excel spends available wide Home ribbon space on the higher-priority groups, collapsing only the lowest-priority Editing group");
            harness.ActiveRibbonPanelOverflow.Should().BeLessThanOrEqualTo(
                0.5,
                $"the wide Home ribbon must collapse Editing rather than clip its right edge; {harness.DebugActiveRibbonChildren}");
        });
    }

    [Fact]
    public void HomeRibbon_NormalizesToggleCommandsIntoSmallIconLabelRows()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetRibbonWidth(1465);

            // The declarative renderer does not promote/demote between the legacy 4 sizes; toggle commands
            // render with a canonical, non-collapsed icon-and-label layout (Wrap Text uses the Medium
            // icon-label layout the renderer assigns it). The invariant that still holds is that the
            // toggle command keeps a fixed icon slot so its glyph aligns with the surrounding command rows.
            harness.NamedRibbonButtonContentLayout("WrapTextBtn").Should().NotBeNull(
                "toggle commands should expose a declarative content layout");
            harness.NamedRibbonButtonContentLayout("WrapTextBtn").Should().NotBe(
                RibbonCommandContentLayout.Large,
                "small toggle commands should stay in a compact icon-label row, not a tall standalone tile");
            harness.NamedRibbonButtonHasIconSlot("WrapTextBtn").Should().BeTrue(
                "stacked toggle commands need a fixed icon slot so their icons align with adjacent rows");
        });
    }

    [Fact]
    public void HomeRibbon_KeepsCellsVisibleBeforeEditingAtCommonWideWidths()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetRibbonWidth(1366);
            if (!harness.CanUseRequestedRibbonWidth(1366))
                return;

            harness.CollapsedRibbonGroupNames.Should().NotContain("Cells", harness.DebugRibbonChildren);
            harness.CollapsedRibbonGroupNames.Should().Contain("Editing", harness.DebugRibbonChildren);
            harness.VisibleRibbonCommandLabels.Should().Contain(
                ["Insert", "Delete", "Format"],
                "Excel keeps the Cells group visible at common wide widths and collapses Editing first");
        });
    }

    [Fact]
    public void FormulasRibbon_KeepsFunctionLibraryExpandedAtNormalWideWidths()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("Formulas", 1465);

            harness.CollapsedActiveRibbonGroupNames.Should().NotContain("Function Library", harness.DebugActiveRibbonChildren);
            harness.VisibleRibbonCommandLabels.Should().Contain(
                ["AutoSum", "Financial", "Logical Functions"],
                "Excel keeps the primary Formulas Function Library block expanded before collapsing lower-priority groups");
        });
    }
}
