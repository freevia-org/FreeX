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

        foreach (var sheet in SheetsForScope(workbook, options))
        {
            var sheetResults = new List<FindResult>();

            foreach (var (addr, text) in EnumerateSearchTexts(sheet, options.LookIn))
            {
                bool isMatch = matchEntireCell
                    ? text.Equals(searchText, comparison)
                    : text.Contains(searchText, comparison);

                if (isMatch && MatchesRequiredFormat(workbook, sheet, addr, options.RequiredFormat))
                    sheetResults.Add(new FindResult(addr, text));
            }

            sheetResults.Sort((a, b) =>
            {
                if (options.SearchOrder == FindSearchOrder.ByColumns)
                {
                    var colCmp = a.Address.Col.CompareTo(b.Address.Col);
                    return colCmp != 0 ? colCmp : a.Address.Row.CompareTo(b.Address.Row);
                }

                var rowCmp = a.Address.Row.CompareTo(b.Address.Row);
                return rowCmp != 0 ? rowCmp : a.Address.Col.CompareTo(b.Address.Col);
            });

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
        bool matchEntireCell = false)
        => ReplaceAll(
            workbook,
            commandBus,
            searchText,
            replaceText,
            new FindOptions(LookIn: FindLookIn.Values),
            matchCase,
            matchEntireCell);

    public static int ReplaceAll(
        Workbook workbook,
        ICommandBus commandBus,
        string searchText,
        string replaceText,
        FindOptions options,
        bool matchCase = false,
        bool matchEntireCell = false)
    {
        if (options.LookIn is not FindLookIn.Values)
            return 0;

        var matches = Find(workbook, searchText, options, matchCase, matchEntireCell);
        if (matches.Count == 0)
            return 0;

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

        foreach (var (sheetId, edits) in editsBySheet)
        {
            var command = new EditCellsCommand(sheetId, edits);
            commandBus.Execute(workbook.Id, command);
        }

        return editsBySheet.Values.Sum(list => list.Count);
    }

    private static IEnumerable<Sheet> SheetsForScope(Workbook workbook, FindOptions options)
    {
        if (options.Within == FindWithin.Sheet && options.CurrentSheetId is { } sheetId)
        {
            var sheet = workbook.GetSheet(sheetId);
            if (sheet is not null)
                yield return sheet;
            yield break;
        }

        foreach (var sheet in workbook.Sheets)
            yield return sheet;
    }

    private static IEnumerable<(CellAddress Address, string Text)> EnumerateSearchTexts(Sheet sheet, FindLookIn lookIn)
    {
        if (lookIn == FindLookIn.Notes)
        {
            foreach (var (address, text) in sheet.Comments)
                yield return (address, text);
            yield break;
        }

        if (lookIn == FindLookIn.Comments)
        {
            foreach (var (address, comment) in sheet.ThreadedComments)
                yield return (address, comment.Text);
            yield break;
        }

        foreach (var (addr, cell) in sheet.EnumerateCells())
        {
            string? text = lookIn == FindLookIn.Formulas && cell.HasFormula
                ? cell.FormulaText
                : GetDisplayText(cell.Value);

            if (text is not null)
                yield return (addr, text);
        }
    }

    private static bool MatchesRequiredFormat(Workbook workbook, Sheet sheet, CellAddress address, StyleDiff? requiredFormat)
    {
        if (requiredFormat is null)
            return true;

        var styleId = sheet.GetCell(address)?.StyleId
            ?? sheet.GetStyleOnly(address.Row, address.Col)
            ?? StyleId.Default;
        var style = workbook.GetStyle(styleId);

        return Matches(requiredFormat.Bold, style.Bold)
            && Matches(requiredFormat.Italic, style.Italic)
            && Matches(requiredFormat.Underline, style.Underline)
            && Matches(requiredFormat.Strikethrough, style.Strikethrough)
            && Matches(requiredFormat.Superscript, style.Superscript)
            && Matches(requiredFormat.Subscript, style.Subscript)
            && Matches(requiredFormat.FontName, style.FontName)
            && Matches(requiredFormat.FontSize, style.FontSize)
            && Matches(requiredFormat.FontColor, style.FontColor)
            && Matches(requiredFormat.FillColor, style.FillColor)
            && Matches(requiredFormat.FillPatternStyle, style.FillPatternStyle)
            && Matches(requiredFormat.FillPatternColor, style.FillPatternColor)
            && Matches(requiredFormat.HAlign, style.HorizontalAlignment)
            && Matches(requiredFormat.VAlign, style.VerticalAlignment)
            && Matches(requiredFormat.WrapText, style.WrapText)
            && Matches(requiredFormat.ShrinkToFit, style.ShrinkToFit)
            && Matches(requiredFormat.NumberFormat, style.NumberFormat)
            && Matches(requiredFormat.DoubleUnderline, style.DoubleUnderline)
            && Matches(requiredFormat.IndentLevel, style.IndentLevel)
            && Matches(requiredFormat.TextRotation, style.TextRotation)
            && Matches(requiredFormat.BorderTop, style.BorderTop)
            && Matches(requiredFormat.BorderRight, style.BorderRight)
            && Matches(requiredFormat.BorderBottom, style.BorderBottom)
            && Matches(requiredFormat.BorderLeft, style.BorderLeft)
            && Matches(requiredFormat.Locked, style.Locked);
    }

    private static bool Matches<T>(T? expected, T actual)
        where T : struct
        => expected is null || EqualityComparer<T>.Default.Equals(expected.Value, actual);

    private static bool Matches(string? expected, string actual) =>
        expected is null || string.Equals(expected, actual, StringComparison.Ordinal);

    private static bool Matches(CellColor? expected, CellColor? actual) =>
        expected is null || expected.Equals(actual);

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
