using System.Globalization;

namespace FreeX.Core.Commands;

public static partial class FlashFillService
{
    private static Func<string, string?>? TryDateComponentExtraction(IReadOnlyList<(string Source, string Expected)> examples)
    {
        int? partIndex = null;

        foreach (var (source, expected) in examples)
        {
            if (!TrySplitDateLikeComponents(source, out var components))
                return null;

            var matches = new List<int>(1);
            for (var i = 0; i < components.Length; i++)
            {
                if (components[i] == expected)
                    matches.Add(i);
            }

            if (matches.Count != 1)
                return null;

            if (partIndex is null)
                partIndex = matches[0];
            else if (partIndex.Value != matches[0])
                return null;
        }

        if (partIndex is null)
            return null;

        var idx = partIndex.Value;
        return source => TrySplitDateLikeComponents(source, out var components)
            ? components[idx]
            : null;
    }

    private static bool TrySplitDateLikeComponents(string source, out string[] components)
    {
        foreach (var separator in DateComponentSeparators)
        {
            var parts = source.Split(separator, StringSplitOptions.TrimEntries);
            if (parts.Length != 3 ||
                parts.Any(part => part.Length == 0 || part.Any(c => !char.IsDigit(c))))
            {
                continue;
            }

            var yearIndex = Array.FindIndex(parts, part => part.Length == 4);
            if (yearIndex < 0 ||
                Array.FindLastIndex(parts, part => part.Length == 4) != yearIndex ||
                (yearIndex != 0 && yearIndex != 2))
            {
                continue;
            }

            if (!int.TryParse(parts[yearIndex], NumberStyles.None, CultureInfo.InvariantCulture, out var year) ||
                year < 1000)
            {
                continue;
            }

            var dateish = true;
            for (var i = 0; i < parts.Length; i++)
            {
                if (i == yearIndex)
                    continue;

                if (parts[i].Length is < 1 or > 2 ||
                    !int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var value) ||
                    value is < 1 or > 31)
                {
                    dateish = false;
                    break;
                }
            }

            if (!dateish)
                continue;

            components = parts;
            return true;
        }

        components = [];
        return false;
    }

    private static Func<string, string?>? TryEmbeddedDateExtraction(IReadOnlyList<(string Source, string Expected)> examples)
    {
        DateOutputPattern? outputPattern = null;
        var changedAny = false;

        foreach (var (source, expected) in examples)
        {
            if (!TryFindEmbeddedDateLikeValue(source, out var sourceDate, out _) ||
                !TryParseDateLikeValue(expected, out var expectedDate, out var currentPattern) ||
                sourceDate != expectedDate)
            {
                return null;
            }

            if (outputPattern is null)
                outputPattern = currentPattern;
            else if (outputPattern.Value != currentPattern)
                return null;

            changedAny |= !string.Equals(source, expected, StringComparison.Ordinal);
        }

        if (outputPattern is null || !changedAny)
            return null;

        var pattern = outputPattern.Value;
        return source => TryFindEmbeddedDateLikeValue(source, out var date, out _)
            ? FormatDateParts(date, pattern)
            : null;
    }

    private static Func<string, string?>? TryDateNormalization(IReadOnlyList<(string Source, string Expected)> examples)
    {
        DateOutputPattern? outputPattern = null;
        var changedAny = false;

        foreach (var (source, expected) in examples)
        {
            if (!TryParseDateLikeValue(source, out var sourceDate, out _) ||
                !TryParseDateLikeValue(expected, out var expectedDate, out var currentPattern) ||
                sourceDate != expectedDate)
            {
                return null;
            }

            if (outputPattern is null)
                outputPattern = currentPattern;
            else if (outputPattern.Value != currentPattern)
                return null;

            changedAny |= !string.Equals(source, expected, StringComparison.Ordinal);
        }

        if (outputPattern is null || !changedAny)
            return null;

        var pattern = outputPattern.Value;
        return source => TryParseDateLikeValue(source, out var date, out _)
            ? FormatDateParts(date, pattern)
            : null;
    }

    private static bool TryParseDateLikeValue(
        string source,
        out DateParts date,
        out DateOutputPattern pattern)
    {
        date = default;
        pattern = default;

        if (!TrySplitDateLikeText(source, out var parts, out var separator))
            return false;

        var yearIndex = Array.FindIndex(parts, part => part.Length == 4);
        if (yearIndex < 0 ||
            Array.FindLastIndex(parts, part => part.Length == 4) != yearIndex ||
            (yearIndex != 0 && yearIndex != 2))
        {
            return false;
        }

        var order = yearIndex == 0
            ? new[] { DatePartKind.Year, DatePartKind.Month, DatePartKind.Day }
            : new[] { DatePartKind.Month, DatePartKind.Day, DatePartKind.Year };

        if (!TryCreateDateParts(parts, order, out date))
            return false;

        pattern = new DateOutputPattern(
            order[0],
            order[1],
            order[2],
            separator,
            parts[Array.IndexOf(order, DatePartKind.Year)].Length,
            parts[Array.IndexOf(order, DatePartKind.Month)].Length,
            parts[Array.IndexOf(order, DatePartKind.Day)].Length);
        return true;
    }

    private static bool TrySplitDateLikeText(string source, out string[] parts, out char separator)
    {
        foreach (var candidate in DateComponentSeparators)
        {
            var split = source.Split(candidate, StringSplitOptions.TrimEntries);
            if (split.Length == 3 &&
                split.All(part => part.Length > 0 && part.All(char.IsDigit)))
            {
                parts = split;
                separator = candidate;
                return true;
            }
        }

        parts = [];
        separator = default;
        return false;
    }

    private static bool TryFindEmbeddedDateLikeValue(
        string source,
        out DateParts date,
        out DateOutputPattern pattern)
    {
        date = default;
        pattern = default;

        var found = false;
        for (var start = 0; start < source.Length; start++)
        {
            if (!char.IsDigit(source[start]) ||
                (start > 0 && char.IsDigit(source[start - 1])))
            {
                continue;
            }

            foreach (var separator in DateComponentSeparators)
            {
                if (!TryReadDateTokenAt(source, start, separator, out var endExclusive) ||
                    !HasDateTokenBoundary(source, start, endExclusive, separator))
                {
                    continue;
                }

                var token = source[start..endExclusive];
                if (!TryParseDateLikeValue(token, out var currentDate, out var currentPattern))
                    continue;

                if (found)
                    return false;

                date = currentDate;
                pattern = currentPattern;
                found = true;
            }
        }

        return found;
    }

    private static bool TryReadDateTokenAt(
        string source,
        int start,
        char separator,
        out int endExclusive)
    {
        endExclusive = start;
        var index = start;

        for (var group = 0; group < 3; group++)
        {
            var groupStart = index;
            while (index < source.Length && char.IsDigit(source[index]))
                index++;

            if (index == groupStart)
                return false;

            if (group < 2)
            {
                if (index >= source.Length || source[index] != separator)
                    return false;

                index++;
            }
        }

        endExclusive = index;
        return true;
    }

    private static bool HasDateTokenBoundary(
        string source,
        int start,
        int endExclusive,
        char separator)
    {
        if (start > 0 && char.IsLetterOrDigit(source[start - 1]))
            return false;

        if (endExclusive >= source.Length)
            return true;

        var next = source[endExclusive];
        if (char.IsLetterOrDigit(next))
            return false;

        return next != separator ||
            endExclusive == source.Length - 1 ||
            !char.IsDigit(source[endExclusive + 1]);
    }

    private static bool TryCreateDateParts(string[] parts, DatePartKind[] order, out DateParts date)
    {
        date = default;
        if (parts.Length != 3 || order.Length != 3)
            return false;

        if (!TryGetDatePartValue(parts, order, DatePartKind.Year, out var year) ||
            !TryGetDatePartValue(parts, order, DatePartKind.Month, out var month) ||
            !TryGetDatePartValue(parts, order, DatePartKind.Day, out var day) ||
            year is < 1000 or > 9999 ||
            month is < 1 or > 12 ||
            day < 1 ||
            day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }

        date = new DateParts(year, month, day);
        return true;
    }

    private static bool TryGetDatePartValue(
        string[] parts,
        DatePartKind[] order,
        DatePartKind kind,
        out int value)
    {
        value = 0;
        var index = Array.IndexOf(order, kind);
        return index >= 0 &&
            int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static string FormatDateParts(DateParts date, DateOutputPattern pattern) =>
        string.Join(
            pattern.Separator,
            new[]
            {
                FormatDatePart(date, pattern.First, pattern),
                FormatDatePart(date, pattern.Second, pattern),
                FormatDatePart(date, pattern.Third, pattern)
            });

    private static string FormatDatePart(DateParts date, DatePartKind kind, DateOutputPattern pattern)
    {
        var value = kind switch
        {
            DatePartKind.Year => date.Year,
            DatePartKind.Month => date.Month,
            _ => date.Day
        };

        var width = kind switch
        {
            DatePartKind.Year => pattern.YearWidth,
            DatePartKind.Month => pattern.MonthWidth,
            _ => pattern.DayWidth
        };

        return value.ToString("D" + width.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }
}
