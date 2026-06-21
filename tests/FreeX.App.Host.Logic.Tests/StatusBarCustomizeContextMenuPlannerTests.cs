using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class StatusBarCustomizeContextMenuPlannerTests
{
    [Fact]
    public void BuildStatusBarCustomizeCommands_HasTitleSeparatorsAndToggleGroups()
    {
        var commands = StatusBarCustomizeContextMenuPlanner.BuildStatusBarCustomizeCommands();

        commands
            .Select(command => command.IsSeparator ? "—" : command.OptionTag.Length == 0 ? "title" : command.OptionTag)
            .Should()
            .Equal(
                "title",
                "—",
                StatusBarOptionTags.CellMode,
                StatusBarOptionTags.EndMode,
                StatusBarOptionTags.SelectionMode,
                StatusBarOptionTags.PageNumber,
                "—",
                StatusBarOptionTags.Average,
                StatusBarOptionTags.Count,
                StatusBarOptionTags.NumericalCount,
                StatusBarOptionTags.Minimum,
                StatusBarOptionTags.Maximum,
                StatusBarOptionTags.Sum,
                "—",
                StatusBarOptionTags.ViewShortcuts,
                StatusBarOptionTags.Zoom,
                StatusBarOptionTags.ZoomSlider);
    }

    [Fact]
    public void BuildStatusBarCustomizeCommands_TitleIsDisabledAndNotCheckable()
    {
        var title = StatusBarCustomizeContextMenuPlanner.BuildStatusBarCustomizeCommands()[0];

        title.ResourceKey.Should().Be("StatusBar_CustomizeStatusBar");
        title.IsEnabled.Should().BeFalse();
        title.IsCheckable.Should().BeFalse();
        title.KeyTip.Should().Be("T");
        title.AutomationId.Should().Be("StatusBarCustomizeTitleMenuItem");
    }

    [Fact]
    public void BuildStatusBarCustomizeCommands_TogglesAreCheckableEnabledAndCarryStableMetadata()
    {
        var toggles = StatusBarCustomizeContextMenuPlanner.BuildStatusBarCustomizeCommands()
            .Where(command => !command.IsSeparator && command.OptionTag.Length > 0)
            .ToList();

        toggles.Should().OnlyContain(command => command.IsCheckable && command.IsEnabled);

        toggles.Select(command => command.KeyTip).Should().Equal(
            "M", "E", "O", "P", "A", "C", "N", "I", "X", "S", "V", "Z", "L");

        toggles.Select(command => command.AutomationId).Should().Equal(
            "StatusBarCellModeMenuItem",
            "StatusBarEndModeMenuItem",
            "StatusBarSelectionModeMenuItem",
            "StatusBarPageNumberMenuItem",
            "StatusBarAverageMenuItem",
            "StatusBarCountMenuItem",
            "StatusBarNumericalCountMenuItem",
            "StatusBarMinimumMenuItem",
            "StatusBarMaximumMenuItem",
            "StatusBarSumMenuItem",
            "StatusBarViewShortcutsMenuItem",
            "StatusBarZoomMenuItem",
            "StatusBarZoomSliderMenuItem");
    }

    [Fact]
    public void BuildStatusBarCustomizeCommands_ReusesCachedPlan()
    {
        StatusBarCustomizeContextMenuPlanner.BuildStatusBarCustomizeCommands()
            .Should()
            .BeSameAs(StatusBarCustomizeContextMenuPlanner.BuildStatusBarCustomizeCommands());
    }
}
