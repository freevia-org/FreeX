using FreeX.App.Presentation.Shell;
using FreeX.App.Services.Ribbon;
using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookApplicationCommandRouteCoverageTests
{
    [Fact]
    public void EveryQuickAccessCatalogCommandHasAnApplicationRoute()
    {
        QuickAccessToolbarCatalog.Commands.Should().HaveCount(37);
        foreach (var command in QuickAccessToolbarCatalog.Commands)
            WorkbookApplicationCommandRouter.TryRouteQuickAccess(command.Id, out _).Should().BeTrue(command.Id);
    }

    [Fact]
    public void EveryGenericWorksheetContextActionHasAnApplicationRoute()
    {
        var actions = Enum.GetValues<WorksheetContextMenuAction>()
            .Where(action => action >= WorksheetContextMenuAction.Cut &&
                action <= WorksheetContextMenuAction.ClearContents)
            .ToArray();

        actions.Should().HaveCount(58);
        foreach (var action in actions)
        {
            WorkbookApplicationCommandRouter.TryRouteWorksheetContextMenu(action.ToString(), out _)
                .Should()
                .BeTrue(action.ToString());
        }
    }
}
