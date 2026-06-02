using FreeX.Core.Model;
using System.Globalization;

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
        var seen = uniqueRecordsOnly ? new UniqueRowSet(sheet, listRange) : null;

        for (var row = listRange.Start.Row + 1; row <= listRange.End.Row; row++)
        {
            if (!MatchesAnyCriteriaRow(sheet, row, criteriaRows))
                continue;

            if (seen is null || seen.Add(row))
                result.Add(row);
        }

        return result;
    }

    public static List<uint> UniqueRows(Sheet sheet, GridRange listRange, IReadOnlyList<uint> rows)
    {
        var result = new List<uint>(rows.Count);
        var seen = new UniqueRowSet(sheet, listRange);
        foreach (var row in rows)
        {
            if (seen.Add(row))
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

    private sealed class UniqueRowSet
    {
        private readonly HashSet<UniqueRowKey> _seen;

        public UniqueRowSet(Sheet sheet, GridRange listRange)
        {
            _seen = new HashSet<UniqueRowKey>(
                GetRowResultCapacity(listRange),
                new RowKeyComparer(sheet, listRange));
        }

        public bool Add(uint row)
        {
            return _seen.Add(new UniqueRowKey(row));
        }
    }

    private readonly record struct UniqueRowKey(uint Row);

    private sealed class RowKeyComparer(Sheet sheet, GridRange listRange) : IEqualityComparer<UniqueRowKey>
    {
        private const int FnvOffsetBasis = unchecked((int)2166136261);
        private const int FnvPrime = 16777619;

        public bool Equals(UniqueRowKey left, UniqueRowKey right)
        {
            if (left.Row == right.Row)
                return true;

            for (var col = listRange.Start.Col; col <= listRange.End.Col; col++)
            {
                if (!FormattedTextEquals(sheet.GetValue(left.Row, col), sheet.GetValue(right.Row, col)))
                    return false;
            }

            return true;
        }

        public int GetHashCode(UniqueRowKey key)
        {
            var hash = FnvOffsetBasis;
            for (var col = listRange.Start.Col; col <= listRange.End.Col; col++)
            {
                if (col != listRange.Start.Col)
                    AddChar(ref hash, '\u001f');

                AddValueTextHash(ref hash, sheet.GetValue(key.Row, col));
            }

            return hash;
        }

        private static bool FormattedTextEquals(ScalarValue left, ScalarValue right)
        {
            Span<char> leftBuffer = stackalloc char[32];
            Span<char> rightBuffer = stackalloc char[32];
            var leftText = GetFormattedText(left, leftBuffer, out _);
            var rightText = GetFormattedText(right, rightBuffer, out _);
            return leftText.SequenceEqual(rightText);
        }

        private static void AddValueTextHash(ref int hash, ScalarValue value)
        {
            Span<char> buffer = stackalloc char[32];
            var text = GetFormattedText(value, buffer, out _);
            foreach (var ch in text)
                AddChar(ref hash, ch);
        }

        private static ReadOnlySpan<char> GetFormattedText(
            ScalarValue value,
            Span<char> buffer,
            out string? fallback)
        {
            fallback = null;
            switch (value)
            {
                case TextValue text:
                    return text.Value.AsSpan();
                case NumberValue number:
                    if (number.Value.TryFormat(buffer, out var numberChars, provider: CultureInfo.InvariantCulture))
                        return buffer[..numberChars];

                    fallback = number.Value.ToString(CultureInfo.InvariantCulture);
                    return fallback.AsSpan();
                case BoolValue boolean:
                    return boolean.Value ? "TRUE".AsSpan() : "FALSE".AsSpan();
                case DateTimeValue dateTime:
                    var date = dateTime.ToDateTime();
                    if (date.TryFormat(buffer, out var dateChars, "yyyy-MM-dd", CultureInfo.InvariantCulture))
                        return buffer[..dateChars];

                    fallback = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    return fallback.AsSpan();
                case ErrorValue error:
                    return error.Code.AsSpan();
                default:
                    return [];
            }
        }

        private static void AddChar(ref int hash, char ch)
        {
            unchecked
            {
                hash = (hash ^ ch) * FnvPrime;
            }
        }
    }
}
