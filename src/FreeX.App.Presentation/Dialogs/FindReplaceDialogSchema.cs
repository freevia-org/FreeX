using Free.Shared.Localization;
using FreeX.Core.Commands;

namespace FreeX.App.Presentation.Dialogs;

public enum FindReplaceDialogText
{
    Title,
    Find,
    Replace,
    FindWhat,
    ReplaceWith,
    Format,
    Clear,
    ChooseFromCell,
    FormatSetButton,
    Options,
    OptionsExpanded,
    Within,
    Search,
    LookIn,
    Sheet,
    Workbook,
    ByRows,
    ByColumns,
    Formulas,
    Values,
    Notes,
    Comments,
    MatchCase,
    MatchEntireCellContents,
    Book,
    Name,
    Cell,
    Value,
    Formula,
    FindAll,
    FindNext,
    ReplaceAll,
    Close,
    FindWhatRequired,
    FindFailed,
    FindAllFailed,
    ReplaceFailed,
    FormatCellsFailed,
    FindResultNotSelected,
    NoMatchesFound,
    MatchStatus,
    FoundRangeStatus,
    FoundSheetCellStatus,
    CellsFoundStatus,
    ReplacedCellsStatus,
    ReplacedRangeStatus,
    NoReplaceableMatchFound,
    ReplacedOneCell,
    SelectFormatSourceStatus,
    NoCellFormatFoundStatus,
    FormatChosenFromResultStatus,
    FormatChosenFromWorksheetStatus,
    FindFormatSetToolTip,
    ReplaceFormatSetToolTip,
    ResultsAutomationName,
}

public sealed record FindReplaceDialogChoice<T>(T Value, FindReplaceDialogText Text);

public static class FindReplaceDialogSchema
{
    private static readonly IReadOnlyList<FindReplaceDialogChoice<FindWithin>> WithinChoiceValues =
        Array.AsReadOnly(
        [
            new FindReplaceDialogChoice<FindWithin>(FindWithin.Sheet, FindReplaceDialogText.Sheet),
            new FindReplaceDialogChoice<FindWithin>(FindWithin.Workbook, FindReplaceDialogText.Workbook),
        ]);

    private static readonly IReadOnlyList<FindReplaceDialogChoice<FindSearchOrder>> SearchChoiceValues =
        Array.AsReadOnly(
        [
            new FindReplaceDialogChoice<FindSearchOrder>(FindSearchOrder.ByRows, FindReplaceDialogText.ByRows),
            new FindReplaceDialogChoice<FindSearchOrder>(FindSearchOrder.ByColumns, FindReplaceDialogText.ByColumns),
        ]);

    private static readonly IReadOnlyList<FindReplaceDialogChoice<FindLookIn>> LookInChoiceValues =
        Array.AsReadOnly(
        [
            new FindReplaceDialogChoice<FindLookIn>(FindLookIn.Formulas, FindReplaceDialogText.Formulas),
            new FindReplaceDialogChoice<FindLookIn>(FindLookIn.Values, FindReplaceDialogText.Values),
            new FindReplaceDialogChoice<FindLookIn>(FindLookIn.Notes, FindReplaceDialogText.Notes),
            new FindReplaceDialogChoice<FindLookIn>(FindLookIn.Comments, FindReplaceDialogText.Comments),
        ]);

    public static IReadOnlyList<FindReplaceDialogChoice<FindWithin>> WithinChoices => WithinChoiceValues;

    public static IReadOnlyList<FindReplaceDialogChoice<FindSearchOrder>> SearchChoices => SearchChoiceValues;

    public static IReadOnlyList<FindReplaceDialogChoice<FindLookIn>> LookInChoices => LookInChoiceValues;

    public static LocalizedTextDescriptor Describe(FindReplaceDialogText text, params object?[] arguments) =>
        LocalizedTextDescriptor.Resource(ResourceKey(text), arguments);

    public static string Resolve(
        FindReplaceDialogText text,
        Func<string, string> getText,
        Func<string, object?[], string> formatText,
        bool stripAccessKeys = false,
        params object?[] arguments)
    {
        var resolved = Describe(text, arguments).Resolve(getText, formatText);
        return stripAccessKeys
            ? resolved.Replace("_", string.Empty, StringComparison.Ordinal)
            : resolved;
    }

    private static string ResourceKey(FindReplaceDialogText text) => text switch
    {
        FindReplaceDialogText.Title => "FindReplace_FindAndReplace",
        FindReplaceDialogText.Find => "FindReplace_Find",
        FindReplaceDialogText.Replace => "FindReplace_Replace",
        FindReplaceDialogText.FindWhat => "FindReplace_FindWhat",
        FindReplaceDialogText.ReplaceWith => "FindReplace_ReplaceWith",
        FindReplaceDialogText.Format => "FindReplace_Format",
        FindReplaceDialogText.Clear => "FindReplace_Clear",
        FindReplaceDialogText.ChooseFromCell => "FindReplace_ChooseFromCell",
        FindReplaceDialogText.FormatSetButton => "FindReplace_FormatSetButton",
        FindReplaceDialogText.Options => "FindReplace_Options",
        FindReplaceDialogText.OptionsExpanded => "FindReplace_OptionsExpanded",
        FindReplaceDialogText.Within => "FindReplace_Within",
        FindReplaceDialogText.Search => "FindReplace_Search",
        FindReplaceDialogText.LookIn => "FindReplace_LookIn",
        FindReplaceDialogText.Sheet => "FindReplace_Sheet",
        FindReplaceDialogText.Workbook => "FindReplace_Workbook",
        FindReplaceDialogText.ByRows => "FindReplace_ByRows",
        FindReplaceDialogText.ByColumns => "FindReplace_ByColumns",
        FindReplaceDialogText.Formulas => "FindReplace_Formulas",
        FindReplaceDialogText.Values => "FindReplace_Values",
        FindReplaceDialogText.Notes => "FindReplace_Notes",
        FindReplaceDialogText.Comments => "FindReplace_Comments",
        FindReplaceDialogText.MatchCase => "FindReplace_MatchCase",
        FindReplaceDialogText.MatchEntireCellContents => "FindReplace_MatchEntireCellContents",
        FindReplaceDialogText.Book => "FindReplace_Book",
        FindReplaceDialogText.Name => "FindReplace_Name",
        FindReplaceDialogText.Cell => "FindReplace_Cell",
        FindReplaceDialogText.Value => "FindReplace_Value",
        FindReplaceDialogText.Formula => "FindReplace_Formula",
        FindReplaceDialogText.FindAll => "FindReplace_FindAll",
        FindReplaceDialogText.FindNext => "FindReplace_FindNext",
        FindReplaceDialogText.ReplaceAll => "FindReplace_ReplaceAll",
        FindReplaceDialogText.Close => "FindReplace_Close",
        FindReplaceDialogText.FindWhatRequired => "FindReplace_FindWhatRequired",
        FindReplaceDialogText.FindFailed => "MainLoc_FindFailed",
        FindReplaceDialogText.FindAllFailed => "MainLoc_FindAllFailed",
        FindReplaceDialogText.ReplaceFailed => "MainLoc_ReplaceFailed",
        FindReplaceDialogText.FormatCellsFailed => "MainLoc_FormatCellsFailed",
        FindReplaceDialogText.FindResultNotSelected => "MainLoc_FindResultNotSelected",
        FindReplaceDialogText.NoMatchesFound => "FindReplace_NoMatchesFound",
        FindReplaceDialogText.MatchStatus => "FindReplace_MatchStatus",
        FindReplaceDialogText.FoundRangeStatus => "MainLoc_FoundRangeOfCount",
        FindReplaceDialogText.FoundSheetCellStatus => "MainLoc_FoundSheetCell",
        FindReplaceDialogText.CellsFoundStatus => "FindReplace_CellsFoundStatus",
        FindReplaceDialogText.ReplacedCellsStatus => "FindReplace_ReplacedCellsStatus",
        FindReplaceDialogText.ReplacedRangeStatus => "MainLoc_ReplacedRangeOfCount",
        FindReplaceDialogText.NoReplaceableMatchFound => "FindReplace_NoReplaceableMatchFound",
        FindReplaceDialogText.ReplacedOneCell => "FindReplace_ReplacedOneCell",
        FindReplaceDialogText.SelectFormatSourceStatus => "FindReplace_SelectFormatSourceStatus",
        FindReplaceDialogText.NoCellFormatFoundStatus => "FindReplace_NoCellFormatFoundStatus",
        FindReplaceDialogText.FormatChosenFromResultStatus => "FindReplace_FormatChosenFromResultStatus",
        FindReplaceDialogText.FormatChosenFromWorksheetStatus => "FindReplace_FormatChosenFromWorksheetStatus",
        FindReplaceDialogText.FindFormatSetToolTip => "FindReplace_FindFormatSetToolTip",
        FindReplaceDialogText.ReplaceFormatSetToolTip => "FindReplace_ReplaceFormatSetToolTip",
        FindReplaceDialogText.ResultsAutomationName => "FindReplace_ResultsAutomationName",
        _ => throw new ArgumentOutOfRangeException(nameof(text), text, null),
    };
}
