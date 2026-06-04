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
                !TryParseDateOutputValue(expected, out var expectedDate, out var currentPattern) ||
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
                !TryParseDateOutputValue(expected, out var expectedDate, out var currentPattern) ||
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
        if (TryParseDateOutputValue(source, out date, out pattern))
            return true;

        return TryParseMonthNameDateLikeValue(source, out date, out pattern);
    }

    private static bool TryParseDateOutputValue(
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

    private static bool TryParseMonthNameDateLikeValue(
        string source,
        out DateParts date,
        out DateOutputPattern pattern)
    {
        date = default;
        pattern = default;

        var parts = source.Split((char[]?)null, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
            return false;

        if (TryParseEnglishMonthNamePart(parts[0], out var month) &&
            TryParseDayPart(parts[1], allowTrailingComma: true, out var day) &&
            TryParseYearPart(parts[2], out var year) &&
            TryCreateDateParts(year, month, day, out date))
        {
            pattern = CreateMonthNameDatePattern(DatePartKind.Month, DatePartKind.Day, DatePartKind.Year);
            return true;
        }

        if (TryParseDayPart(parts[0], allowTrailingComma: false, out day) &&
            TryParseEnglishMonthNamePart(parts[1], out month) &&
            TryParseYearPart(parts[2], out year) &&
            TryCreateDateParts(year, month, day, out date))
        {
            pattern = CreateMonthNameDatePattern(DatePartKind.Day, DatePartKind.Month, DatePartKind.Year);
            return true;
        }

        if (TryParseYearPart(parts[0], out year) &&
            TryParseEnglishMonthNamePart(parts[1], out month) &&
            TryParseDayPart(parts[2], allowTrailingComma: false, out day) &&
            TryCreateDateParts(year, month, day, out date))
        {
            pattern = CreateMonthNameDatePattern(DatePartKind.Year, DatePartKind.Month, DatePartKind.Day);
            return true;
        }

        return false;
    }

    private static DateOutputPattern CreateMonthNameDatePattern(
        DatePartKind first,
        DatePartKind second,
        DatePartKind third) =>
        new(first, second, third, ' ', 4, 2, 2);

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
            if (TryFindEmbeddedNumericDateLikeValueAt(source, start, out var currentDate, out var currentPattern) ||
                TryFindEmbeddedMonthNameDateLikeValueAt(source, start, out currentDate, out currentPattern))
            {
                if (found)
                    return false;

                date = currentDate;
                pattern = currentPattern;
                found = true;
            }
        }

        return found;
    }

    private static bool TryFindEmbeddedNumericDateLikeValueAt(
        string source,
        int start,
        out DateParts date,
        out DateOutputPattern pattern)
    {
        date = default;
        pattern = default;

        if (!char.IsDigit(source[start]) ||
            (start > 0 && char.IsDigit(source[start - 1])))
        {
            return false;
        }

        foreach (var separator in DateComponentSeparators)
        {
            if (!TryReadDateTokenAt(source, start, separator, out var endExclusive) ||
                !HasDateTokenBoundary(source, start, endExclusive, separator))
            {
                continue;
            }

            var token = source[start..endExclusive];
            if (TryParseDateOutputValue(token, out date, out pattern))
                return true;
        }

        return false;
    }

    private static bool TryFindEmbeddedMonthNameDateLikeValueAt(
        string source,
        int start,
        out DateParts date,
        out DateOutputPattern pattern)
    {
        date = default;
        pattern = default;

        if (!char.IsLetterOrDigit(source[start]) ||
            (start > 0 && char.IsLetterOrDigit(source[start - 1])) ||
            !TryReadMonthNameDateTokenAt(source, start, out var endExclusive) ||
            !HasMonthNameDateTokenBoundary(source, start, endExclusive))
        {
            return false;
        }

        return TryParseMonthNameDateLikeValue(source[start..endExclusive], out date, out pattern);
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

    private static bool TryReadMonthNameDateTokenAt(string source, int start, out int endExclusive) =>
        TryReadMonthDayYearTokenAt(source, start, out endExclusive) ||
        TryReadDayMonthYearTokenAt(source, start, out endExclusive) ||
        TryReadYearMonthDayTokenAt(source, start, out endExclusive);

    private static bool TryReadMonthDayYearTokenAt(string source, int start, out int endExclusive)
    {
        endExclusive = start;
        var index = start;

        if (!TryReadMonthNameToken(source, ref index) ||
            !TryReadRequiredWhitespace(source, ref index) ||
            !TryReadDigitToken(source, ref index, minLength: 1, maxLength: 2, allowTrailingComma: true, allowOrdinalSuffix: true) ||
            !TryReadRequiredWhitespace(source, ref index) ||
            !TryReadDigitToken(source, ref index, minLength: 4, maxLength: 4, allowTrailingComma: false))
        {
            return false;
        }

        endExclusive = index;
        return true;
    }

    private static bool TryReadDayMonthYearTokenAt(string source, int start, out int endExclusive)
    {
        endExclusive = start;
        var index = start;

        if (!TryReadDigitToken(source, ref index, minLength: 1, maxLength: 2, allowTrailingComma: false, allowOrdinalSuffix: true) ||
            !TryReadRequiredWhitespace(source, ref index) ||
            !TryReadMonthNameToken(source, ref index) ||
            !TryReadRequiredWhitespace(source, ref index) ||
            !TryReadDigitToken(source, ref index, minLength: 4, maxLength: 4, allowTrailingComma: false))
        {
            return false;
        }

        endExclusive = index;
        return true;
    }

    private static bool TryReadYearMonthDayTokenAt(string source, int start, out int endExclusive)
    {
        endExclusive = start;
        var index = start;

        if (!TryReadDigitToken(source, ref index, minLength: 4, maxLength: 4, allowTrailingComma: false) ||
            !TryReadRequiredWhitespace(source, ref index) ||
            !TryReadMonthNameToken(source, ref index) ||
            !TryReadRequiredWhitespace(source, ref index) ||
            !TryReadDigitToken(source, ref index, minLength: 1, maxLength: 2, allowTrailingComma: false, allowOrdinalSuffix: true))
        {
            return false;
        }

        endExclusive = index;
        return true;
    }

    private static bool TryReadMonthNameToken(string source, ref int index)
    {
        var start = index;
        while (index < source.Length && char.IsLetter(source[index]))
            index++;

        if (index == start)
            return false;

        if (index < source.Length && source[index] == '.')
            index++;

        return true;
    }

    private static bool TryReadDigitToken(
        string source,
        ref int index,
        int minLength,
        int maxLength,
        bool allowTrailingComma,
        bool allowOrdinalSuffix = false)
    {
        var start = index;
        while (index < source.Length &&
            index - start < maxLength &&
            char.IsDigit(source[index]))
        {
            index++;
        }

        var length = index - start;
        if (length < minLength)
            return false;

        if (allowOrdinalSuffix &&
            !TryReadOrdinalDaySuffixIfPresent(source, start, index, ref index))
        {
            return false;
        }

        if (allowTrailingComma && index < source.Length && source[index] == ',')
            index++;

        return true;
    }

    private static bool TryReadOrdinalDaySuffixIfPresent(
        string source,
        int digitStart,
        int suffixStart,
        ref int index)
    {
        if (suffixStart + 2 > source.Length ||
            !char.IsLetter(source[suffixStart]) ||
            !char.IsLetter(source[suffixStart + 1]))
        {
            return true;
        }

        if (!int.TryParse(source.AsSpan(digitStart, suffixStart - digitStart), NumberStyles.None, CultureInfo.InvariantCulture, out var day) ||
            !IsValidOrdinalDaySuffix(day, source.Substring(suffixStart, 2)))
        {
            return false;
        }

        index = suffixStart + 2;
        return true;
    }

    private static bool TryReadRequiredWhitespace(string source, ref int index)
    {
        var start = index;
        while (index < source.Length && char.IsWhiteSpace(source[index]))
            index++;

        return index > start;
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

    private static bool HasMonthNameDateTokenBoundary(string source, int start, int endExclusive)
    {
        if (start > 0 && char.IsLetterOrDigit(source[start - 1]))
            return false;

        return endExclusive >= source.Length || !char.IsLetterOrDigit(source[endExclusive]);
    }

    private static bool TryParseEnglishMonthNamePart(string part, out int month)
    {
        var normalized = part.EndsWith(".", StringComparison.Ordinal)
            ? part[..^1].ToLowerInvariant()
            : part.ToLowerInvariant();

        month = normalized switch
        {
            "jan" or "january" => 1,
            "feb" or "february" => 2,
            "mar" or "march" => 3,
            "apr" or "april" => 4,
            "may" => 5,
            "jun" or "june" => 6,
            "jul" or "july" => 7,
            "aug" or "august" => 8,
            "sep" or "sept" or "september" => 9,
            "oct" or "october" => 10,
            "nov" or "november" => 11,
            "dec" or "december" => 12,
            _ => 0
        };

        return month != 0;
    }

    private static bool TryParseDayPart(string part, bool allowTrailingComma, out int day)
    {
        day = 0;
        if (allowTrailingComma && part.EndsWith(",", StringComparison.Ordinal))
            part = part[..^1];

        var digitLength = 0;
        while (digitLength < part.Length && char.IsDigit(part[digitLength]))
            digitLength++;

        if (digitLength is < 1 or > 2 ||
            !int.TryParse(part[..digitLength], NumberStyles.None, CultureInfo.InvariantCulture, out day))
        {
            return false;
        }

        if (digitLength == part.Length)
            return true;

        return part.Length - digitLength == 2 &&
            IsValidOrdinalDaySuffix(day, part[digitLength..]);
    }

    private static bool IsValidOrdinalDaySuffix(int day, string suffix)
    {
        var expected = day % 100 is >= 11 and <= 13
            ? "th"
            : day % 10 switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th"
            };

        return string.Equals(suffix, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseYearPart(string part, out int year)
    {
        year = 0;
        return part.Length == 4 &&
        part.All(char.IsDigit) &&
        int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out year);
    }

    private static bool TryCreateDateParts(string[] parts, DatePartKind[] order, out DateParts date)
    {
        date = default;
        if (parts.Length != 3 || order.Length != 3)
            return false;

        if (!TryGetDatePartValue(parts, order, DatePartKind.Year, out var year) ||
            !TryGetDatePartValue(parts, order, DatePartKind.Month, out var month) ||
            !TryGetDatePartValue(parts, order, DatePartKind.Day, out var day))
        {
            return false;
        }

        return TryCreateDateParts(year, month, day, out date);
    }

    private static bool TryCreateDateParts(int year, int month, int day, out DateParts date)
    {
        date = default;
        if (year is < 1000 or > 9999 ||
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
