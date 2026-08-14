using Free.Shared.AppServices;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public static class FindReplaceDialogPlanner
{
    public const double Width = 720;
    public const double Height = 430;
    public const double MinWidth = 520;
    public const double MinHeight = 360;
    public const double RootMargin = 12;
    public const double AvaloniaRootRightMargin = 28;
    public const double AvaloniaRootBottomMargin = 44;

    // WPF-authoritative logical layout values shared by the WPF XAML and Avalonia consumer.
    public const double TabContentMargin = 10;
    public const double FieldLabelColumnWidth = 88;
    public const double FieldMinWidth = 260;
    public const double FormatButtonWidth = 84;
    public const double ClearFormatButtonWidth = 52;
    public const double ChooseFormatButtonWidth = 136;
    public const double FormatButtonMargin = 8;
    public const double AdjacentFormatButtonMargin = 6;
    // WPF's auto-sized tab host heights at the shared 720x430 evidence target.
    public const double FindTabHeight = 74;
    public const double ReplaceTabHeight = 108;
    public const double ResultsMinimumHeight = 120;
    public const double ResultsBottomMargin = 7;
    public const double ResultsHeaderHeight = 24;
    public const double StatusMinimumHeight = 18;
    public const double StatusBottomMargin = 8;
    public const double ActionButtonSpacing = 8;
    public const double ActionButtonHeight = 20;
    public const double OptionsHeaderMinimumWidth = 112;
    public const double AvaloniaOptionsBottomMargin = 13;
    public const double FindAllButtonWidth = 76;
    public const double FindNextButtonWidth = 80;
    public const double ReplaceButtonWidth = 76;
    public const double ReplaceAllButtonWidth = 88;
    public const double CloseButtonWidth = 60;
    public const double ResultBookColumnWidth = 110;
    public const double ResultSheetColumnWidth = 100;
    public const double ResultNameColumnWidth = 90;
    public const double ResultCellColumnWidth = 70;

    /// <summary>
    /// Projects the host's <c>replaceMode</c> flag onto the cross-app open mode owned by
    /// <see cref="FindReplaceDialogPolicy"/>. FreeX renders the mode as a selected TabItem;
    /// FreeW and FreeP render it differently, but the state itself is the same decision.
    /// </summary>
    public static FindReplaceOpenMode OpenModeFor(bool replaceMode) =>
        FindReplaceDialogPolicy.OpenModeFor(replaceMode);

    /// <summary>
    /// Whether the Replace / Replace All commands (and the "Replace with" row in the Avalonia
    /// renderer) are offered for the given mode. Shared with FreeP through
    /// <see cref="FindReplaceDialogPolicy.ShowsReplaceSurface"/>.
    /// </summary>
    public static bool ShowsReplaceCommands(FindReplaceOpenMode mode) =>
        FindReplaceDialogPolicy.ShowsReplaceSurface(mode);

    /// <summary>
    /// Convenience overload for renderers that only know "is the Replace tab selected".
    /// </summary>
    public static bool ShowsReplaceCommands(bool replaceMode) =>
        ShowsReplaceCommands(OpenModeFor(replaceMode));

    // Deliberately NOT shared with FreeW/FreeP (see FindReplaceDialogPolicy):
    // * Status text. FreeX resolves every status through localized FindReplaceDialogText resource
    //   keys, so it cannot consume the policy's English literals without regressing localization.
    // * The blank-search allowance. Excel permits an empty "Find what" when a Format criterion is
    //   set (R64-commands-find-replace-6-1); no sister app has a format criterion.
    // * The result cursor. FindReplaceWorkflowSession anchors the next match to the active cell in
    //   workbook (sheet, row, column) order with replaceable-match skipping -- not the modular
    //   wrap cursor FindReplaceDialogPolicy.Navigate serves for FreeP.

    public static FindOptions CreateFindOptions(
        SheetId? currentSheetId,
        int withinSelectedIndex,
        int searchOrderSelectedIndex,
        int lookInSelectedIndex,
        StyleDiff? requiredFormat = null,
        IReadOnlyList<GridRange>? selectionScope = null) =>
        new(
            Within: withinSelectedIndex == 1 ? FindWithin.Workbook : FindWithin.Sheet,
            CurrentSheetId: currentSheetId,
            SearchOrder: searchOrderSelectedIndex == 1 ? FindSearchOrder.ByColumns : FindSearchOrder.ByRows,
            LookIn: lookInSelectedIndex switch
            {
                0 => FindLookIn.Formulas,
                2 => FindLookIn.Notes,
                3 => FindLookIn.Comments,
                _ => FindLookIn.Values
            },
            RequiredFormat: requiredFormat,
            SelectionScope: selectionScope);

    public static IReadOnlyList<GridRange>? ResolveSelectionScopeAtOpen(
        GridRange? selectedRange,
        IReadOnlyList<GridRange>? selectedRanges)
    {
        var ranges = SelectionStyleCommandPlanner.ResolveRanges(selectedRange, selectedRanges);
        if (ranges.Count == 0 ||
            (ranges.Count == 1 && ranges[0].Start == ranges[0].End))
        {
            return null;
        }

        return ranges;
    }

    public static IReadOnlyList<FindResultRow> BuildFindResultRows(Workbook workbook, IReadOnlyList<FindResult> results) =>
        results
            .Select(result => CreateFindResultRow(workbook, result))
            .ToList();

    public static StyleDiff? CreateFormatDiffFromCell(Workbook workbook, CellAddress address)
    {
        var sheet = workbook.GetSheet(address.Sheet);
        var cell = sheet?.GetCell(address);
        return cell is null ? null : StyleDiff.FromStyle(workbook.GetStyle(cell.StyleId));
    }

    private static FindResultRow CreateFindResultRow(Workbook workbook, FindResult result)
    {
        var sheet = workbook.GetSheet(result.Address.Sheet);
        var cell = sheet?.GetCell(result.Address);
        return new FindResultRow(
            workbook.Name,
            sheet?.Name ?? "",
            FindNameForAddress(workbook, result.Address),
            result.Address,
            result.Address.ToA1(),
            result.MatchedText,
            cell?.HasFormula == true ? cell.FormulaText ?? "" : "");
    }

    private static string FindNameForAddress(Workbook workbook, CellAddress address)
    {
        string? namedRangeName = null;
        long namedRangeCellCount = 0;
        foreach (var pair in workbook.NamedRanges)
        {
            if (!pair.Value.Contains(address))
                continue;

            if (namedRangeName is null
                || pair.Value.CellCount < namedRangeCellCount
                || (pair.Value.CellCount == namedRangeCellCount
                    && string.Compare(pair.Key, namedRangeName, StringComparison.OrdinalIgnoreCase) < 0))
            {
                namedRangeName = pair.Key;
                namedRangeCellCount = pair.Value.CellCount;
            }
        }

        return string.IsNullOrEmpty(namedRangeName) ? "" : namedRangeName;
    }

    public static bool ReplaceSingleMatch(
        Workbook workbook,
        ICommandBus commandBus,
        FindResult match,
        string searchText,
        string replaceText,
        bool matchCase,
        bool matchEntireCell,
        FindLookIn lookIn = FindLookIn.Values,
        StyleDiff? replacementFormat = null)
        => TryReplaceSingleMatch(
            workbook,
            commandBus,
            match,
            searchText,
            replaceText,
            matchCase,
            matchEntireCell,
            lookIn,
            replacementFormat).Replaced;

    public static ReplaceSingleMatchResult TryReplaceSingleMatch(
        Workbook workbook,
        ICommandBus commandBus,
        FindResult match,
        string searchText,
        string replaceText,
        bool matchCase,
        bool matchEntireCell,
        FindLookIn lookIn = FindLookIn.Values,
        StyleDiff? replacementFormat = null)
    {
        if (!FindReplaceDialogPolicy.CanRunWithQuery(searchText))
            return new ReplaceSingleMatchResult(false, null);

        var sheet = workbook.GetSheet(match.Address.Sheet);
        if (sheet is null)
            return new ReplaceSingleMatchResult(false, null);

        if (!FindReplaceService.TryCreateReplacementCommand(
                sheet,
                match,
                searchText,
                replaceText,
                matchCase,
                matchEntireCell,
                FindLookInForTarget(match.Target, lookIn),
                replacementFormat,
                out var command,
                workbook: workbook))
            return new ReplaceSingleMatchResult(false, null);

        var outcome = commandBus.Execute(workbook.Id, command);
        return outcome.Success
            ? new ReplaceSingleMatchResult(true, null)
            : new ReplaceSingleMatchResult(false, outcome);
    }

    private static FindLookIn FindLookInForTarget(FindResultTarget target, FindLookIn lookIn) => target switch
    {
        FindResultTarget.Note => FindLookIn.Notes,
        FindResultTarget.ThreadedComment or FindResultTarget.ThreadedCommentReply => FindLookIn.Comments,
        _ => lookIn
    };
}

public sealed record FindResultRow(
    string Book,
    string Sheet,
    string Name,
    CellAddress Address,
    string Cell,
    string Value,
    string Formula);

public sealed record ReplaceSingleMatchResult(bool Replaced, CommandOutcome? Failure);
