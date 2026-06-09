using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Identifies which cell-owned text surface matched a search.</summary>
public enum FindResultTarget
{
    Cell,
    Note,
    ThreadedComment,
    ThreadedCommentReply
}

/// <summary>Represents a cell-owned text surface that matched a search.</summary>
public sealed record FindResult(
    CellAddress Address,
    string MatchedText,
    FindResultTarget Target = FindResultTarget.Cell,
    int? ReplyIndex = null);

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

            foreach (var candidate in FindReplaceSearchPlanner.EnumerateSearchTexts(sheet, options.LookIn))
            {
                bool isMatch = matchEntireCell
                    ? candidate.Text.Equals(searchText, comparison)
                    : candidate.Text.Contains(searchText, comparison);

                if (isMatch && FindReplaceSearchPlanner.MatchesRequiredFormat(workbook, sheet, candidate.Address, options.RequiredFormat))
                {
                    sheetResults.Add(new FindResult(
                        candidate.Address,
                        candidate.Text,
                        candidate.Target,
                        candidate.ReplyIndex));
                }
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
        var matches = Find(workbook, searchText, options, matchCase, matchEntireCell);
        if (matches.Count == 0)
            return new ReplaceAllResult(0, null);

        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var editsBySheet = new Dictionary<SheetId, List<(CellAddress Address, Cell NewCell)>>();
        var commands = new List<IWorkbookCommand>();

        foreach (var result in matches)
        {
            var sheet = workbook.GetSheet(result.Address.Sheet);
            if (sheet is null) continue;

            if (TryCreateReplacementCell(
                    sheet,
                    result.Address,
                    searchText,
                    replaceText,
                    comparison,
                    matchEntireCell,
                    options.LookIn,
                    out var newCell))
            {
                if (!editsBySheet.TryGetValue(result.Address.Sheet, out var list))
                {
                    list = [];
                    editsBySheet[result.Address.Sheet] = list;
                }
                list.Add((result.Address, newCell));
                continue;
            }

            if (TryCreateReplacementCommentCommand(
                    sheet,
                    result,
                    searchText,
                    replaceText,
                    comparison,
                    matchEntireCell,
                    options.LookIn,
                    out var commentCommand))
                commands.Add(commentCommand);
        }

        foreach (var (sheetId, edits) in editsBySheet)
        {
            commands.Add(new EditCellsCommand(sheetId, edits));
            if (replacementFormat is not null)
            {
                commands.AddRange(edits.Select(edit => new ApplyStyleCommand(
                    sheetId,
                    new GridRange(edit.Address, edit.Address),
                    replacementFormat)));
            }
        }

        var replacedCount = editsBySheet.Values.Sum(static edits => edits.Count)
            + commands.Count(command => command is not EditCellsCommand and not ApplyStyleCommand);
        if (commands.Count == 0)
            return new ReplaceAllResult(0, null);

        var command = commands.Count == 1
            ? commands[0]
            : new CompositeWorkbookCommand("Replace All", commands);
        var outcome = commandBus.Execute(workbook.Id, command);
        if (!outcome.Success)
            return new ReplaceAllResult(0, outcome);

        return new ReplaceAllResult(replacedCount, null);
    }

    public static bool TryCreateReplacementCommand(
        Sheet sheet,
        FindResult match,
        string searchText,
        string replaceText,
        bool matchCase,
        bool matchEntireCell,
        FindLookIn lookIn,
        StyleDiff? replacementFormat,
        out IWorkbookCommand command)
    {
        command = null!;
        if (string.IsNullOrEmpty(searchText))
            return false;

        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (TryCreateReplacementCell(
                sheet,
                match.Address,
                searchText,
                replaceText,
                comparison,
                matchEntireCell,
                lookIn,
                out var newCell))
        {
            var editCommand = new EditCellsCommand(sheet.Id, [(match.Address, newCell)]);
            command = replacementFormat is null
                ? editCommand
                : new CompositeWorkbookCommand(
                    "Replace",
                    [
                        editCommand,
                        new ApplyStyleCommand(
                            sheet.Id,
                            new GridRange(match.Address, match.Address),
                            replacementFormat)
                    ]);
            return true;
        }

        return TryCreateReplacementCommentCommand(
            sheet,
            match,
            searchText,
            replaceText,
            comparison,
            matchEntireCell,
            lookIn,
            out command);
    }

    private static bool TryCreateReplacementCell(
        Sheet sheet,
        CellAddress address,
        string searchText,
        string replaceText,
        StringComparison comparison,
        bool matchEntireCell,
        FindLookIn lookIn,
        out Cell newCell)
    {
        newCell = null!;
        var cell = sheet.GetCell(address);
        if (cell is null)
            return false;

        var currentText = lookIn switch
        {
            FindLookIn.Formulas => cell.FormulaText,
            FindLookIn.Values => cell.HasFormula ? null : GetDisplayText(cell.Value),
            _ => null
        };
        if (currentText is null ||
            !TryCreateReplacementText(currentText, searchText, replaceText, comparison, matchEntireCell, out var newText))
            return false;

        if (lookIn == FindLookIn.Formulas)
        {
            newCell = cell.Clone();
            newCell.FormulaText = newText;
            return true;
        }

        ScalarValue newValue = double.TryParse(newText, NumberStyles.Any, CultureInfo.InvariantCulture, out var number)
            ? new NumberValue(number)
            : new TextValue(newText);

        newCell = cell.Clone();
        newCell.Value = newValue;
        newCell.FormulaText = null;
        return true;
    }

    private static bool TryCreateReplacementCommentCommand(
        Sheet sheet,
        FindResult match,
        string searchText,
        string replaceText,
        StringComparison comparison,
        bool matchEntireCell,
        FindLookIn lookIn,
        out IWorkbookCommand command)
    {
        command = null!;
        var currentText = lookIn switch
        {
            FindLookIn.Notes when
                match.Target == FindResultTarget.Note &&
                sheet.Comments.TryGetValue(match.Address, out var note) => note,
            FindLookIn.Comments when
                match.Target == FindResultTarget.ThreadedComment &&
                sheet.ThreadedComments.TryGetValue(match.Address, out var threadedComment) => threadedComment.Text,
            FindLookIn.Comments when
                match.Target == FindResultTarget.ThreadedCommentReply &&
                match.ReplyIndex is { } replyIndex &&
                sheet.ThreadedComments.TryGetValue(match.Address, out var threadedComment) &&
                replyIndex >= 0 &&
                replyIndex < threadedComment.Replies.Count => threadedComment.Replies[replyIndex].Text,
            _ => null
        };
        if (currentText is null ||
            !TryCreateReplacementText(currentText, searchText, replaceText, comparison, matchEntireCell, out var newText))
            return false;

        command = lookIn switch
        {
            FindLookIn.Notes when match.Target == FindResultTarget.Note =>
                new SetCommentCommand(sheet.Id, match.Address, newText),
            FindLookIn.Comments when match.Target == FindResultTarget.ThreadedComment =>
                new UpdateThreadedCommentTextCommand(sheet.Id, match.Address, newText),
            FindLookIn.Comments when
                match.Target == FindResultTarget.ThreadedCommentReply &&
                match.ReplyIndex is { } replyIndex =>
                new UpdateThreadedCommentReplyCommand(sheet.Id, match.Address, replyIndex, newText),
            _ => null!
        };

        return command is not null;
    }

    private static bool TryCreateReplacementText(
        string currentText,
        string searchText,
        string replaceText,
        StringComparison comparison,
        bool matchEntireCell,
        out string newText)
    {
        newText = "";
        var isMatch = matchEntireCell
            ? currentText.Equals(searchText, comparison)
            : currentText.Contains(searchText, comparison);
        if (!isMatch)
            return false;

        newText = matchEntireCell
            ? replaceText
            : currentText.Replace(searchText, replaceText, comparison);
        return true;
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
