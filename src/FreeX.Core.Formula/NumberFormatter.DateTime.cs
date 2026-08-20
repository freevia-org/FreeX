using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class NumberFormatter
{
    private static readonly Regex SpecialDateTimeLocaleTokenRegex = new(
        @"^\s*\[\$-F(?<kind>400|800)\](?<suffix>.*)$",
        RegexOptions.IgnoreCase);
    private static readonly Regex FractionalSecondPrecisionRegex = new(@"(?<=[sS])\.(0+)");
    private static readonly Regex QuotedNumberFormatTextRegex = new("\"[^\"]*\"");
    private static readonly Regex ElapsedTimeFractionalSecondRegex = new(@"(?:s|\[[sS]\])\.(0+)");

    private const int SimpleDateTimeFormatCacheSize = 1024;
    private static readonly SimpleDateTimeFormatCacheEntry?[] SimpleDateTimeFormatCache =
        new SimpleDateTimeFormatCacheEntry[SimpleDateTimeFormatCacheSize];

    private readonly record struct SimpleDateTimeFormatPlan(string NetFormat, int FractionalSecondPrecision, bool HasSecond);

    private sealed record SimpleDateTimeFormatCacheEntry(string ExcelFormat, SimpleDateTimeFormatPlan Plan);

    private enum SpecialDateTimeLocaleToken
    {
        LongTime,
        LongDate
    }

    private static FormatResult FormatDateTimeWithColor(
        double oaDate,
        string[] sections,
        int? targetWidthCharacters,
        WorkbookIndexedColorPalette? indexedColors,
        WorkbookTheme? theme,
        bool uses1904DateSystem = false)
    {
        var (parsed, displayValue) = SelectDateTimeSection(oaDate, sections, indexedColors, theme);
        if (parsed is null)
            return new FormatResult(FormatGeneralDateTime(oaDate, uses1904DateSystem));

        if (parsed.Format == "")
            return new FormatResult("", parsed.ColorHex);

        var text = FormatDateTime(displayValue, parsed.Format, uses1904DateSystem);
        text = ApplyAccountingTargetWidth(text, parsed.Format, targetWidthCharacters);
        return new FormatResult(text, parsed.ColorHex);
    }

    private static (ParsedSection? Section, double DisplayValue) SelectDateTimeSection(
        double value,
        string[] sections,
        WorkbookIndexedColorPalette? indexedColors,
        WorkbookTheme? theme)
    {
        var parsedSections = ParseSections(sections, indexedColors, theme, out var hasConditions);
        // No explicit [condition] sections: fall back to Excel's positional pos/neg/zero rule
        // (same as the numeric path's SelectPositionalSection) instead of always using section 0
        // -- a date/time-typed value can carry a multi-section custom format too (e.g.
        // "h:mm:ss;;\"midnight\"" or "m/d/yyyy;\"neg date\"") and must pick its section by sign
        // just like a plain number does.
        if (!hasConditions)
            return SelectPositionalSection(value, parsedSections);

        for (var i = 0; i < parsedSections.Length; i++)
        {
            var section = parsedSections[i];
            if (section.Condition is not null && section.Condition.Matches(value))
                return (section, value);
        }

        for (var i = 0; i < parsedSections.Length; i++)
        {
            var section = parsedSections[i];
            if (section.Condition is null)
                return (section, value);
        }

        // Every section carries an explicit [condition] and none matched: Excel falls back to
        // General format (uncolored), not the first conditioned section's pattern/color --
        // matching the numeric path's identical fallback in FormatNumber.
        return (null, value);
    }

    private static string FormatDateTime(double oaDate, string format, bool uses1904DateSystem = false)
    {
        string NativeDigits(string text) => ApplyNativeDigitSubstitution(text, format);

        var (_, cleanFmt) = NumberFormatColorMapper.ExtractColor(format);
        if (TryResolveSpecialDateTimeLocaleToken(cleanFmt, out var specialDateTimeToken))
        {
            try
            {
                // Use DateTime.FromOADate (not ExcelDateSystem.SerialToDate) so the
                // roundtrip DateTime→ToOADate→FromOADate is lossless for modern dates.
                // The 1900 phantom-leap-day correction only matters for regular date formats;
                // under the 1904 date system the epoch itself differs, so route through
                // ExcelDateSystem.SerialToDate there instead.
                var specialDt = uses1904DateSystem
                    ? ExcelDateSystem.SerialToDate(oaDate, uses1904DateSystem)
                    : DateTime.FromOADate(oaDate);
                return NativeDigits(FormatSpecialDateTimeLocaleValue(specialDt, specialDateTimeToken));
            }
            catch { return oaDate.ToString(CultureInfo.InvariantCulture); }
        }
        cleanFmt = PreserveLocaleCurrencyTokens(cleanFmt, out _, out var dateTimeFormat);

        var directiveFormat = PreprocessBracketFormatDirectives(cleanFmt);
        if (directiveFormat.ElapsedTimeMatch.Success)
        {
            return NativeDigits(FormatElapsedTime(
                oaDate,
                directiveFormat.Format,
                directiveFormat.ElapsedTimeMatch));
        }

        cleanFmt = RemoveSpacingAndFillDirectives(directiveFormat.Format);
        try
        {
            var dt = ExcelDateSystem.SerialToDate(oaDate, uses1904DateSystem);
            if (IsDateTimeFormat(cleanFmt))
            {
                // Excel's phantom 1900-02-29 (serial 60) collides with serial 59 on the same
                // DateTime (see ExcelDateSystem.SerialToDate) -- month/year/weekday still come
                // out right, only the day-of-month digits need correcting. This is the multi-
                // section/bracket-leading-format path into date display (reached from
                // FormatDateTimeWithColor's SelectDateTimeSection above); the single-section fast
                // path applies the identical correction in NumberFormatter.cs's
                // TryFormatSimpleDateTime before it ever reaches here.
                var effectiveCleanFmt = !uses1904DateSystem && ExcelDateSystem.IsPhantomLeapDaySerial(oaDate)
                    ? OverridePhantomLeapDayOfMonthTokens(cleanFmt)
                    : cleanFmt;
                return NativeDigits(FormatDateTimeValue(dt, effectiveCleanFmt, dateTimeFormat));
            }
            return NativeDigits(dt.ToString(cleanFmt, dateTimeFormat));
        }
        catch { return oaDate.ToString(CultureInfo.InvariantCulture); }
    }

    private static bool TryFormatCachedSimpleDateTime(
        double oaDate,
        string format,
        bool uses1904DateSystem,
        out string text)
    {
        if (!TryGetSimpleDateTimeFormatPlan(format, out var plan))
        {
            text = "";
            return false;
        }

        try
        {
            var dt = ExcelDateSystem.SerialToDate(oaDate, uses1904DateSystem);
            if (plan.FractionalSecondPrecision > 0)
                dt = RoundToFractionalSecondPrecision(dt, plan.FractionalSecondPrecision);
            else if (plan.HasSecond)
                dt = RoundToNearestSecond(dt);

            text = dt.ToString(plan.NetFormat, CultureInfo.InvariantCulture.DateTimeFormat);
            return true;
        }
        catch
        {
            text = oaDate.ToString(CultureInfo.InvariantCulture);
            return true;
        }
    }

    private static bool TryGetSimpleDateTimeFormatPlan(
        string excelFormat,
        out SimpleDateTimeFormatPlan plan)
    {
        if (!CanCacheSimpleDateTimeFormat(excelFormat))
        {
            plan = default;
            return false;
        }

        var slot = StringComparer.Ordinal.GetHashCode(excelFormat) & (SimpleDateTimeFormatCacheSize - 1);
        var cached = Volatile.Read(ref SimpleDateTimeFormatCache[slot]);
        if (cached is not null &&
            string.Equals(cached.ExcelFormat, excelFormat, StringComparison.Ordinal))
        {
            plan = cached.Plan;
            return true;
        }

        var cleanFormat = RemoveSpacingAndFillDirectives(excelFormat);
        TryGetFractionalSecondPrecision(cleanFormat, out var fractionalSecondPrecision);
        plan = new SimpleDateTimeFormatPlan(
            ExcelDateTimeFormatConverter.ToNetDateFormat(cleanFormat),
            fractionalSecondPrecision,
            HasTimeToken(cleanFormat));
        Volatile.Write(ref SimpleDateTimeFormatCache[slot], new SimpleDateTimeFormatCacheEntry(excelFormat, plan));
        return true;
    }

    private static bool CanCacheSimpleDateTimeFormat(string format)
        => format.IndexOf('[') < 0 &&
            !format.Contains("mmmmm", StringComparison.OrdinalIgnoreCase);

    private static bool ShouldAttemptSimpleDateTimeFormat(string formatString)
    {
        if (formatString.IndexOf(';') >= 0 ||
            (formatString.Length > 0 && formatString[0] == '['))
        {
            return ShouldFormatDateTimeValue(SplitSections(formatString));
        }

        return HasUnquotedDateTimeTokenWithoutNumericPlaceholder(formatString);
    }

    private static bool ShouldFormatDateTimeValue(string[] sections)
    {
        for (var i = 0; i < sections.Length; i++)
        {
            var (_, format) = NumberFormatColorMapper.ExtractColor(sections[i]);
            if (TryResolveSpecialDateTimeLocaleToken(format, out _))
                return true;

            format = PreserveLocaleCurrencyTokens(format, out _, out _);
            var directiveFormat = PreprocessBracketFormatDirectives(format);
            if (directiveFormat.ElapsedTimeMatch.Success)
                return true;

            format = RemoveSpacingAndFillDirectives(directiveFormat.Format);
            if (IsDateTimeFormat(format))
                return true;
        }

        return false;
    }

    private static bool HasUnquotedDateTimeTokenWithoutNumericPlaceholder(string format)
    {
        var hasDateTimeToken = false;
        var inQuote = false;

        for (var i = 0; i < format.Length; i++)
        {
            var ch = format[i];
            if (ch == '\\')
            {
                i++;
                continue;
            }

            if (ch == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (inQuote)
                continue;

            switch (ch)
            {
                case '0':
                case '#':
                    return false;
                case 'y':
                case 'Y':
                case 'd':
                case 'D':
                case 'h':
                case 'H':
                case 's':
                case 'S':
                case 'm':
                case 'M':
                    hasDateTimeToken = true;
                    break;
            }
        }

        return hasDateTimeToken;
    }

    private static string FormatDateTimeValue(
        DateTime dateTime,
        string excelFormat,
        DateTimeFormatInfo dateTimeFormat)
    {
        if (TryGetFractionalSecondPrecision(excelFormat, out int precision))
        {
            dateTime = RoundToFractionalSecondPrecision(dateTime, precision);
        }
        else if (HasTimeToken(excelFormat))
        {
            // No fractional-second component in the format — Excel rounds to the nearest
            // second at display time (e.g. 0:28.8 → "0:29" not "0:28", and near a minute
            // boundary 44999.7s → "12:30" not "12:29"). Match that.
            dateTime = RoundToNearestSecond(dateTime);
        }

        var preparedFormat = ExcelDateTimeFormatConverter.PrepareFormat(excelFormat, dateTime, dateTimeFormat);
        return dateTime.ToString(ExcelDateTimeFormatConverter.ToNetDateFormat(preparedFormat), dateTimeFormat);
    }

    private static bool HasSecond(string format)
    {
        bool inQuote = false;
        for (int i = 0; i < format.Length; i++)
        {
            char c = format[i];
            if (c == '"') { inQuote = !inQuote; continue; }
            if (c == '\\') { i++; continue; }
            if (!inQuote && (c == 's' || c == 'S'))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true when the format string contains any time token (h, m, s) outside of
    /// quotes or escapes. Used to determine whether to round to the nearest second before
    /// formatting, matching Excel's display-level rounding behaviour.
    /// </summary>
    private static bool HasTimeToken(string format)
    {
        bool inQuote = false;
        for (int i = 0; i < format.Length; i++)
        {
            char c = format[i];
            if (c == '"') { inQuote = !inQuote; continue; }
            if (c == '\\') { i++; continue; }
            if (!inQuote && (c is 'h' or 'H' or 'm' or 'M' or 's' or 'S'))
                return true;
        }

        return false;
    }

    private static DateTime RoundToNearestSecond(DateTime dt)
    {
        // Round to nearest second (500ms threshold), matching Excel's display behaviour.
        long ticks = dt.Ticks;
        long halfSecondTicks = TimeSpan.TicksPerSecond / 2;
        long secondTicks = TimeSpan.TicksPerSecond;
        long remainder = ticks % secondTicks;
        long rounded = remainder >= halfSecondTicks
            ? ticks + (secondTicks - remainder)
            : ticks - remainder;
        return new DateTime(rounded, dt.Kind);
    }

    private static string FormatSpecialDateTimeLocaleValue(
        DateTime dateTime,
        SpecialDateTimeLocaleToken token)
    {
        var dateTimeFormat = CultureInfo.CurrentCulture.DateTimeFormat;
        var netFormat = token == SpecialDateTimeLocaleToken.LongDate
            ? dateTimeFormat.LongDatePattern
            : dateTimeFormat.LongTimePattern;
        return dateTime.ToString(netFormat, dateTimeFormat);
    }

    private static bool TryResolveSpecialDateTimeLocaleToken(string format, out SpecialDateTimeLocaleToken token)
    {
        if (format.IndexOf('[') < 0)
        {
            token = SpecialDateTimeLocaleToken.LongDate;
            return false;
        }

        var match = SpecialDateTimeLocaleTokenRegex.Match(format);
        if (!match.Success)
        {
            token = SpecialDateTimeLocaleToken.LongDate;
            return false;
        }

        var suffix = match.Groups["suffix"].Value.Trim();
        if (suffix.Length > 0 && !IsDateTimeFormat(suffix))
        {
            token = SpecialDateTimeLocaleToken.LongDate;
            return false;
        }

        token = string.Equals(match.Groups["kind"].Value, "800", StringComparison.OrdinalIgnoreCase)
            ? SpecialDateTimeLocaleToken.LongDate
            : SpecialDateTimeLocaleToken.LongTime;
        return true;
    }

    private static bool TryGetFractionalSecondPrecision(string format, out int precision)
    {
        var match = FractionalSecondPrecisionRegex.Match(format);
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

    /// <summary>
    /// Public entry point for callers outside this partial class (e.g. AutoFilter's dropdown/
    /// checklist planners in FreeX.App.Presentation) that need to know whether a cell's number
    /// format string is date/time-like -- mirroring Excel, which has no separate "date value"
    /// runtime type distinct from a formatted double: a cell is a date purely because its number
    /// format displays it as one. See R103-app-presentation-autofilter-1-1.
    /// </summary>
    public static bool IsDateTimeNumberFormat(string format) => IsDateTimeFormat(format);

    // Detect date/time format: has date/time tokens and no digit-only tokens
    private static bool IsDateTimeFormat(string format)
    {
        if (format.IndexOfAny(['y', 'Y', 'd', 'D', 'h', 'H', 's', 'S', 'm', 'M']) < 0)
            return false;

        // Strip quoted strings before checking
        var stripped = QuotedNumberFormatTextRegex.Replace(format, "");
        stripped = FractionalSecondPrecisionRegex.Replace(stripped, "");
        bool hasDateToken = stripped.IndexOfAny(['y', 'Y', 'd', 'D', 'h', 'H', 's', 'S', 'm', 'M']) >= 0;
        bool hasNumberToken = stripped.IndexOfAny(['0', '#']) >= 0;
        return hasDateToken && !hasNumberToken;
    }

    // ── Elapsed time format [h]:mm:ss, [m]:ss, [s] ───────────────────────────

    private static string FormatElapsedTime(double value, string format, Match elapsedMatch)
    {
        // value is an OADate fraction; each unit = 1 day = 86400 seconds.
        var fractionalMatch = ElapsedTimeFractionalSecondRegex.Match(format);
        int fractionalDotIndex = fractionalMatch.Success
            ? fractionalMatch.Index + fractionalMatch.Value.IndexOf('.', StringComparison.Ordinal)
            : -1;
        int fractionalPrecision = fractionalMatch.Success ? fractionalMatch.Groups[1].Value.Length : 0;

        double totalSecondsD = Math.Abs(value) * 86400.0;
        if (fractionalPrecision > 0)
            totalSecondsD = Math.Round(totalSecondsD, fractionalPrecision, MidpointRounding.AwayFromZero);
        else
            totalSecondsD = Math.Round(totalSecondsD, 0, MidpointRounding.AwayFromZero);

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
        int leadWidth; // count of the repeated bracket letter, e.g. "hh" in "[hh]" -> 2
        if (elapsedMatch.Groups[1].Success)       // [h] or [H]
        {
            leadValue = totalHours;
            leadToken = elapsedMatch.Value;        // e.g. "[h]"
            leadWidth = elapsedMatch.Groups[1].Length;
        }
        else if (elapsedMatch.Groups[2].Success)  // [m] or [M]
        {
            leadValue = totalMinutes;
            leadToken = elapsedMatch.Value;
            leadWidth = elapsedMatch.Groups[2].Length;
            remMinutes = (int)(totalSeconds % 60);  // remSeconds stands; remMinutes not used here
        }
        else                                      // [s] or [S]
        {
            leadValue = totalSeconds;
            leadToken = elapsedMatch.Value;
            leadWidth = elapsedMatch.Groups[3].Length;
        }

        // Build output: replace the lead bracket with its numeric value,
        // then fill in mm and ss with the remainder components.
        // Quote-aware (mirrors the inQuote scanning convention used throughout this formatter,
        // e.g. FindUnquotedElapsedTimeToken/RemoveUnquotedBracketDirectives): text inside "..."
        // literals is copied verbatim (quote marks dropped, matching Excel's rendering) and never
        // treated as a token, so a literal such as "mm" or "ss" is not mistaken for a substitution.
        var sb = new System.Text.StringBuilder();
        // Excel never shows a sign on a displayed zero: a tiny negative like -0.0000005 rounds
        // (via totalSecondsD above, which already applied the format's requested precision) to
        // an all-zero elapsed time, so the leading '-' must be suppressed in that case -- mirrors
        // the negative-zero guards elsewhere in NumberFormatter (IsNegativeZeroRepresentation /
        // IsAllZeroText). A genuine non-zero negative elapsed value still shows its '-'.
        if (value < 0 && totalSecondsD != 0) sb.Append('-');
        int i = 0;
        bool inQuote = false;
        while (i < format.Length)
        {
            if (format[i] == '"')
            {
                inQuote = !inQuote;
                i++;
            }
            else if (inQuote)
            {
                sb.Append(format[i++]);
            }
            // Skip the bracket token we already handled
            else if (string.Compare(format, i, leadToken, 0, leadToken.Length, StringComparison.OrdinalIgnoreCase) == 0)
            {
                // A doubled (or longer) bracket letter, e.g. "[hh]", zero-pads the lead unit
                // to that width; a single letter, e.g. "[h]", is left unpadded.
                sb.Append(leadValue.ToString(
                    "D" + leadWidth.ToString(CultureInfo.InvariantCulture),
                    CultureInfo.InvariantCulture));
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
            else if (format[i] == 'm' && elapsedMatch.Groups[1].Success) // single m after [h]
            {
                sb.Append(remMinutes);
                i += 1;
            }
            else if (format[i] == 's') // single s (remainder seconds after [h] or [m] lead)
            {
                sb.Append(remSeconds);
                i += 1;
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
