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
    public void CollapsedRibbonGroupMenu_BuildsItemsOnlyWhenOpened()
    {
        StaTestRunner.Run(() =>
        {
            var group = new StackPanel();
            RibbonMetadata.SetGroupName(group, "Editing");

            var sourceButton = new Button();
            RibbonTooltip.SetTitle(sourceButton, "AutoSum");
            RibbonTooltip.SetKeyTip(sourceButton, "AS");
            group.Children.Add(sourceButton);

            var createMenu = typeof(MainWindow)
                .GetMethod("CreateLazyCollapsedRibbonGroupMenu", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "CreateLazyCollapsedRibbonGroupMenu");
            var menu = (ContextMenu)createMenu.Invoke(null, [group])!;

            menu.Items.Count.Should().Be(0, "collapsed groups should not clone menus during resize or tab switching");

            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent, menu));

            menu.Items.OfType<MenuItem>()
                .Select(item => item.Header?.ToString())
                .Should().ContainSingle("AutoSum");
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

            harness.CollapsedRibbonGroupNames.Should().NotContain("Editing", harness.DebugRibbonChildren);
            harness.VisibleRibbonCommandLabels.Should().Contain(
                ["AutoSum", "Fill", "Clear", "Sort & Filter", "Find & Select"],
                "Excel spends available wide Home ribbon space on the Editing commands instead of leaving a collapsed group beside empty space");
        });
    }

    [Fact]
    public void HomeRibbon_NormalizesToggleCommandsIntoSmallIconLabelRows()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetRibbonWidth(1465);

            harness.NamedRibbonButtonContentLayout("WrapTextBtn").Should().Be(
                RibbonCommandContentLayout.Small,
                "small toggle commands should use the same canonical icon slot and label row as ordinary stacked commands");
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
                ["Insert Function", "AutoSum"],
                "Excel keeps the primary Formulas command block available before collapsing lower-priority groups");
        });
    }
}
