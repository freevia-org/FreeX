using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class PivotFieldContextMenuPlannerTests
{
    [Fact]
    public void BuildPivotFieldCommands_BucketList_HasSortFilterSettingsAndRemove()
    {
        var commands = PivotFieldContextMenuPlanner.BuildPivotFieldCommands(includeRemove: true);

        commands
            .Select(command => command.IsSeparator ? "—" : command.Action.ToString())
            .Should()
            .Equal(
                "SortAscending",
                "SortDescending",
                "SelectItems",
                "LabelFilter",
                "ValueFilter",
                "ClearFilter",
                "—",
                "ValueFieldSettings",
                "—",
                "Remove");
    }

    [Fact]
    public void BuildPivotFieldCommands_AvailableFields_OmitsTrailingRemove()
    {
        var commands = PivotFieldContextMenuPlanner.BuildPivotFieldCommands(includeRemove: false);

        commands
            .Select(command => command.IsSeparator ? "—" : command.Action.ToString())
            .Should()
            .Equal(
                "SortAscending",
                "SortDescending",
                "SelectItems",
                "LabelFilter",
                "ValueFilter",
                "ClearFilter",
                "—",
                "ValueFieldSettings");
    }

    [Fact]
    public void BuildPivotFieldCommands_CarriesExplicitKeyTipsResourceKeysAndCommandNames()
    {
        var commands = PivotFieldContextMenuPlanner.BuildPivotFieldCommands(includeRemove: true)
            .Where(command => !command.IsSeparator)
            .ToList();

        commands.Select(command => command.ResourceKey).Should().Equal(
            "MainWindow_Header_SortAToZ",
            "MainWindow_Header_SortZToA",
            "MainWindow_Header_SelectItems",
            "MainWindow_Header_LabelFilter",
            "MainWindow_Header_ValueFilter",
            "MainWindow_Header_ClearFilter",
            "MainWindow_Header_ValueFieldSettings",
            "MainWindow_Content_Remove");

        commands.Select(command => command.KeyTip).Should().Equal(
            "S", "O", "I", "L", "F", "C", "V", "R");

        commands.Select(command => command.CommandName).Should().Equal(
            "Sort A to Z",
            "Sort Z to A",
            "Select Items",
            "Label Filter",
            "Value Filter",
            "Clear Filter",
            "Value Field Settings",
            "Remove");
    }

    [Fact]
    public void BuildPivotFieldCommands_AllItemsEnabled()
    {
        PivotFieldContextMenuPlanner.BuildPivotFieldCommands(includeRemove: true)
            .Where(command => !command.IsSeparator)
            .Should()
            .OnlyContain(command => command.IsEnabled);
    }

    [Fact]
    public void BuildPivotFieldCommands_ReusesCachedPlans()
    {
        PivotFieldContextMenuPlanner.BuildPivotFieldCommands(includeRemove: true)
            .Should()
            .BeSameAs(PivotFieldContextMenuPlanner.BuildPivotFieldCommands(includeRemove: true));
        PivotFieldContextMenuPlanner.BuildPivotFieldCommands(includeRemove: false)
            .Should()
            .BeSameAs(PivotFieldContextMenuPlanner.BuildPivotFieldCommands(includeRemove: false));
    }

    [Fact]
    public void Separator_IsNeutralDisabledMarker()
    {
        var separator = PivotFieldContextMenuCommand.Separator;

        separator.IsSeparator.Should().BeTrue();
        separator.IsEnabled.Should().BeFalse();
        separator.Action.Should().Be(PivotFieldContextMenuAction.None);
        separator.KeyTip.Should().BeEmpty();
        separator.CommandName.Should().BeEmpty();
    }
}
