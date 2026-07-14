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
