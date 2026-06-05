using System.Globalization;

namespace FreeX.Core.Commands;

public static partial class FlashFillService
{
    private enum TimeComponentKind
    {
        Hour,
        Minute,
        Second,
        Meridiem
    }

    private enum TimeHourComponentStyle
    {
        SourceText,
        Unpadded
    }

    private enum TimeRangeEndpointKind
    {
        First,
        Second
    }

    private readonly record struct TimeRangeTextFrame(string Prefix, string Between, string Suffix);

    private readonly record struct TimeLikeComponents(
        string HourText,
        string UnpaddedHourText,
        string MinuteText,
        string? SecondText,
        string? MeridiemText);

    private static Func<string, string?>? TryTimeComponentExtraction(IReadOnlyList<(string Source, string Expected)> examples)
    {
        TimeComponentKind? componentKind = null;
        var sawComponentCandidate = false;
        var sawNonComponentExpected = false;
        var hourCanUseSourceText = true;
        var hourCanUseUnpadded = true;

        foreach (var (source, expected) in examples)
        {
            if (!TryParseTimeLikeComponents(source, out var components))
                return null;

            var matches = GetTimeComponentMatches(components, expected);
            if (matches.Count == 0)
            {
                sawNonComponentExpected = true;
                continue;
            }

            if (matches.Count > 1)
                return _ => null;

            sawComponentCandidate = true;
            var currentKind = matches[0];
            if (componentKind is null)
                componentKind = currentKind;
            else if (componentKind.Value != currentKind)
                return _ => null;

            if (currentKind == TimeComponentKind.Hour)
            {
                hourCanUseSourceText &= components.HourText == expected;
                hourCanUseUnpadded &= components.UnpaddedHourText == expected;
                if (!hourCanUseSourceText && !hourCanUseUnpadded)
                    return _ => null;
            }
        }

        if (!sawComponentCandidate)
            return null;

        if (sawNonComponentExpected || componentKind is null)
            return _ => null;

        var kind = componentKind.Value;
        var hourStyle = hourCanUseUnpadded
            ? TimeHourComponentStyle.Unpadded
            : TimeHourComponentStyle.SourceText;

        return source => TryParseTimeLikeComponents(source, out var components)
            ? GetTimeComponent(components, kind, hourStyle)
            : null;
    }

    private static Func<string, string?>? TryEmbeddedTimeComponentExtraction(
        IReadOnlyList<(string Source, string Expected)> examples)
    {
        TimeComponentKind? componentKind = null;
        var sawComponentCandidate = false;
        var sawNonComponentExpected = false;

        foreach (var (source, expected) in examples)
        {
            var sourceTimeCount = CountEmbeddedTimeLikeComponents(source, out var components);
            if (sourceTimeCount != 1)
            {
                if (sourceTimeCount > 1 && HasAnyEmbeddedTimeComponentMatch(source, expected))
                    return _ => null;

                return null;
            }

            var matches = GetEmbeddedTimeComponentMatches(components, expected);
            if (matches.Count == 0)
            {
                sawNonComponentExpected = true;
                continue;
            }

            if (matches.Count > 1)
                return _ => null;

            sawComponentCandidate = true;
            var currentKind = matches[0];
            if (componentKind is null)
                componentKind = currentKind;
            else if (componentKind.Value != currentKind)
                return _ => null;
        }

        if (!sawComponentCandidate)
            return null;

        if (sawNonComponentExpected || componentKind is null)
            return _ => null;

        var kind = componentKind.Value;
        return source => CountEmbeddedTimeLikeComponents(source, out var components) == 1
            ? GetTimeComponent(components, kind, TimeHourComponentStyle.SourceText)
            : null;
    }

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

    private static Func<string, string?>? TryEmbeddedTimeRangeEndpointExtraction(
        IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (examples.Count < 2)
            return null;

        TimeRangeEndpointKind? endpointKind = null;
        TimeRangeTextFrame? firstFrame = null;
        TimeParts? previousFirstTime = null;
        TimeParts? previousSecondTime = null;
        var sawDifferentFrame = false;
        var sawOpposingEndpointMovement = false;
        var sawEndpointCandidate = false;

        foreach (var (source, expected) in examples)
        {
            if (!TryGetExactlyTwoEmbeddedTimeTokenRanges(source, out var first, out var second))
                return null;

            var matchesFirst = TimeTokenEquals(source, first, expected);
            var matchesSecond = TimeTokenEquals(source, second, expected);
            if (matchesFirst == matchesSecond)
                return TryParseTimeLikeValue(expected, out _, out _) ? _ => null : null;

            sawEndpointCandidate = true;
            var currentKind = matchesFirst
                ? TimeRangeEndpointKind.First
                : TimeRangeEndpointKind.Second;
            if (endpointKind is null)
                endpointKind = currentKind;
            else if (endpointKind.Value != currentKind)
                return _ => null;

            var currentFrame = GetTimeRangeTextFrame(source, first, second);
            if (firstFrame is null)
                firstFrame = currentFrame;
            else if (firstFrame.Value != currentFrame)
                sawDifferentFrame = true;

            if (!TryGetTimeParts(source, first, out var firstTime) ||
                !TryGetTimeParts(source, second, out var secondTime))
            {
                return null;
            }

            if (previousFirstTime is { } previousFirst && previousSecondTime is { } previousSecond)
            {
                var firstMovement = Math.Sign(CompareTimeParts(firstTime, previousFirst));
                var secondMovement = Math.Sign(CompareTimeParts(secondTime, previousSecond));
                if (firstMovement != 0 && secondMovement != 0 && firstMovement != secondMovement)
                    sawOpposingEndpointMovement = true;
            }

            previousFirstTime = firstTime;
            previousSecondTime = secondTime;
        }

        if (!sawEndpointCandidate || endpointKind is null)
            return null;

        if (!sawDifferentFrame && !sawOpposingEndpointMovement)
            return _ => null;

        var kind = endpointKind.Value;
        var cache = new ExtractedSegmentCache();
        return source =>
            TryGetExactlyTwoEmbeddedTimeTokenRanges(source, out var first, out var second)
                ? cache.GetOrAdd(source, kind == TimeRangeEndpointKind.First ? first.Start : second.Start,
                    kind == TimeRangeEndpointKind.First ? first.EndExclusive : second.EndExclusive)
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

    private static bool TryParseTimeLikeComponents(string source, out TimeLikeComponents components)
    {
        components = default;

        var candidate = source.Trim();
        if (candidate.Length == 0 ||
            !TryReadTimeTokenAt(candidate, 0, out var endExclusive, out _, out _) ||
            endExclusive != candidate.Length)
        {
            return false;
        }

        var hourEnd = candidate.IndexOf(':', StringComparison.Ordinal);
        var hourText = candidate[..hourEnd];
        var minuteStart = hourEnd + 1;
        var minuteText = candidate.Substring(minuteStart, 2);
        var index = minuteStart + 2;

        string? secondText = null;
        if (index < candidate.Length && candidate[index] == ':')
        {
            var secondStart = index + 1;
            secondText = candidate.Substring(secondStart, 2);
            index = secondStart + 2;
        }

        while (index < candidate.Length && char.IsWhiteSpace(candidate[index]))
            index++;

        var meridiemText = index < candidate.Length ? candidate[index..] : null;
        components = new TimeLikeComponents(
            hourText,
            FormatUnpaddedTimeHour(hourText),
            minuteText,
            secondText,
            meridiemText);
        return true;
    }

    private static List<TimeComponentKind> GetTimeComponentMatches(TimeLikeComponents components, string expected)
    {
        var matches = new List<TimeComponentKind>(1);

        if (components.HourText == expected || components.UnpaddedHourText == expected)
            matches.Add(TimeComponentKind.Hour);

        if (components.MinuteText == expected)
            matches.Add(TimeComponentKind.Minute);

        if (components.SecondText == expected)
            matches.Add(TimeComponentKind.Second);

        if (components.MeridiemText == expected)
            matches.Add(TimeComponentKind.Meridiem);

        return matches;
    }

    private static List<TimeComponentKind> GetEmbeddedTimeComponentMatches(TimeLikeComponents components, string expected)
    {
        var matches = new List<TimeComponentKind>(1);

        if (components.HourText == expected)
            matches.Add(TimeComponentKind.Hour);

        if (components.MinuteText == expected)
            matches.Add(TimeComponentKind.Minute);

        if (components.SecondText == expected)
            matches.Add(TimeComponentKind.Second);

        if (components.MeridiemText == expected)
            matches.Add(TimeComponentKind.Meridiem);

        return matches;
    }

    private static string? GetTimeComponent(
        TimeLikeComponents components,
        TimeComponentKind kind,
        TimeHourComponentStyle hourStyle) =>
        kind switch
        {
            TimeComponentKind.Hour => hourStyle == TimeHourComponentStyle.Unpadded
                ? components.UnpaddedHourText
                : components.HourText,
            TimeComponentKind.Minute => components.MinuteText,
            TimeComponentKind.Second => components.SecondText,
            _ => components.MeridiemText
        };

    private static string FormatUnpaddedTimeHour(string hourText) =>
        int.Parse(hourText, NumberStyles.None, CultureInfo.InvariantCulture)
            .ToString(CultureInfo.InvariantCulture);

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

    private static int CountEmbeddedTimeLikeComponents(string source, out TimeLikeComponents components)
    {
        components = default;

        var count = 0;
        foreach (var (start, endExclusive) in EnumerateEmbeddedTimeTokenRanges(source))
        {
            count++;
            if (count > 1)
                return count;

            _ = TryParseTimeLikeComponents(source[start..endExclusive], out components);
        }

        return count;
    }

    private static bool TryGetExactlyTwoEmbeddedTimeTokenRanges(
        string source,
        out (int Start, int EndExclusive) first,
        out (int Start, int EndExclusive) second)
    {
        first = default;
        second = default;

        var count = 0;
        for (var start = 0; start < source.Length; start++)
        {
            if (!IsEmbeddedTimeCandidateStart(source, start))
                continue;

            if (!TryReadTimeTokenAt(source, start, out var endExclusive, out _, out _) ||
                !HasTimeTokenBoundary(source, start, endExclusive))
            {
                return false;
            }

            count++;
            if (count == 1)
                first = (start, endExclusive);
            else if (count == 2)
                second = (start, endExclusive);
            else
                return false;

            start = endExclusive - 1;
        }

        return count == 2;
    }

    private static bool IsEmbeddedTimeCandidateStart(string source, int start)
    {
        if (!char.IsDigit(source[start]) ||
            (start > 0 && (char.IsDigit(source[start - 1]) || source[start - 1] == ':')))
        {
            return false;
        }

        var index = start + 1;
        if (index < source.Length && char.IsDigit(source[index]))
            index++;

        return index < source.Length && source[index] == ':';
    }

    private static bool TimeTokenEquals(
        string source,
        (int Start, int EndExclusive) range,
        string expected)
    {
        var length = range.EndExclusive - range.Start;
        return length == expected.Length &&
               source.AsSpan(range.Start, length).SequenceEqual(expected.AsSpan());
    }

    private static TimeRangeTextFrame GetTimeRangeTextFrame(
        string source,
        (int Start, int EndExclusive) first,
        (int Start, int EndExclusive) second) =>
        new(
            source[..first.Start],
            source[first.EndExclusive..second.Start],
            source[second.EndExclusive..]);

    private static bool TryGetTimeParts(
        string source,
        (int Start, int EndExclusive) range,
        out TimeParts time) =>
        TryReadTimeTokenAt(source, range.Start, out var endExclusive, out time, out _) &&
        endExclusive == range.EndExclusive;

    private static int CompareTimeParts(TimeParts left, TimeParts right)
    {
        var leftSeconds = left.Hour * 60 * 60 + left.Minute * 60 + left.Second;
        var rightSeconds = right.Hour * 60 * 60 + right.Minute * 60 + right.Second;
        return leftSeconds.CompareTo(rightSeconds);
    }

    private static bool HasAnyEmbeddedTimeComponentMatch(string source, string expected)
    {
        foreach (var (start, endExclusive) in EnumerateEmbeddedTimeTokenRanges(source))
        {
            if (TryParseTimeLikeComponents(source[start..endExclusive], out var components) &&
                GetEmbeddedTimeComponentMatches(components, expected).Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<(int Start, int EndExclusive)> EnumerateEmbeddedTimeTokenRanges(string source)
    {
        for (var start = 0; start < source.Length; start++)
        {
            if (!char.IsDigit(source[start]) ||
                (start > 0 && (char.IsDigit(source[start - 1]) || source[start - 1] == ':')))
            {
                continue;
            }

            if (!TryReadTimeTokenAt(source, start, out var endExclusive, out _, out _) ||
                !HasTimeTokenBoundary(source, start, endExclusive))
            {
                continue;
            }

            yield return (start, endExclusive);
            start = endExclusive - 1;
        }
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
