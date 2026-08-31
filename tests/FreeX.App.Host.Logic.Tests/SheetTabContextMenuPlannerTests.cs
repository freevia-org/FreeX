using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class SheetTabContextMenuPlannerTests
{
    [Fact]
    public void BuildSheetTabCommands_DefaultCompositionUsesExcelLabelsAndOmitsUngroupWhenNotGrouped()
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
            "—",
            "LinkToThisSheet");
    }

    [Fact]
    public void BuildSheetTabCommands_CarriesExplicitKeyTipsResourceKeysAndCommandNames()
    {
        var commands = SheetTabContextMenuPlanner.BuildSheetTabCommands()
            .Where(command => !command.IsSeparator)
            .ToList();

        commands.Select(command => command.ResourceKey).Should().Equal(
            "Common_Insert",
            "MainWindow_Content_Delete",
            "MainWindow_Header_Rename",
            "MainWindow_Header_MoveOrCopy",
            "MainWindow_Header_ViewCode",
            "MainWindow_Header_ProtectSheet",
            "MainWindow_Header_TabColor",
            "MainWindow_Header_Hide",
            "MainWindow_Header_Unhide",
            "MainWindow_Header_SelectAllSheets",
            "SheetTabContext_LinkToThisSheet");

        commands.Select(command => command.KeyTip).Should().Equal(
            "I", "E", "R", "M", "V", "P", "T", "H", "U", "A", "L");

        commands.Select(command => command.CommandName).Should().Equal(
            "Insert",
            "Delete",
            "Rename",
            "Move or Copy",
            "View Code",
            "Protect Sheet",
            "Tab Color",
            "Hide",
            "Unhide",
            "Select All Sheets",
            "Link to this Sheet");
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
    public void BuildSheetTabCommands_OmitsUngroupRatherThanShowingANonActionableRow()
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
            SheetTabContextMenuAction.LinkToThisSheet);

        commands.Where(command => command.Action is
                SheetTabContextMenuAction.DeleteSheet or
                SheetTabContextMenuAction.ViewCode or
                SheetTabContextMenuAction.Hide or
                SheetTabContextMenuAction.Unhide or
                SheetTabContextMenuAction.SelectAllSheets)
            .Should()
            .OnlyContain(command => !command.IsEnabled);
        commands.Where(command => command.Action is
                SheetTabContextMenuAction.InsertSheet or
                SheetTabContextMenuAction.Rename or
                SheetTabContextMenuAction.MoveOrCopy or
                SheetTabContextMenuAction.ProtectSheet or
                SheetTabContextMenuAction.TabColor or
                SheetTabContextMenuAction.LinkToThisSheet)
            .Should()
            .OnlyContain(command => command.IsEnabled);
    }

    [Fact]
    public void BuildSheetTabCommands_OnlyAddsUngroupWhenTheWorkbookIsGrouped()
    {
        SheetTabContextMenuPlanner.BuildSheetTabCommands(
                new SheetTabContextMenuState(CanUngroupSheets: true))
            .Select(command => command.Action)
            .Should().Contain(SheetTabContextMenuAction.UngroupSheets);

        SheetTabContextMenuPlanner.BuildSheetTabCommands()
            .Select(command => command.Action)
            .Should().NotContain(SheetTabContextMenuAction.UngroupSheets);
    }

    [Theory]
    [InlineData("Sheet1", "#Sheet1!A1")]
    [InlineData("Data Sheet", "#'Data Sheet'!A1")]
    [InlineData("O'Brien", "#'O''Brien'!A1")]
    public void BuildClipboardText_CreatesANavigableInternalSheetLink(string sheetName, string expected)
    {
        SheetTabLinkFormatter.BuildClipboardText(sheetName).Should().Be(expected);
    }

    [Fact]
    public void BuildSheetTabCommands_ReusesCachedDefaultPlan()
    {
        SheetTabContextMenuPlanner.BuildSheetTabCommands()
            .Should()
            .BeSameAs(SheetTabContextMenuPlanner.BuildSheetTabCommands());
    }

    [Fact]
    public void BuildSheetTabCommands_GraysInsertRenameMoveOrCopyUnderWorkbookStructureProtection()
    {
        // R139-workbook-protection (finding F2): InsertSheet/Rename/MoveOrCopy previously had no
        // enablement wiring at all (always rendered enabled, unlike Delete/Hide/Unhide), so
        // workbook structure protection never grayed them out even though the command layer
        // (SheetCommands.cs) already refused all three.
        var commands = SheetTabContextMenuPlanner.BuildSheetTabCommands(
                new SheetTabContextMenuState(
                    CanInsertSheet: false,
                    CanRename: false,
                    CanMoveOrCopy: false))
            .Where(command => !command.IsSeparator)
            .ToList();

        commands.Where(command => command.Action is
                SheetTabContextMenuAction.InsertSheet or
                SheetTabContextMenuAction.Rename or
                SheetTabContextMenuAction.MoveOrCopy)
            .Should()
            .OnlyContain(command => !command.IsEnabled);

        // Sibling: everything else defaults to enabled, so this state only grays the three
        // targeted commands and does not collaterally disable unrelated rows.
        commands.Where(command => command.Action is not (
                SheetTabContextMenuAction.InsertSheet or
                SheetTabContextMenuAction.Rename or
                SheetTabContextMenuAction.MoveOrCopy or
                SheetTabContextMenuAction.ViewCode))
            .Should()
            .OnlyContain(command => command.IsEnabled);
    }

    [Fact]
    public void BuildSheetTabCommands_DefaultStateLeavesInsertRenameMoveOrCopyEnabled()
    {
        // Sibling to the gray-out test above: the new CanInsertSheet/CanRename/CanMoveOrCopy
        // fields must default to true so every pre-existing caller that doesn't know about them
        // (an unprotected sheet in an unprotected workbook) keeps the same enabled menu it had
        // before this fix.
        var commands = SheetTabContextMenuPlanner.BuildSheetTabCommands(SheetTabContextMenuState.Default)
            .Where(command => !command.IsSeparator)
            .ToList();

        commands.Where(command => command.Action is
                SheetTabContextMenuAction.InsertSheet or
                SheetTabContextMenuAction.Rename or
                SheetTabContextMenuAction.MoveOrCopy)
            .Should()
            .OnlyContain(command => command.IsEnabled);
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
