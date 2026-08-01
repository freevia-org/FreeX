namespace FreeX.App.Services.Ribbon;

public static class WorksheetContextMenuPlanner
{
    private const int WorksheetStateCacheSize = 1 << 8;

    private static readonly IReadOnlyList<WorksheetContextMenuCommand> PictureCommands =
        BuildPictureCommands();

    private static readonly IReadOnlyList<WorksheetContextMenuCommand> ShapeCommands =
        BuildDrawingObjectCommands("Format Shape...", includeReorder: true);

    private static readonly IReadOnlyList<WorksheetContextMenuCommand> TextBoxCommands =
        BuildDrawingObjectCommands("Format Text Box...", includeReorder: false);

    private static readonly IReadOnlyList<WorksheetContextMenuCommand> ChartCommands =
        BuildChartCommands();

    private static readonly IReadOnlyList<WorksheetContextMenuCommand> RowSelectionCommands =
        BuildRowSelectionCommands();

    private static readonly IReadOnlyList<WorksheetContextMenuCommand> ColumnSelectionCommands =
        BuildColumnSelectionCommands();

    private static readonly IReadOnlyList<WorksheetContextMenuCommand>[] WorksheetCommandCache =
        CreateWorksheetCommandCache();

    public static IReadOnlyList<WorksheetContextMenuCommand> BuildCommands(
        WorksheetContextMenuTargetKind targetKind = WorksheetContextMenuTargetKind.Worksheet,
        WorksheetContextMenuState? state = null)
    {
        state ??= WorksheetContextMenuState.Default;

        return targetKind switch
        {
            WorksheetContextMenuTargetKind.Picture => PictureCommands,
            WorksheetContextMenuTargetKind.Shape => ShapeCommands,
            WorksheetContextMenuTargetKind.TextBox => TextBoxCommands,
            WorksheetContextMenuTargetKind.Chart => ChartCommands,
            WorksheetContextMenuTargetKind.RowSelection => RowSelectionCommands,
            WorksheetContextMenuTargetKind.ColumnSelection => ColumnSelectionCommands,
            _ => WorksheetCommandCache[GetStateCacheIndex(state)]
        };
    }

    /// <summary>
    /// Maps a selected drawing object's <see cref="FreeX.Core.Model.SelectionPaneObjectKind"/> to the
    /// matching per-target context-menu kind, so the shells can raise the right object menu
    /// (Picture / Shape / TextBox / Chart) when a drawing object is right-clicked in the grid.
    /// Kinds without a dedicated drawing menu (slicers, timelines, form controls) fall back to the
    /// generic worksheet menu.
    /// </summary>
    public static WorksheetContextMenuTargetKind TargetKindForObject(FreeX.Core.Model.SelectionPaneObjectKind kind) =>
        kind switch
        {
            FreeX.Core.Model.SelectionPaneObjectKind.Picture => WorksheetContextMenuTargetKind.Picture,
            FreeX.Core.Model.SelectionPaneObjectKind.Shape => WorksheetContextMenuTargetKind.Shape,
            FreeX.Core.Model.SelectionPaneObjectKind.TextBox => WorksheetContextMenuTargetKind.TextBox,
            FreeX.Core.Model.SelectionPaneObjectKind.Chart => WorksheetContextMenuTargetKind.Chart,
            _ => WorksheetContextMenuTargetKind.Worksheet
        };

    private static IReadOnlyList<WorksheetContextMenuCommand>[] CreateWorksheetCommandCache()
    {
        var cache = new IReadOnlyList<WorksheetContextMenuCommand>[WorksheetStateCacheSize];
        for (var index = 0; index < cache.Length; index++)
            cache[index] = BuildWorksheetCommands(CreateState(index));

        return cache;
    }

    private static WorksheetContextMenuState CreateState(int index) =>
        new(
            HasThreadedComment: (index & 1) != 0,
            IsThreadedCommentResolved: (index & (1 << 1)) != 0,
            HasNote: (index & (1 << 2)) != 0,
            HasHyperlink: (index & (1 << 3)) != 0,
            HasAutoFilterHeaderTarget: (index & (1 << 4)) != 0,
            HasDropdownTarget: (index & (1 << 5)) != 0,
            HasPivotTableTarget: (index & (1 << 6)) != 0,
            NoteIsShown: (index & (1 << 7)) != 0);

    private static int GetStateCacheIndex(WorksheetContextMenuState state)
    {
        var index = 0;
        if (state.HasThreadedComment)
            index |= 1;
        if (state.IsThreadedCommentResolved)
            index |= 1 << 1;
        if (state.HasNote)
            index |= 1 << 2;
        if (state.HasHyperlink)
            index |= 1 << 3;
        if (state.HasAutoFilterHeaderTarget)
            index |= 1 << 4;
        if (state.HasDropdownTarget)
            index |= 1 << 5;
        if (state.HasPivotTableTarget)
            index |= 1 << 6;
        if (state.NoteIsShown)
            index |= 1 << 7;

        return index;
    }

    private static IReadOnlyList<WorksheetContextMenuCommand> Freeze(WorksheetContextMenuCommand[] commands) =>
        Array.AsReadOnly(commands);

    private static WorksheetContextMenuCommand Submenu(
        string header,
        string accessHeader,
        params WorksheetContextMenuCommand[] children) =>
        new(header, WorksheetContextMenuAction.None, AccessHeader: accessHeader, Children: Freeze(children));

    private static IReadOnlyList<WorksheetContextMenuCommand> BuildWorksheetCommands(WorksheetContextMenuState state) =>
        Freeze([
        new("Cut", WorksheetContextMenuAction.Cut, AccessHeader: "Cu_t"),
        new("Copy", WorksheetContextMenuAction.Copy, AccessHeader: "_Copy"),
        new("Paste", WorksheetContextMenuAction.Paste, AccessHeader: "_Paste"),
        WorksheetContextMenuCommand.Separator,
        Submenu(
            "Paste Options",
            "_Paste Options",
            new WorksheetContextMenuCommand("Paste Special...", WorksheetContextMenuAction.PasteSpecial, AccessHeader: "Paste _Special..."),
            new WorksheetContextMenuCommand("Insert Copied Cells...", WorksheetContextMenuAction.InsertCopiedCells, AccessHeader: "Insert Copied _Cells...")),
        Submenu(
            "Insert and Delete",
            "_Insert and Delete",
            new WorksheetContextMenuCommand("Insert...", WorksheetContextMenuAction.InsertCells, AccessHeader: "_Insert..."),
            new WorksheetContextMenuCommand("Insert Row Above", WorksheetContextMenuAction.InsertRowAbove, AccessHeader: "Insert Row _Above"),
            new WorksheetContextMenuCommand("Insert Row Below", WorksheetContextMenuAction.InsertRowBelow, AccessHeader: "Insert Row _Below"),
            new WorksheetContextMenuCommand("Insert Column Left", WorksheetContextMenuAction.InsertColumnLeft, AccessHeader: "Insert Column _Left"),
            new WorksheetContextMenuCommand("Insert Column Right", WorksheetContextMenuAction.InsertColumnRight, AccessHeader: "Insert Column _Right"),
            WorksheetContextMenuCommand.Separator,
            new WorksheetContextMenuCommand("Delete...", WorksheetContextMenuAction.DeleteCells, AccessHeader: "_Delete..."),
            new WorksheetContextMenuCommand("Delete Row(s)", WorksheetContextMenuAction.DeleteRows, AccessHeader: "Delete _Row(s)"),
            new WorksheetContextMenuCommand("Delete Column(s)", WorksheetContextMenuAction.DeleteColumns, AccessHeader: "Delete _Column(s)")),
        WorksheetContextMenuCommand.Separator,
        Submenu(
            "Sort and Filter",
            "Sort and _Filter",
            new WorksheetContextMenuCommand("Sort A to Z", WorksheetContextMenuAction.SortAscending, AccessHeader: "Sort _A to Z"),
            new WorksheetContextMenuCommand("Sort Z to A", WorksheetContextMenuAction.SortDescending, AccessHeader: "Sort _Z to A"),
            new WorksheetContextMenuCommand("Custom Sort...", WorksheetContextMenuAction.CustomSort, AccessHeader: "C_ustom Sort..."),
            WorksheetContextMenuCommand.Separator,
            new WorksheetContextMenuCommand("Filter...", WorksheetContextMenuAction.Filter, AccessHeader: "_Filter..."),
            new WorksheetContextMenuCommand("Clear Filter", WorksheetContextMenuAction.ClearFilter, AccessHeader: "C_lear Filter", IsEnabled: state.HasAutoFilterHeaderTarget),
            new WorksheetContextMenuCommand("Reapply Filter", WorksheetContextMenuAction.ReapplyFilter, AccessHeader: "_Reapply Filter", IsEnabled: state.HasAutoFilterHeaderTarget),
            new WorksheetContextMenuCommand("Pick From Drop-down List...", WorksheetContextMenuAction.PickFromDropDown, AccessHeader: "Pick From _Drop-down List...", IsEnabled: state.HasDropdownTarget)),
        new("Quick Analysis", WorksheetContextMenuAction.QuickAnalysis, AccessHeader: "_Quick Analysis"),
        Submenu(
            "Data Tools",
            "Data _Tools",
            new WorksheetContextMenuCommand("Define Name...", WorksheetContextMenuAction.DefineName, AccessHeader: "Define _Name..."),
            new WorksheetContextMenuCommand("Create Table...", WorksheetContextMenuAction.CreateTable, AccessHeader: "Create Ta_ble..."),
            new WorksheetContextMenuCommand("Format as Table...", WorksheetContextMenuAction.FormatAsTable, AccessHeader: "Format as _Table..."),
            WorksheetContextMenuCommand.Separator,
            new WorksheetContextMenuCommand("Text to Columns...", WorksheetContextMenuAction.TextToColumns, AccessHeader: "Te_xt to Columns..."),
            new WorksheetContextMenuCommand("Remove Duplicates...", WorksheetContextMenuAction.RemoveDuplicates, AccessHeader: "Remove D_uplicates..."),
            new WorksheetContextMenuCommand("Data Validation...", WorksheetContextMenuAction.DataValidation, AccessHeader: "Data _Validation...")),
        Submenu(
            "Rows and Columns",
            "_Rows and Columns",
            [.. BuildRowSizingVisibilityCommands(), WorksheetContextMenuCommand.Separator, .. BuildColumnSizingVisibilityCommands()]),
        WorksheetContextMenuCommand.Separator,
        Submenu(
            "Comments and Notes",
            "Co_mments and Notes",
            new WorksheetContextMenuCommand("New Comment", WorksheetContextMenuAction.NewComment, AccessHeader: "New Co_mment"),
            new WorksheetContextMenuCommand("Edit Comment...", WorksheetContextMenuAction.EditComment, AccessHeader: "_Edit Comment...", IsEnabled: state.HasThreadedComment),
            BuildThreadedCommentResolveCommand(state),
            new WorksheetContextMenuCommand("Delete Comment", WorksheetContextMenuAction.DeleteComment, AccessHeader: "Delete _Comment", IsEnabled: state.HasThreadedComment),
            WorksheetContextMenuCommand.Separator,
            new WorksheetContextMenuCommand("New Note", WorksheetContextMenuAction.NewNote, AccessHeader: "New No_te"),
            new WorksheetContextMenuCommand("Edit Note...", WorksheetContextMenuAction.EditNote, AccessHeader: "_Edit Note...", IsEnabled: state.HasNote),
            new WorksheetContextMenuCommand("Delete Note", WorksheetContextMenuAction.DeleteNote, AccessHeader: "De_lete Note", IsEnabled: state.HasNote),
            new WorksheetContextMenuCommand(state.NoteIsShown ? "Hide Note" : "Show Note", WorksheetContextMenuAction.ShowHideNote, AccessHeader: state.NoteIsShown ? "_Hide Note" : "S_how Note", IsEnabled: state.HasNote),
            new WorksheetContextMenuCommand("Show Notes", WorksheetContextMenuAction.ShowAllNotes, AccessHeader: "_Show Notes")),
        .. BuildHyperlinkCommands(state),
        .. BuildPivotTableCommands(state),
        WorksheetContextMenuCommand.Separator,
        new("Format Cells...", WorksheetContextMenuAction.FormatCells, AccessHeader: "_Format Cells..."),
        Submenu(
            "Clear",
            "C_lear",
            new WorksheetContextMenuCommand("Clear Contents", WorksheetContextMenuAction.ClearContents, AccessHeader: "Clear C_ontents"),
            new WorksheetContextMenuCommand("Clear All", WorksheetContextMenuAction.ClearAll, AccessHeader: "Clear _All"),
            new WorksheetContextMenuCommand("Clear Formats", WorksheetContextMenuAction.ClearFormats, AccessHeader: "Clear _Formats"),
            new WorksheetContextMenuCommand("Clear Comments and Notes", WorksheetContextMenuAction.ClearComments, AccessHeader: "Clear Co_mments and Notes"),
            new WorksheetContextMenuCommand("Clear Hyperlinks", WorksheetContextMenuAction.ClearHyperlinks, AccessHeader: "Clear _Hyperlinks", IsEnabled: state.HasHyperlink))
    ]);

    private static IReadOnlyList<WorksheetContextMenuCommand> BuildPictureCommands() =>
        Freeze([
        new("Cut", WorksheetContextMenuAction.Cut, AccessHeader: "Cu_t"),
        new("Copy", WorksheetContextMenuAction.Copy, AccessHeader: "_Copy"),
        new("Paste", WorksheetContextMenuAction.Paste, AccessHeader: "_Paste"),
        WorksheetContextMenuCommand.Separator,
        new("Format Picture...", WorksheetContextMenuAction.FormatPicture, AccessHeader: "_Format Picture..."),
        new("Crop...", WorksheetContextMenuAction.CropPicture, AccessHeader: "_Crop..."),
        new("Reset Crop", WorksheetContextMenuAction.ResetPictureCrop, AccessHeader: "_Reset Crop"),
        WorksheetContextMenuCommand.Separator,
        new("Edit Alt Text...", WorksheetContextMenuAction.EditAltText, AccessHeader: "Edit _Alt Text..."),
        new("Selection Pane...", WorksheetContextMenuAction.SelectionPane, AccessHeader: "_Selection Pane...")
    ]);

    private static IReadOnlyList<WorksheetContextMenuCommand> BuildChartCommands() =>
        Freeze([
        new("Cut", WorksheetContextMenuAction.Cut, AccessHeader: "Cu_t"),
        new("Copy", WorksheetContextMenuAction.Copy, AccessHeader: "_Copy"),
        new("Paste", WorksheetContextMenuAction.Paste, AccessHeader: "_Paste"),
        WorksheetContextMenuCommand.Separator,
        new("Format Chart Area...", WorksheetContextMenuAction.FormatChartArea, AccessHeader: "_Format Chart Area..."),
        new("Select Data...", WorksheetContextMenuAction.SelectChartData, AccessHeader: "Select _Data..."),
        new("Change Chart Type...", WorksheetContextMenuAction.ChangeChartType, AccessHeader: "_Change Chart Type..."),
        new("Chart Styles...", WorksheetContextMenuAction.ChartStyles, AccessHeader: "Chart _Styles..."),
        new("Chart Titles...", WorksheetContextMenuAction.ChartTitles, AccessHeader: "Chart _Titles..."),
        new("Size and Properties...", WorksheetContextMenuAction.ChartSizeAndProperties, AccessHeader: "Si_ze and Properties..."),
        new("Move Chart...", WorksheetContextMenuAction.MoveChart, AccessHeader: "_Move Chart..."),
        WorksheetContextMenuCommand.Separator,
        new("Selection Pane...", WorksheetContextMenuAction.SelectionPane, AccessHeader: "_Selection Pane...")
    ]);

    private static IReadOnlyList<WorksheetContextMenuCommand> BuildRowSizingVisibilityCommands(
        bool rowSelectionOrder = false) =>
        rowSelectionOrder
            ? Freeze([
                new("Row Height...", WorksheetContextMenuAction.RowHeight, AccessHeader: "Row _Height..."),
                new("AutoFit Row Height", WorksheetContextMenuAction.AutoFitRowHeight, AccessHeader: "AutoFit Row He_ight"),
                new("Hide Rows", WorksheetContextMenuAction.HideRows, AccessHeader: "_Hide Rows"),
                new("Unhide Rows", WorksheetContextMenuAction.UnhideRows, AccessHeader: "Unhide Ro_ws")
            ])
            : Freeze([
                new("Hide Rows", WorksheetContextMenuAction.HideRows, AccessHeader: "_Hide Rows"),
                new("Unhide Rows", WorksheetContextMenuAction.UnhideRows, AccessHeader: "Unhide Ro_ws"),
                new("Row Height...", WorksheetContextMenuAction.RowHeight, AccessHeader: "Row _Height..."),
                new("AutoFit Row Height", WorksheetContextMenuAction.AutoFitRowHeight, AccessHeader: "AutoFit Row He_ight")
            ]);

    private static IReadOnlyList<WorksheetContextMenuCommand> BuildColumnSizingVisibilityCommands(
        bool columnSelectionOrder = false) =>
        columnSelectionOrder
            ? Freeze([
                new("Column Width...", WorksheetContextMenuAction.ColumnWidth, AccessHeader: "Column _Width..."),
                new("AutoFit Column Width", WorksheetContextMenuAction.AutoFitColumnWidth, AccessHeader: "AutoFit Column Wi_dth"),
                new("Hide Columns", WorksheetContextMenuAction.HideColumns, AccessHeader: "Hide Col_umns"),
                new("Unhide Columns", WorksheetContextMenuAction.UnhideColumns, AccessHeader: "Unhide Co_lumns")
            ])
            : Freeze([
                new("Hide Columns", WorksheetContextMenuAction.HideColumns, AccessHeader: "Hide Col_umns"),
                new("Unhide Columns", WorksheetContextMenuAction.UnhideColumns, AccessHeader: "Unhide Co_lumns"),
                new("Column Width...", WorksheetContextMenuAction.ColumnWidth, AccessHeader: "Column _Width..."),
                new("AutoFit Column Width", WorksheetContextMenuAction.AutoFitColumnWidth, AccessHeader: "AutoFit Column Wi_dth")
            ]);

    private static IReadOnlyList<WorksheetContextMenuCommand> BuildRowSelectionCommands() =>
        Freeze([
        new("Cut", WorksheetContextMenuAction.Cut, AccessHeader: "Cu_t"),
        new("Copy", WorksheetContextMenuAction.Copy, AccessHeader: "_Copy"),
        new("Paste", WorksheetContextMenuAction.Paste, AccessHeader: "_Paste"),
        new("Insert Row Above", WorksheetContextMenuAction.InsertRowAbove, AccessHeader: "Insert Row _Above"),
        new("Delete Row(s)", WorksheetContextMenuAction.DeleteRows, AccessHeader: "Delete _Row(s)"),
        WorksheetContextMenuCommand.Separator,
        .. BuildRowSizingVisibilityCommands(rowSelectionOrder: true),
        WorksheetContextMenuCommand.Separator,
        new("Group", WorksheetContextMenuAction.Group, AccessHeader: "_Group"),
        new("Ungroup", WorksheetContextMenuAction.Ungroup, AccessHeader: "_Ungroup"),
        WorksheetContextMenuCommand.Separator,
        new("Format Cells...", WorksheetContextMenuAction.FormatCells, AccessHeader: "_Format Cells..."),
        WorksheetContextMenuCommand.Separator,
        new("Clear Contents", WorksheetContextMenuAction.ClearContents, AccessHeader: "Clear C_ontents")
    ]);

    private static WorksheetContextMenuCommand BuildThreadedCommentResolveCommand(WorksheetContextMenuState state) =>
        state.IsThreadedCommentResolved
            ? new("Unresolve Comment", WorksheetContextMenuAction.UnresolveComment, AccessHeader: "Un_resolve Comment", IsEnabled: state.HasThreadedComment)
            : new("Resolve Comment", WorksheetContextMenuAction.ResolveComment, AccessHeader: "Resol_ve Comment", IsEnabled: state.HasThreadedComment);

    private static IReadOnlyList<WorksheetContextMenuCommand> BuildColumnSelectionCommands() =>
        Freeze([
        new("Cut", WorksheetContextMenuAction.Cut, AccessHeader: "Cu_t"),
        new("Copy", WorksheetContextMenuAction.Copy, AccessHeader: "_Copy"),
        new("Paste", WorksheetContextMenuAction.Paste, AccessHeader: "_Paste"),
        new("Insert Column Left", WorksheetContextMenuAction.InsertColumnLeft, AccessHeader: "Insert Column _Left"),
        new("Delete Column(s)", WorksheetContextMenuAction.DeleteColumns, AccessHeader: "Delete _Column(s)"),
        WorksheetContextMenuCommand.Separator,
        .. BuildColumnSizingVisibilityCommands(columnSelectionOrder: true),
        WorksheetContextMenuCommand.Separator,
        new("Group", WorksheetContextMenuAction.Group, AccessHeader: "_Group"),
        new("Ungroup", WorksheetContextMenuAction.Ungroup, AccessHeader: "_Ungroup"),
        WorksheetContextMenuCommand.Separator,
        new("Format Cells...", WorksheetContextMenuAction.FormatCells, AccessHeader: "_Format Cells..."),
        WorksheetContextMenuCommand.Separator,
        new("Clear Contents", WorksheetContextMenuAction.ClearContents, AccessHeader: "Clear C_ontents")
    ]);

    private static IReadOnlyList<WorksheetContextMenuCommand> BuildDrawingObjectCommands(
        string formatHeader,
        bool includeReorder)
    {
        var commands = new List<WorksheetContextMenuCommand>
        {
            new("Cut", WorksheetContextMenuAction.Cut, AccessHeader: "Cu_t"),
            new("Copy", WorksheetContextMenuAction.Copy, AccessHeader: "_Copy"),
            new("Paste", WorksheetContextMenuAction.Paste, AccessHeader: "_Paste"),
            WorksheetContextMenuCommand.Separator,
            new(formatHeader, WorksheetContextMenuAction.FormatDrawingObject, AccessHeader: $"_Format {formatHeader["Format ".Length..]}"),
            new("Size and Properties...", WorksheetContextMenuAction.ResizeDrawingObject, AccessHeader: "_Size and Properties..."),
            new("Rotate...", WorksheetContextMenuAction.RotateDrawingObject, AccessHeader: "_Rotate..."),
            new("Shape Fill...", WorksheetContextMenuAction.ShapeFill, AccessHeader: "Shape _Fill..."),
            new("Shape Outline...", WorksheetContextMenuAction.ShapeOutline, AccessHeader: "Shape _Outline..."),
            WorksheetContextMenuCommand.Separator,
            new("Edit Alt Text...", WorksheetContextMenuAction.EditAltText, AccessHeader: "Edit _Alt Text..."),
            new("Selection Pane...", WorksheetContextMenuAction.SelectionPane, AccessHeader: "_Selection Pane...")
        };

        if (includeReorder)
        {
            commands.Add(WorksheetContextMenuCommand.Separator);
            commands.Add(new WorksheetContextMenuCommand("Bring Forward", WorksheetContextMenuAction.BringForward, AccessHeader: "Bring _Forward"));
            commands.Add(new WorksheetContextMenuCommand("Send Backward", WorksheetContextMenuAction.SendBackward, AccessHeader: "Send _Backward"));
        }

        return Freeze(commands.ToArray());
    }

    private static IReadOnlyList<WorksheetContextMenuCommand> BuildHyperlinkCommands(WorksheetContextMenuState state) =>
        state.HasHyperlink
            ? Freeze([
                Submenu(
                    "Hyperlink",
                    "_Hyperlink",
                    new WorksheetContextMenuCommand("Open Hyperlink", WorksheetContextMenuAction.OpenHyperlink, AccessHeader: "_Open Hyperlink"),
                    new WorksheetContextMenuCommand("Edit Hyperlink...", WorksheetContextMenuAction.Hyperlink, AccessHeader: "_Edit Hyperlink..."),
                    new WorksheetContextMenuCommand("Remove Hyperlink", WorksheetContextMenuAction.RemoveHyperlinks, AccessHeader: "_Remove Hyperlink"))
            ])
            : Freeze([
                new("Hyperlink...", WorksheetContextMenuAction.Hyperlink, AccessHeader: "_Hyperlink...")
            ]);

    private static IReadOnlyList<WorksheetContextMenuCommand> BuildPivotTableCommands(WorksheetContextMenuState state) =>
        state.HasPivotTableTarget
            ? Freeze([
                Submenu(
                    "PivotTable",
                    "_PivotTable",
                    new WorksheetContextMenuCommand("PivotTable Options...", WorksheetContextMenuAction.PivotTableOptions, AccessHeader: "PivotTable _Options..."))
            ])
            : [];
}

public sealed record WorksheetContextMenuCommand(
    string Header,
    WorksheetContextMenuAction Action,
    bool IsSeparator = false,
    string? AccessHeader = null,
    bool IsEnabled = true,
    IReadOnlyList<WorksheetContextMenuCommand>? Children = null)
{
    public static WorksheetContextMenuCommand Separator { get; } =
        new("", WorksheetContextMenuAction.None, IsSeparator: true, IsEnabled: false);

    public string AccessHeader { get; init; } = AccessHeader ?? Header;

    public IReadOnlyList<WorksheetContextMenuCommand> Children { get; init; } = Children ?? [];

    public bool HasChildren => Children.Count > 0;
}

public sealed record WorksheetContextMenuState(
    bool HasThreadedComment = false,
    bool IsThreadedCommentResolved = false,
    bool HasNote = false,
    bool HasHyperlink = false,
    bool HasAutoFilterHeaderTarget = false,
    bool HasDropdownTarget = false,
    bool HasPivotTableTarget = false,
    bool NoteIsShown = false)
{
    public static WorksheetContextMenuState Default { get; } = new();
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
    Group,
    Ungroup,
    NewComment,
    EditComment,
    ResolveComment,
    UnresolveComment,
    DeleteComment,
    NewNote,
    EditNote,
    DeleteNote,
    ShowNotes,
    ShowHideNote,
    ShowAllNotes,
    OpenHyperlink,
    Hyperlink,
    PivotTableOptions,
    FormatCells,
    ClearAll,
    ClearFormats,
    ClearComments,
    ClearHyperlinks,
    RemoveHyperlinks,
    ClearContents,
    FormatPicture,
    CropPicture,
    ResetPictureCrop,
    FormatDrawingObject,
    ResizeDrawingObject,
    RotateDrawingObject,
    ShapeFill,
    ShapeOutline,
    FormatChartArea,
    SelectChartData,
    ChangeChartType,
    ChartStyles,
    ChartTitles,
    ChartSizeAndProperties,
    MoveChart,
    BringForward,
    SendBackward,
    EditAltText,
    SelectionPane
}

public enum WorksheetContextMenuTargetKind
{
    Worksheet,
    Picture,
    Shape,
    TextBox,
    Chart,
    RowSelection,
    ColumnSelection
}
