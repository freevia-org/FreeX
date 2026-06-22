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
        source.Should().Contain("case QuickAccessToolbarCommandIds.CheckAccessibility:");
        source.Should().Contain("AccessibilityCheckerBtn_Click(sender, args);");
        source.Should().Contain("case QuickAccessToolbarCommandIds.ShareWorkbook:");
        source.Should().Contain("ShareWorkbookBtn_Click(sender, args);");
        source.Should().Contain("case QuickAccessToolbarCommandIds.SelectionPane:");
        source.Should().Contain("SelectionPaneBtn_Click(sender, args);");
    }
}
