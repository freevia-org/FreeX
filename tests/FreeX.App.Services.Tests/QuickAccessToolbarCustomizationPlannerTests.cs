using FluentAssertions;
using FreeX.App.Services.Ribbon;

namespace FreeX.App.Services.Tests;

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
    public void FilterAvailable_SearchesIdNameTitleAndDescriptionAndExcludesSelectedCommands()
    {
        var available = QuickAccessToolbarCustomizationPlanner.FilterAvailable(
            [QuickAccessToolbarCommandIds.Save],
            "clipboard",
            command => command.Id == QuickAccessToolbarCommandIds.Copy
                ? ["Clipboard command"]
                : []);

        available.Should().ContainSingle(command => command.Id == QuickAccessToolbarCommandIds.Copy);
        available.Should().NotContain(command => command.Id == QuickAccessToolbarCommandIds.Save);
    }

    [Fact]
    public void Move_ReordersOnlyWithinBoundsAndResetRestoresDefaults()
    {
        QuickAccessToolbarCustomizationPlanner.Move(
            [QuickAccessToolbarCommandIds.Save, QuickAccessToolbarCommandIds.Undo, QuickAccessToolbarCommandIds.Redo],
            QuickAccessToolbarCommandIds.Undo,
            -1)
            .Should()
            .Equal(QuickAccessToolbarCommandIds.Undo, QuickAccessToolbarCommandIds.Save, QuickAccessToolbarCommandIds.Redo);

        QuickAccessToolbarCustomizationPlanner.Move(
            [QuickAccessToolbarCommandIds.Save],
            QuickAccessToolbarCommandIds.Save,
            1)
            .Should()
            .Equal(QuickAccessToolbarCommandIds.Save);

        QuickAccessToolbarCustomizationPlanner.Reset()
            .Should()
            .Equal(QuickAccessToolbarCatalog.DefaultCommandIds);
    }
}
