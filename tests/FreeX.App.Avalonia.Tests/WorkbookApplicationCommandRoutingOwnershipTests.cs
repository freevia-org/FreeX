using System.IO;
using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class WorkbookApplicationCommandRoutingOwnershipTests
{
    [Fact]
    public void AvaloniaQuickAccessWorksheetAndShortcutRoutesDelegateToPresentationRouter()
    {
        var quickAccess = File.ReadAllText(RepoFile("MainWindow.CatalogContextMenus.cs"));
        var mainWindow = File.ReadAllText(RepoFile("MainWindow.cs"));
        var bindings = File.ReadAllText(RepoFile("MainWindow.ApplicationCommandRouting.cs"));

        quickAccess.Should().Contain("WorkbookApplicationCommandRouter.TryRouteQuickAccess");
        quickAccess.Should().NotContain("case QuickAccessToolbarCommandIds.");
        mainWindow.Should().Contain("WorkbookApplicationCommandRouter.TryRouteWorksheetContextMenu");
        mainWindow.Should().Contain("WorkbookApplicationCommandRouter.TryRouteShortcut");
        mainWindow.Should().NotContain("case WorksheetContextMenuAction.Cut:");
        mainWindow.Should().NotContain("case WorkbookShortcutRoute.");
        bindings.Should().Contain("WorkbookApplicationWorkareaCommandBinder.Bind(");
        bindings.Should().Contain("ExecuteWorkbookApplicationWorkareaCommandAsync");
        bindings.Should().NotContain("bindings.Bind(WorkbookApplicationCommandIntent");
        bindings.Should().NotContain("bindings.BindAsync(WorkbookApplicationCommandIntent");
    }

    [Fact]
    public void AvaloniaApplicationFrameRoutesUseSharedBinder()
    {
        var bindings = File.ReadAllText(RepoFile("MainWindow.ApplicationCommandRouting.cs"));

        bindings.Should().Contain("WorkbookApplicationFrameCommandBinder.Bind(");
        bindings.Should().Contain("new WorkbookApplicationFrameCommandHandlers(");
        bindings.Should().NotContain("bindings.BindAsync(WorkbookApplicationCommandIntent.NewWorkbook");
        bindings.Should().NotContain("bindings.BindAsync(WorkbookApplicationCommandIntent.OpenWorkbook");
    }

    private static string RepoFile(string fileName) =>
        TestWorkspaceFileLocator.Find(fileName);
}
