using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.App.Services.Ribbon;

namespace FreeX.App.Host.Tests;

public sealed class WorksheetContextMenuRendererTests
{
    [Fact]
    public void Render_FromPlannerViaAdapter_MatchesPlannerHeadersNamesIdsEnabledAndNesting()
    {
        StaTestRunner.Run(() =>
        {
            // No threaded comment / hyperlink: exercises a disabled item and the non-hyperlink branch.
            var commands = WorksheetContextMenuPlanner.BuildCommands(
                WorksheetContextMenuTargetKind.Worksheet,
                new WorksheetContextMenuState(HasThreadedComment: false, HasHyperlink: false));
            var ribbonMenu = WorksheetContextMenuRibbonAdapter.ToRibbonMenu(commands);

            var dispatched = new List<WorksheetContextMenuAction>();
            var menu = new ContextMenu();
            WorksheetContextMenuRenderer.AddItems(menu.Items, ribbonMenu.Items, dispatched.Add);

            AssertEquivalent(commands, menu.Items);
        });
    }

    [Fact]
    public void Render_LeafClick_DispatchesParsedAction()
    {
        StaTestRunner.Run(() =>
        {
            var commands = WorksheetContextMenuPlanner.BuildCommands();
            var ribbonMenu = WorksheetContextMenuRibbonAdapter.ToRibbonMenu(commands);

            var dispatched = new List<WorksheetContextMenuAction>();
            var menu = new ContextMenu();
            WorksheetContextMenuRenderer.AddItems(menu.Items, ribbonMenu.Items, dispatched.Add);

            // "Cut" is the first leaf.
            var cut = menu.Items.OfType<MenuItem>().First();
            cut.Header.Should().Be("Cu_t");
            cut.RaiseEvent(new System.Windows.RoutedEventArgs(MenuItem.ClickEvent));

            dispatched.Should().ContainSingle().Which.Should().Be(WorksheetContextMenuAction.Cut);
        });
    }

    [Fact]
    public void Render_SubmenuParent_UsesNormalizedAutomationIdAndDoesNotDispatch()
    {
        StaTestRunner.Run(() =>
        {
            var commands = WorksheetContextMenuPlanner.BuildCommands();
            var ribbonMenu = WorksheetContextMenuRibbonAdapter.ToRibbonMenu(commands);

            var dispatched = new List<WorksheetContextMenuAction>();
            var menu = new ContextMenu();
            WorksheetContextMenuRenderer.AddItems(menu.Items, ribbonMenu.Items, dispatched.Add);

            var insertAndDelete = menu.Items.OfType<MenuItem>()
                .First(item => (item.Header as string) == "_Insert and Delete");
            AutomationProperties.GetName(insertAndDelete).Should().Be("Insert and Delete");
            AutomationProperties.GetAutomationId(insertAndDelete)
                .Should().Be("WorksheetContextMenu_InsertandDelete");
            insertAndDelete.Items.OfType<MenuItem>().Should().NotBeEmpty();
        });
    }

    [Fact]
    public void AddItemsByCommandId_CheckableItem_RendersIsCheckableWithStateAndDispatchesCommandId()
    {
        StaTestRunner.Run(() =>
        {
            var ribbonMenu = new RibbonMenu(new[]
            {
                new RibbonMenuItem("_Set as Total", CommandId: new RibbonCommandId("WaterfallSetAsTotal"))
                {
                    IsChecked = true
                }
            });

            var dispatched = new List<RibbonCommandId>();
            var menu = new ContextMenu();
            WorksheetContextMenuRenderer.AddItemsByCommandId(menu.Items, ribbonMenu.Items, dispatched.Add);

            var item = menu.Items.OfType<MenuItem>().Should().ContainSingle().Subject;
            item.IsCheckable.Should().BeTrue();
            item.IsChecked.Should().BeTrue();
            AutomationProperties.GetName(item).Should().Be("Set as Total");

            item.RaiseEvent(new System.Windows.RoutedEventArgs(MenuItem.ClickEvent));
            dispatched.Should().ContainSingle().Which.Should().Be(new RibbonCommandId("WaterfallSetAsTotal"));
        });
    }

    [Fact]
    public void AddItems_NullIsChecked_LeavesItemNonCheckable()
    {
        StaTestRunner.Run(() =>
        {
            var commands = WorksheetContextMenuPlanner.BuildCommands();
            var ribbonMenu = WorksheetContextMenuRibbonAdapter.ToRibbonMenu(commands);

            var menu = new ContextMenu();
            WorksheetContextMenuRenderer.AddItems(menu.Items, ribbonMenu.Items, _ => { });

            menu.Items.OfType<MenuItem>().First().IsCheckable.Should().BeFalse();
        });
    }

    [Fact]
    public void AddSearchBox_AddsExcelLikeSearchRowAndFiltersVisibleCommands()
    {
        StaTestRunner.Run(() =>
        {
            var commands = WorksheetContextMenuPlanner.BuildCommands();
            var ribbonMenu = WorksheetContextMenuRibbonAdapter.ToRibbonMenu(commands);
            var menu = new ContextMenu();
            WorksheetContextMenuRenderer.AddItems(menu.Items, ribbonMenu.Items, _ => { });

            var searchBox = WorksheetContextMenuRenderer.AddSearchBox(menu);

            var searchItem = menu.Items[0].Should().BeOfType<MenuItem>().Subject;
            searchItem.Tag.Should().Be(WorksheetContextMenuRenderer.SearchMenuItemTag);
            searchItem.StaysOpenOnClick.Should().BeTrue();
            AutomationProperties.GetName(searchBox).Should().Be("Search the menus");
            ((Grid)searchItem.Header).Children.OfType<TextBlock>().Single()
                .Text.Should().Be("Search the menus");

            searchBox.Text = "hyperlink";

            menu.Items.OfType<MenuItem>()
                .Single(item => (item.Header as string) == "_Hyperlink...")
                .Visibility.Should().Be(Visibility.Visible);
            menu.Items.OfType<MenuItem>()
                .Single(item => (item.Header as string) == "Cu_t")
                .Visibility.Should().Be(Visibility.Collapsed);
        });
    }

    private static void AssertEquivalent(
        IReadOnlyList<WorksheetContextMenuCommand> source,
        ItemCollection rendered)
    {
        rendered.Count.Should().Be(source.Count);
        for (var i = 0; i < source.Count; i++)
        {
            var command = source[i];
            if (command.IsSeparator)
            {
                rendered[i].Should().BeOfType<Separator>();
                continue;
            }

            var item = rendered[i].Should().BeOfType<MenuItem>().Subject;
            item.Header.Should().Be(command.AccessHeader);
            item.IsEnabled.Should().Be(command.IsEnabled);

            var cleanHeader = command.Header;
            AutomationProperties.GetName(item).Should().Be(cleanHeader);
            AutomationProperties.GetAutomationId(item).Should().Be(
                command.Action == WorksheetContextMenuAction.None
                    ? $"WorksheetContextMenu_{NormalizeAutomationId(cleanHeader)}"
                    : $"WorksheetContextMenu_{command.Action}");

            AssertEquivalent(command.Children, item.Items);
        }
    }

    private static string NormalizeAutomationId(string header)
    {
        var builder = new StringBuilder(header.Length);
        foreach (var character in header)
        {
            if (char.IsLetterOrDigit(character))
                builder.Append(character);
        }

        return builder.Length == 0 ? "Item" : builder.ToString();
    }
}
