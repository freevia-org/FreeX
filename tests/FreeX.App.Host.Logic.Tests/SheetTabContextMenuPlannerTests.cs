using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class SheetTabContextMenuPlannerTests
{
    [Fact]
    public void BuildSheetTabCommands_HasSeparatorsBetweenLogicalGroups()
    {
        var commands = SheetTabContextMenuPlanner.BuildSheetTabCommands();

        var kinds = commands
            .Select(command => command.IsSeparator
                ? "—"
                : command.Action.ToString())
            .ToList();

        kinds.Should().Equal(
            "InsertSheet",
            "DeleteSheet",
            "Rename",
            "MoveOrCopy",
            "—",
            "ViewCode",
            "ProtectSheet",
            "TabColor",
            "—",
            "Hide",
            "Unhide",
            "—",
            "SelectAllSheets",
            "UngroupSheets");
    }

    [Fact]
    public void BuildSheetTabCommands_CarriesExplicitKeyTipsResourceKeysAndCommandNames()
    {
        var commands = SheetTabContextMenuPlanner.BuildSheetTabCommands()
            .Where(command => !command.IsSeparator)
            .ToList();

        commands.Select(command => command.ResourceKey).Should().Equal(
            "MainWindow_Header_InsertSheet",
            "MainWindow_Header_DeleteSheet",
            "MainWindow_Header_Rename",
            "MainWindow_Header_MoveOrCopy",
            "MainWindow_Header_ViewCode",
            "MainWindow_Header_ProtectSheet",
            "MainWindow_Header_TabColor",
            "MainWindow_Header_Hide",
            "MainWindow_Header_Unhide",
            "MainWindow_Header_SelectAllSheets",
            "MainWindow_Header_UngroupSheets");

        commands.Select(command => command.KeyTip).Should().Equal(
            "I", "E", "R", "M", "V", "P", "T", "H", "U", "A", "G");

        commands.Select(command => command.CommandName).Should().Equal(
            "Insert Sheet",
            "Delete Sheet",
            "Rename",
            "Move or Copy",
            "View Code",
            "Protect Sheet",
            "Tab Color",
            "Hide",
            "Unhide",
            "Select All Sheets",
            "Ungroup Sheets");
    }

    [Fact]
    public void BuildSheetTabCommands_DisablesOnlyViewCode()
    {
        var commands = SheetTabContextMenuPlanner.BuildSheetTabCommands()
            .Where(command => !command.IsSeparator)
            .ToList();

        commands.Single(command => command.Action == SheetTabContextMenuAction.ViewCode)
            .IsEnabled.Should().BeFalse();
        commands.Where(command => command.Action != SheetTabContextMenuAction.ViewCode)
            .Should().OnlyContain(command => command.IsEnabled);
    }

    [Fact]
    public void BuildSheetTabCommands_DisablesWorkbookStateDependentRowsWithoutHidingThem()
    {
        var commands = SheetTabContextMenuPlanner.BuildSheetTabCommands(
                new SheetTabContextMenuState(
                    CanDeleteSheet: false,
                    CanHideSheet: false,
                    CanUnhideSheet: false,
                    CanSelectAllSheets: false,
                    CanUngroupSheets: false))
            .Where(command => !command.IsSeparator)
            .ToList();

        commands.Select(command => command.Action).Should().Equal(
            SheetTabContextMenuAction.InsertSheet,
            SheetTabContextMenuAction.DeleteSheet,
            SheetTabContextMenuAction.Rename,
            SheetTabContextMenuAction.MoveOrCopy,
            SheetTabContextMenuAction.ViewCode,
            SheetTabContextMenuAction.ProtectSheet,
            SheetTabContextMenuAction.TabColor,
            SheetTabContextMenuAction.Hide,
            SheetTabContextMenuAction.Unhide,
            SheetTabContextMenuAction.SelectAllSheets,
            SheetTabContextMenuAction.UngroupSheets);

        commands.Where(command => command.Action is
                SheetTabContextMenuAction.DeleteSheet or
                SheetTabContextMenuAction.ViewCode or
                SheetTabContextMenuAction.Hide or
                SheetTabContextMenuAction.Unhide or
                SheetTabContextMenuAction.SelectAllSheets or
                SheetTabContextMenuAction.UngroupSheets)
            .Should()
            .OnlyContain(command => !command.IsEnabled);
        commands.Where(command => command.Action is
                SheetTabContextMenuAction.InsertSheet or
                SheetTabContextMenuAction.Rename or
                SheetTabContextMenuAction.MoveOrCopy or
                SheetTabContextMenuAction.ProtectSheet or
                SheetTabContextMenuAction.TabColor)
            .Should()
            .OnlyContain(command => command.IsEnabled);
    }

    [Fact]
    public void BuildSheetTabCommands_ReusesCachedDefaultPlan()
    {
        SheetTabContextMenuPlanner.BuildSheetTabCommands()
            .Should()
            .BeSameAs(SheetTabContextMenuPlanner.BuildSheetTabCommands());
    }

    [Fact]
    public void Separator_IsNeutralDisabledMarker()
    {
        var separator = SheetTabContextMenuCommand.Separator;

        separator.IsSeparator.Should().BeTrue();
        separator.IsEnabled.Should().BeFalse();
        separator.Action.Should().Be(SheetTabContextMenuAction.None);
        separator.KeyTip.Should().BeEmpty();
        separator.CommandName.Should().BeEmpty();
    }
}
