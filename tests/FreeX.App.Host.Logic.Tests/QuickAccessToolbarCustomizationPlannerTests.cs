using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class QuickAccessToolbarCustomizationPlannerTests
{
    [Fact]
    public void MainWindowQuickAccessToolbar_WiresRibbonAndQatContextMenuCustomization()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.QuickAccessToolbar.cs");

        source.Should().Contain("FrameworkElement.ContextMenuOpeningEvent");
        source.Should().Contain("TryFindQuickAccessToolbarCatalogCommand");
        source.Should().Contain("CreateQuickAccessToolbarCustomizationContextMenu(command.Id)");
        source.Should().Contain("QuickAccessToolbarCustomizationPlanner.Apply");
        // Both QAT context menus now build their structure from the neutral planner.
        source.Should().Contain("QuickAccessToolbarContextMenuPlanner.BuildCustomizationCommands");
        source.Should().Contain("QuickAccessToolbarContextMenuPlanner.BuildHistoryCommands");
        source.Should().Contain("WorkbookApplicationCommandRouter.TryRouteQuickAccess(commandId, out var route)");
        source.Should().Contain("WorkbookApplicationCommands.TryExecuteAsync(");

        var bindings = DialogSourceTestSupport.ReadHostSources("MainWindow.ApplicationCommandRouting.cs");
        bindings.Should().Contain("CheckAccessibility = Handled<WorkbookApplicationCommandInvocation>");
        bindings.Should().Contain("AccessibilityCheckerBtn_Click(NativeSource(invocation), RoutedArgs(invocation))");
        bindings.Should().Contain("ShareWorkbook = Handled<WorkbookApplicationCommandInvocation>");
        bindings.Should().Contain("ShareWorkbookBtn_Click(NativeSource(invocation), RoutedArgs(invocation))");
        bindings.Should().Contain("OpenSelectionPane = Handled<WorkbookApplicationCommandInvocation>");
        bindings.Should().Contain("SelectionPaneBtn_Click(NativeSource(invocation), RoutedArgs(invocation))");
    }
}
