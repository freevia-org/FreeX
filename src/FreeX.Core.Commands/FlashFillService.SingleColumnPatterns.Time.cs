using System.Globalization;

namespace FreeX.Core.Commands;

public static partial class FlashFillService
{
    private static Func<string, string?>? TryTimeNormalization(IReadOnlyList<(string Source, string Expected)> examples)
    {
        TimeOutputPattern? outputPattern = null;
        var changedAny = false;

        foreach (var (source, expected) in examples)
        {
            if (!TryParseTimeLikeValue(source, out var sourceTime, out _) ||
                !TryParseTimeLikeValue(expected, out var expectedTime, out var currentPattern) ||
                sourceTime != expectedTime)
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
        return source => TryParseTimeLikeValue(source, out var time, out _)
            ? FormatTimeParts(time, pattern)
            : null;
    }

    private static Func<string, string?>? TryEmbeddedTimeExtraction(IReadOnlyList<(string Source, string Expected)> examples)
    {
        TimeOutputPattern? outputPattern = null;
        var changedAny = false;

        foreach (var (source, expected) in examples)
        {
            var sourceTimeCount = CountEmbeddedTimeLikeValues(source, out var sourceTime);
            if (sourceTimeCount != 1)
            {
                return sourceTimeCount > 1 && TryParseTimeLikeValue(expected, out _, out _)
                    ? _ => null
                    : null;
            }

            if (!TryParseTimeLikeValue(expected, out var expectedTime, out var currentPattern) ||
                sourceTime != expectedTime)
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
        return source => CountEmbeddedTimeLikeValues(source, out var time) == 1
            ? FormatTimeParts(time, pattern)
            : null;
    }

    private static bool TryParseTimeLikeValue(
        string source,
        out TimeParts time,
        out TimeOutputPattern pattern)
    {
        time = default;
        pattern = default;

        var candidate = source.Trim();
        return candidate.Length > 0 &&
               TryReadTimeTokenAt(candidate, 0, out var endExclusive, out time, out pattern) &&
               endExclusive == candidate.Length;
    }

    private static int CountEmbeddedTimeLikeValues(string source, out TimeParts time)
    {
        time = default;

        var count = 0;
        for (var start = 0; start < source.Length; start++)
        {
            if (!char.IsDigit(source[start]) ||
                (start > 0 && (char.IsDigit(source[start - 1]) || source[start - 1] == ':')))
            {
                continue;
            }

            if (!TryReadTimeTokenAt(source, start, out var endExclusive, out var currentTime, out _) ||
                !HasTimeTokenBoundary(source, start, endExclusive))
            {
                continue;
            }

            count++;
            if (count > 1)
                return count;

            time = currentTime;
            start = endExclusive - 1;
        }

        return count;
    }

    private static bool TryReadTimeTokenAt(
        string source,
        int start,
        out int endExclusive,
        out TimeParts time,
        out TimeOutputPattern pattern)
    {
        endExclusive = start;
        time = default;
        pattern = default;

        if (!TryReadHour(source, start, out var hourText, out var hour, out var index) ||
            index >= source.Length ||
            source[index] != ':')
        {
            return false;
        }

        index++;
        if (!TryReadTwoDigitNumber(source, index, out var minute) || minute > 59)
            return false;

        index += 2;
        var hasSeconds = false;
        var second = 0;
        if (index < source.Length && source[index] == ':')
        {
            index++;
            if (!TryReadTwoDigitNumber(source, index, out second) || second > 59)
                return false;

            index += 2;
            hasSeconds = true;
        }

        var meridiemStart = index;
        while (meridiemStart < source.Length && char.IsWhiteSpace(source[meridiemStart]))
            meridiemStart++;

        if (TryReadMeridiem(source, meridiemStart, out var isPm, out var meridiemCasing, out var meridiemEnd))
        {
            if (hour is < 1 or > 12)
                return false;

            var normalizedHour = hour % 12;
            if (isPm)
                normalizedHour += 12;

            endExclusive = meridiemEnd;
            time = new TimeParts(normalizedHour, minute, second);
            pattern = new TimeOutputPattern(
                TimeOutputKind.TwelveHour,
                hasSeconds,
                hourText.StartsWith('0') ? 2 : 1,
                meridiemStart > index,
                meridiemCasing);
            return true;
        }

        if (hour > 23)
            return false;

        endExclusive = index;
        time = new TimeParts(hour, minute, second);
        pattern = new TimeOutputPattern(
            TimeOutputKind.TwentyFourHour,
            hasSeconds,
            hourText.Length,
            SpaceBeforeMeridiem: false,
            TimeMeridiemCasing.Upper);
        return true;
    }

    private static bool TryReadHour(
        string source,
        int start,
        out string hourText,
        out int hour,
        out int endExclusive)
    {
        hourText = string.Empty;
        hour = 0;
        endExclusive = start;

        if (start >= source.Length || !char.IsDigit(source[start]))
            return false;

        var index = start;
        while (index < source.Length && index - start < 2 && char.IsDigit(source[index]))
            index++;

        if (index < source.Length && char.IsDigit(source[index]))
            return false;

        hourText = source[start..index];
        endExclusive = index;
        return int.TryParse(hourText, NumberStyles.None, CultureInfo.InvariantCulture, out hour);
    }

    private static bool TryReadTwoDigitNumber(string source, int start, out int value)
    {
        value = 0;
        if (start + 2 > source.Length ||
            !char.IsDigit(source[start]) ||
            !char.IsDigit(source[start + 1]))
        {
            return false;
        }

        value = (source[start] - '0') * 10 + source[start + 1] - '0';
        return start + 2 >= source.Length || !char.IsDigit(source[start + 2]);
    }

    private static bool TryReadMeridiem(
        string source,
        int start,
        out bool isPm,
        out TimeMeridiemCasing casing,
        out int endExclusive)
    {
        isPm = false;
        casing = TimeMeridiemCasing.Upper;
        endExclusive = start;

        if (start + 2 > source.Length)
            return false;

        var first = source[start];
        var second = source[start + 1];
        if ((first != 'a' && first != 'A' && first != 'p' && first != 'P') ||
            (second != 'm' && second != 'M'))
        {
            return false;
        }

        isPm = first == 'p' || first == 'P';
        casing = (char.IsUpper(first), char.IsUpper(second)) switch
        {
            (true, true) => TimeMeridiemCasing.Upper,
            (false, false) => TimeMeridiemCasing.Lower,
            _ => TimeMeridiemCasing.Title
        };
        endExclusive = start + 2;
        return true;
    }

    private static bool HasTimeTokenBoundary(string source, int start, int endExclusive)
    {
        if (start > 0)
        {
            var previous = source[start - 1];
            if (char.IsLetterOrDigit(previous) || previous == ':')
                return false;
        }

        if (endExclusive >= source.Length)
            return true;

        var next = source[endExclusive];
        return !char.IsLetterOrDigit(next) && next != ':';
    }

    private static string FormatTimeParts(TimeParts time, TimeOutputPattern pattern)
    {
        var hour = pattern.Kind == TimeOutputKind.TwelveHour
            ? time.Hour % 12
            : time.Hour;
        if (pattern.Kind == TimeOutputKind.TwelveHour && hour == 0)
            hour = 12;

        var result = hour.ToString("D" + pattern.HourWidth.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) +
                     ":" +
                     time.Minute.ToString("D2", CultureInfo.InvariantCulture);

        if (pattern.IncludeSeconds)
            result += ":" + time.Second.ToString("D2", CultureInfo.InvariantCulture);

        if (pattern.Kind == TimeOutputKind.TwelveHour)
        {
            if (pattern.SpaceBeforeMeridiem)
                result += " ";

            result += FormatMeridiem(time.Hour >= 12, pattern.MeridiemCasing);
        }

        return result;
    }

    private static string FormatMeridiem(bool isPm, TimeMeridiemCasing casing) =>
        casing switch
        {
            TimeMeridiemCasing.Lower => isPm ? "pm" : "am",
            TimeMeridiemCasing.Title => isPm ? "Pm" : "Am",
            _ => isPm ? "PM" : "AM"
        };
}
