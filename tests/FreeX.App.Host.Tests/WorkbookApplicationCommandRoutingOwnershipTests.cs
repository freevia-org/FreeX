using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class WorkbookApplicationCommandRoutingOwnershipTests
{
    [Fact]
    public void WpfQuickAccessAndWorksheetRoutesDelegateToPresentationRouter()
    {
        var quickAccess = DialogSourceTestSupport.ReadHostSources("MainWindow.QuickAccessToolbar.cs");
        var worksheet = DialogSourceTestSupport.ReadHostSources("MainWindow.WorksheetContextMenu.cs");

        quickAccess.Should().Contain("WorkbookApplicationCommandRouter.TryRouteQuickAccess");
        quickAccess.Should().NotContain("case QuickAccessToolbarCommandIds.");
        worksheet.Should().Contain("WorkbookApplicationCommandRouter.TryRouteWorksheetContextMenu");
        worksheet.Should().NotContain("case WorksheetContextMenuAction.Cut:");
        worksheet.Should().NotContain("case WorksheetContextMenuAction.ClearContents:");
        worksheet.Should().Contain("case WorksheetContextMenuAction.FormatPicture:");
    }

    [Fact]
    public void WpfPortableKeyboardRoutesUseSharedApplicationIntents()
    {
        var keyboard = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyboardCommands.cs");

        keyboard.Should().Contain("RegisterPortableKeyboardCommand");
        keyboard.Should().Contain("WorkbookApplicationCommandRouter.TryRouteShortcut");
        keyboard.Should().NotContain(
            "_keyboardCommandDispatcher.Register(KeyboardCommandShortcut.NewWorkbook,");
        keyboard.Should().NotContain(
            "_keyboardCommandDispatcher.Register(KeyboardCommandShortcut.Copy,");
    }

    [Fact]
    public void WpfApplicationFrameRoutesUseSharedBinder()
    {
        var bindings = DialogSourceTestSupport.ReadHostSources("MainWindow.ApplicationCommandRouting.cs");

        bindings.Should().Contain("WorkbookApplicationFrameCommandBinder.Bind(");
        bindings.Should().Contain("new WorkbookApplicationFrameCommandHandlers(");
        bindings.Should().NotContain("bindings.BindAsync(WorkbookApplicationCommandIntent.NewWorkbook");
        bindings.Should().NotContain("bindings.Bind(WorkbookApplicationCommandIntent.OpenWorkbook");
    }

    [Fact]
    public void WpfWorkareaRoutesUseSharedBinder()
    {
        var bindings = DialogSourceTestSupport.ReadHostSources("MainWindow.ApplicationCommandRouting.cs");

        bindings.Should().Contain("WorkbookApplicationWorkareaCommandBinder.Bind(");
        bindings.Should().Contain("new WorkbookApplicationWorkareaCommandEndpointProfile");
        bindings.Should().Contain("Undo = Handled(");
        bindings.Should().NotContain("ExecuteWorkbookApplicationWorkareaCommandAsync");
        bindings.Should().NotContain("WorkbookApplicationCommandIntent.");
        bindings.Should().NotContain("bindings.Bind(WorkbookApplicationCommandIntent");
        bindings.Should().NotContain("bindings.BindAsync(WorkbookApplicationCommandIntent");
    }
}
