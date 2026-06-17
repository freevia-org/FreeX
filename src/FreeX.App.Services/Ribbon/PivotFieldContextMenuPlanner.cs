namespace FreeX.App.Services.Ribbon;

/// <summary>
/// Platform-neutral planner that describes the PivotTable field-area context menu (the right-click menu
/// shared by the available-fields list and the Filters/Columns/Rows/Values bucket lists) as a declarative
/// command tree, mirroring <see cref="SheetTabContextMenuPlanner"/>. The host (WPF today) resolves each
/// command's <see cref="PivotFieldContextMenuCommand.ResourceKey"/> into a localized header, applies the
/// explicit <see cref="PivotFieldContextMenuCommand.KeyTip"/>, and dispatches the
/// <see cref="PivotFieldContextMenuAction"/> to the existing pivot-field Click handlers. The hand-authored
/// XAML duplicated this menu five times; single-sourcing it here keeps the labels/order/keytips identical
/// while removing the copy/paste drift.
/// </summary>
public static class PivotFieldContextMenuPlanner
{
    private static readonly IReadOnlyList<PivotFieldContextMenuCommand> AvailableFieldsCommands =
        BuildCommands(includeRemove: false);

    private static readonly IReadOnlyList<PivotFieldContextMenuCommand> BucketCommands =
        BuildCommands(includeRemove: true);

    /// <summary>
    /// Builds the field-area context menu commands. The available-fields list omits the trailing
    /// separator + "Remove" pair the four bucket lists (Filters/Columns/Rows/Values) carry, matching the
    /// previous XAML exactly.
    /// </summary>
    public static IReadOnlyList<PivotFieldContextMenuCommand> BuildPivotFieldCommands(bool includeRemove) =>
        includeRemove ? BucketCommands : AvailableFieldsCommands;

    private static IReadOnlyList<PivotFieldContextMenuCommand> BuildCommands(bool includeRemove)
    {
        var commands = new List<PivotFieldContextMenuCommand>
        {
            new("MainWindow_Header_SortAToZ", PivotFieldContextMenuAction.SortAscending, KeyTip: "S", CommandName: "Sort A to Z"),
            new("MainWindow_Header_SortZToA", PivotFieldContextMenuAction.SortDescending, KeyTip: "O", CommandName: "Sort Z to A"),
            new("MainWindow_Header_SelectItems", PivotFieldContextMenuAction.SelectItems, KeyTip: "I", CommandName: "Select Items"),
            new("MainWindow_Header_LabelFilter", PivotFieldContextMenuAction.LabelFilter, KeyTip: "L", CommandName: "Label Filter"),
            new("MainWindow_Header_ValueFilter", PivotFieldContextMenuAction.ValueFilter, KeyTip: "F", CommandName: "Value Filter"),
            new("MainWindow_Header_ClearFilter", PivotFieldContextMenuAction.ClearFilter, KeyTip: "C", CommandName: "Clear Filter"),
            PivotFieldContextMenuCommand.Separator,
            new("MainWindow_Header_ValueFieldSettings", PivotFieldContextMenuAction.ValueFieldSettings, KeyTip: "V", CommandName: "Value Field Settings"),
        };

        if (includeRemove)
        {
            commands.Add(PivotFieldContextMenuCommand.Separator);
            commands.Add(new("MainWindow_Content_Remove", PivotFieldContextMenuAction.Remove, KeyTip: "R", CommandName: "Remove"));
        }

        return Array.AsReadOnly(commands.ToArray());
    }
}

public sealed record PivotFieldContextMenuCommand(
    string ResourceKey,
    PivotFieldContextMenuAction Action,
    bool IsSeparator = false,
    string? KeyTip = null,
    string? CommandName = null,
    bool IsEnabled = true)
{
    public static PivotFieldContextMenuCommand Separator { get; } =
        new("", PivotFieldContextMenuAction.None, IsSeparator: true, IsEnabled: false);

    public string KeyTip { get; init; } = KeyTip ?? "";

    public string CommandName { get; init; } = CommandName ?? "";
}

public enum PivotFieldContextMenuAction
{
    None,
    SortAscending,
    SortDescending,
    SelectItems,
    LabelFilter,
    ValueFilter,
    ClearFilter,
    ValueFieldSettings,
    Remove
}
