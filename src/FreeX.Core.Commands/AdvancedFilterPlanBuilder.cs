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
        for (var row = criteriaRange.Start.Row + 1; row <= criteriaRange.End.Row; row++)
        {
            var criteriaRow = new List<(uint Col, IFilterCriterion Criterion)>();
            for (var col = criteriaRange.Start.Col; col <= criteriaRange.End.Col; col++)
            {
                var headerText = FilterValueFormatter.ToText(sheet.GetValue(criteriaRange.Start.Row, col));
                if (string.IsNullOrWhiteSpace(headerText))
                    continue;
                if (!headers.TryGetValue(headerText, out var listCol))
                    return ([], $"Criteria header '{headerText}' was not found in the list range.");

                var criteriaText = FilterValueFormatter.ToText(sheet.GetValue(row, col));
                if (criteriaText.Length == 0)
                    continue;

                criteriaRow.Add((listCol, CreateCriterion(criteriaText)));
            }

            if (criteriaRow.Count > 0)
                result.Add(criteriaRow);
        }

        return (result, null);
    }

    public static IEnumerable<uint> MatchingRows(
        Sheet sheet,
        GridRange listRange,
        IReadOnlyList<List<(uint Col, IFilterCriterion Criterion)>> criteriaRows)
    {
        for (var row = listRange.Start.Row + 1; row <= listRange.End.Row; row++)
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
                {
                    yield return row;
                    break;
                }
            }
        }
    }

    public static IEnumerable<uint> UniqueRows(Sheet sheet, GridRange listRange, IReadOnlyList<uint> rows)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var key = string.Join('\u001f', Enumerable
                .Range((int)listRange.Start.Col, (int)listRange.ColCount)
                .Select(col => FilterValueFormatter.ToText(sheet.GetValue(row, (uint)col))));
            if (seen.Add(key))
                yield return row;
        }
    }

    private static IFilterCriterion CreateCriterion(string criteriaText)
    {
        if (FilterInputParser.TryParseCriterion(criteriaText, out var parsed, out _))
            return parsed!;
        if (criteriaText.StartsWith('='))
            return new TextEqualsFilterCriterion(criteriaText[1..]);
        return new TextEqualsFilterCriterion(criteriaText);
    }
}
