using System.Globalization;
using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>Represents a cell that matched a search.</summary>
public sealed record FindResult(CellAddress Address, string MatchedText);

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
    {
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var results = new List<FindResult>();

        foreach (var sheet in workbook.Sheets)
        {
            var sheetResults = new List<FindResult>();

            foreach (var (addr, cell) in sheet.EnumerateCells())
            {
                string? text;

                if (searchFormulas && cell.HasFormula)
                {
                    text = cell.FormulaText;
                    if (text is null) continue;
                }
                else
                {
                    text = GetDisplayText(cell.Value);
                    if (text is null)
                        continue;
                }

                bool isMatch = matchEntireCell
                    ? text.Equals(searchText, comparison)
                    : text.Contains(searchText, comparison);

                if (isMatch)
                    sheetResults.Add(new FindResult(addr, text));
            }

            sheetResults.Sort((a, b) =>
            {
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
    {
        var matches = Find(workbook, searchText, matchCase, matchEntireCell, searchFormulas: false);
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

        return matches.Count;
    }

    private static string? GetDisplayText(ScalarValue value) => value switch
    {
        BlankValue => null,
        NumberValue n => n.Value.ToString(CultureInfo.InvariantCulture),
        TextValue t => t.Value,
        BoolValue b => b.Value ? "TRUE" : "FALSE",
        DateTimeValue dt => dt.ToDateTime().ToString(),
        ErrorValue err => err.Code,
        _ => null
    };
}
