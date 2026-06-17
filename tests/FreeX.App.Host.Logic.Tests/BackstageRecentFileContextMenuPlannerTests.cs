using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class BackstageRecentFileContextMenuPlannerTests
{
    [Fact]
    public void BuildRecentFileCommands_ExposesPinThenRemove()
    {
        var commands = BackstageRecentFileContextMenuPlanner.BuildRecentFileCommands();

        commands.Select(command => command.Action).Should().Equal(
            BackstageRecentFileMenuAction.Pin,
            BackstageRecentFileMenuAction.Remove);

        commands.Select(command => command.ResourceKey).Should().Equal(
            "MainWindow_Header_PinToList",
            "MainWindow_Header_RemoveFromList");

        commands.Select(command => command.KeyTip).Should().Equal("P", "R");
        commands.Select(command => command.CommandName).Should().Equal("Pin to list", "Remove from list");
        commands.Select(command => command.AutomationId).Should().Equal(
            "BackstageRecentPinMenuItem",
            "BackstageRecentRemoveMenuItem");
    }

    [Fact]
    public void BuildPinnedFileCommands_ExposesUnpinThenRemove()
    {
        var commands = BackstageRecentFileContextMenuPlanner.BuildPinnedFileCommands();

        commands.Select(command => command.Action).Should().Equal(
            BackstageRecentFileMenuAction.Unpin,
            BackstageRecentFileMenuAction.Remove);

        commands.Select(command => command.ResourceKey).Should().Equal(
            "MainWindow_Header_UnpinFromList",
            "MainWindow_Header_RemoveFromList");

        commands.Select(command => command.KeyTip).Should().Equal("U", "R");
        commands.Select(command => command.CommandName).Should().Equal("Unpin from list", "Remove from list");
        commands.Select(command => command.AutomationId).Should().Equal(
            "BackstagePinnedUnpinMenuItem",
            "BackstagePinnedRemoveMenuItem");
    }

    [Fact]
    public void BuildCommands_BindAutomationTextToRecentFileViewModelPaths()
    {
        var recent = BackstageRecentFileContextMenuPlanner.BuildRecentFileCommands();
        var pinned = BackstageRecentFileContextMenuPlanner.BuildPinnedFileCommands();

        // Pin/Unpin reuse the Pin automation strings; Remove uses the Remove automation strings.
        recent[0].AutomationNamePath.Should().Be("PinAutomationName");
        recent[0].AutomationHelpTextPath.Should().Be("PinAutomationHelpText");
        recent[1].AutomationNamePath.Should().Be("RemoveAutomationName");
        recent[1].AutomationHelpTextPath.Should().Be("RemoveAutomationHelpText");

        pinned[0].AutomationNamePath.Should().Be("PinAutomationName");
        pinned[0].AutomationHelpTextPath.Should().Be("PinAutomationHelpText");
        pinned[1].AutomationNamePath.Should().Be("RemoveAutomationName");
        pinned[1].AutomationHelpTextPath.Should().Be("RemoveAutomationHelpText");
    }

    [Fact]
    public void BuildCommands_ReuseCachedPlans()
    {
        BackstageRecentFileContextMenuPlanner.BuildRecentFileCommands()
            .Should()
            .BeSameAs(BackstageRecentFileContextMenuPlanner.BuildRecentFileCommands());
        BackstageRecentFileContextMenuPlanner.BuildPinnedFileCommands()
            .Should()
            .BeSameAs(BackstageRecentFileContextMenuPlanner.BuildPinnedFileCommands());
    }
}
