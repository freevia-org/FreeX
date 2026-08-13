using System.Globalization;
using Free.Shared.Ribbon;

namespace FreeX.App.Services.Ribbon;

public static class QuickAccessToolbarCommandIds
{
    public const string Save = "Save";
    public const string Undo = "Undo";
    public const string Redo = "Redo";
    public const string New = "New";
    public const string Open = "Open";
    public const string SaveAs = "SaveAs";
    public const string Print = "Print";
    public const string ExportPdfXps = "ExportPdfXps";
    public const string Cut = "Cut";
    public const string Copy = "Copy";
    public const string Paste = "Paste";
    public const string FormatPainter = "FormatPainter";
    public const string Bold = "Bold";
    public const string Italic = "Italic";
    public const string Underline = "Underline";
    public const string FillColor = "FillColor";
    public const string FontColor = "FontColor";
    public const string FormatCells = "FormatCells";
    public const string InsertFunction = "InsertFunction";
    public const string AutoSum = "AutoSum";
    public const string CalculateNow = "CalculateNow";
    public const string CalculateSheet = "CalculateSheet";
    public const string RefreshAll = "RefreshAll";
    public const string SortAscending = "SortAscending";
    public const string SortDescending = "SortDescending";
    public const string Filter = "Filter";
    public const string DataValidation = "DataValidation";
    public const string NameManager = "NameManager";
    public const string Spelling = "Spelling";
    public const string CheckAccessibility = "CheckAccessibility";
    public const string ShareWorkbook = "ShareWorkbook";
    public const string Zoom100 = "Zoom100";
    public const string ZoomSelection = "ZoomSelection";
    public const string FreezePanes = "FreezePanes";
    public const string InsertSheet = "InsertSheet";
    public const string FindSelect = "FindSelect";
    public const string SelectionPane = "SelectionPane";
}

public sealed record QuickAccessToolbarCommandDefinition(
    string Id,
    string CommandName,
    string TitleResourceKey,
    string DescriptionResourceKey,
    RibbonCommandIconKind IconKind,
    string AutomationId);

public static class QuickAccessToolbarCatalog
{
    public static readonly IReadOnlyList<string> DefaultCommandIds =
    [
        QuickAccessToolbarCommandIds.Save,
        QuickAccessToolbarCommandIds.Undo,
        QuickAccessToolbarCommandIds.Redo
    ];

    public static readonly IReadOnlyList<QuickAccessToolbarCommandDefinition> Commands =
    [
        new(QuickAccessToolbarCommandIds.Save, "Save", "MainWindow_TooltipTitle_Save", "MainWindow_TooltipDescription_SaveTheWorkbook", RibbonCommandIconKind.Save, "SaveQatBtn"),
        new(QuickAccessToolbarCommandIds.Undo, "Undo", "MainWindow_TooltipTitle_Undo", "MainWindow_TooltipDescription_UndoTheLastAction", RibbonCommandIconKind.Undo, "UndoQatBtn"),
        new(QuickAccessToolbarCommandIds.Redo, "Redo", "MainWindow_TooltipTitle_Redo", "MainWindow_TooltipDescription_RedoTheLastUndoneAction", RibbonCommandIconKind.Redo, "RedoQatBtn"),
        new(QuickAccessToolbarCommandIds.New, "New", "MainWindow_TooltipTitle_New", "MainWindow_TooltipDescription_CreateANewWorkbook", RibbonCommandIconKind.Insert, "NewQatBtn"),
        new(QuickAccessToolbarCommandIds.Open, "Open", "MainWindow_TooltipTitle_Open", "MainWindow_TooltipDescription_OpenAnExistingWorkbook", RibbonCommandIconKind.Book, "OpenQatBtn"),
        new(QuickAccessToolbarCommandIds.SaveAs, "Save As", "MainWindow_TooltipTitle_SaveAs", "MainWindow_TooltipDescription_SaveTheWorkbookWithANewNameOrFormat", RibbonCommandIconKind.Save, "SaveAsQatBtn"),
        new(QuickAccessToolbarCommandIds.Print, "Print", "MainWindow_TooltipTitle_Print", "MainWindow_TooltipDescription_OpenThePrintPreviewAndNativePrintDialogForTheRenderedWorksheet", RibbonCommandIconKind.Print, "PrintQatBtn"),
        new(QuickAccessToolbarCommandIds.ExportPdfXps, "Export PDF/XPS", "MainWindow_TooltipTitle_ExportPDFXPS", "MainWindow_TooltipDescription_SaveSheetsTheCurrentSelectionOrTheWorkbookAsAPDFFileOrAnXPSPackage", RibbonCommandIconKind.Print, "ExportPdfXpsQatBtn"),
        new(QuickAccessToolbarCommandIds.Cut, "Cut", "MainWindow_TooltipTitle_Cut", "MainWindow_TooltipDescription_CutTheSelectionAndPutItOnTheClipboardCtrlX", RibbonCommandIconKind.Cut, "CutQatBtn"),
        new(QuickAccessToolbarCommandIds.Copy, "Copy", "MainWindow_TooltipTitle_Copy", "MainWindow_TooltipDescription_CopyTheSelectionAndPutItOnTheClipboardCtrlC", RibbonCommandIconKind.Copy, "CopyQatBtn"),
        new(QuickAccessToolbarCommandIds.Paste, "Paste", "MainWindow_TooltipTitle_Paste", "MainWindow_TooltipDescription_PasteTheContentsOfTheClipboardCtrlV", RibbonCommandIconKind.Paste, "PasteQatBtn"),
        new(QuickAccessToolbarCommandIds.FormatPainter, "Format Painter", "MainWindow_TooltipTitle_FormatPainter", "MainWindow_TooltipDescription_CopyFormattingFromOnePlaceAndApplyItToAnother", RibbonCommandIconKind.FormatPainter, "FormatPainterQatBtn"),
        new(QuickAccessToolbarCommandIds.Bold, "Bold", "MainWindow_TooltipTitle_Bold", "MainWindow_TooltipDescription_MakeTheSelectedTextBoldCtrlB", RibbonCommandIconKind.Bold, "BoldQatBtn"),
        new(QuickAccessToolbarCommandIds.Italic, "Italic", "MainWindow_TooltipTitle_Italic", "MainWindow_TooltipDescription_ItalicizeTheSelectedTextCtrlI", RibbonCommandIconKind.Italic, "ItalicQatBtn"),
        new(QuickAccessToolbarCommandIds.Underline, "Underline", "MainWindow_TooltipTitle_Underline", "MainWindow_TooltipDescription_UnderlineTheSelectedTextCtrlU", RibbonCommandIconKind.Underline, "UnderlineQatBtn"),
        new(QuickAccessToolbarCommandIds.FillColor, "Fill Color", "MainWindow_TooltipTitle_FillColor", "MainWindow_TooltipDescription_ColorTheBackgroundOfTheSelectedCells", RibbonCommandIconKind.Fill, "FillColorQatBtn"),
        new(QuickAccessToolbarCommandIds.FontColor, "Font Color", "MainWindow_TooltipTitle_FontColor", "MainWindow_TooltipDescription_ChangeTheColorOfTheText", RibbonCommandIconKind.Color, "FontColorQatBtn"),
        new(QuickAccessToolbarCommandIds.FormatCells, "Format Cells", "FormatCells_FormatCells", "MainWindow_TooltipDescription_OpenFormatCellsDialogForTheSelection", RibbonCommandIconKind.Grid, "FormatCellsQatBtn"),
        new(QuickAccessToolbarCommandIds.InsertFunction, "Insert Function", "MainWindow_TooltipTitle_InsertFunction", "MainWindow_TooltipDescription_SearchForAndInsertAFunctionIntoTheSelectedCell", RibbonCommandIconKind.Function, "InsertFunctionQatBtn"),
        new(QuickAccessToolbarCommandIds.AutoSum, "AutoSum", "MainWindow_TooltipTitle_AutoSum", "MainWindow_TooltipDescription_AutomaticallyInsertASUMAVERAGECOUNTCOUNTAMAXOrMINFormula", RibbonCommandIconKind.Sum, "AutoSumQatBtn"),
        new(QuickAccessToolbarCommandIds.CalculateNow, "Calculate Now", "MainWindow_TooltipTitle_CalculateNow", "MainWindow_TooltipDescription_RecalculateAllFormulasInAllOpenWorkbooksNow", RibbonCommandIconKind.Refresh, "CalculateNowQatBtn"),
        new(QuickAccessToolbarCommandIds.CalculateSheet, "Calculate Sheet", "MainWindow_TooltipTitle_CalculateSheet", "MainWindow_TooltipDescription_RecalculateAllFormulasInTheActiveSheet", RibbonCommandIconKind.Refresh, "CalculateSheetQatBtn"),
        new(QuickAccessToolbarCommandIds.RefreshAll, "Refresh All", "MainWindow_TooltipTitle_RefreshAll", "MainWindow_TooltipDescription_RecalculateFormulasAndRefreshFreeXManagedWorkbookDataExternalDataConnect_ECF2806B", RibbonCommandIconKind.Refresh, "RefreshAllQatBtn"),
        new(QuickAccessToolbarCommandIds.SortAscending, "Sort A to Z", "MainWindow_TooltipTitle_SortAToZ", "MainWindow_TooltipDescription_SortTheSelectedColumnFromSmallestToLargestAToZ", RibbonCommandIconKind.SortAscending, "SortAscendingQatBtn"),
        new(QuickAccessToolbarCommandIds.SortDescending, "Sort Z to A", "MainWindow_TooltipTitle_SortZToA", "MainWindow_TooltipDescription_SortTheSelectedColumnFromLargestToSmallestZToA", RibbonCommandIconKind.SortDescending, "SortDescendingQatBtn"),
        new(QuickAccessToolbarCommandIds.Filter, "Filter", "MainWindow_TooltipTitle_Filter", "MainWindow_TooltipDescription_EnableDropdownFiltersOnEachColumnToShowOnlyTheRowsYouWant", RibbonCommandIconKind.Filter, "FilterQatBtn"),
        new(QuickAccessToolbarCommandIds.DataValidation, "Data Validation", "MainWindow_TooltipTitle_DataValidation", "MainWindow_TooltipDescription_ControlWhatDataCanBeEnteredInTheSelectedCells", RibbonCommandIconKind.Warning, "DataValidationQatBtn"),
        new(QuickAccessToolbarCommandIds.NameManager, "Name Manager", "MainWindow_TooltipTitle_NameManager", "MainWindow_TooltipDescription_ViewCreateEditAndDeleteAllNamedRangesInTheWorkbook", RibbonCommandIconKind.Label, "NameManagerQatBtn"),
        new(QuickAccessToolbarCommandIds.Spelling, "Spelling", "MainWindow_TooltipTitle_Spelling", "MainWindow_TooltipDescription_FindKnownMisspellingsInTextCellsOnTheActiveSheetWithReplaceReplaceAllAnd_D58B6767", RibbonCommandIconKind.Spelling, "SpellingQatBtn"),
        new(QuickAccessToolbarCommandIds.CheckAccessibility, "Check Accessibility", "MainWindow_TooltipTitle_CheckAccessibility", "MainWindow_TooltipDescription_FindMergedCellsBlankTableHeadersObjectsMissingAlternateTextAndChartsWith_4FECDB20", RibbonCommandIconKind.Accessibility, "CheckAccessibilityQatBtn"),
        new(QuickAccessToolbarCommandIds.ShareWorkbook, "Share Workbook", "MainWindow_TooltipTitle_ShareWorkbook", "MainWindow_TooltipDescription_SaveTheWorkbookIfNeededAndOpenWindowsShareForTheFile", RibbonCommandIconKind.Share, "ShareWorkbookQatBtn"),
        new(QuickAccessToolbarCommandIds.Zoom100, "100% Zoom", "MainWindow_TooltipTitle_Zoom100", "MainWindow_TooltipDescription_ResetTheZoomLevelTo100", RibbonCommandIconKind.Zoom, "Zoom100QatBtn"),
        new(QuickAccessToolbarCommandIds.ZoomSelection, "Zoom to Selection", "MainWindow_TooltipTitle_ZoomToSelection", "MainWindow_TooltipDescription_ZoomTheSelectedRangeToFitTheWindow", RibbonCommandIconKind.Zoom, "ZoomSelectionQatBtn"),
        new(QuickAccessToolbarCommandIds.FreezePanes, "Freeze Panes", "MainWindow_TooltipTitle_FreezePanes", "MainWindow_TooltipDescription_KeepRowsOrColumnsVisibleWhileScrollingCreatingFrozenPanesClearsSplitPane_D77EB1E0", RibbonCommandIconKind.Freeze, "FreezePanesQatBtn"),
        new(QuickAccessToolbarCommandIds.InsertSheet, "Insert Sheet", "MainWindow_TooltipTitle_InsertSheet", "MainWindow_TooltipDescription_AddANewSheetToTheWorkbook", RibbonCommandIconKind.Insert, "InsertSheetQatBtn"),
        new(QuickAccessToolbarCommandIds.FindSelect, "Find & Select", "MainWindow_TooltipTitle_FindSelect", "MainWindow_TooltipDescription_FindReplaceOrGoToSpecificCellsInTheWorkbook", RibbonCommandIconKind.Search, "FindSelectQatBtn"),
        new(QuickAccessToolbarCommandIds.SelectionPane, "Selection Pane", "MainWindow_TooltipTitle_SelectionPane", "MainWindow_TooltipDescription_ListSheetObjectsAndControlVisibilityOrStackingOrder", RibbonCommandIconKind.List, "SelectionPaneQatBtn")
    ];

    private static readonly IReadOnlyDictionary<string, QuickAccessToolbarCommandDefinition> ById =
        Commands.ToDictionary(command => command.Id, StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, QuickAccessToolbarCommandDefinition> ByCommandName =
        Commands
            .GroupBy(command => command.CommandName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<QuickAccessToolbarCommandDefinition> Normalize(IEnumerable<string>? commandIds)
    {
        var result = new List<QuickAccessToolbarCommandDefinition>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in commandIds ?? DefaultCommandIds)
        {
            if (string.IsNullOrWhiteSpace(id) ||
                !ById.TryGetValue(id.Trim(), out var command) ||
                !seen.Add(command.Id))
            {
                continue;
            }

            result.Add(command);
        }

        if (result.Count == 0)
            result.AddRange(DefaultCommandIds.Select(id => ById[id]));

        return result;
    }

    public static IReadOnlyList<string> NormalizeCommandIds(IEnumerable<string>? commandIds) =>
        Normalize(commandIds).Select(command => command.Id).ToList();

    public static string FormatKeyTip(int visibleIndex)
    {
        if (visibleIndex <= 9)
            return visibleIndex.ToString(CultureInfo.InvariantCulture);

        var offset = visibleIndex - 9;
        const string extraKeyTipCharacters = "123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        return offset <= extraKeyTipCharacters.Length
            ? $"0{extraKeyTipCharacters[offset - 1]}"
            : visibleIndex.ToString(CultureInfo.InvariantCulture);
    }

    public static bool TryGet(string id, out QuickAccessToolbarCommandDefinition definition) =>
        ById.TryGetValue(id, out definition!);

    public static bool TryGetByCommandName(string commandName, out QuickAccessToolbarCommandDefinition definition) =>
        ByCommandName.TryGetValue(commandName.Trim(), out definition!);
}
