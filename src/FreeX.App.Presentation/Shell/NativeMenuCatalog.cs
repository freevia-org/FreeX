namespace FreeX.App.Presentation.Shell;

public enum NativeMenuTopLevelId
{
    File,
    Home,
    Insert,
    PageLayout,
    Formulas,
    Data,
    Review,
    View,
    Sheet,
    Window,
    Help
}

public enum NativeFileMenuItemId
{
    NewWorkbook,
    Open,
    OpenRecent,
    ShareWorkbook,
    BackstageInfo,
    Save,
    SaveAs,
    Print,
    PrintPreview,
    BackstageExport,
    ExportPdf,
    WorkbookStatistics,
    PageSetup,
    CloseWorkbook,
    BackstageAccount,
    Options,
    Quit
}

public enum NativeMenuItemId
{
    NewSheet,
    RenameSheet,
    DuplicateSheet,
    MoveSheetLeft,
    MoveSheetRight,
    TabColor,
    SelectAllSheets,
    UngroupSheets,
    HideSheet,
    UnhideSheet,
    DeleteSheet,
    Undo,
    Redo,
    Cut,
    Copy,
    Paste,
    PasteSpecial,
    FormatPainter,
    Bold,
    Italic,
    Underline,
    DoubleUnderline,
    Strikethrough,
    IncreaseFontSize,
    DecreaseFontSize,
    FillColor,
    ClearFill,
    FontColor,
    Borders,
    CellStyles,
    FormatCells,
    ConditionalFormatting,
    HorizontalText,
    AngleCounterclockwise,
    AngleClockwise,
    VerticalText,
    RotateTextUp,
    RotateTextDown,
    CurrencyFormat,
    PercentFormat,
    CommaStyle,
    IncreaseDecimal,
    DecreaseDecimal,
    AlignTop,
    AlignMiddle,
    AlignBottom,
    WrapText,
    MergeAndCenter,
    UnmergeCells,
    DecreaseIndent,
    IncreaseIndent,
    AlignLeft,
    AlignCenter,
    AlignRight,
    FillCells,
    FillDown,
    FillRight,
    FillUp,
    FillLeft,
    FillSeries,
    Clear,
    ClearAll,
    ClearFormats,
    ClearContents,
    ClearComments,
    ClearHyperlinks,
    SelectAll,
    Find,
    FindNext,
    Replace,
    GoTo,
    GoToSpecial,
    OpenHyperlink,
    InsertHyperlink,
    InsertColumnChart,
    InsertBarChart,
    InsertLineChart,
    InsertPieChart,
    InsertAreaChart,
    InsertScatterChart,
    InsertTable,
    InsertPivotTable,
    InsertPicture,
    InsertShape,
    InsertTextBox,
    SortAscending,
    SortDescending,
    CustomSort,
    FlashFill,
    ToggleFilter,
    AdvancedFilter,
    RemoveDuplicates,
    Subtotal,
    TextToColumns,
    Consolidate,
    DataValidationPreview,
    DataValidation,
    QuickAnalysis,
    WhatIfAnalysis,
    GoalSeek,
    ScenarioManager,
    DataTable,
    ForecastSheet,
    ReviewSummary,
    CheckAccessibility,
    ProtectSheet,
    ProtectWorkbook,
    NextNote,
    PreviousNote,
    NextComment,
    PreviousComment,
    Themes,
    ThemeColors,
    ThemeFonts,
    ThemeEffects,
    PageMargins,
    PageMarginsNormal,
    PageMarginsWide,
    PageMarginsNarrow,
    PageMarginsCustom,
    PageOrientation,
    PageOrientationPortrait,
    PageOrientationLandscape,
    PaperSize,
    PaperSizeLetter,
    PaperSizeLegal,
    PaperSizeA4,
    PaperSizeMore,
    PrintArea,
    SetPrintArea,
    ClearPrintArea,
    PageBreaks,
    SheetBackground,
    ChooseSheetBackground,
    DeleteSheetBackground,
    PageSetup,
    PrintGridlines,
    PrintHeadings,
    InsertFunction,
    NameManager,
    DefineName,
    CreateNamesFromSelection,
    AutoSum,
    AutoSumSum,
    AutoSumAverage,
    AutoSumCountNumbers,
    AutoSumCountAll,
    AutoSumMax,
    AutoSumMin,
    ShowFormulas,
    ShowGridlines,
    ShowHeadings,
    ZoomIn,
    ZoomOut,
    Zoom100,
    ZoomToSelection,
    FreezePanes,
    FreezeTopRow,
    FreezeFirstColumn,
    UnfreezePanes,
    PageBreakPreview,
    MinimizeWindow,
    ZoomWindow,
    BringAllToFront,
    HelpOnline,
    SendFeedback,
    CheckForUpdates,
    About,
    LegalNotices
}

public enum NativeMenuEntryKind
{
    Item,
    Separator
}

public enum NativeMenuGestureKey
{
    A,
    B,
    C,
    D,
    D0,
    D1,
    D5,
    Delete,
    E,
    F,
    F1,
    F3,
    F11,
    G,
    H,
    I,
    M,
    N,
    O,
    P,
    Q,
    R,
    S,
    U,
    V,
    W,
    X,
    Z,
    OemComma,
    OemMinus,
    OemPlus,
    Oem3
}

[Flags]
public enum NativeMenuGestureModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Meta = 8
}

public sealed record NativeMenuTopLevelPlan(
    NativeMenuTopLevelId Id,
    string Header);

public sealed record NativeMenuGesturePlan(
    NativeMenuGestureKey Key,
    NativeMenuGestureModifiers Modifiers = NativeMenuGestureModifiers.None);

public sealed record NativeFileMenuItemPlan(
    NativeFileMenuItemId Id,
    string Label,
    NativeMenuGesturePlan? Gesture = null,
    bool UsesResourceKey = true,
    bool RequiresGestureInSmoke = true);

public sealed record NativeMenuItemPlan(
    NativeMenuItemId Id,
    string Label,
    NativeMenuGesturePlan? Gesture = null,
    bool UsesResourceKey = false,
    bool RequiresGestureInSmoke = true);

public sealed record NativeFileMenuEntryPlan(
    NativeMenuEntryKind Kind,
    NativeFileMenuItemPlan? Item)
{
    public static NativeFileMenuEntryPlan ForItem(NativeFileMenuItemPlan item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new NativeFileMenuEntryPlan(NativeMenuEntryKind.Item, item);
    }

    public static NativeFileMenuEntryPlan Separator { get; } =
        new(NativeMenuEntryKind.Separator, Item: null);
}

public sealed record NativeMenuEntryPlan(
    NativeMenuEntryKind Kind,
    NativeMenuItemId? ItemId)
{
    public static NativeMenuEntryPlan ForItem(NativeMenuItemId itemId) =>
        new(NativeMenuEntryKind.Item, itemId);

    public static NativeMenuEntryPlan Separator { get; } =
        new(NativeMenuEntryKind.Separator, ItemId: null);
}

public sealed record NativeFileMenuAvailabilityContext(
    bool IsIdle,
    bool CanOpen,
    bool CanSave,
    bool CanSaveAs,
    bool CanSaveThroughStorageProvider);

public sealed record NativeFileMenuAvailabilityItem(
    NativeFileMenuItemId Id,
    bool IsEnabled);

public sealed record NativeFileMenuAvailabilityPlan(
    IReadOnlyList<NativeFileMenuAvailabilityItem> Items)
{
    public bool IsEnabled(NativeFileMenuItemId id) =>
        Items.First(item => item.Id == id).IsEnabled;
}

public sealed record NativeMenuAvailabilityContext(
    bool IsIdle,
    bool CanAddSheet,
    int ActiveSheetTabIndex,
    int SheetTabCount,
    bool IsWorkbookGrouped,
    bool CanHideActiveSheet,
    int HiddenSheetCount,
    bool CanUndo,
    bool CanRedo,
    bool CanCut,
    bool CanCopy,
    bool CanPaste,
    bool CanPasteSpecial,
    bool CanFormatPainter,
    bool CanFindNext,
    bool CanOpenSelectedHyperlink,
    bool CanInsertPicture,
    bool CanSortSelectedRange,
    long SelectedRangeRowCount,
    long SelectedRangeColCount,
    long SelectedRangeCellCount,
    bool CanFillCells,
    bool CanFillDown,
    bool CanFillRight,
    bool CanFillUp,
    bool CanFillLeft,
    bool CanFillSeries,
    bool CanClear,
    bool CanBold,
    bool CanItalic,
    bool CanUnderline,
    bool CanDoubleUnderline,
    bool CanStrikethrough,
    bool CanIncreaseFontSize,
    bool CanDecreaseFontSize,
    bool CanFillColor,
    bool CanFontColor,
    bool CanBorders,
    bool CanCellStyles,
    bool CanCurrencyFormat,
    bool CanPercentFormat,
    bool CanCommaStyle,
    bool CanIncreaseDecimal,
    bool CanDecreaseDecimal,
    bool CanAlignLeft,
    bool CanAlignCenter,
    bool CanAlignRight,
    bool CanAlignTop,
    bool CanAlignMiddle,
    bool CanAlignBottom,
    bool CanWrapText,
    bool CanMergeAndCenter,
    bool IsSelectedRangeMerged,
    bool CanDecreaseIndent,
    bool CanIncreaseIndent,
    bool IsShowingGridlines,
    bool IsShowingHeadings,
    bool CanZoomIn,
    bool CanZoomOut,
    bool IsPageBreakPreview,
    bool IsShowingFormulas);

public sealed record NativeMenuAvailabilityItem(
    NativeMenuItemId Id,
    bool IsEnabled,
    bool? IsChecked = null);

public sealed record NativeMenuAvailabilityPlan(
    IReadOnlyList<NativeMenuAvailabilityItem> Items)
{
    public bool IsEnabled(NativeMenuItemId id) =>
        Items.First(item => item.Id == id).IsEnabled;

    public bool? IsChecked(NativeMenuItemId id) =>
        Items.First(item => item.Id == id).IsChecked;
}

public static class NativeMenuCatalog
{
    private static readonly NativeFileMenuItemPlan[] FileMenuItems =
    [
        new(
            NativeFileMenuItemId.NewWorkbook,
            "AvaloniaNativeMenu_NewWorkbook",
            NativeMenuGesture(WorkbookShortcutRoute.NewWorkbook)),
        new(
            NativeFileMenuItemId.Open,
            "AvaloniaNativeMenu_Open",
            NativeMenuGesture(WorkbookShortcutRoute.OpenWorkbook)),
        new(
            NativeFileMenuItemId.OpenRecent,
            "AvaloniaNativeMenu_OpenRecent",
            Gesture: null,
            RequiresGestureInSmoke: false),
        new(
            NativeFileMenuItemId.ShareWorkbook,
            "AvaloniaNativeMenu_ShareWorkbook",
            Gesture: null,
            RequiresGestureInSmoke: false),
        new(
            NativeFileMenuItemId.BackstageInfo,
            "Backstage_Info_MenuItem",
            Gesture: null,
            RequiresGestureInSmoke: false),
        new(
            NativeFileMenuItemId.Save,
            "AvaloniaNativeMenu_Save",
            NativeMenuGesture(WorkbookShortcutRoute.SaveWorkbook)),
        new(
            NativeFileMenuItemId.SaveAs,
            "AvaloniaNativeMenu_SaveAs",
            new NativeMenuGesturePlan(
                NativeMenuGestureKey.S,
                NativeMenuGestureModifiers.Meta | NativeMenuGestureModifiers.Shift)),
        new(
            NativeFileMenuItemId.Print,
            "Print_MenuItem",
            new NativeMenuGesturePlan(NativeMenuGestureKey.P, NativeMenuGestureModifiers.Meta)),
        new(
            NativeFileMenuItemId.PrintPreview,
            "AvaloniaNativeMenu_PrintPreview",
            new NativeMenuGesturePlan(
                NativeMenuGestureKey.P,
                NativeMenuGestureModifiers.Meta | NativeMenuGestureModifiers.Shift)),
        new(
            NativeFileMenuItemId.BackstageExport,
            "Backstage_Export_MenuItem",
            Gesture: null,
            RequiresGestureInSmoke: false),
        new(
            NativeFileMenuItemId.ExportPdf,
            "AvaloniaNativeMenu_ExportPdf",
            Gesture: null,
            RequiresGestureInSmoke: false),
        new(
            NativeFileMenuItemId.WorkbookStatistics,
            "AvaloniaNativeMenu_WorkbookStatistics",
            NativeMenuGesture(WorkbookShortcutRoute.WorkbookStatistics)),
        new(
            NativeFileMenuItemId.PageSetup,
            "AvaloniaNativeMenu_PageSetup",
            Gesture: null,
            RequiresGestureInSmoke: false),
        new(
            NativeFileMenuItemId.CloseWorkbook,
            "AvaloniaNativeMenu_CloseWorkbook",
            new NativeMenuGesturePlan(NativeMenuGestureKey.W, NativeMenuGestureModifiers.Meta)),
        new(
            NativeFileMenuItemId.BackstageAccount,
            "Backstage_Account_MenuItem",
            Gesture: null,
            RequiresGestureInSmoke: false),
        new(
            NativeFileMenuItemId.Options,
            "Options_Title",
            new NativeMenuGesturePlan(NativeMenuGestureKey.OemComma, NativeMenuGestureModifiers.Meta)),
        new(
            NativeFileMenuItemId.Quit,
            "Quit FreeX",
            new NativeMenuGesturePlan(NativeMenuGestureKey.Q, NativeMenuGestureModifiers.Meta),
            UsesResourceKey: false)
    ];

    private static readonly NativeMenuItemPlan[] MenuItems =
    [
        new(NativeMenuItemId.NewSheet, "AvaloniaNativeMenu_NewSheet", NativeMenuGesture(WorkbookShortcutRoute.InsertWorksheet), UsesResourceKey: true),
        new(NativeMenuItemId.RenameSheet, "Rename Sheet...", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.DuplicateSheet, "Duplicate Sheet", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.MoveSheetLeft, "Move Sheet Left", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.MoveSheetRight, "Move Sheet Right", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.TabColor, "Tab Color", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.SelectAllSheets, "Select All Sheets", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.UngroupSheets, "Ungroup Sheets", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.HideSheet, "Hide Sheet", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.UnhideSheet, "Unhide Sheet...", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.DeleteSheet, "Delete Sheet", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.Undo, "Undo", NativeMenuGesture(WorkbookShortcutRoute.Undo)),
        new(NativeMenuItemId.Redo, "Redo", NativeMenuGesture(WorkbookShortcutRoute.Redo)),
        new(NativeMenuItemId.Cut, "Cut", NativeMenuGesture(WorkbookShortcutRoute.Cut)),
        new(NativeMenuItemId.Copy, "Copy", NativeMenuGesture(WorkbookShortcutRoute.Copy)),
        new(NativeMenuItemId.Paste, "Paste", NativeMenuGesture(WorkbookShortcutRoute.Paste)),
        new(NativeMenuItemId.PasteSpecial, "Paste Special", NativeMenuGesture(WorkbookShortcutRoute.PasteSpecial)),
        new(NativeMenuItemId.FormatPainter, "Format Painter", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.Bold, "Bold", NativeMenuGesture(WorkbookShortcutRoute.ToggleBold)),
        new(NativeMenuItemId.Italic, "Italic", NativeMenuGesture(WorkbookShortcutRoute.ToggleItalic)),
        new(NativeMenuItemId.Underline, "Underline", NativeMenuGesture(WorkbookShortcutRoute.ToggleUnderline)),
        new(NativeMenuItemId.DoubleUnderline, "Double Underline", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.Strikethrough, "Strikethrough", NativeMenuGesture(WorkbookShortcutRoute.ToggleStrikethrough)),
        new(NativeMenuItemId.IncreaseFontSize, "Increase Font Size", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.DecreaseFontSize, "Decrease Font Size", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.FillColor, "Fill Color", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.ClearFill, "No Fill", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.FontColor, "Font Color", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.Borders, "Borders", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.CellStyles, "Cell Styles", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.FormatCells, "Format Cells...", NativeMenuGesture(WorkbookShortcutRoute.OpenFormatCells)),
        new(NativeMenuItemId.ConditionalFormatting, "Conditional Formatting", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.HorizontalText, "Horizontal", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.AngleCounterclockwise, "Angle Counterclockwise", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.AngleClockwise, "Angle Clockwise", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.VerticalText, "Vertical Text", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.RotateTextUp, "Rotate Text Up", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.RotateTextDown, "Rotate Text Down", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.CurrencyFormat, "Accounting Number Format", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.PercentFormat, "Percent Style", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.CommaStyle, "Comma Style", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.IncreaseDecimal, "Increase Decimal Places", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.DecreaseDecimal, "Decrease Decimal Places", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.AlignTop, "Align Top", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.AlignMiddle, "Align Middle", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.AlignBottom, "Align Bottom", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.WrapText, "Wrap Text", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.MergeAndCenter, "Merge & Center", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.UnmergeCells, "Unmerge Cells", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.DecreaseIndent, "Decrease Indent", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.IncreaseIndent, "Increase Indent", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.AlignLeft, "Align Left", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.AlignCenter, "Align Center", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.AlignRight, "Align Right", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.FillCells, "Fill", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.FillDown, "Down", NativeMenuGesture(WorkbookShortcutRoute.FillDown)),
        new(NativeMenuItemId.FillRight, "Right", NativeMenuGesture(WorkbookShortcutRoute.FillRight)),
        new(NativeMenuItemId.FillUp, "Up", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.FillLeft, "Left", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.FillSeries, "Series...", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.Clear, "Clear", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.ClearAll, "Clear All", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.ClearFormats, "Clear Formats", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.ClearContents, "Clear Contents", new NativeMenuGesturePlan(NativeMenuGestureKey.Delete)),
        new(NativeMenuItemId.ClearComments, "Clear Comments and Notes", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.ClearHyperlinks, "Clear Hyperlinks", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.SelectAll, "Select All", new NativeMenuGesturePlan(NativeMenuGestureKey.A, NativeMenuGestureModifiers.Meta)),
        new(NativeMenuItemId.Find, "Find...", NativeMenuGesture(WorkbookShortcutRoute.Find)),
        new(NativeMenuItemId.FindNext, "Find Next", new NativeMenuGesturePlan(NativeMenuGestureKey.G, NativeMenuGestureModifiers.Meta)),
        new(NativeMenuItemId.Replace, "Replace...", NativeMenuGesture(WorkbookShortcutRoute.Replace)),
        new(NativeMenuItemId.GoTo, "Go To...", NativeMenuGesture(WorkbookShortcutRoute.GoTo)),
        new(NativeMenuItemId.GoToSpecial, "Go To Special...", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.OpenHyperlink, "Open Hyperlink", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.InsertHyperlink, "Hyperlink...", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.InsertColumnChart, "Column Chart", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.InsertBarChart, "Bar Chart", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.InsertLineChart, "Line Chart", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.InsertPieChart, "Pie Chart", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.InsertAreaChart, "Area Chart", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.InsertScatterChart, "Scatter Chart", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.InsertTable, "Table...", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.InsertPivotTable, "PivotTable...", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.InsertPicture, "Picture...", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.InsertShape, "Shape", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.InsertTextBox, "Text Box", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.SortAscending, "Sort A to Z", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.SortDescending, "Sort Z to A", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.CustomSort, "Sort...", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.FlashFill, "Flash Fill", NativeMenuGesture(WorkbookShortcutRoute.FlashFill)),
        new(NativeMenuItemId.ToggleFilter, "Filter", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.AdvancedFilter, "Advanced Filter...", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.RemoveDuplicates, "Remove Duplicates...", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.Subtotal, "Subtotal...", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.TextToColumns, "Text to Columns...", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.Consolidate, "Consolidate...", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.DataValidationPreview, "Data Validation Preview...", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.DataValidation, "Data Validation...", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.QuickAnalysis, "Quick Analysis...", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.WhatIfAnalysis, "What-If Analysis", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.GoalSeek, "Goal Seek...", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.ScenarioManager, "Scenario Manager...", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.DataTable, "Data Table...", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.ForecastSheet, "Forecast Sheet...", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.ReviewSummary, "Review Summary...", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.CheckAccessibility, "Check Accessibility...", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.ProtectSheet, "Protect Sheet...", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.ProtectWorkbook, "Protect Workbook...", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.NextNote, "Next Note", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.PreviousNote, "Previous Note", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.NextComment, "Next Comment", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.PreviousComment, "Previous Comment", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.Themes, "Themes", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.ThemeColors, "Theme Colors", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.ThemeFonts, "Theme Fonts", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.ThemeEffects, "Theme Effects", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.PageMargins, "Margins", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.PageMarginsNormal, "Normal", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.PageMarginsWide, "Wide", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.PageMarginsNarrow, "Narrow", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.PageMarginsCustom, "Custom Margins...", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.PageOrientation, "Page Orientation", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.PageOrientationPortrait, "Portrait", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.PageOrientationLandscape, "Landscape", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.PaperSize, "Paper Size", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.PaperSizeLetter, "Letter", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.PaperSizeLegal, "Legal", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.PaperSizeA4, "A4", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.PaperSizeMore, "More Paper Sizes...", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.PrintArea, "Print Area", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.SetPrintArea, "Set Print Area", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.ClearPrintArea, "Clear Print Area", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.PageBreaks, "Breaks...", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.SheetBackground, "Background", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.ChooseSheetBackground, "Choose Background...", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.DeleteSheetBackground, "Delete Background", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.PageSetup, "AvaloniaNativeMenu_PageSetup", UsesResourceKey: true, RequiresGestureInSmoke: false),
        new(NativeMenuItemId.PrintGridlines, "Gridlines...", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.PrintHeadings, "Headings...", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.InsertFunction, "Insert Function...", NativeMenuGesture(WorkbookShortcutRoute.InsertFunction)),
        new(NativeMenuItemId.NameManager, "Name Manager...", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.DefineName, "Define Name...", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.CreateNamesFromSelection, "Create from Selection...", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.AutoSum, "AutoSum", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.AutoSumSum, "Sum", NativeMenuGesture(WorkbookShortcutRoute.AutoSum)),
        new(NativeMenuItemId.AutoSumAverage, "Average", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.AutoSumCountNumbers, "Count Numbers", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.AutoSumCountAll, "Count All", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.AutoSumMax, "Max", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.AutoSumMin, "Min", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.ShowFormulas, "Show Formulas", NativeMenuGesture(WorkbookShortcutRoute.ToggleShowFormulas)),
        new(NativeMenuItemId.ShowGridlines, "Gridlines", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.ShowHeadings, "Headings", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.ZoomIn, "Zoom In", new NativeMenuGesturePlan(NativeMenuGestureKey.OemPlus, NativeMenuGestureModifiers.Meta)),
        new(NativeMenuItemId.ZoomOut, "Zoom Out", new NativeMenuGesturePlan(NativeMenuGestureKey.OemMinus, NativeMenuGestureModifiers.Meta)),
        new(NativeMenuItemId.Zoom100, "100%", new NativeMenuGesturePlan(NativeMenuGestureKey.D0, NativeMenuGestureModifiers.Meta)),
        new(NativeMenuItemId.ZoomToSelection, "Zoom to Selection", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.FreezePanes, "Freeze Panes", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.FreezeTopRow, "Freeze Top Row", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.FreezeFirstColumn, "Freeze First Column", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.UnfreezePanes, "Unfreeze Panes", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.PageBreakPreview, "Page Break Preview", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.MinimizeWindow, "Minimize", new NativeMenuGesturePlan(NativeMenuGestureKey.M, NativeMenuGestureModifiers.Meta)),
        new(NativeMenuItemId.ZoomWindow, "Zoom", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.BringAllToFront, "Bring All to Front", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.HelpOnline, "Help Online", new NativeMenuGesturePlan(NativeMenuGestureKey.F1)),
        new(NativeMenuItemId.SendFeedback, "Send Feedback", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.CheckForUpdates, "Check for Updates", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.About, "About FreeX", RequiresGestureInSmoke: false),
        new(NativeMenuItemId.LegalNotices, "Legal Notices", RequiresGestureInSmoke: false)
    ];

    public static IReadOnlyList<NativeMenuTopLevelPlan> TopLevelMenus { get; } =
    [
        new(NativeMenuTopLevelId.File, "File"),
        new(NativeMenuTopLevelId.Home, "Home"),
        new(NativeMenuTopLevelId.Insert, "Insert"),
        new(NativeMenuTopLevelId.PageLayout, "Page Layout"),
        new(NativeMenuTopLevelId.Formulas, "Formulas"),
        new(NativeMenuTopLevelId.Data, "Data"),
        new(NativeMenuTopLevelId.Review, "Review"),
        new(NativeMenuTopLevelId.View, "View"),
        new(NativeMenuTopLevelId.Sheet, "Sheet"),
        new(NativeMenuTopLevelId.Window, "Window"),
        new(NativeMenuTopLevelId.Help, "Help")
    ];

    public static IReadOnlyList<NativeFileMenuEntryPlan> FileMenuEntries { get; } =
    [
        FileItem(NativeFileMenuItemId.NewWorkbook),
        FileItem(NativeFileMenuItemId.Open),
        FileItem(NativeFileMenuItemId.OpenRecent),
        FileItem(NativeFileMenuItemId.ShareWorkbook),
        NativeFileMenuEntryPlan.Separator,
        FileItem(NativeFileMenuItemId.BackstageInfo),
        FileItem(NativeFileMenuItemId.Save),
        FileItem(NativeFileMenuItemId.SaveAs),
        NativeFileMenuEntryPlan.Separator,
        FileItem(NativeFileMenuItemId.Print),
        FileItem(NativeFileMenuItemId.PrintPreview),
        FileItem(NativeFileMenuItemId.BackstageExport),
        FileItem(NativeFileMenuItemId.ExportPdf),
        FileItem(NativeFileMenuItemId.WorkbookStatistics),
        FileItem(NativeFileMenuItemId.PageSetup),
        NativeFileMenuEntryPlan.Separator,
        FileItem(NativeFileMenuItemId.CloseWorkbook),
        NativeFileMenuEntryPlan.Separator,
        FileItem(NativeFileMenuItemId.BackstageAccount),
        FileItem(NativeFileMenuItemId.Options),
        NativeFileMenuEntryPlan.Separator,
        FileItem(NativeFileMenuItemId.Quit)
    ];

    public static IReadOnlyList<NativeMenuEntryPlan> HomeMenuEntries { get; } =
    [
        Item(NativeMenuItemId.Undo),
        Item(NativeMenuItemId.Redo),
        NativeMenuEntryPlan.Separator,
        Item(NativeMenuItemId.Cut),
        Item(NativeMenuItemId.Copy),
        Item(NativeMenuItemId.Paste),
        Item(NativeMenuItemId.PasteSpecial),
        Item(NativeMenuItemId.FormatPainter),
        NativeMenuEntryPlan.Separator,
        Item(NativeMenuItemId.Bold),
        Item(NativeMenuItemId.Italic),
        Item(NativeMenuItemId.Underline),
        Item(NativeMenuItemId.DoubleUnderline),
        Item(NativeMenuItemId.Strikethrough),
        Item(NativeMenuItemId.IncreaseFontSize),
        Item(NativeMenuItemId.DecreaseFontSize),
        Item(NativeMenuItemId.FillColor),
        Item(NativeMenuItemId.ClearFill),
        Item(NativeMenuItemId.FontColor),
        Item(NativeMenuItemId.Borders),
        Item(NativeMenuItemId.CellStyles),
        Item(NativeMenuItemId.FormatCells),
        Item(NativeMenuItemId.ConditionalFormatting),
        NativeMenuEntryPlan.Separator,
        Item(NativeMenuItemId.HorizontalText),
        Item(NativeMenuItemId.AngleCounterclockwise),
        Item(NativeMenuItemId.AngleClockwise),
        Item(NativeMenuItemId.VerticalText),
        Item(NativeMenuItemId.RotateTextUp),
        Item(NativeMenuItemId.RotateTextDown),
        NativeMenuEntryPlan.Separator,
        Item(NativeMenuItemId.CurrencyFormat),
        Item(NativeMenuItemId.PercentFormat),
        Item(NativeMenuItemId.CommaStyle),
        Item(NativeMenuItemId.IncreaseDecimal),
        Item(NativeMenuItemId.DecreaseDecimal),
        NativeMenuEntryPlan.Separator,
        Item(NativeMenuItemId.AlignTop),
        Item(NativeMenuItemId.AlignMiddle),
        Item(NativeMenuItemId.AlignBottom),
        Item(NativeMenuItemId.WrapText),
        Item(NativeMenuItemId.MergeAndCenter),
        Item(NativeMenuItemId.UnmergeCells),
        Item(NativeMenuItemId.DecreaseIndent),
        Item(NativeMenuItemId.IncreaseIndent),
        Item(NativeMenuItemId.AlignLeft),
        Item(NativeMenuItemId.AlignCenter),
        Item(NativeMenuItemId.AlignRight),
        NativeMenuEntryPlan.Separator,
        Item(NativeMenuItemId.FillCells),
        Item(NativeMenuItemId.Clear),
        Item(NativeMenuItemId.SelectAll),
        NativeMenuEntryPlan.Separator,
        Item(NativeMenuItemId.Find),
        Item(NativeMenuItemId.FindNext),
        Item(NativeMenuItemId.Replace),
        Item(NativeMenuItemId.GoTo),
        Item(NativeMenuItemId.GoToSpecial),
        Item(NativeMenuItemId.OpenHyperlink)
    ];

    public static IReadOnlyList<NativeMenuEntryPlan> InsertMenuEntries { get; } =
    [
        Item(NativeMenuItemId.InsertHyperlink),
        NativeMenuEntryPlan.Separator,
        Item(NativeMenuItemId.InsertColumnChart),
        Item(NativeMenuItemId.InsertBarChart),
        Item(NativeMenuItemId.InsertLineChart),
        Item(NativeMenuItemId.InsertPieChart),
        Item(NativeMenuItemId.InsertAreaChart),
        Item(NativeMenuItemId.InsertScatterChart),
        NativeMenuEntryPlan.Separator,
        Item(NativeMenuItemId.InsertTable),
        Item(NativeMenuItemId.InsertPivotTable),
        NativeMenuEntryPlan.Separator,
        Item(NativeMenuItemId.InsertPicture),
        Item(NativeMenuItemId.InsertShape),
        Item(NativeMenuItemId.InsertTextBox)
    ];

    public static IReadOnlyList<NativeMenuEntryPlan> PageLayoutMenuEntries { get; } =
    [
        Item(NativeMenuItemId.Themes),
        Item(NativeMenuItemId.ThemeColors),
        Item(NativeMenuItemId.ThemeFonts),
        Item(NativeMenuItemId.ThemeEffects),
        NativeMenuEntryPlan.Separator,
        Item(NativeMenuItemId.PageMargins),
        Item(NativeMenuItemId.PageOrientation),
        Item(NativeMenuItemId.PaperSize),
        Item(NativeMenuItemId.PrintArea),
        Item(NativeMenuItemId.PageBreaks),
        Item(NativeMenuItemId.SheetBackground),
        Item(NativeMenuItemId.PageSetup),
        NativeMenuEntryPlan.Separator,
        Item(NativeMenuItemId.PrintGridlines),
        Item(NativeMenuItemId.PrintHeadings)
    ];

    public static IReadOnlyList<NativeMenuEntryPlan> FormulasMenuEntries { get; } =
    [
        Item(NativeMenuItemId.AutoSum),
        Item(NativeMenuItemId.InsertFunction),
        NativeMenuEntryPlan.Separator,
        Item(NativeMenuItemId.NameManager),
        Item(NativeMenuItemId.DefineName),
        Item(NativeMenuItemId.CreateNamesFromSelection),
        NativeMenuEntryPlan.Separator,
        Item(NativeMenuItemId.ShowFormulas)
    ];

    public static IReadOnlyList<NativeMenuEntryPlan> DataMenuEntries { get; } =
    [
        Item(NativeMenuItemId.SortAscending),
        Item(NativeMenuItemId.SortDescending),
        Item(NativeMenuItemId.CustomSort),
        Item(NativeMenuItemId.FlashFill),
        Item(NativeMenuItemId.ToggleFilter),
        Item(NativeMenuItemId.AdvancedFilter),
        Item(NativeMenuItemId.RemoveDuplicates),
        Item(NativeMenuItemId.Subtotal),
        NativeMenuEntryPlan.Separator,
        Item(NativeMenuItemId.TextToColumns),
        Item(NativeMenuItemId.Consolidate),
        NativeMenuEntryPlan.Separator,
        Item(NativeMenuItemId.DataValidationPreview),
        Item(NativeMenuItemId.DataValidation),
        NativeMenuEntryPlan.Separator,
        Item(NativeMenuItemId.QuickAnalysis),
        NativeMenuEntryPlan.Separator,
        Item(NativeMenuItemId.WhatIfAnalysis),
        Item(NativeMenuItemId.ForecastSheet)
    ];

    public static IReadOnlyList<NativeMenuEntryPlan> ReviewMenuEntries { get; } =
    [
        Item(NativeMenuItemId.ReviewSummary),
        Item(NativeMenuItemId.CheckAccessibility),
        NativeMenuEntryPlan.Separator,
        Item(NativeMenuItemId.ProtectSheet),
        Item(NativeMenuItemId.ProtectWorkbook),
        NativeMenuEntryPlan.Separator,
        Item(NativeMenuItemId.NextNote),
        Item(NativeMenuItemId.PreviousNote),
        NativeMenuEntryPlan.Separator,
        Item(NativeMenuItemId.NextComment),
        Item(NativeMenuItemId.PreviousComment)
    ];

    public static IReadOnlyList<NativeMenuEntryPlan> ViewMenuEntries { get; } =
    [
        Item(NativeMenuItemId.ShowGridlines),
        Item(NativeMenuItemId.ShowHeadings),
        NativeMenuEntryPlan.Separator,
        Item(NativeMenuItemId.ZoomIn),
        Item(NativeMenuItemId.ZoomOut),
        Item(NativeMenuItemId.Zoom100),
        Item(NativeMenuItemId.ZoomToSelection),
        NativeMenuEntryPlan.Separator,
        Item(NativeMenuItemId.FreezePanes),
        Item(NativeMenuItemId.FreezeTopRow),
        Item(NativeMenuItemId.FreezeFirstColumn),
        Item(NativeMenuItemId.UnfreezePanes),
        Item(NativeMenuItemId.PageBreakPreview)
    ];

    public static IReadOnlyList<NativeMenuEntryPlan> SheetMenuEntries { get; } =
    [
        Item(NativeMenuItemId.NewSheet),
        Item(NativeMenuItemId.RenameSheet),
        Item(NativeMenuItemId.DuplicateSheet),
        Item(NativeMenuItemId.MoveSheetLeft),
        Item(NativeMenuItemId.MoveSheetRight),
        Item(NativeMenuItemId.TabColor),
        Item(NativeMenuItemId.SelectAllSheets),
        Item(NativeMenuItemId.UngroupSheets),
        NativeMenuEntryPlan.Separator,
        Item(NativeMenuItemId.HideSheet),
        Item(NativeMenuItemId.UnhideSheet),
        NativeMenuEntryPlan.Separator,
        Item(NativeMenuItemId.DeleteSheet)
    ];

    public static IReadOnlyList<NativeMenuEntryPlan> WindowMenuEntries { get; } =
    [
        Item(NativeMenuItemId.MinimizeWindow),
        Item(NativeMenuItemId.ZoomWindow),
        NativeMenuEntryPlan.Separator,
        Item(NativeMenuItemId.BringAllToFront)
    ];

    public static IReadOnlyList<NativeMenuEntryPlan> HelpMenuEntries { get; } =
    [
        Item(NativeMenuItemId.HelpOnline),
        Item(NativeMenuItemId.SendFeedback),
        Item(NativeMenuItemId.CheckForUpdates),
        NativeMenuEntryPlan.Separator,
        Item(NativeMenuItemId.About),
        Item(NativeMenuItemId.LegalNotices)
    ];

    public static IReadOnlyList<NativeMenuEntryPlan> FillCellsMenuEntries { get; } =
    [
        Item(NativeMenuItemId.FillDown),
        Item(NativeMenuItemId.FillRight),
        Item(NativeMenuItemId.FillUp),
        Item(NativeMenuItemId.FillLeft),
        NativeMenuEntryPlan.Separator,
        Item(NativeMenuItemId.FillSeries)
    ];

    public static IReadOnlyList<NativeMenuEntryPlan> ClearMenuEntries { get; } =
    [
        Item(NativeMenuItemId.ClearAll),
        Item(NativeMenuItemId.ClearFormats),
        Item(NativeMenuItemId.ClearContents),
        Item(NativeMenuItemId.ClearComments),
        Item(NativeMenuItemId.ClearHyperlinks)
    ];

    public static IReadOnlyList<NativeMenuEntryPlan> PageMarginsMenuEntries { get; } =
    [
        Item(NativeMenuItemId.PageMarginsNormal),
        Item(NativeMenuItemId.PageMarginsWide),
        Item(NativeMenuItemId.PageMarginsNarrow),
        NativeMenuEntryPlan.Separator,
        Item(NativeMenuItemId.PageMarginsCustom)
    ];

    public static IReadOnlyList<NativeMenuEntryPlan> PageOrientationMenuEntries { get; } =
    [
        Item(NativeMenuItemId.PageOrientationPortrait),
        Item(NativeMenuItemId.PageOrientationLandscape)
    ];

    public static IReadOnlyList<NativeMenuEntryPlan> PaperSizeMenuEntries { get; } =
    [
        Item(NativeMenuItemId.PaperSizeLetter),
        Item(NativeMenuItemId.PaperSizeLegal),
        Item(NativeMenuItemId.PaperSizeA4),
        NativeMenuEntryPlan.Separator,
        Item(NativeMenuItemId.PaperSizeMore)
    ];

    public static IReadOnlyList<NativeMenuEntryPlan> PrintAreaMenuEntries { get; } =
    [
        Item(NativeMenuItemId.SetPrintArea),
        Item(NativeMenuItemId.ClearPrintArea)
    ];

    public static IReadOnlyList<NativeMenuEntryPlan> SheetBackgroundMenuEntries { get; } =
    [
        Item(NativeMenuItemId.ChooseSheetBackground),
        Item(NativeMenuItemId.DeleteSheetBackground)
    ];

    public static IReadOnlyList<NativeMenuEntryPlan> AutoSumMenuEntries { get; } =
    [
        Item(NativeMenuItemId.AutoSumSum),
        Item(NativeMenuItemId.AutoSumAverage),
        Item(NativeMenuItemId.AutoSumCountNumbers),
        Item(NativeMenuItemId.AutoSumCountAll),
        Item(NativeMenuItemId.AutoSumMax),
        Item(NativeMenuItemId.AutoSumMin)
    ];

    public static IReadOnlyList<NativeMenuEntryPlan> WhatIfAnalysisMenuEntries { get; } =
    [
        Item(NativeMenuItemId.GoalSeek),
        Item(NativeMenuItemId.ScenarioManager),
        Item(NativeMenuItemId.DataTable)
    ];

    public static IReadOnlyList<NativeMenuEntryPlan> GetMenuEntries(NativeMenuTopLevelId id) =>
        id switch
        {
            NativeMenuTopLevelId.Home => HomeMenuEntries,
            NativeMenuTopLevelId.Insert => InsertMenuEntries,
            NativeMenuTopLevelId.PageLayout => PageLayoutMenuEntries,
            NativeMenuTopLevelId.Formulas => FormulasMenuEntries,
            NativeMenuTopLevelId.Data => DataMenuEntries,
            NativeMenuTopLevelId.Review => ReviewMenuEntries,
            NativeMenuTopLevelId.View => ViewMenuEntries,
            NativeMenuTopLevelId.Sheet => SheetMenuEntries,
            NativeMenuTopLevelId.Window => WindowMenuEntries,
            NativeMenuTopLevelId.Help => HelpMenuEntries,
            _ => []
        };

    public static NativeFileMenuItemPlan GetFileMenuItem(NativeFileMenuItemId id) =>
        FileMenuItems.First(item => item.Id == id);

    public static NativeMenuItemPlan GetMenuItem(NativeMenuItemId id) =>
        MenuItems.First(item => item.Id == id);

    public static NativeFileMenuAvailabilityPlan PlanFileMenuAvailability(
        NativeFileMenuAvailabilityContext context) =>
        new(
        [
            new(NativeFileMenuItemId.NewWorkbook, context.IsIdle),
            new(NativeFileMenuItemId.Open, context.CanOpen),
            new(NativeFileMenuItemId.OpenRecent, context.IsIdle),
            new(NativeFileMenuItemId.ShareWorkbook, context.IsIdle),
            new(NativeFileMenuItemId.BackstageInfo, context.IsIdle),
            new(NativeFileMenuItemId.Save, context.CanSave),
            new(NativeFileMenuItemId.SaveAs, context.CanSaveAs),
            new(NativeFileMenuItemId.Print, context.IsIdle),
            new(NativeFileMenuItemId.PrintPreview, context.IsIdle),
            new(NativeFileMenuItemId.BackstageExport, context.IsIdle && context.CanSaveThroughStorageProvider),
            new(NativeFileMenuItemId.ExportPdf, context.IsIdle && context.CanSaveThroughStorageProvider),
            new(NativeFileMenuItemId.WorkbookStatistics, context.IsIdle),
            new(NativeFileMenuItemId.PageSetup, context.IsIdle),
            new(NativeFileMenuItemId.CloseWorkbook, context.IsIdle),
            new(NativeFileMenuItemId.BackstageAccount, context.IsIdle),
            new(NativeFileMenuItemId.Options, true),
            new(NativeFileMenuItemId.Quit, true)
        ]);

    public static NativeMenuAvailabilityPlan PlanMenuAvailability(
        NativeMenuAvailabilityContext context) =>
        new(
        [
            new(NativeMenuItemId.NewSheet, context.CanAddSheet),
            new(NativeMenuItemId.RenameSheet, context.IsIdle),
            new(NativeMenuItemId.DuplicateSheet, context.IsIdle),
            new(NativeMenuItemId.MoveSheetLeft, context.IsIdle && context.ActiveSheetTabIndex > 0),
            new(
                NativeMenuItemId.MoveSheetRight,
                context.IsIdle &&
                context.ActiveSheetTabIndex >= 0 &&
                context.ActiveSheetTabIndex < context.SheetTabCount - 1),
            new(NativeMenuItemId.TabColor, context.IsIdle),
            new(NativeMenuItemId.SelectAllSheets, context.IsIdle && context.SheetTabCount > 1),
            new(NativeMenuItemId.UngroupSheets, context.IsIdle && context.IsWorkbookGrouped),
            new(NativeMenuItemId.HideSheet, context.IsIdle && context.CanHideActiveSheet),
            new(NativeMenuItemId.UnhideSheet, context.IsIdle && context.HiddenSheetCount > 0),
            new(NativeMenuItemId.DeleteSheet, context.IsIdle),
            new(NativeMenuItemId.Undo, context.CanUndo),
            new(NativeMenuItemId.Redo, context.CanRedo),
            new(NativeMenuItemId.Cut, context.CanCut),
            new(NativeMenuItemId.Copy, context.CanCopy),
            new(NativeMenuItemId.Paste, context.CanPaste),
            new(NativeMenuItemId.PasteSpecial, context.CanPasteSpecial),
            new(NativeMenuItemId.FormatPainter, context.CanFormatPainter),
            new(NativeMenuItemId.SelectAll, context.IsIdle),
            new(NativeMenuItemId.Find, context.IsIdle),
            new(NativeMenuItemId.FindNext, context.IsIdle && context.CanFindNext),
            new(NativeMenuItemId.Replace, context.IsIdle),
            new(NativeMenuItemId.GoTo, context.IsIdle),
            new(NativeMenuItemId.GoToSpecial, context.IsIdle),
            new(NativeMenuItemId.OpenHyperlink, context.IsIdle && context.CanOpenSelectedHyperlink),
            new(NativeMenuItemId.InsertHyperlink, context.IsIdle),
            new(NativeMenuItemId.InsertColumnChart, context.IsIdle),
            new(NativeMenuItemId.InsertBarChart, context.IsIdle),
            new(NativeMenuItemId.InsertLineChart, context.IsIdle),
            new(NativeMenuItemId.InsertPieChart, context.IsIdle),
            new(NativeMenuItemId.InsertAreaChart, context.IsIdle),
            new(NativeMenuItemId.InsertScatterChart, context.IsIdle),
            new(NativeMenuItemId.InsertTable, context.IsIdle && context.SelectedRangeRowCount > 1),
            new(NativeMenuItemId.InsertPivotTable, context.IsIdle && context.SelectedRangeRowCount > 1),
            new(NativeMenuItemId.InsertPicture, context.IsIdle && context.CanInsertPicture),
            new(NativeMenuItemId.InsertShape, context.IsIdle),
            new(NativeMenuItemId.InsertTextBox, context.IsIdle),
            new(NativeMenuItemId.SortAscending, context.IsIdle && context.CanSortSelectedRange),
            new(NativeMenuItemId.SortDescending, context.IsIdle && context.CanSortSelectedRange),
            new(NativeMenuItemId.CustomSort, context.IsIdle && context.CanSortSelectedRange),
            new(NativeMenuItemId.FlashFill, context.IsIdle),
            new(NativeMenuItemId.ToggleFilter, context.IsIdle),
            new(NativeMenuItemId.AdvancedFilter, context.IsIdle),
            new(NativeMenuItemId.RemoveDuplicates, context.IsIdle && context.SelectedRangeRowCount > 1),
            new(
                NativeMenuItemId.Subtotal,
                context.IsIdle &&
                context.SelectedRangeRowCount > 1 &&
                context.SelectedRangeColCount > 1),
            new(NativeMenuItemId.TextToColumns, context.IsIdle && context.SelectedRangeColCount == 1),
            new(NativeMenuItemId.Consolidate, context.IsIdle),
            new(NativeMenuItemId.DataValidationPreview, context.IsIdle),
            new(NativeMenuItemId.DataValidation, context.IsIdle),
            new(NativeMenuItemId.QuickAnalysis, context.IsIdle && context.SelectedRangeCellCount > 1),
            new(NativeMenuItemId.WhatIfAnalysis, context.IsIdle),
            new(NativeMenuItemId.GoalSeek, context.IsIdle),
            new(NativeMenuItemId.ScenarioManager, context.IsIdle),
            new(
                NativeMenuItemId.DataTable,
                context.IsIdle &&
                context.SelectedRangeRowCount > 1 &&
                context.SelectedRangeColCount > 1),
            new(NativeMenuItemId.ForecastSheet, context.IsIdle),
            new(NativeMenuItemId.ReviewSummary, context.IsIdle),
            new(NativeMenuItemId.CheckAccessibility, context.IsIdle),
            new(NativeMenuItemId.ProtectSheet, context.IsIdle),
            new(NativeMenuItemId.ProtectWorkbook, context.IsIdle),
            new(NativeMenuItemId.NextNote, context.IsIdle),
            new(NativeMenuItemId.PreviousNote, context.IsIdle),
            new(NativeMenuItemId.NextComment, context.IsIdle),
            new(NativeMenuItemId.PreviousComment, context.IsIdle),
            new(NativeMenuItemId.Themes, context.IsIdle),
            new(NativeMenuItemId.ThemeColors, context.IsIdle),
            new(NativeMenuItemId.ThemeFonts, context.IsIdle),
            new(NativeMenuItemId.ThemeEffects, context.IsIdle),
            new(NativeMenuItemId.PageMargins, context.IsIdle),
            new(NativeMenuItemId.PageMarginsNormal, context.IsIdle),
            new(NativeMenuItemId.PageMarginsWide, context.IsIdle),
            new(NativeMenuItemId.PageMarginsNarrow, context.IsIdle),
            new(NativeMenuItemId.PageMarginsCustom, context.IsIdle),
            new(NativeMenuItemId.PageOrientation, context.IsIdle),
            new(NativeMenuItemId.PageOrientationPortrait, context.IsIdle),
            new(NativeMenuItemId.PageOrientationLandscape, context.IsIdle),
            new(NativeMenuItemId.PaperSize, context.IsIdle),
            new(NativeMenuItemId.PaperSizeLetter, context.IsIdle),
            new(NativeMenuItemId.PaperSizeLegal, context.IsIdle),
            new(NativeMenuItemId.PaperSizeA4, context.IsIdle),
            new(NativeMenuItemId.PaperSizeMore, context.IsIdle),
            new(NativeMenuItemId.PrintArea, context.IsIdle),
            new(NativeMenuItemId.SetPrintArea, context.IsIdle),
            new(NativeMenuItemId.ClearPrintArea, context.IsIdle),
            new(NativeMenuItemId.PageBreaks, context.IsIdle),
            new(NativeMenuItemId.SheetBackground, context.IsIdle),
            new(NativeMenuItemId.ChooseSheetBackground, context.IsIdle),
            new(NativeMenuItemId.DeleteSheetBackground, context.IsIdle),
            new(NativeMenuItemId.PageSetup, context.IsIdle),
            new(NativeMenuItemId.PrintGridlines, context.IsIdle),
            new(NativeMenuItemId.PrintHeadings, context.IsIdle),
            new(NativeMenuItemId.InsertFunction, context.IsIdle),
            new(NativeMenuItemId.NameManager, context.IsIdle),
            new(NativeMenuItemId.DefineName, context.IsIdle),
            new(NativeMenuItemId.CreateNamesFromSelection, context.IsIdle),
            new(NativeMenuItemId.AutoSum, context.IsIdle),
            new(NativeMenuItemId.AutoSumSum, context.IsIdle),
            new(NativeMenuItemId.AutoSumAverage, context.IsIdle),
            new(NativeMenuItemId.AutoSumCountNumbers, context.IsIdle),
            new(NativeMenuItemId.AutoSumCountAll, context.IsIdle),
            new(NativeMenuItemId.AutoSumMax, context.IsIdle),
            new(NativeMenuItemId.AutoSumMin, context.IsIdle),
            new(NativeMenuItemId.ShowFormulas, context.IsIdle, context.IsShowingFormulas),
            new(NativeMenuItemId.Bold, context.CanBold),
            new(NativeMenuItemId.Italic, context.CanItalic),
            new(NativeMenuItemId.Underline, context.CanUnderline),
            new(NativeMenuItemId.DoubleUnderline, context.CanDoubleUnderline),
            new(NativeMenuItemId.Strikethrough, context.CanStrikethrough),
            new(NativeMenuItemId.IncreaseFontSize, context.CanIncreaseFontSize),
            new(NativeMenuItemId.DecreaseFontSize, context.CanDecreaseFontSize),
            new(NativeMenuItemId.FillColor, context.CanFillColor),
            new(NativeMenuItemId.ClearFill, context.CanFillColor),
            new(NativeMenuItemId.FontColor, context.CanFontColor),
            new(NativeMenuItemId.Borders, context.CanBorders),
            new(NativeMenuItemId.CellStyles, context.CanCellStyles),
            new(NativeMenuItemId.FormatCells, context.IsIdle),
            new(NativeMenuItemId.ConditionalFormatting, true),
            new(NativeMenuItemId.HorizontalText, context.IsIdle),
            new(NativeMenuItemId.AngleCounterclockwise, context.IsIdle),
            new(NativeMenuItemId.AngleClockwise, context.IsIdle),
            new(NativeMenuItemId.VerticalText, context.IsIdle),
            new(NativeMenuItemId.RotateTextUp, context.IsIdle),
            new(NativeMenuItemId.RotateTextDown, context.IsIdle),
            new(NativeMenuItemId.CurrencyFormat, context.CanCurrencyFormat),
            new(NativeMenuItemId.PercentFormat, context.CanPercentFormat),
            new(NativeMenuItemId.CommaStyle, context.CanCommaStyle),
            new(NativeMenuItemId.IncreaseDecimal, context.CanIncreaseDecimal),
            new(NativeMenuItemId.DecreaseDecimal, context.CanDecreaseDecimal),
            new(NativeMenuItemId.AlignLeft, context.CanAlignLeft),
            new(NativeMenuItemId.AlignCenter, context.CanAlignCenter),
            new(NativeMenuItemId.AlignRight, context.CanAlignRight),
            new(NativeMenuItemId.AlignTop, context.CanAlignTop),
            new(NativeMenuItemId.AlignMiddle, context.CanAlignMiddle),
            new(NativeMenuItemId.AlignBottom, context.CanAlignBottom),
            new(NativeMenuItemId.WrapText, context.CanWrapText),
            new(NativeMenuItemId.MergeAndCenter, context.CanMergeAndCenter),
            new(NativeMenuItemId.UnmergeCells, context.IsIdle && context.IsSelectedRangeMerged),
            new(NativeMenuItemId.DecreaseIndent, context.CanDecreaseIndent),
            new(NativeMenuItemId.IncreaseIndent, context.CanIncreaseIndent),
            new(NativeMenuItemId.FillCells, context.CanFillCells),
            new(NativeMenuItemId.FillDown, context.CanFillDown),
            new(NativeMenuItemId.FillRight, context.CanFillRight),
            new(NativeMenuItemId.FillUp, context.CanFillUp),
            new(NativeMenuItemId.FillLeft, context.CanFillLeft),
            new(NativeMenuItemId.FillSeries, context.CanFillSeries),
            new(NativeMenuItemId.Clear, context.CanClear),
            new(NativeMenuItemId.ClearAll, context.CanClear),
            new(NativeMenuItemId.ClearFormats, context.CanClear),
            new(NativeMenuItemId.ClearContents, context.CanClear),
            new(NativeMenuItemId.ClearComments, context.CanClear),
            new(NativeMenuItemId.ClearHyperlinks, context.CanClear),
            new(NativeMenuItemId.ShowGridlines, context.IsIdle, context.IsShowingGridlines),
            new(NativeMenuItemId.ShowHeadings, context.IsIdle, context.IsShowingHeadings),
            new(NativeMenuItemId.ZoomIn, context.IsIdle && context.CanZoomIn),
            new(NativeMenuItemId.ZoomOut, context.IsIdle && context.CanZoomOut),
            new(NativeMenuItemId.Zoom100, context.IsIdle),
            new(NativeMenuItemId.ZoomToSelection, context.IsIdle),
            new(NativeMenuItemId.FreezePanes, context.IsIdle),
            new(NativeMenuItemId.FreezeTopRow, context.IsIdle),
            new(NativeMenuItemId.FreezeFirstColumn, context.IsIdle),
            new(NativeMenuItemId.UnfreezePanes, context.IsIdle),
            new(NativeMenuItemId.PageBreakPreview, context.IsIdle, context.IsPageBreakPreview),
            new(NativeMenuItemId.MinimizeWindow, true),
            new(NativeMenuItemId.ZoomWindow, true),
            new(NativeMenuItemId.BringAllToFront, true),
            new(NativeMenuItemId.HelpOnline, true),
            new(NativeMenuItemId.SendFeedback, true),
            new(NativeMenuItemId.CheckForUpdates, true),
            new(NativeMenuItemId.About, true),
            new(NativeMenuItemId.LegalNotices, true)
        ]);

    private static NativeFileMenuEntryPlan FileItem(NativeFileMenuItemId id) =>
        NativeFileMenuEntryPlan.ForItem(GetFileMenuItem(id));

    private static NativeMenuEntryPlan Item(NativeMenuItemId id) =>
        NativeMenuEntryPlan.ForItem(id);

    private static NativeMenuGesturePlan NativeMenuGesture(WorkbookShortcutRoute route)
    {
        var chord = WorkbookKeyboardShortcutCatalog.GetNativeMenuChord(route);
        return new NativeMenuGesturePlan(
            ToNativeMenuGestureKey(chord.Key),
            ToNativeMenuGestureModifiers(chord.Modifiers));
    }

    private static NativeMenuGestureKey ToNativeMenuGestureKey(WorkbookShortcutKey key) =>
        key switch
        {
            WorkbookShortcutKey.B => NativeMenuGestureKey.B,
            WorkbookShortcutKey.C => NativeMenuGestureKey.C,
            WorkbookShortcutKey.D => NativeMenuGestureKey.D,
            WorkbookShortcutKey.D1 => NativeMenuGestureKey.D1,
            WorkbookShortcutKey.D5 => NativeMenuGestureKey.D5,
            WorkbookShortcutKey.E => NativeMenuGestureKey.E,
            WorkbookShortcutKey.F => NativeMenuGestureKey.F,
            WorkbookShortcutKey.F3 => NativeMenuGestureKey.F3,
            WorkbookShortcutKey.F11 => NativeMenuGestureKey.F11,
            WorkbookShortcutKey.G => NativeMenuGestureKey.G,
            WorkbookShortcutKey.H => NativeMenuGestureKey.H,
            WorkbookShortcutKey.I => NativeMenuGestureKey.I,
            WorkbookShortcutKey.N => NativeMenuGestureKey.N,
            WorkbookShortcutKey.O => NativeMenuGestureKey.O,
            WorkbookShortcutKey.Oem3 => NativeMenuGestureKey.Oem3,
            WorkbookShortcutKey.OemPlus => NativeMenuGestureKey.OemPlus,
            WorkbookShortcutKey.R => NativeMenuGestureKey.R,
            WorkbookShortcutKey.S => NativeMenuGestureKey.S,
            WorkbookShortcutKey.U => NativeMenuGestureKey.U,
            WorkbookShortcutKey.V => NativeMenuGestureKey.V,
            WorkbookShortcutKey.X => NativeMenuGestureKey.X,
            WorkbookShortcutKey.Z => NativeMenuGestureKey.Z,
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, null)
        };

    private static NativeMenuGestureModifiers ToNativeMenuGestureModifiers(WorkbookShortcutModifiers modifiers)
    {
        var result = NativeMenuGestureModifiers.None;
        if (modifiers.HasFlag(WorkbookShortcutModifiers.Control))
            result |= NativeMenuGestureModifiers.Control;
        if (modifiers.HasFlag(WorkbookShortcutModifiers.Alt))
            result |= NativeMenuGestureModifiers.Alt;
        if (modifiers.HasFlag(WorkbookShortcutModifiers.Shift))
            result |= NativeMenuGestureModifiers.Shift;
        if (modifiers.HasFlag(WorkbookShortcutModifiers.Meta))
            result |= NativeMenuGestureModifiers.Meta;
        return result;
    }
}
