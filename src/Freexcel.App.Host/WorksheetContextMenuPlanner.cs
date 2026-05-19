namespace Freexcel.App.Host;

public static class WorksheetContextMenuPlanner
{
    public static IReadOnlyList<WorksheetContextMenuCommand> BuildCommands() =>
    [
        new("Cut", WorksheetContextMenuAction.Cut),
        new("Copy", WorksheetContextMenuAction.Copy),
        new("Paste", WorksheetContextMenuAction.Paste),
        new("Paste Special...", WorksheetContextMenuAction.PasteSpecial),
        WorksheetContextMenuCommand.Separator,
        new("Insert...", WorksheetContextMenuAction.InsertCells),
        new("Insert Row Above", WorksheetContextMenuAction.InsertRowAbove),
        new("Insert Row Below", WorksheetContextMenuAction.InsertRowBelow),
        new("Insert Column Left", WorksheetContextMenuAction.InsertColumnLeft),
        new("Insert Column Right", WorksheetContextMenuAction.InsertColumnRight),
        WorksheetContextMenuCommand.Separator,
        new("Delete...", WorksheetContextMenuAction.DeleteCells),
        new("Delete Row(s)", WorksheetContextMenuAction.DeleteRows),
        new("Delete Column(s)", WorksheetContextMenuAction.DeleteColumns),
        WorksheetContextMenuCommand.Separator,
        new("Sort A to Z", WorksheetContextMenuAction.SortAscending),
        new("Sort Z to A", WorksheetContextMenuAction.SortDescending),
        new("Filter...", WorksheetContextMenuAction.Filter),
        new("Clear Filter", WorksheetContextMenuAction.ClearFilter),
        new("Pick From Drop-down List...", WorksheetContextMenuAction.PickFromDropDown),
        WorksheetContextMenuCommand.Separator,
        new("Hide Rows", WorksheetContextMenuAction.HideRows),
        new("Unhide Rows", WorksheetContextMenuAction.UnhideRows),
        new("Hide Columns", WorksheetContextMenuAction.HideColumns),
        new("Unhide Columns", WorksheetContextMenuAction.UnhideColumns),
        WorksheetContextMenuCommand.Separator,
        new("New Note", WorksheetContextMenuAction.NewNote),
        new("Edit Note...", WorksheetContextMenuAction.EditNote),
        new("Delete Note", WorksheetContextMenuAction.DeleteNote),
        new("Show Notes", WorksheetContextMenuAction.ShowNotes),
        new("Hyperlink...", WorksheetContextMenuAction.Hyperlink),
        WorksheetContextMenuCommand.Separator,
        new("Format Cells...", WorksheetContextMenuAction.FormatCells),
        WorksheetContextMenuCommand.Separator,
        new("Clear All", WorksheetContextMenuAction.ClearAll),
        new("Clear Formats", WorksheetContextMenuAction.ClearFormats),
        new("Clear Comments", WorksheetContextMenuAction.ClearComments),
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
    PasteSpecial,
    InsertCells,
    InsertRowAbove,
    InsertRowBelow,
    InsertColumnLeft,
    InsertColumnRight,
    DeleteCells,
    DeleteRows,
    DeleteColumns,
    SortAscending,
    SortDescending,
    Filter,
    ClearFilter,
    PickFromDropDown,
    HideRows,
    UnhideRows,
    HideColumns,
    UnhideColumns,
    NewNote,
    EditNote,
    DeleteNote,
    ShowNotes,
    Hyperlink,
    FormatCells,
    ClearAll,
    ClearFormats,
    ClearComments,
    ClearHyperlinks,
    ClearContents
}
