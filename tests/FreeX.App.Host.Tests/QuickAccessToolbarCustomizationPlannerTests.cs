using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class QuickAccessToolbarCustomizationPlannerTests
{
    [Fact]
    public void CreatePlan_OffersAddForCatalogCommandThatIsNotOnQuickAccessToolbar()
    {
        var plan = QuickAccessToolbarCustomizationPlanner.CreatePlan(
            QuickAccessToolbarCommandIds.Bold,
            QuickAccessToolbarCatalog.DefaultCommandIds);

        plan.Action.Should().Be(QuickAccessToolbarCustomizationAction.Add);
        plan.IsEnabled.Should().BeTrue();
        plan.HeaderResourceKey.Should().Be(QuickAccessToolbarCustomizationPlanner.AddHeaderResourceKey);
        plan.AutomationId.Should().Be(QuickAccessToolbarCustomizationPlanner.AddAutomationId);
    }

    [Fact]
    public void CreatePlan_OffersRemoveForCatalogCommandThatIsAlreadyOnQuickAccessToolbar()
    {
        var plan = QuickAccessToolbarCustomizationPlanner.CreatePlan(
            QuickAccessToolbarCommandIds.Bold,
            [
                QuickAccessToolbarCommandIds.Save,
                QuickAccessToolbarCommandIds.Bold
            ]);

        plan.Action.Should().Be(QuickAccessToolbarCustomizationAction.Remove);
        plan.IsEnabled.Should().BeTrue();
        plan.HeaderResourceKey.Should().Be(QuickAccessToolbarCustomizationPlanner.RemoveHeaderResourceKey);
        plan.AutomationId.Should().Be(QuickAccessToolbarCustomizationPlanner.RemoveAutomationId);
    }

    [Fact]
    public void Apply_AddsRemovesAndKeepsAtLeastOneQuickAccessToolbarCommand()
    {
        QuickAccessToolbarCustomizationPlanner.Apply(
            QuickAccessToolbarCatalog.DefaultCommandIds,
            QuickAccessToolbarCommandIds.Bold,
            QuickAccessToolbarCustomizationAction.Add)
            .Should()
            .Equal(
                QuickAccessToolbarCommandIds.Save,
                QuickAccessToolbarCommandIds.Undo,
                QuickAccessToolbarCommandIds.Redo,
                QuickAccessToolbarCommandIds.Bold);

        QuickAccessToolbarCustomizationPlanner.Apply(
            [
                QuickAccessToolbarCommandIds.Save,
                QuickAccessToolbarCommandIds.Bold
            ],
            QuickAccessToolbarCommandIds.Bold,
            QuickAccessToolbarCustomizationAction.Remove)
            .Should()
            .Equal(QuickAccessToolbarCommandIds.Save);

        QuickAccessToolbarCustomizationPlanner.Apply(
            [QuickAccessToolbarCommandIds.Save],
            QuickAccessToolbarCommandIds.Save,
            QuickAccessToolbarCustomizationAction.Remove)
            .Should()
            .Equal(QuickAccessToolbarCommandIds.Save);
    }

    [Fact]
    public void Catalog_MapsRibbonCommandNamesToEligibleQuickAccessToolbarCommands()
    {
        QuickAccessToolbarCatalog.TryGetByCommandName("Bold", out var boldCommand).Should().BeTrue();
        boldCommand.Id.Should().Be(QuickAccessToolbarCommandIds.Bold);

        QuickAccessToolbarCatalog.TryGetByCommandName("Check Accessibility", out var accessibilityCommand).Should().BeTrue();
        accessibilityCommand.Id.Should().Be(QuickAccessToolbarCommandIds.CheckAccessibility);

        QuickAccessToolbarCatalog.TryGetByCommandName("Selection Pane", out var selectionPaneCommand).Should().BeTrue();
        selectionPaneCommand.Id.Should().Be(QuickAccessToolbarCommandIds.SelectionPane);

        QuickAccessToolbarCatalog.TryGetByCommandName("Share Workbook", out var shareCommand).Should().BeTrue();
        shareCommand.Id.Should().Be(QuickAccessToolbarCommandIds.ShareWorkbook);

        QuickAccessToolbarCatalog.TryGetByCommandName("Not a QAT command", out _).Should().BeFalse();
    }

    [Fact]
    public void MainWindowQuickAccessToolbar_WiresRibbonAndQatContextMenuCustomization()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.QuickAccessToolbar.cs"));

        source.Should().Contain("FrameworkElement.ContextMenuOpeningEvent");
        source.Should().Contain("TryFindQuickAccessToolbarCatalogCommand");
        source.Should().Contain("CreateQuickAccessToolbarCustomizationContextMenu(command.Id)");
        source.Should().Contain("QuickAccessToolbarCustomizationPlanner.Apply");
        source.Should().Contain("case QuickAccessToolbarCommandIds.CheckAccessibility:");
        source.Should().Contain("AccessibilityCheckerBtn_Click(sender, args);");
        source.Should().Contain("case QuickAccessToolbarCommandIds.ShareWorkbook:");
        source.Should().Contain("ShareWorkbookBtn_Click(sender, args);");
        source.Should().Contain("case QuickAccessToolbarCommandIds.SelectionPane:");
        source.Should().Contain("SelectionPaneBtn_Click(sender, args);");
    }
}
