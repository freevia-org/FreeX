namespace FreeX.App.Services.Ribbon;

/// <summary>
/// Platform-neutral planner that describes the sheet-tab context menu as a declarative command tree,
/// mirroring <see cref="WorksheetContextMenuPlanner"/>. The host (WPF today) resolves each command's
/// <see cref="SheetTabContextMenuCommand.ResourceKey"/> into a localized header, applies the explicit
/// <see cref="SheetTabContextMenuCommand.KeyTip"/>, and dispatches <see cref="SheetTabContextMenuAction"/>
/// to the existing sheet-tab handlers. Keeping the structure here lets the WPF in-app menu and the
/// Avalonia native menu single-source their labels/order/enablement from one place.
/// </summary>
public static class SheetTabContextMenuPlanner
{
    private static readonly IReadOnlyList<SheetTabContextMenuCommand> Commands = BuildCommands();

    public static IReadOnlyList<SheetTabContextMenuCommand> BuildSheetTabCommands(
        SheetTabContextMenuState? state = null)
    {
        state ??= SheetTabContextMenuState.Default;

        if (state == SheetTabContextMenuState.Default)
            return Commands;

        return BuildCommands(state);
    }

    private static IReadOnlyList<SheetTabContextMenuCommand> BuildCommands(
        SheetTabContextMenuState? state = null)
    {
        state ??= SheetTabContextMenuState.Default;

        return Freeze([
            new("MainWindow_Header_InsertSheet", SheetTabContextMenuAction.InsertSheet, KeyTip: "I", CommandName: "Insert Sheet"),
            new("MainWindow_Header_DeleteSheet", SheetTabContextMenuAction.DeleteSheet, KeyTip: "E", CommandName: "Delete Sheet", IsEnabled: state.CanDeleteSheet),
            new("MainWindow_Header_Rename", SheetTabContextMenuAction.Rename, KeyTip: "R", CommandName: "Rename"),
            new("MainWindow_Header_MoveOrCopy", SheetTabContextMenuAction.MoveOrCopy, KeyTip: "M", CommandName: "Move or Copy"),
            SheetTabContextMenuCommand.Separator,
            new("MainWindow_Header_ViewCode", SheetTabContextMenuAction.ViewCode, KeyTip: "V", CommandName: "View Code", IsEnabled: false),
            new("MainWindow_Header_ProtectSheet", SheetTabContextMenuAction.ProtectSheet, KeyTip: "P", CommandName: "Protect Sheet"),
            new("MainWindow_Header_TabColor", SheetTabContextMenuAction.TabColor, KeyTip: "T", CommandName: "Tab Color"),
            SheetTabContextMenuCommand.Separator,
            new("MainWindow_Header_Hide", SheetTabContextMenuAction.Hide, KeyTip: "H", CommandName: "Hide", IsEnabled: state.CanHideSheet),
            new("MainWindow_Header_Unhide", SheetTabContextMenuAction.Unhide, KeyTip: "U", CommandName: "Unhide", IsEnabled: state.CanUnhideSheet),
            SheetTabContextMenuCommand.Separator,
            new("MainWindow_Header_SelectAllSheets", SheetTabContextMenuAction.SelectAllSheets, KeyTip: "A", CommandName: "Select All Sheets", IsEnabled: state.CanSelectAllSheets),
            new("MainWindow_Header_UngroupSheets", SheetTabContextMenuAction.UngroupSheets, KeyTip: "G", CommandName: "Ungroup Sheets", IsEnabled: state.CanUngroupSheets)
        ]);
    }

    private static IReadOnlyList<SheetTabContextMenuCommand> Freeze(SheetTabContextMenuCommand[] commands) =>
        Array.AsReadOnly(commands);
}

public sealed record SheetTabContextMenuCommand(
    string ResourceKey,
    SheetTabContextMenuAction Action,
    bool IsSeparator = false,
    string? KeyTip = null,
    string? CommandName = null,
    bool IsEnabled = true)
{
    public static SheetTabContextMenuCommand Separator { get; } =
        new("", SheetTabContextMenuAction.None, IsSeparator: true, IsEnabled: false);

    public string KeyTip { get; init; } = KeyTip ?? "";

    public string CommandName { get; init; } = CommandName ?? "";
}

public sealed record SheetTabContextMenuState(
    bool CanDeleteSheet = true,
    bool CanHideSheet = true,
    bool CanUnhideSheet = true,
    bool CanSelectAllSheets = true,
    bool CanUngroupSheets = true)
{
    public static SheetTabContextMenuState Default { get; } = new();
}

public enum SheetTabContextMenuAction
{
    None,
    InsertSheet,
    DeleteSheet,
    Rename,
    MoveOrCopy,
    ViewCode,
    ProtectSheet,
    TabColor,
    Hide,
    Unhide,
    SelectAllSheets,
    UngroupSheets
}
