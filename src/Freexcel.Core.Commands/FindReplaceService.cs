using System.Globalization;
using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>Represents a cell that matched a search.</summary>
public sealed record FindResult(CellAddress Address, string MatchedText);

public enum FindWithin
{
    Workbook,
    Sheet
}

public enum FindSearchOrder
{
    ByRows,
    ByColumns
}

public enum FindLookIn
{
    Formulas,
    Values,
    Notes,
    Comments
}

public sealed record FindOptions(
    FindWithin Within = FindWithin.Workbook,
    SheetId? CurrentSheetId = null,
    FindSearchOrder SearchOrder = FindSearchOrder.ByRows,
    FindLookIn LookIn = FindLookIn.Values,
    StyleDiff? RequiredFormat = null);

public sealed record ReplaceAllResult(int ReplacedCount, CommandOutcome? Failure);

/// <summary>Search and replace service. Replace goes through ICommandBus for undo support.</summary>
public static class FindReplaceService
{
    /// <summary>
    /// Find all cells in the workbook whose display text (or formula text) contains searchText.
    /// Results are ordered: sheet order, then row-major within each sheet.
    /// </summary>
    public static IReadOnlyList<FindResult> Find(
        Workbook workbook,
        string searchText,
        bool matchCase = false,
        bool matchEntireCell = false,
        bool searchFormulas = false)
        => Find(
            workbook,
            searchText,
            new FindOptions(LookIn: searchFormulas ? FindLookIn.Formulas : FindLookIn.Values),
            matchCase,
            matchEntireCell);

    public static IReadOnlyList<FindResult> Find(
        Workbook workbook,
        string searchText,
        FindOptions options,
        bool matchCase = false,
        bool matchEntireCell = false)
    {
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var results = new List<FindResult>();

        foreach (var sheet in FindReplaceSearchPlanner.SheetsForScope(workbook, options))
        {
            var sheetResults = new List<FindResult>();

            foreach (var (addr, text) in FindReplaceSearchPlanner.EnumerateSearchTexts(sheet, options.LookIn))
            {
                bool isMatch = matchEntireCell
                    ? text.Equals(searchText, comparison)
                    : text.Contains(searchText, comparison);

                if (isMatch && FindReplaceSearchPlanner.MatchesRequiredFormat(workbook, sheet, addr, options.RequiredFormat))
                    sheetResults.Add(new FindResult(addr, text));
            }

            FindReplaceSearchPlanner.SortResults(sheetResults, options.SearchOrder);
            results.AddRange(sheetResults);
        }

        return results;
    }

    /// <summary>
    /// Replace all matches in cell values (not formulas). Returns the count of replacements made.
    /// Each replaced cell becomes an EditCellsCommand in a single transaction on the command bus.
    /// </summary>
    public static int ReplaceAll(
        Workbook workbook,
        ICommandBus commandBus,
        string searchText,
        string replaceText,
        bool matchCase = false,
        bool matchEntireCell = false,
        StyleDiff? replacementFormat = null)
        => TryReplaceAll(
            workbook,
            commandBus,
            searchText,
            replaceText,
            new FindOptions(LookIn: FindLookIn.Values),
            matchCase,
            matchEntireCell,
            replacementFormat).ReplacedCount;

    public static int ReplaceAll(
        Workbook workbook,
        ICommandBus commandBus,
        string searchText,
        string replaceText,
        FindOptions options,
        bool matchCase = false,
        bool matchEntireCell = false,
        StyleDiff? replacementFormat = null)
        => TryReplaceAll(
            workbook,
            commandBus,
            searchText,
            replaceText,
            options,
            matchCase,
            matchEntireCell,
            replacementFormat).ReplacedCount;

    public static ReplaceAllResult TryReplaceAll(
        Workbook workbook,
        ICommandBus commandBus,
        string searchText,
        string replaceText,
        bool matchCase = false,
        bool matchEntireCell = false,
        StyleDiff? replacementFormat = null)
        => TryReplaceAll(
            workbook,
            commandBus,
            searchText,
            replaceText,
            new FindOptions(LookIn: FindLookIn.Values),
            matchCase,
            matchEntireCell,
            replacementFormat);

    public static ReplaceAllResult TryReplaceAll(
        Workbook workbook,
        ICommandBus commandBus,
        string searchText,
        string replaceText,
        FindOptions options,
        bool matchCase = false,
        bool matchEntireCell = false,
        StyleDiff? replacementFormat = null)
    {
        if (options.LookIn is not FindLookIn.Values)
            return new ReplaceAllResult(0, null);

        var matches = Find(workbook, searchText, options, matchCase, matchEntireCell);
        if (matches.Count == 0)
            return new ReplaceAllResult(0, null);

        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        // Group edits by sheet so we can issue one command per sheet
        var editsBySheet = new Dictionary<SheetId, List<(CellAddress Address, Cell NewCell)>>();

        foreach (var result in matches)
        {
            var sheet = workbook.GetSheet(result.Address.Sheet);
            if (sheet is null) continue;

            var cell = sheet.GetCell(result.Address);
            if (cell is null || cell.HasFormula) continue;

            var newText = matchEntireCell
                ? replaceText
                : result.MatchedText.Replace(searchText, replaceText, comparison);

            ScalarValue newValue = double.TryParse(newText, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
                ? new NumberValue(d)
                : new TextValue(newText);

            var newCell = Cell.FromValue(newValue);

            if (!editsBySheet.TryGetValue(result.Address.Sheet, out var list))
            {
                list = [];
                editsBySheet[result.Address.Sheet] = list;
            }
            list.Add((result.Address, newCell));
        }

        var replacedCount = 0;
        foreach (var (sheetId, edits) in editsBySheet)
        {
            var commands = new List<IWorkbookCommand> { new EditCellsCommand(sheetId, edits) };
            if (replacementFormat is not null)
            {
                commands.AddRange(edits.Select(edit => new ApplyStyleCommand(
                    sheetId,
                    new GridRange(edit.Address, edit.Address),
                    replacementFormat)));
            }

            var command = commands.Count == 1
                ? commands[0]
                : new CompositeWorkbookCommand("Replace All", commands);
            var outcome = commandBus.Execute(workbook.Id, command);
            if (!outcome.Success)
                return new ReplaceAllResult(replacedCount, outcome);

            replacedCount += edits.Count;
        }

        return new ReplaceAllResult(replacedCount, null);
    }

}
