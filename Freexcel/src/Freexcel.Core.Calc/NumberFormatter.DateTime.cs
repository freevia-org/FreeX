using System.Globalization;
using System.Text.RegularExpressions;

namespace Freexcel.Core.Calc;

public static partial class NumberFormatter
{
    private static FormatResult FormatDateTimeWithColor(double oaDate, string[] sections)
    {
        var parsed = SelectDateTimeSection(oaDate, sections);
        return new FormatResult(FormatDateTime(oaDate, parsed.Format), parsed.ColorHex);
    }

    private static ParsedSection SelectDateTimeSection(double value, string[] sections)
    {
        var parsedSections = sections.Select(ParseSection).ToArray();
        if (!parsedSections.Any(section => section.Condition is not null))
            return parsedSections[0];

        var selectedIndex = Array.FindIndex(parsedSections, section =>
            section.Condition is not null && section.Condition.Matches(value));
        if (selectedIndex >= 0)
            return parsedSections[selectedIndex];

        selectedIndex = Array.FindIndex(parsedSections, section => section.Condition is null);
        return selectedIndex >= 0 ? parsedSections[selectedIndex] : parsedSections[0];
    }

    private static string FormatDateTime(double oaDate, string format)
    {
        var (_, cleanFmt) = NumberFormatColorMapper.ExtractColor(format);
        if (TryResolveSpecialDateTimeLocaleToken(cleanFmt, out var specialDateTimeFormat))
        {
            try
            {
                return FormatDateTimeValue(DateTime.FromOADate(oaDate), specialDateTimeFormat, CultureInfo.InvariantCulture.DateTimeFormat);
            }
            catch { return oaDate.ToString(CultureInfo.InvariantCulture); }
        }
        cleanFmt = PreserveLocaleCurrencyTokens(cleanFmt, out _, out var dateTimeFormat);

        var elapsedMatch = Regex.Match(cleanFmt, @"\[([hH])\]|\[([mM])\]|\[([sS])\]");
        if (elapsedMatch.Success)
            return FormatElapsedTime(oaDate, RemoveSpacingAndFillDirectives(cleanFmt), elapsedMatch);

        cleanFmt = Regex.Replace(cleanFmt, @"\[[^\]]*\]", "");
        cleanFmt = RemoveSpacingAndFillDirectives(cleanFmt);
        try
        {
            var dt = DateTime.FromOADate(oaDate);
            if (IsDateTimeFormat(cleanFmt))
                return FormatDateTimeValue(dt, cleanFmt, dateTimeFormat);
            return dt.ToString(cleanFmt, dateTimeFormat);
        }
        catch { return oaDate.ToString(CultureInfo.InvariantCulture); }
    }

    private static string FormatDateTimeValue(
        DateTime dateTime,
        string excelFormat,
        DateTimeFormatInfo dateTimeFormat)
    {
        if (TryGetFractionalSecondPrecision(excelFormat, out int precision))
            dateTime = RoundToFractionalSecondPrecision(dateTime, precision);

        var preparedFormat = ReplaceMonthInitialTokens(excelFormat, dateTime, dateTimeFormat);
        return dateTime.ToString(ToNetDateFormat(preparedFormat), dateTimeFormat);
    }

    private static string ReplaceMonthInitialTokens(
        string excelFormat,
        DateTime dateTime,
        DateTimeFormatInfo dateTimeFormat)
    {
        var result = new System.Text.StringBuilder();
        string monthName = dateTimeFormat.MonthNames[dateTime.Month - 1];
        if (string.IsNullOrEmpty(monthName))
            monthName = dateTime.ToString("MMMM", dateTimeFormat);

        string initial = monthName[..1].Replace("'", "''", StringComparison.Ordinal);
        for (int i = 0; i < excelFormat.Length;)
        {
            if (excelFormat[i] == '"')
            {
                int end = excelFormat.IndexOf('"', i + 1);
                if (end < 0) end = excelFormat.Length - 1;
                result.Append(excelFormat[i..(end + 1)]);
                i = end + 1;
                continue;
            }

            if (excelFormat[i] == '\\' && i + 1 < excelFormat.Length)
            {
                result.Append(excelFormat, i, 2);
                i += 2;
                continue;
            }

            if (i + 5 <= excelFormat.Length &&
                string.Compare(excelFormat, i, "mmmmm", 0, 5, StringComparison.OrdinalIgnoreCase) == 0)
            {
                result.Append('\'');
                result.Append(initial);
                result.Append('\'');
                i += 5;
                continue;
            }

            result.Append(excelFormat[i++]);
        }

        return result.ToString();
    }

    private static bool TryResolveSpecialDateTimeLocaleToken(string format, out string excelFormat)
    {
        var match = Regex.Match(format, @"^\s*\[\$-F(?<kind>400|800)\]\s*$", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            excelFormat = "";
            return false;
        }

        excelFormat = string.Equals(match.Groups["kind"].Value, "800", StringComparison.OrdinalIgnoreCase)
            ? "dddd, mmmm d, yyyy"
            : "h:mm:ss AM/PM";
        return true;
    }

    private static bool TryGetFractionalSecondPrecision(string format, out int precision)
    {
        var match = Regex.Match(format, @"(?<=[sS])\.(0+)");
        if (match.Success)
        {
            precision = match.Groups[1].Value.Length;
            return true;
        }

        precision = 0;
        return false;
    }

    private static DateTime RoundToFractionalSecondPrecision(DateTime dateTime, int precision)
    {
        if (precision >= 7)
            return dateTime;

        long scale = (long)Math.Pow(10, 7 - precision);
        long roundedTicks = ((dateTime.Ticks + (scale / 2)) / scale) * scale;
        return new DateTime(roundedTicks, dateTime.Kind);
    }

    // Detect date/time format: has date/time tokens and no digit-only tokens
    private static bool IsDateTimeFormat(string format)
    {
        // Strip quoted strings before checking
        var stripped = Regex.Replace(format, "\"[^\"]*\"", "");
        stripped = Regex.Replace(stripped, @"(?<=[sS])\.0+", "");
        bool hasDateToken = stripped.IndexOfAny(['y', 'Y', 'd', 'D', 'h', 'H', 's', 'S', 'm', 'M']) >= 0;
        bool hasNumberToken = stripped.IndexOfAny(['0', '#']) >= 0;
        return hasDateToken && !hasNumberToken;
    }

    // Map Excel date format tokens to .NET format string equivalents.
    private static string ToNetDateFormat(string excelFmt)
    {
        // When AM/PM is present the hour token uses 12-hour lowercase (h/hh);
        // without AM/PM use 24-hour uppercase (H/HH).
        bool hasAmPm =
            excelFmt.IndexOf("AM/PM", StringComparison.OrdinalIgnoreCase) >= 0 ||
            excelFmt.IndexOf("A/P", StringComparison.OrdinalIgnoreCase) >= 0;
        string hourToken2 = hasAmPm ? "hh" : "HH";
        string hourToken1 = hasAmPm ? "h"  : "H";

        // Process token by token to avoid double-replacement issues
        var sb = new System.Text.StringBuilder();
        int i = 0;
        while (i < excelFmt.Length)
        {
            char c = excelFmt[i];
            if (c == '"')
            {
                // Copy quoted literal unchanged
                int end = excelFmt.IndexOf('"', i + 1);
                if (end < 0) end = excelFmt.Length - 1;
                sb.Append(excelFmt[i..(end + 1)]);
                i = end + 1;
                continue;
            }
            if (c == '\\' && i + 1 < excelFmt.Length)
            {
                sb.Append('\'');
                if (excelFmt[i + 1] == '\'')
                    sb.Append("''");
                else
                    sb.Append(excelFmt[i + 1]);
                sb.Append('\'');
                i += 2;
                continue;
            }
            // Longest-match for each token group
            if (TryConsumeFractionalSeconds(excelFmt, i, sb, out int ni) ||
                TryConsume(excelFmt, i, "AM/PM", "tt", sb, out ni) ||
                TryConsume(excelFmt, i, "am/pm", "tt", sb, out ni) ||
                TryConsume(excelFmt, i, "A/P", "t", sb, out ni) ||
                TryConsume(excelFmt, i, "a/p", "t", sb, out ni) ||
                TryConsume(excelFmt, i, "yyyy", "yyyy", sb, out ni) ||
                TryConsume(excelFmt, i, "yy",   "yy",   sb, out ni) ||
                TryConsume(excelFmt, i, "mmmm", "MMMM", sb, out ni) ||
                TryConsume(excelFmt, i, "mmm",  "MMM",  sb, out ni) ||
                TryConsumeMonthOrMinute(excelFmt, i, "mm", sb, out ni) ||
                TryConsumeMonthOrMinute(excelFmt, i, "m", sb, out ni) ||
                TryConsume(excelFmt, i, "dddd", "dddd", sb, out ni) ||
                TryConsume(excelFmt, i, "ddd",  "ddd",  sb, out ni) ||
                TryConsume(excelFmt, i, "dd",   "dd",   sb, out ni) ||
                TryConsume(excelFmt, i, "d",    "d",    sb, out ni) ||
                TryConsume(excelFmt, i, "hh",   hourToken2, sb, out ni) ||
                TryConsume(excelFmt, i, "h",    hourToken1, sb, out ni) ||
                TryConsume(excelFmt, i, "ss",   "ss",   sb, out ni) ||
                TryConsume(excelFmt, i, "s",    "s",    sb, out ni))
            {
                i = ni;
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }
        return sb.ToString();
    }

    private static bool TryConsumeFractionalSeconds(
        string src,
        int pos,
        System.Text.StringBuilder sb,
        out int newPos)
    {
        if (pos < 1 || src[pos] != '.' || !IsTimeToken(src[pos - 1]))
        {
            newPos = pos;
            return false;
        }

        int end = pos + 1;
        while (end < src.Length && src[end] == '0')
            end++;

        if (end == pos + 1)
        {
            newPos = pos;
            return false;
        }

        sb.Append('.');
        sb.Append('f', end - pos - 1);
        newPos = end;
        return true;
    }

    private static bool TryConsume(string src, int pos, string token, string replacement,
        System.Text.StringBuilder sb, out int newPos)
    {
        if (pos + token.Length <= src.Length &&
            string.Compare(src, pos, token, 0, token.Length, StringComparison.OrdinalIgnoreCase) == 0)
        {
            sb.Append(replacement);
            newPos = pos + token.Length;
            return true;
        }
        newPos = pos;
        return false;
    }

    private static bool TryConsumeMonthOrMinute(
        string src,
        int pos,
        string token,
        System.Text.StringBuilder sb,
        out int newPos)
    {
        if (pos + token.Length > src.Length ||
            string.Compare(src, pos, token, 0, token.Length, StringComparison.OrdinalIgnoreCase) != 0)
        {
            newPos = pos;
            return false;
        }

        bool isMinute = IsAdjacentToTimeToken(src, pos, token.Length);
        sb.Append(isMinute
            ? token.Length == 2 ? "mm" : "m"
            : token.Length == 2 ? "MM" : "M");
        newPos = pos + token.Length;
        return true;
    }

    private static bool IsAdjacentToTimeToken(string format, int tokenStart, int tokenLength)
    {
        int before = PreviousFormatTokenIndex(format, tokenStart - 1);
        if (before >= 0 && IsTimeToken(format[before]))
            return true;

        int after = NextFormatTokenIndex(format, tokenStart + tokenLength);
        return after >= 0 && IsTimeToken(format[after]);
    }

    private static int PreviousFormatTokenIndex(string format, int index)
    {
        for (int i = index; i >= 0; i--)
        {
            char c = format[i];
            if (char.IsWhiteSpace(c) || c is ':' or '/' or '-' or ',')
                continue;
            if (c == '"')
            {
                int open = format.LastIndexOf('"', i - 1);
                if (open >= 0)
                {
                    i = open;
                    continue;
                }
            }
            if (c == ']')
            {
                int open = format.LastIndexOf('[', i - 1);
                if (open >= 0)
                {
                    i = open;
                    continue;
                }
            }
            return i;
        }

        return -1;
    }

    private static int NextFormatTokenIndex(string format, int index)
    {
        for (int i = index; i < format.Length; i++)
        {
            char c = format[i];
            if (char.IsWhiteSpace(c) || c is ':' or '/' or '-' or ',')
                continue;
            if (c == '"')
            {
                int close = format.IndexOf('"', i + 1);
                if (close >= 0)
                {
                    i = close;
                    continue;
                }
            }
            if (c == '[')
            {
                int close = format.IndexOf(']', i + 1);
                if (close >= 0)
                {
                    i = close;
                    continue;
                }
            }
            return i;
        }

        return -1;
    }

    private static bool IsTimeToken(char c)
        => c is 'h' or 'H' or 's' or 'S';

    // ── Elapsed time format [h]:mm:ss, [m]:ss, [s] ───────────────────────────

    private static string FormatElapsedTime(double value, string format, Match elapsedMatch)
    {
        // value is an OADate fraction; each unit = 1 day = 86400 seconds.
        var fractionalMatch = Regex.Match(format, @"(?:s|\[[sS]\])\.(0+)");
        int fractionalDotIndex = fractionalMatch.Success
            ? fractionalMatch.Index + fractionalMatch.Value.IndexOf('.', StringComparison.Ordinal)
            : -1;
        int fractionalPrecision = fractionalMatch.Success ? fractionalMatch.Groups[1].Value.Length : 0;

        double totalSecondsD = Math.Abs(value) * 86400.0;
        if (fractionalPrecision > 0)
            totalSecondsD = Math.Round(totalSecondsD, fractionalPrecision, MidpointRounding.AwayFromZero);

        long totalSeconds = (long)totalSecondsD;
        long totalMinutes = totalSeconds / 60;
        long totalHours   = totalSeconds / 3600;
        int remMinutes = (int)(totalMinutes % 60);
        int remSeconds = (int)(totalSeconds % 60);
        int fractionalSecondUnits = fractionalPrecision > 0
            ? (int)Math.Round((totalSecondsD - totalSeconds) * Math.Pow(10, fractionalPrecision),
                MidpointRounding.AwayFromZero)
            : 0;

        // Which bracket is the "lead" elapsed unit?
        long leadValue;
        string leadToken;
        if (elapsedMatch.Groups[1].Success)       // [h] or [H]
        {
            leadValue = totalHours;
            leadToken = elapsedMatch.Value;        // e.g. "[h]"
        }
        else if (elapsedMatch.Groups[2].Success)  // [m] or [M]
        {
            leadValue = totalMinutes;
            leadToken = elapsedMatch.Value;
            remMinutes = (int)(totalSeconds % 60);  // remSeconds stands; remMinutes not used here
        }
        else                                      // [s] or [S]
        {
            leadValue = totalSeconds;
            leadToken = elapsedMatch.Value;
        }

        // Build output: replace the lead bracket with its numeric value,
        // then fill in mm and ss with the remainder components.
        var sb = new System.Text.StringBuilder();
        if (value < 0) sb.Append('-');
        int i = 0;
        while (i < format.Length)
        {
            // Skip the bracket token we already handled
            if (string.Compare(format, i, leadToken, 0, leadToken.Length, StringComparison.OrdinalIgnoreCase) == 0)
            {
                sb.Append(leadValue);
                i += leadToken.Length;
            }
            // Skip any other bracket content (locale, color, etc.)
            else if (format[i] == '[')
            {
                int close = format.IndexOf(']', i + 1);
                i = close >= 0 ? close + 1 : format.Length;
            }
            else if (i + 1 < format.Length &&
                     format[i] == 'm' && format[i + 1] == 'm' &&
                     elapsedMatch.Groups[1].Success) // mm after [h]
            {
                sb.Append(remMinutes.ToString("D2"));
                i += 2;
            }
            else if (i + 1 < format.Length && format[i] == 's' && format[i + 1] == 's')
            {
                sb.Append(remSeconds.ToString("D2"));
                i += 2;
            }
            else if (i == fractionalDotIndex)
            {
                sb.Append('.');
                sb.Append(fractionalSecondUnits.ToString("D" + fractionalPrecision.ToString(CultureInfo.InvariantCulture),
                    CultureInfo.InvariantCulture));
                i += fractionalPrecision + 1;
            }
            else if (format[i] == '\\' && i + 1 < format.Length)
            {
                sb.Append(format[i + 1]);
                i += 2;
            }
            else
            {
                sb.Append(format[i++]);
            }
        }
        return sb.ToString();
    }

    // ── Text section ──────────────────────────────────────────────────────────
}
