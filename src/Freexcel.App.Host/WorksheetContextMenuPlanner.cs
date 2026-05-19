namespace Freexcel.App.Host;

public static class WorksheetContextMenuPlanner
{
    public static IReadOnlyList<WorksheetContextMenuCommand> BuildCommands() =>
    [
        new("Cut", WorksheetContextMenuAction.Cut),
        new("Copy", WorksheetContextMenuAction.Copy),
        new("Paste", WorksheetContextMenuAction.Paste),
        WorksheetContextMenuCommand.Separator,
        new("Insert Row Above", WorksheetContextMenuAction.InsertRowAbove),
        new("Insert Row Below", WorksheetContextMenuAction.InsertRowBelow),
        new("Insert Column Left", WorksheetContextMenuAction.InsertColumnLeft),
        new("Insert Column Right", WorksheetContextMenuAction.InsertColumnRight),
        WorksheetContextMenuCommand.Separator,
        new("Delete Row(s)", WorksheetContextMenuAction.DeleteRows),
        new("Delete Column(s)", WorksheetContextMenuAction.DeleteColumns),
        WorksheetContextMenuCommand.Separator,
        new("Sort A to Z", WorksheetContextMenuAction.SortAscending),
        new("Sort Z to A", WorksheetContextMenuAction.SortDescending),
        new("Filter...", WorksheetContextMenuAction.Filter),
        WorksheetContextMenuCommand.Separator,
        new("Hide Rows", WorksheetContextMenuAction.HideRows),
        new("Unhide Rows", WorksheetContextMenuAction.UnhideRows),
        new("Hide Columns", WorksheetContextMenuAction.HideColumns),
        new("Unhide Columns", WorksheetContextMenuAction.UnhideColumns),
        WorksheetContextMenuCommand.Separator,
        new("New Note", WorksheetContextMenuAction.NewNote),
        new("Hyperlink...", WorksheetContextMenuAction.Hyperlink),
        WorksheetContextMenuCommand.Separator,
        new("Format Cells...", WorksheetContextMenuAction.FormatCells),
        WorksheetContextMenuCommand.Separator,
        new("Clear Formats", WorksheetContextMenuAction.ClearFormats),
        new("Clear Hyperlinks", WorksheetContextMenuAction.ClearHyperlinks),
        new("Clear Contents", WorksheetContextMenuAction.ClearContents)
    ];
}

public sealed record WorksheetContextMenuCommand(string Header, WorksheetContextMenuAction Action, bool IsSeparator = false)
{
    public static WorksheetContextMenuCommand Separator { get; } =
        new("", WorksheetContextMenuAction.None, IsSeparator: true);
}

public enum WorksheetContextMenuAction
{
    None,
    Cut,
    Copy,
    Paste,
    InsertRowAbove,
    InsertRowBelow,
    InsertColumnLeft,
    InsertColumnRight,
    DeleteRows,
    DeleteColumns,
    SortAscending,
    SortDescending,
    Filter,
    HideRows,
    UnhideRows,
    HideColumns,
    UnhideColumns,
    NewNote,
    Hyperlink,
    FormatCells,
    ClearFormats,
    ClearHyperlinks,
    ClearContents
}
