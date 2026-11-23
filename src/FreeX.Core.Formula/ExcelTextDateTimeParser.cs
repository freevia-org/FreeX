using System.Globalization;
using System.Text.RegularExpressions;

namespace FreeX.Core.Formula;

/// <summary>
/// Excel's en-US text-to-date/time grammar for DATEVALUE, TIMEVALUE and the implicit text-to-number
/// coercion of VALUE and arithmetic. It replaces reliance on the lenient <see cref="DateTime.TryParse(string, IFormatProvider, DateTimeStyles, out DateTime)"/>,
/// which accepts many shapes Excel rejects (ISO "T" separators, weekday names, "Jan 15 2020",
/// "10:30AM", dotted dates) and rejects several Excel accepts (month-name prefixes such as "Sept",
/// "M/yy" fallbacks such as "1/32", hours beyond 23, a trailing colon).
/// <para>Every rule below was measured against real Excel (en-US) with a COM probe; see
/// <c>ExcelTextDateTimeParserTests</c> for the pinned cases.</para>
/// </summary>
internal static class ExcelTextDateTimeParser
{
    private const long MillisPerDay = 86_400_000L;

    internal readonly struct Parsed
    {
        public Parsed(bool hasDate, int year, int month, int day, long millis)
        {
            HasDate = hasDate;
            Year = year;
            Month = month;
            Day = day;
            Millis = millis;
        }

        public bool HasDate { get; }
        public int Year { get; }
        public int Month { get; }
        public int Day { get; }

        /// <summary>The time of day (or elapsed time) in milliseconds, already rounded to the millisecond.</summary>
        public long Millis { get; }
    }

    private static readonly string[] MonthNames =
    [
        "january", "february", "march", "april", "may", "june",
        "july", "august", "september", "october", "november", "december",
    ];

    // An hour/minute/second component: one or two digits, or three/four without a leading zero.
    private const string Component = @"(?:\d{1,2}|[1-9]\d{2,3})";
    private const string SepNumeric = @"[ ]*[/-][ ]*";
    private const string SepMonth = @"(?:[ ]*[/-][ ]*|[ ]+|(?<=[A-Za-z])(?=\d)|(?<=\d)(?=[A-Za-z]))";

    private static string TimePattern(string p) =>
        $@"(?:(?<{p}h>{Component})[ ]*:[ ]*(?<{p}m>{Component})(?:[ ]*:[ ]*(?<{p}s>{Component}))?(?<{p}c>[ ]*:)?(?:[ ]*\.[ ]*(?<{p}f>\d*))?(?:[ ]+(?<{p}ap>[aApP][mM]?)(?:[ ]*\.)?)?"
        + $@"|(?<{p}h1>\d{{1,2}})[ ]+(?<{p}ap1>[aApP][mM]?)(?:[ ]*\.)?"
        + $@"|(?<{p}h2>{Component})[ ]*:(?![ ]*\d))";

    private static readonly Regex TimeOnly = new(
        $@"^[ ]*{TimePattern("a")}[ ]*$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);

    private static Regex WithTime(string dateForm) => new(
        $@"^[ ]*(?:{TimePattern("a")}[ ]+)?(?:{dateForm})(?:[ ]+{TimePattern("b")})?[ ]*$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);

    private enum Form { YearMonthDay, MonthDayYear, MonthDay, MonthYear, DayMonthYear, MonthNameNumber }

    private static readonly (Form Form, Regex Pattern)[] DateForms =
    [
        (Form.YearMonthDay, WithTime($@"(?<y>\d{{4}}){SepNumeric}(?<mo>\d{{1,2}}){SepNumeric}(?<d>\d{{1,2}})")),
        (Form.MonthDayYear, WithTime($@"(?<mo>\d{{1,2}}){SepNumeric}(?<d>\d{{1,2}}){SepNumeric}(?<y>\d{{1,4}})")),
        (Form.MonthDay, WithTime($@"(?<mo>\d{{1,2}}){SepNumeric}(?<d>\d{{1,2}})")),
        (Form.MonthYear, WithTime($@"(?<mo>\d{{1,2}}){SepNumeric}(?<y>\d{{4}})")),
        (Form.DayMonthYear, WithTime($@"(?<d>\d{{1,2}}){SepMonth}(?<mon>[A-Za-z]+)(?:{SepMonth}(?<y>\d{{1,4}}))?")),
        (Form.MonthNameNumber, WithTime($@"(?<mon>[A-Za-z]+){SepMonth}(?<n>\d{{1,4}})(?:[ ]*,[ ]+(?<y>\d{{1,4}}))?")),
    ];

    /// <summary>
    /// Parses <paramref name="text"/> as an Excel date, a time, or a date with a time. Only the ASCII
    /// space counts as whitespace (a tab or NBSP is #VALUE! in Excel). <paramref name="today"/>
    /// supplies the year for forms that omit it ("1/15", "Jan 15").
    /// </summary>
    public static bool TryParse(string text, DateTime today, out Parsed parsed)
    {
        parsed = default;
        string s = ExcelNumberTextGrammar.NormalizeDigits(text);
        if (s.Length == 0 || !ContainsDigit(s)) return false;

        var timeMatch = TimeOnly.Match(s);
        if (timeMatch.Success)
        {
            if (!TryReadTime(timeMatch, "a", out long millis)) return false;
            parsed = new Parsed(false, 0, 0, 0, millis);
            return true;
        }

        foreach (var (form, pattern) in DateForms)
        {
            var m = pattern.Match(s);
            if (!m.Success) continue;
            if (!TryResolveDate(form, m, today, out int year, out int month, out int day)) continue;

            bool hasLeading = m.Groups["ah"].Success || m.Groups["ah1"].Success || m.Groups["ah2"].Success;
            bool hasTrailing = m.Groups["bh"].Success || m.Groups["bh1"].Success || m.Groups["bh2"].Success;
            if (hasLeading && hasTrailing) continue;

            long timeMillis = 0;
            if (hasLeading && !TryReadTime(m, "a", out timeMillis)) continue;
            if (hasTrailing && !TryReadTime(m, "b", out timeMillis)) continue;

            parsed = new Parsed(true, year, month, day, timeMillis);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Serial number of the parsed text (date serial plus the time fraction), as VALUE returns it.
    /// A time-only text yields just the fraction (possibly above 1 for elapsed hours).
    /// </summary>
    public static double ToSerial(in Parsed parsed, bool uses1904DateSystem)
    {
        double dateSerial = 0;
        if (parsed.HasDate)
        {
            dateSerial = parsed.Year == 1900 && parsed.Month == 2 && parsed.Day == 29
                ? (uses1904DateSystem ? -1462 + 60 : 60)
                : ExcelDateSystem.DateToSerial(new DateTime(parsed.Year, parsed.Month, parsed.Day), uses1904DateSystem);
        }

        return dateSerial + parsed.Millis / (double)MillisPerDay;
    }

    /// <summary>The time-of-day fraction TIMEVALUE reports: the time modulo one day.</summary>
    public static double TimeFraction(in Parsed parsed) =>
        (parsed.Millis % MillisPerDay) / (double)MillisPerDay;

    private static bool ContainsDigit(string s)
    {
        foreach (char c in s)
            if (c is >= '0' and <= '9') return true;
        return false;
    }

    private static bool TryReadTime(Match m, string p, out long millis)
    {
        millis = 0;
        var hourColon = m.Groups[p + "h2"];
        if (hourColon.Success)
            return Finish(long.Parse(hourColon.Value, CultureInfo.InvariantCulture), 0, 0, 0, null, out millis);

        var hourOnly = m.Groups[p + "h1"];
        long hours, minutes = 0, seconds = 0;
        string? ap;

        if (hourOnly.Success)
        {
            hours = long.Parse(hourOnly.Value, CultureInfo.InvariantCulture);
            ap = m.Groups[p + "ap1"].Value;
        }
        else
        {
            long first = long.Parse(m.Groups[p + "h"].Value, CultureInfo.InvariantCulture);
            long second = long.Parse(m.Groups[p + "m"].Value, CultureInfo.InvariantCulture);
            var sGroup = m.Groups[p + "s"];
            var fGroup = m.Groups[p + "f"];
            ap = m.Groups[p + "ap"].Success ? m.Groups[p + "ap"].Value : null;

            if (sGroup.Success)
            {
                hours = first;
                minutes = second;
                seconds = long.Parse(sGroup.Value, CultureInfo.InvariantCulture);
            }
            else if (fGroup.Success && fGroup.Value.Length > 0)
            {
                // "1:30.5" is minutes:seconds.fraction, not hours:minutes.
                hours = 0;
                minutes = first;
                seconds = second;
            }
            else
            {
                hours = first;
                minutes = second;
            }

            long fractionMillis = 0;
            if (fGroup.Success && fGroup.Value.Length > 0)
            {
                string digits = fGroup.Value.Length >= 4 ? fGroup.Value[..4] : fGroup.Value.PadRight(4, '0');
                fractionMillis = int.Parse(digits.AsSpan(0, 3), NumberStyles.None, CultureInfo.InvariantCulture)
                    + (digits[3] >= '5' ? 1 : 0);
            }

            return Finish(hours, minutes, seconds, fractionMillis, ap, out millis);
        }

        return Finish(hours, minutes, seconds, 0, ap, out millis);
    }

    private static bool Finish(long hours, long minutes, long seconds, long fractionMillis, string? ap, out long millis)
    {
        millis = 0;
        if (ap is not null && ap.Length > 0)
        {
            // With an AM/PM marker nothing may overflow: 0..12 hours, minutes and seconds below 60.
            if (hours > 12 || minutes >= 60 || seconds >= 60) return false;
            bool pm = ap[0] is 'p' or 'P';
            hours = hours % 12 + (pm ? 12 : 0);
        }
        else
        {
            // Without a marker at most one component may overflow its normal range
            // ("23:61" and "25:00" and "1:59:99" are valid; "25:61" and "1:99:99" are not).
            int overflow = (hours >= 24 ? 1 : 0) + (minutes >= 60 ? 1 : 0) + (seconds >= 60 ? 1 : 0);
            if (overflow > 1) return false;
        }

        millis = hours * 3_600_000L + minutes * 60_000L + seconds * 1_000L + fractionMillis;
        return true;
    }

    private static bool TryResolveDate(Form form, Match m, DateTime today, out int year, out int month, out int day)
    {
        year = month = day = 0;
        switch (form)
        {
            case Form.YearMonthDay:
            case Form.MonthDayYear:
                {
                    if (!TryResolveYear(m.Groups["y"].Value, out year)) return false;
                    month = int.Parse(m.Groups["mo"].Value, CultureInfo.InvariantCulture);
                    day = int.Parse(m.Groups["d"].Value, CultureInfo.InvariantCulture);
                    return IsValidDate(year, month, day);
                }
            case Form.MonthDay:
                {
                    month = int.Parse(m.Groups["mo"].Value, CultureInfo.InvariantCulture);
                    return ResolveDayOrYear(m.Groups["d"].Value, month, today, out year, out day);
                }
            case Form.MonthYear:
                {
                    month = int.Parse(m.Groups["mo"].Value, CultureInfo.InvariantCulture);
                    day = 1;
                    return TryResolveYear(m.Groups["y"].Value, out year) && IsValidDate(year, month, day);
                }
            case Form.DayMonthYear:
                {
                    if (!TryResolveMonthName(m.Groups["mon"].Value, out month)) return false;
                    day = int.Parse(m.Groups["d"].Value, CultureInfo.InvariantCulture);
                    var yGroup = m.Groups["y"];
                    if (yGroup.Success)
                    {
                        if (!TryResolveYear(yGroup.Value, out year)) return false;
                    }
                    else
                    {
                        year = today.Year;
                    }

                    return IsValidDate(year, month, day);
                }
            case Form.MonthNameNumber:
                {
                    if (!TryResolveMonthName(m.Groups["mon"].Value, out month)) return false;
                    string n = m.Groups["n"].Value;
                    var yGroup = m.Groups["y"];
                    if (yGroup.Success)
                    {
                        if (n.Length > 2 || !TryResolveYear(yGroup.Value, out year)) return false;
                        day = int.Parse(n, CultureInfo.InvariantCulture);
                        return IsValidDate(year, month, day);
                    }

                    if (n.Length == 4)
                    {
                        day = 1;
                        return TryResolveYear(n, out year) && IsValidDate(year, month, day);
                    }

                    return ResolveDayOrYear(n, month, today, out year, out day);
                }
            default:
                return false;
        }
    }

    // "M/N" and "Mon N": N is the day of the current year when it is a valid day of that month,
    // otherwise a two-digit year ("1/32" is January 1932, "Feb 30" February 1930).
    private static bool ResolveDayOrYear(string text, int month, DateTime today, out int year, out int day)
    {
        year = today.Year;
        day = int.Parse(text, CultureInfo.InvariantCulture);
        if (month is < 1 or > 12) return false;
        if (text.Length > 2) return false;
        if (day >= 1 && IsValidDate(year, month, day)) return true;

        if (!TryResolveYear(text, out year)) return false;
        day = 1;
        return IsValidDate(year, month, day);
    }

    private static bool TryResolveYear(string text, out int year)
    {
        year = 0;
        if (text.Length is 1 or 2)
        {
            int v = int.Parse(text, CultureInfo.InvariantCulture);
            year = v <= 29 ? 2000 + v : 1900 + v;
            return true;
        }

        if (text.Length == 4)
        {
            year = int.Parse(text, CultureInfo.InvariantCulture);
            return year is >= 1900 and <= 9999;
        }

        return false;
    }

    private static bool IsValidDate(int year, int month, int day)
    {
        if (month is < 1 or > 12 || day < 1) return false;
        if (year == 1900 && month == 2 && day == 29) return true; // Excel's phantom leap day
        return day <= DateTime.DaysInMonth(year, month);
    }

    // Month names match on any prefix of three or more letters ("Jan", "Janu", "Sept", "Septem").
    private static bool TryResolveMonthName(string token, out int month)
    {
        month = 0;
        if (token.Length < 3) return false;
        for (int i = 0; i < MonthNames.Length; i++)
        {
            if (MonthNames[i].StartsWith(token, StringComparison.OrdinalIgnoreCase))
            {
                month = i + 1;
                return true;
            }
        }

        return false;
    }
}
