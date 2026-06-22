using FluentAssertions;
using FreeX.App.Services.Ribbon;

namespace FreeX.App.Services.Tests;

public sealed class QuickAccessToolbarContextMenuPlannerTests
{
    [Fact]
    public void BuildCustomizationCommands_OffersAddForCommandNotOnQuickAccessToolbar()
    {
        var commands = QuickAccessToolbarContextMenuPlanner.BuildCustomizationCommands(
            new QuickAccessToolbarCustomizationMenuState("Bold", ["Save", "Undo", "Redo"]));

        commands.Should().HaveCount(1);
        var command = commands[0];
        command.Action.Should().Be(QuickAccessToolbarMenuAction.Add);
        command.IsEnabled.Should().BeTrue();
        command.ResourceKey.Should().Be(QuickAccessToolbarContextMenuPlanner.AddHeaderResourceKey);
        command.AutomationId.Should().Be(QuickAccessToolbarContextMenuPlanner.AddAutomationId);
        command.CommandId.Should().Be("Bold");
    }

    [Fact]
    public void BuildCustomizationCommands_OffersRemoveForCommandAlreadyOnQuickAccessToolbar()
    {
        var commands = QuickAccessToolbarContextMenuPlanner.BuildCustomizationCommands(
            new QuickAccessToolbarCustomizationMenuState("Bold", ["Save", "Bold"]));

        var command = commands.Single();
        command.Action.Should().Be(QuickAccessToolbarMenuAction.Remove);
        command.IsEnabled.Should().BeTrue();
        command.ResourceKey.Should().Be(QuickAccessToolbarContextMenuPlanner.RemoveHeaderResourceKey);
        command.AutomationId.Should().Be(QuickAccessToolbarContextMenuPlanner.RemoveAutomationId);
        command.CommandId.Should().Be("Bold");
    }

    [Fact]
    public void BuildCustomizationCommands_DisablesRemoveWhenItWouldEmptyTheQuickAccessToolbar()
    {
        var commands = QuickAccessToolbarContextMenuPlanner.BuildCustomizationCommands(
            new QuickAccessToolbarCustomizationMenuState("Save", ["Save"]));

        var command = commands.Single();
        command.Action.Should().Be(QuickAccessToolbarMenuAction.Remove);
        command.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void BuildCustomizationCommands_MatchesCommandIdCaseInsensitively()
    {
        var commands = QuickAccessToolbarContextMenuPlanner.BuildCustomizationCommands(
            new QuickAccessToolbarCustomizationMenuState("bold", ["Save", "Bold"]));

        commands.Single().Action.Should().Be(QuickAccessToolbarMenuAction.Remove);
    }

    [Fact]
    public void BuildHistoryCommands_EmitsDisabledPlaceholderWhenUndoHistoryIsEmpty()
    {
        var commands = QuickAccessToolbarContextMenuPlanner.BuildHistoryCommands(
            new QuickAccessToolbarHistoryMenuState(IsRedo: false, []));

        var command = commands.Single();
        command.Action.Should().Be(QuickAccessToolbarMenuAction.None);
        command.IsEnabled.Should().BeFalse();
        command.Header.Should().Be("No actions to undo");
    }

    [Fact]
    public void BuildHistoryCommands_EmitsDisabledPlaceholderWhenRedoHistoryIsEmpty()
    {
        var commands = QuickAccessToolbarContextMenuPlanner.BuildHistoryCommands(
            new QuickAccessToolbarHistoryMenuState(IsRedo: true, []));

        commands.Single().Header.Should().Be("No actions to redo");
        commands.Single().IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void BuildHistoryCommands_EmitsOneEnabledItemPerSpanWithLabelCountAndAutomationId()
    {
        var commands = QuickAccessToolbarContextMenuPlanner.BuildHistoryCommands(
            new QuickAccessToolbarHistoryMenuState(IsRedo: false, ["Type A1", "Bold B2", "Clear C3"]));

        commands.Should().HaveCount(3);
        commands.Select(command => command.Header).Should().Equal("Type A1", "Bold B2", "Clear C3");
        commands.Select(command => command.ActionCount).Should().Equal(1, 2, 3);
        commands.Should().OnlyContain(command =>
            command.IsEnabled && command.Action == QuickAccessToolbarMenuAction.ExecuteHistory);
        commands.Select(command => command.AutomationId).Should().Equal(
            "UndoQatHistoryItem1",
            "UndoQatHistoryItem2",
            "UndoQatHistoryItem3");
    }

    [Fact]
    public void BuildHistoryCommands_UsesRedoAutomationIdPrefixForRedoEntries()
    {
        var commands = QuickAccessToolbarContextMenuPlanner.BuildHistoryCommands(
            new QuickAccessToolbarHistoryMenuState(IsRedo: true, ["Redo Type A1", "Redo Bold B2"]));

        commands.Select(command => command.AutomationId).Should().Equal(
            "RedoQatHistoryItem1",
            "RedoQatHistoryItem2");
    }
}
