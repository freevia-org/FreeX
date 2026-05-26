using System.Globalization;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

internal static class FindReplaceDialogPlanner
{
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

    public static int FindPreviousResultIndexBeforeActiveCell(
        Workbook workbook,
        IReadOnlyList<FindResult> results,
        CellAddress? activeCell,
        FindSearchOrder searchOrder)
    {
        if (activeCell is null || results.Count == 0)
            return -1;

        var activeSheetIndex = SheetIndex(workbook, activeCell.Value.Sheet);
        if (activeSheetIndex < 0)
            return -1;

        var previousIndex = -1;
        for (var i = 0; i < results.Count; i++)
        {
            if (CompareAddress(workbook, results[i].Address, activeCell.Value, activeSheetIndex, searchOrder) > 0)
                break;

            previousIndex = i;
        }

        return previousIndex;
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
        var namedRange = workbook.NamedRanges
            .Where(pair => pair.Value.Contains(address))
            .OrderBy(pair => pair.Value.CellCount)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return string.IsNullOrEmpty(namedRange.Key) ? "" : namedRange.Key;
    }

    private static int CompareAddress(
        Workbook workbook,
        CellAddress left,
        CellAddress right,
        int rightSheetIndex,
        FindSearchOrder searchOrder)
    {
        var leftSheetIndex = SheetIndex(workbook, left.Sheet);
        if (leftSheetIndex != rightSheetIndex)
            return leftSheetIndex.CompareTo(rightSheetIndex);

        if (searchOrder == FindSearchOrder.ByColumns)
        {
            var colComparison = left.Col.CompareTo(right.Col);
            return colComparison != 0 ? colComparison : left.Row.CompareTo(right.Row);
        }

        var rowComparison = left.Row.CompareTo(right.Row);
        return rowComparison != 0 ? rowComparison : left.Col.CompareTo(right.Col);
    }

    private static int SheetIndex(Workbook workbook, SheetId sheetId)
    {
        for (var i = 0; i < workbook.Sheets.Count; i++)
        {
            if (workbook.Sheets[i].Id.Equals(sheetId))
                return i;
        }

        return -1;
    }

    public static bool ReplaceSingleMatch(
        Workbook workbook,
        ICommandBus commandBus,
        FindResult match,
        string searchText,
        string replaceText,
        bool matchCase,
        bool matchEntireCell,
        StyleDiff? replacementFormat = null)
        => TryReplaceSingleMatch(
            workbook,
            commandBus,
            match,
            searchText,
            replaceText,
            matchCase,
            matchEntireCell,
            replacementFormat).Replaced;

    public static ReplaceSingleMatchResult TryReplaceSingleMatch(
        Workbook workbook,
        ICommandBus commandBus,
        FindResult match,
        string searchText,
        string replaceText,
        bool matchCase,
        bool matchEntireCell,
        StyleDiff? replacementFormat = null)
    {
        if (string.IsNullOrEmpty(searchText))
            return new ReplaceSingleMatchResult(false, null);

        var sheet = workbook.GetSheet(match.Address.Sheet);
        var cell = sheet?.GetCell(match.Address);
        if (cell is null || cell.HasFormula)
            return new ReplaceSingleMatchResult(false, null);

        var currentText = GetDisplayText(cell.Value);
        if (currentText is null)
            return new ReplaceSingleMatchResult(false, null);

        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var isMatch = matchEntireCell
            ? currentText.Equals(searchText, comparison)
            : currentText.Contains(searchText, comparison);

        if (!isMatch)
            return new ReplaceSingleMatchResult(false, null);

        var newText = matchEntireCell
            ? replaceText
            : currentText.Replace(searchText, replaceText, comparison);

        ScalarValue newValue = double.TryParse(newText, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
            ? new NumberValue(d)
            : new TextValue(newText);

        IWorkbookCommand command = new EditCellsCommand(match.Address.Sheet, [(match.Address, Cell.FromValue(newValue))]);
        if (replacementFormat is not null)
        {
            command = new CompositeWorkbookCommand(
                "Replace",
                [
                    command,
                    new ApplyStyleCommand(
                        match.Address.Sheet,
                        new GridRange(match.Address, match.Address),
                        replacementFormat)
                ]);
        }

        var outcome = commandBus.Execute(workbook.Id, command);
        return outcome.Success
            ? new ReplaceSingleMatchResult(true, null)
            : new ReplaceSingleMatchResult(false, outcome);
    }

    private static string? GetDisplayText(ScalarValue value) => value switch
    {
        BlankValue => null,
        NumberValue n => n.Value.ToString(CultureInfo.InvariantCulture),
        TextValue t => t.Value,
        BoolValue b => b.Value ? "TRUE" : "FALSE",
        DateTimeValue dt => dt.ToDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ErrorValue err => err.Code,
        _ => null
    };
}

internal sealed record FindResultRow(
    string Book,
    string Sheet,
    string Name,
    CellAddress Address,
    string Cell,
    string Value,
    string Formula);

internal sealed record ReplaceSingleMatchResult(bool Replaced, CommandOutcome? Failure);
