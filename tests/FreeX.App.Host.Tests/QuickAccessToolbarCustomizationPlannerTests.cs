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
    }
}
