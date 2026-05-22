namespace Freexcel.App.Host;

public static class WorksheetContextMenuPlanner
{
    public static IReadOnlyList<WorksheetContextMenuCommand> BuildCommands(
        WorksheetContextMenuTargetKind targetKind = WorksheetContextMenuTargetKind.Worksheet)
    {
        return targetKind switch
        {
            WorksheetContextMenuTargetKind.Picture => BuildPictureCommands(),
            WorksheetContextMenuTargetKind.Shape => BuildDrawingObjectCommands("Format Shape...", includeReorder: true),
            WorksheetContextMenuTargetKind.TextBox => BuildDrawingObjectCommands("Format Text Box...", includeReorder: false),
            _ => BuildWorksheetCommands()
        };
    }

    private static IReadOnlyList<WorksheetContextMenuCommand> BuildWorksheetCommands() =>
    [
        new("Cut", WorksheetContextMenuAction.Cut, AccessHeader: "Cu_t"),
        new("Copy", WorksheetContextMenuAction.Copy, AccessHeader: "_Copy"),
        new("Paste", WorksheetContextMenuAction.Paste, AccessHeader: "_Paste"),
        new("Paste Special...", WorksheetContextMenuAction.PasteSpecial, AccessHeader: "Paste _Special..."),
        new("Insert Copied Cells...", WorksheetContextMenuAction.InsertCopiedCells, AccessHeader: "Insert Copied _Cells..."),
        WorksheetContextMenuCommand.Separator,
        new("Insert...", WorksheetContextMenuAction.InsertCells, AccessHeader: "_Insert..."),
        new("Insert Row Above", WorksheetContextMenuAction.InsertRowAbove, AccessHeader: "Insert Row _Above"),
        new("Insert Row Below", WorksheetContextMenuAction.InsertRowBelow, AccessHeader: "Insert Row _Below"),
        new("Insert Column Left", WorksheetContextMenuAction.InsertColumnLeft, AccessHeader: "Insert Column _Left"),
        new("Insert Column Right", WorksheetContextMenuAction.InsertColumnRight, AccessHeader: "Insert Column _Right"),
        WorksheetContextMenuCommand.Separator,
        new("Delete...", WorksheetContextMenuAction.DeleteCells, AccessHeader: "_Delete..."),
        new("Delete Row(s)", WorksheetContextMenuAction.DeleteRows, AccessHeader: "Delete _Row(s)"),
        new("Delete Column(s)", WorksheetContextMenuAction.DeleteColumns, AccessHeader: "Delete _Column(s)"),
        WorksheetContextMenuCommand.Separator,
        new("Sort A to Z", WorksheetContextMenuAction.SortAscending, AccessHeader: "Sort _A to Z"),
        new("Sort Z to A", WorksheetContextMenuAction.SortDescending, AccessHeader: "Sort _Z to A"),
        new("Custom Sort...", WorksheetContextMenuAction.CustomSort, AccessHeader: "C_ustom Sort..."),
        new("Filter...", WorksheetContextMenuAction.Filter, AccessHeader: "_Filter..."),
        new("Clear Filter", WorksheetContextMenuAction.ClearFilter, AccessHeader: "C_lear Filter"),
        new("Reapply Filter", WorksheetContextMenuAction.ReapplyFilter, AccessHeader: "_Reapply Filter"),
        new("Pick From Drop-down List...", WorksheetContextMenuAction.PickFromDropDown, AccessHeader: "Pick From _Drop-down List..."),
        new("Quick Analysis", WorksheetContextMenuAction.QuickAnalysis, AccessHeader: "_Quick Analysis"),
        new("Define Name...", WorksheetContextMenuAction.DefineName, AccessHeader: "Define _Name..."),
        new("Create Table...", WorksheetContextMenuAction.CreateTable, AccessHeader: "Create Ta_ble..."),
        new("Format as Table...", WorksheetContextMenuAction.FormatAsTable, AccessHeader: "Format as _Table..."),
        new("Text to Columns...", WorksheetContextMenuAction.TextToColumns, AccessHeader: "Te_xt to Columns..."),
        new("Remove Duplicates...", WorksheetContextMenuAction.RemoveDuplicates, AccessHeader: "Remove D_uplicates..."),
        new("Data Validation...", WorksheetContextMenuAction.DataValidation, AccessHeader: "Data _Validation..."),
        WorksheetContextMenuCommand.Separator,
        new("Hide Rows", WorksheetContextMenuAction.HideRows, AccessHeader: "_Hide Rows"),
        new("Unhide Rows", WorksheetContextMenuAction.UnhideRows, AccessHeader: "Unhide Ro_ws"),
        new("Row Height...", WorksheetContextMenuAction.RowHeight, AccessHeader: "Row _Height..."),
        new("AutoFit Row Height", WorksheetContextMenuAction.AutoFitRowHeight, AccessHeader: "AutoFit Row He_ight"),
        new("Hide Columns", WorksheetContextMenuAction.HideColumns, AccessHeader: "Hide Col_umns"),
        new("Unhide Columns", WorksheetContextMenuAction.UnhideColumns, AccessHeader: "Unhide Co_lumns"),
        new("Column Width...", WorksheetContextMenuAction.ColumnWidth, AccessHeader: "Column _Width..."),
        new("AutoFit Column Width", WorksheetContextMenuAction.AutoFitColumnWidth, AccessHeader: "AutoFit Column Wi_dth"),
        WorksheetContextMenuCommand.Separator,
        new("New Comment", WorksheetContextMenuAction.NewComment, AccessHeader: "New Co_mment"),
        new("Edit Comment...", WorksheetContextMenuAction.EditComment, AccessHeader: "_Edit Comment..."),
        new("Delete Comment", WorksheetContextMenuAction.DeleteComment, AccessHeader: "Delete _Comment"),
        new("New Note", WorksheetContextMenuAction.NewNote, AccessHeader: "New No_te"),
        new("Edit Note...", WorksheetContextMenuAction.EditNote, AccessHeader: "_Edit Note..."),
        new("Delete Note", WorksheetContextMenuAction.DeleteNote, AccessHeader: "De_lete Note"),
        new("Show Notes", WorksheetContextMenuAction.ShowNotes, AccessHeader: "_Show Notes"),
        new("Hyperlink...", WorksheetContextMenuAction.Hyperlink, AccessHeader: "_Hyperlink..."),
        WorksheetContextMenuCommand.Separator,
        new("Format Cells...", WorksheetContextMenuAction.FormatCells, AccessHeader: "_Format Cells..."),
        WorksheetContextMenuCommand.Separator,
        new("Clear All", WorksheetContextMenuAction.ClearAll, AccessHeader: "Clear _All"),
        new("Clear Formats", WorksheetContextMenuAction.ClearFormats, AccessHeader: "Clear _Formats"),
        new("Clear Comments", WorksheetContextMenuAction.ClearComments, AccessHeader: "Clear Co_mments"),
        new("Clear Hyperlinks", WorksheetContextMenuAction.ClearHyperlinks, AccessHeader: "Clear _Hyperlinks"),
        new("Clear Contents", WorksheetContextMenuAction.ClearContents, AccessHeader: "Clear C_ontents")
    ];

    private static IReadOnlyList<WorksheetContextMenuCommand> BuildPictureCommands() =>
    [
        new("Format Picture...", WorksheetContextMenuAction.FormatPicture, AccessHeader: "_Format Picture..."),
        new("Crop...", WorksheetContextMenuAction.CropPicture, AccessHeader: "_Crop..."),
        new("Reset Crop", WorksheetContextMenuAction.ResetPictureCrop, AccessHeader: "_Reset Crop")
    ];

    private static IReadOnlyList<WorksheetContextMenuCommand> BuildDrawingObjectCommands(
        string formatHeader,
        bool includeReorder)
    {
        var commands = new List<WorksheetContextMenuCommand>
        {
            new(formatHeader, WorksheetContextMenuAction.FormatDrawingObject, AccessHeader: "_Format..."),
            new("Size and Properties...", WorksheetContextMenuAction.ResizeDrawingObject, AccessHeader: "_Size and Properties..."),
            new("Rotate...", WorksheetContextMenuAction.RotateDrawingObject, AccessHeader: "_Rotate..."),
            new("Shape Fill...", WorksheetContextMenuAction.ShapeFill, AccessHeader: "Shape _Fill..."),
            new("Shape Outline...", WorksheetContextMenuAction.ShapeOutline, AccessHeader: "Shape _Outline...")
        };

        if (includeReorder)
        {
            commands.Add(WorksheetContextMenuCommand.Separator);
            commands.Add(new WorksheetContextMenuCommand("Bring Forward", WorksheetContextMenuAction.BringForward, AccessHeader: "Bring _Forward"));
            commands.Add(new WorksheetContextMenuCommand("Send Backward", WorksheetContextMenuAction.SendBackward, AccessHeader: "Send _Backward"));
        }

        return commands;
    }
}

public sealed record WorksheetContextMenuCommand(
    string Header,
    WorksheetContextMenuAction Action,
    bool IsSeparator = false,
    string? AccessHeader = null)
{
    public static WorksheetContextMenuCommand Separator { get; } =
        new("", WorksheetContextMenuAction.None, IsSeparator: true);

    public string AccessHeader { get; init; } = AccessHeader ?? Header;
}

public enum WorksheetContextMenuAction
{
    None,
    Cut,
    Copy,
    Paste,
    PasteSpecial,
    InsertCopiedCells,
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
    CustomSort,
    Filter,
    ClearFilter,
    ReapplyFilter,
    PickFromDropDown,
    QuickAnalysis,
    DefineName,
    CreateTable,
    FormatAsTable,
    TextToColumns,
    RemoveDuplicates,
    DataValidation,
    HideRows,
    UnhideRows,
    RowHeight,
    AutoFitRowHeight,
    HideColumns,
    UnhideColumns,
    ColumnWidth,
    AutoFitColumnWidth,
    NewComment,
    EditComment,
    DeleteComment,
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
    ClearContents,
    FormatPicture,
    CropPicture,
    ResetPictureCrop,
    FormatDrawingObject,
    ResizeDrawingObject,
    RotateDrawingObject,
    ShapeFill,
    ShapeOutline,
    BringForward,
    SendBackward
}

public enum WorksheetContextMenuTargetKind
{
    Worksheet,
    Picture,
    Shape,
    TextBox
}
