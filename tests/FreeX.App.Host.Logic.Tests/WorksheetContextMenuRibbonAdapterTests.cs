using System.Collections.Generic;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public class WorksheetContextMenuRibbonAdapterTests
{
    [Fact]
    public void MapsTopLevelCommand_HeaderCommandIdAndEnabled()
    {
        var commands = WorksheetContextMenuPlanner.BuildCommands();

        var menu = WorksheetContextMenuRibbonAdapter.ToRibbonMenu(commands);

        var first = menu.Items[0];
        first.Header.Should().Be("Cu_t"); // carries the access mnemonic verbatim
        first.CommandId.Should().Be(new RibbonCommandId(WorksheetContextMenuAction.Cut.ToString()));
        first.Kind.Should().Be(Free.Shared.Ribbon.RibbonMenuItemKind.Command);
        first.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void MapsSeparator_ToSeparatorKind()
    {
        var commands = WorksheetContextMenuPlanner.BuildCommands();

        var menu = WorksheetContextMenuRibbonAdapter.ToRibbonMenu(commands);

        // The planner places a separator after Cut/Copy/Paste (index 3).
        menu.Items[3].Kind.Should().Be(Free.Shared.Ribbon.RibbonMenuItemKind.Separator);
    }

    [Fact]
    public void MapsSecondarySubmenu_NullCommandIdWithPreservedChildren()
    {
        var commands = WorksheetContextMenuPlanner.BuildCommands();

        var menu = WorksheetContextMenuRibbonAdapter.ToRibbonMenu(commands);

        var dataTools = FindByHeader(menu.Items, "Data _Tools");
        dataTools.Should().NotBeNull();
        dataTools!.CommandId.Should().BeNull(); // submenu parents have no action
        dataTools.Children.Should().NotBeEmpty();
        FindByHeader(dataTools.Children, "Te_xt to Columns...")!.CommandId
            .Should().Be(new RibbonCommandId(WorksheetContextMenuAction.TextToColumns.ToString()));
    }

    [Fact]
    public void PropagatesIsEnabled_FromStateDependentCommand()
    {
        // No threaded comment → "Edit Comment..." is disabled by the planner.
        var commands = WorksheetContextMenuPlanner.BuildCommands(
            WorksheetContextMenuTargetKind.Worksheet,
            new WorksheetContextMenuState(HasThreadedComment: false));

        var menu = WorksheetContextMenuRibbonAdapter.ToRibbonMenu(commands);

        var comments = FindByHeader(menu.Items, "Co_mments and Notes");
        comments.Should().NotBeNull();
        FindByHeader(comments!.Children, "_Edit Comment...")!.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void ProducesStructurallyEquivalentTree()
    {
        var commands = WorksheetContextMenuPlanner.BuildCommands();

        var menu = WorksheetContextMenuRibbonAdapter.ToRibbonMenu(commands);

        AssertEquivalent(commands, menu.Items);
    }

    private static void AssertEquivalent(
        IReadOnlyList<WorksheetContextMenuCommand> source,
        IReadOnlyList<RibbonMenuItem> mapped)
    {
        mapped.Count.Should().Be(source.Count);
        for (var i = 0; i < source.Count; i++)
        {
            var command = source[i];
            var item = mapped[i];

            if (command.IsSeparator)
            {
                item.Kind.Should().Be(Free.Shared.Ribbon.RibbonMenuItemKind.Separator);
                continue;
            }

            item.Kind.Should().Be(Free.Shared.Ribbon.RibbonMenuItemKind.Command);
            item.Header.Should().Be(command.AccessHeader);
            item.IsEnabled.Should().Be(command.IsEnabled);
            item.CommandId.Should().Be(
                command.Action == WorksheetContextMenuAction.None
                    ? (RibbonCommandId?)null
                    : new RibbonCommandId(command.Action.ToString()));

            AssertEquivalent(command.Children, item.Children);
        }
    }

    private static RibbonMenuItem? FindByHeader(IReadOnlyList<RibbonMenuItem> items, string header)
    {
        foreach (var item in items)
        {
            if (item.Header == header)
                return item;
        }

        return null;
    }
}
