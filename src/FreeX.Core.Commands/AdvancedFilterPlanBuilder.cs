using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static class AdvancedFilterPlanBuilder
{
    public static Dictionary<string, uint> BuildHeaderMap(Sheet sheet, GridRange range)
    {
        var headers = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        for (var col = range.Start.Col; col <= range.End.Col; col++)
        {
            var text = FilterValueFormatter.ToText(sheet.GetValue(range.Start.Row, col));
            if (text.Length > 0 && !headers.ContainsKey(text))
                headers[text] = col;
        }

        return headers;
    }

    public static (List<List<(uint Col, IFilterCriterion Criterion)>> Rows, string? Error) BuildCriteriaRows(
        Sheet sheet,
        GridRange criteriaRange,
        Dictionary<string, uint> headers)
    {
        var result = new List<List<(uint Col, IFilterCriterion Criterion)>>();
        if (criteriaRange.Start.Row >= criteriaRange.End.Row)
            return (result, null);

        var criteriaColumns = new List<(uint CriteriaCol, uint ListCol)>();
        for (var col = criteriaRange.Start.Col; col <= criteriaRange.End.Col; col++)
        {
            var headerText = FilterValueFormatter.ToText(sheet.GetValue(criteriaRange.Start.Row, col));
            if (string.IsNullOrWhiteSpace(headerText))
                continue;
            if (!headers.TryGetValue(headerText, out var listCol))
                return ([], $"Criteria header '{headerText}' was not found in the list range.");

            criteriaColumns.Add((col, listCol));
        }

        for (var row = criteriaRange.Start.Row + 1; row <= criteriaRange.End.Row; row++)
        {
            List<(uint Col, IFilterCriterion Criterion)>? criteriaRow = null;
            foreach (var (criteriaCol, listCol) in criteriaColumns)
            {
                var criteriaText = FilterValueFormatter.ToText(sheet.GetValue(row, criteriaCol));
                if (criteriaText.Length == 0)
                    continue;

                criteriaRow ??= new List<(uint Col, IFilterCriterion Criterion)>();
                criteriaRow.Add((listCol, CreateCriterion(criteriaText)));
            }

            if (criteriaRow is not null)
                result.Add(criteriaRow);
        }

        return (result, null);
    }

    public static List<uint> MatchingRows(
        Sheet sheet,
        GridRange listRange,
        IReadOnlyList<List<(uint Col, IFilterCriterion Criterion)>> criteriaRows,
        bool uniqueRecordsOnly = false)
    {
        var result = new List<uint>(GetRowResultCapacity(listRange));
        var seen = uniqueRecordsOnly ? new HashSet<string>(StringComparer.Ordinal) : null;
        var keyParts = uniqueRecordsOnly ? new string[(int)listRange.ColCount] : null;

        for (var row = listRange.Start.Row + 1; row <= listRange.End.Row; row++)
        {
            if (!MatchesAnyCriteriaRow(sheet, row, criteriaRows))
                continue;

            if (seen is null || AddUniqueRowKey(sheet, listRange, row, seen, keyParts!))
                result.Add(row);
        }

        return result;
    }

    public static List<uint> UniqueRows(Sheet sheet, GridRange listRange, IReadOnlyList<uint> rows)
    {
        var result = new List<uint>(rows.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var keyParts = new string[(int)listRange.ColCount];
        foreach (var row in rows)
        {
            if (AddUniqueRowKey(sheet, listRange, row, seen, keyParts))
                result.Add(row);
        }

        return result;
    }

    private static int GetRowResultCapacity(GridRange listRange)
    {
        var rowCount = listRange.RowCount > 0 ? listRange.RowCount - 1 : 0;
        return rowCount > 4096 ? 4096 : (int)rowCount;
    }

    private static bool MatchesAnyCriteriaRow(
        Sheet sheet,
        uint row,
        IReadOnlyList<List<(uint Col, IFilterCriterion Criterion)>> criteriaRows)
    {
        foreach (var criteriaRow in criteriaRows)
        {
            var matches = true;
            foreach (var (col, criterion) in criteriaRow)
            {
                if (!criterion.Matches(sheet.GetValue(row, col)))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
                return true;
        }

        return false;
    }

    private static IFilterCriterion CreateCriterion(string criteriaText)
    {
        if (FilterInputParser.TryParseCriterion(criteriaText, out var parsed, out _))
            return parsed!;
        if (criteriaText.StartsWith('='))
            return new TextEqualsFilterCriterion(criteriaText[1..]);
        return new TextEqualsFilterCriterion(criteriaText);
    }

    private static bool AddUniqueRowKey(
        Sheet sheet,
        GridRange listRange,
        uint row,
        HashSet<string> seen,
        string[] keyParts)
    {
        var index = 0;
        for (var col = listRange.Start.Col; col <= listRange.End.Col; col++)
        {
            keyParts[index] = FilterValueFormatter.ToText(sheet.GetValue(row, col));
            index++;
        }

        return seen.Add(string.Join('\u001f', keyParts));
    }
}
