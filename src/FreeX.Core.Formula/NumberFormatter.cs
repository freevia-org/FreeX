using System.Globalization;
using System.Text.RegularExpressions;
using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class NumberFormatter
{
    private static readonly Regex NumericQuotedTextRegex = new("\"[^\"]*\"");

    // Returned alongside display text so the grid can apply conditional colors.
    public readonly record struct FormatResult(string Text, string? ColorHex = null);

    public static string Format(ScalarValue value, string formatString)
        => FormatWithColor(value, formatString).Text;

    public static string Format(ScalarValue value, string formatString, int targetWidthCharacters)
        => FormatWithColor(value, formatString, targetWidthCharacters).Text;

    public static string Format(ScalarValue value, string formatString, bool uses1904DateSystem)
        => FormatWithColor(value, formatString, (int?)null, null, null, uses1904DateSystem).Text;

    public static FormatResult FormatWithColor(ScalarValue value, string formatString)
        => FormatWithColor(value, formatString, (int?)null);

    public static FormatResult FormatWithColor(ScalarValue value, string formatString, int targetWidthCharacters)
        => FormatWithColor(value, formatString, (int?)targetWidthCharacters);

    public static FormatResult FormatWithColor(ScalarValue value, string formatString, bool uses1904DateSystem)
        => FormatWithColor(value, formatString, (int?)null, null, null, uses1904DateSystem);

    public static FormatResult FormatWithColor(
        ScalarValue value,
        string formatString,
        WorkbookIndexedColorPalette indexedColors)
        => FormatWithColor(value, formatString, (int?)null, indexedColors, null);

    public static FormatResult FormatWithColor(
        ScalarValue value,
        string formatString,
        int targetWidthCharacters,
        WorkbookIndexedColorPalette indexedColors)
        => FormatWithColor(value, formatString, (int?)targetWidthCharacters, indexedColors, null);

    public static FormatResult FormatWithColor(
        ScalarValue value,
        string formatString,
        WorkbookIndexedColorPalette indexedColors,
        WorkbookTheme theme)
        => FormatWithColor(value, formatString, (int?)null, indexedColors, theme);

    public static FormatResult FormatWithColor(
        ScalarValue value,
        string formatString,
        WorkbookIndexedColorPalette indexedColors,
        WorkbookTheme theme,
        bool uses1904DateSystem)
        => FormatWithColor(value, formatString, (int?)null, indexedColors, theme, uses1904DateSystem);

    public static FormatResult FormatWithColor(
        ScalarValue value,
        string formatString,
        int targetWidthCharacters,
        WorkbookIndexedColorPalette indexedColors,
        WorkbookTheme theme)
        => FormatWithColor(value, formatString, (int?)targetWidthCharacters, indexedColors, theme);

    public static FormatResult FormatWithColor(
        ScalarValue value,
        string formatString,
        int targetWidthCharacters,
        WorkbookIndexedColorPalette indexedColors,
        WorkbookTheme theme,
        bool uses1904DateSystem)
        => FormatWithColor(value, formatString, (int?)targetWidthCharacters, indexedColors, theme, uses1904DateSystem);

    /// <summary>
    /// Same as the other <paramref name="targetWidthCharacters"/>-aware overload, but lets the
    /// caller suppress Excel's width-based '#' overflow indicator entirely -- used for cells whose
    /// style has ShrinkToFit, where the real value must shrink to fit the column instead of being
    /// replaced by hashes (the '#' indicator is only ever correct for the non-shrink, clip/overflow
    /// rendering path).
    /// </summary>
    public static FormatResult FormatWithColor(
        ScalarValue value,
        string formatString,
        int targetWidthCharacters,
        WorkbookIndexedColorPalette indexedColors,
        WorkbookTheme theme,
        bool uses1904DateSystem,
        bool suppressWidthOverflowIndicator)
        => FormatWithColor(value, formatString, (int?)targetWidthCharacters, indexedColors, theme, uses1904DateSystem, suppressWidthOverflowIndicator);

    private static FormatResult FormatWithColor(
        ScalarValue value,
        string formatString,
        int? targetWidthCharacters,
        WorkbookIndexedColorPalette? indexedColors = null,
        WorkbookTheme? theme = null,
        bool uses1904DateSystem = false,
        bool suppressWidthOverflowIndicator = false)
    {
        if (string.IsNullOrEmpty(formatString) || IsGeneralFormat(formatString))
            return new FormatResult(FormatGeneral(value, uses1904DateSystem, targetWidthCharacters));

        // Pure text format
        if (formatString == "@")
        {
            return value switch
            {
                TextValue t   => new FormatResult(t.Value),
                // A numeric value under Text format is NOT converted to a string -- it still
                // renders via General numeric formatting, including the column-width-aware
                // '#####' overflow indicator (matching the General-format dispatch above).
                NumberValue n => new FormatResult(FormatGeneral(value, uses1904DateSystem, targetWidthCharacters)),
                _             => new FormatResult(FormatGeneral(value, uses1904DateSystem, targetWidthCharacters))
            };
        }

        if (value is DateTimeValue dateTimeValue &&
            ShouldAttemptSimpleDateTimeFormat(formatString) &&
            TryFormatSimpleDateTime(dateTimeValue.Value, formatString, targetWidthCharacters, uses1904DateSystem, out var simpleDateTime))
        {
            return suppressWidthOverflowIndicator
                ? simpleDateTime
                : ApplyWidthOverflowIndicator(simpleDateTime, targetWidthCharacters);
        }

        var sections = SplitSections(formatString);

        var formatted = value switch
        {
            NumberValue n   => FormatNumber(n.Value, sections, targetWidthCharacters, indexedColors, theme, uses1904DateSystem),
            DateTimeValue d => ShouldFormatDateTimeValue(sections)
                ? FormatDateTimeWithColor(d.Value, sections, targetWidthCharacters, indexedColors, theme, uses1904DateSystem)
                : FormatNumber(d.Value, sections, targetWidthCharacters, indexedColors, theme, uses1904DateSystem),
            TextValue t     => FormatTextWithColor(t.Value, sections, indexedColors, theme),
            BoolValue b     => new FormatResult(b.Value ? "TRUE" : "FALSE"),
            ErrorValue e    => new FormatResult(e.Code),
            BlankValue      => new FormatResult(""),
            _               => new FormatResult("")
        };

        // Excel shows a width-based '#' overflow indicator for ANY explicit (non-General)
        // number/date/currency format whose formatted text is wider than the target column --
        // not just the specific invalid-value/accounting-fill cases already handled inside
        // FormatNumber/FormatDateTimeWithColor. Scope this strictly to numeric/date-time values
        // (never text/bool/error/blank, which always overflow into neighboring cells instead).
        return value is NumberValue or DateTimeValue && !suppressWidthOverflowIndicator
            ? ApplyWidthOverflowIndicator(formatted, targetWidthCharacters)
            : formatted;
    }

    /// <summary>
    /// Replaces an over-wide formatted numeric/date result with Excel's "value doesn't fit"
    /// indicator (a run of '#' characters sized to the target column) once the formatted text
    /// exceeds the available character budget. A value that already fits is returned unchanged.
    /// With no column-width context (targetWidthCharacters is null -- e.g. TEXT()/formula-bar
    /// evaluation with no target cell), the result is never truncated to '#'.
    /// </summary>
    private static FormatResult ApplyWidthOverflowIndicator(FormatResult result, int? targetWidthCharacters)
    {
        if (targetWidthCharacters is not > 0 || result.Text.Length <= targetWidthCharacters.Value)
            return result;

        return new FormatResult(new string('#', targetWidthCharacters.Value), result.ColorHex);
    }

    // ── General format ────────────────────────────────────────────────────────

    // ── Section splitting ─────────────────────────────────────────────────────

    // ── Number formatting ─────────────────────────────────────────────────────

    private static bool TryFormatSimpleDateTime(
        double oaDate,
        string formatString,
        int? targetWidthCharacters,
        bool uses1904DateSystem,
        out FormatResult result)
    {
        if (formatString.IndexOf(';') >= 0 ||
            (formatString.Length > 0 && formatString[0] == '['))
        {
            result = new FormatResult("");
            return false;
        }

        // Excel treats a negative value as fundamentally invalid for a calendar date/time
        // format (widening the column never reveals a real date underneath) -- reaching here
        // means ShouldAttemptSimpleDateTimeFormat already confirmed formatString has date/time
        // tokens and no numeric placeholder, so a negative oaDate must show the invalid-value
        // indicator instead of silently formatting a bogus (or sign-dropped) calendar date.
        if (oaDate < 0)
        {
            result = new FormatResult(BuildInvalidDateTimeIndicator(formatString, targetWidthCharacters));
            return true;
        }

        var text = TryFormatCachedSimpleDateTime(oaDate, formatString, uses1904DateSystem, out var cachedText)
            ? cachedText
            : FormatDateTime(oaDate, formatString, uses1904DateSystem);
        text = ApplyAccountingTargetWidth(text, formatString, targetWidthCharacters);
        result = new FormatResult(text);
        return true;
    }

    /// <summary>
    /// Builds Excel's "value doesn't fit"/invalid-value indicator (a run of '#' characters) for
    /// a value that is fundamentally invalid for the given format -- currently used for negative
    /// values formatted with a calendar date/time format. This is not a column-width artifact:
    /// Excel shows this regardless of how wide the column is, because the value itself (e.g. a
    /// negative serial date) cannot be represented by the format, not because the result is too
    /// long to display. <paramref name="targetWidthCharacters"/> is honored when the caller has
    /// real column-width context; otherwise the format's own length is used as a reasonable
    /// stand-in so the result is still visibly "all hashes" rather than a single character.
    /// </summary>
    private static string BuildInvalidDateTimeIndicator(string format, int? targetWidthCharacters)
    {
        var width = targetWidthCharacters is > 0 ? targetWidthCharacters.Value : format.Length;
        return new string('#', Math.Max(width, 1));
    }

    private static FormatResult FormatNumber(
        double value,
        string[] sections,
        int? targetWidthCharacters,
        WorkbookIndexedColorPalette? indexedColors,
        WorkbookTheme? theme,
        bool uses1904DateSystem = false)
    {
        // A single-section format (no ';') that starts with a bracket token is still "one
        // section" for Excel's sign-placement purposes when that bracket is a modifier --
        // locale/currency ([$-409], [$$-409]), color ([Red]/[Color12]), theme, or a DBNum/
        // NatNum directive (see ParseSection) -- not a second section to select between. Only
        // two kinds of leading bracket actually change how the section is selected/measured
        // and must keep going through the ParseSections/hasConditions path below unchanged:
        // an elapsed-time lead token ([h], [hh], [m], [mm], [s], [ss] -- measured via
        // Math.Abs inside FormatElapsedTime, which already drops the sign on its own) and an
        // explicit [condition] (which, with only one section, never has an unconditioned
        // section to fall back to -- see the hasConditions branch's General-format fallback).
        ParsedSection? bracketModifierSection = null;
        if (sections.Length == 1 && sections[0].Length > 0 && sections[0][0] == '[' &&
            !FindUnquotedElapsedTimeToken(sections[0]).Success)
        {
            var probe = ParseSection(sections[0], indexedColors, theme);
            if (probe.Condition is null)
                bracketModifierSection = probe;
        }

        if (sections.Length == 1 &&
            (sections[0].Length == 0 || sections[0][0] != '[' || bracketModifierSection is not null))
        {
            var effectiveFormat = bracketModifierSection?.Format ?? sections[0];
            var colorHex = bracketModifierSection?.ColorHex;

            // Excel treats a negative value as fundamentally invalid for a date/time-only format
            // (see BuildInvalidDateTimeIndicator) -- check this before splitting sign/magnitude
            // below, since formatting the (always-positive) magnitude as a date would otherwise
            // fabricate a plausible-looking but bogus calendar date/time with no hint that the
            // underlying value is negative. A retained "[$..." locale/currency token (see
            // ParseSection's IsRetainedSectionDirective) keeps its literal digits (e.g. the
            // "409" in "[$-409]") in effectiveFormat, which would otherwise trip
            // IsDateTimeFormat's "no digit-only tokens" heuristic into a false negative -- run
            // the same locale-token cleanup the generic numeric-format code path further below
            // applies before checking/reporting, so both the detection and the fallback '#'-run
            // width match what that generic date/time branch already does.
            var dateTimeCheckFormat = effectiveFormat.Contains("[$", StringComparison.Ordinal)
                ? PreserveLocaleCurrencyTokens(effectiveFormat, out _, out _)
                : effectiveFormat;
            if (value < 0 && dateTimeCheckFormat.Length > 0 && IsDateTimeFormat(dateTimeCheckFormat))
                return new FormatResult(BuildInvalidDateTimeIndicator(dateTimeCheckFormat, targetWidthCharacters), colorHex);

            // A simple fraction format (e.g. "?/2", "# ?/?") implements Excel's negative-rounds-
            // to-all-zero sign suppression internally in FormatSimpleFraction, keyed off whether
            // the rounded NUMERATOR (and whole part) come out to zero -- not off whether the
            // rendered text's digits are all '0'. A fixed literal denominator (e.g. the "2" in
            // "?/2") always renders a non-zero digit even when the fraction itself has rounded
            // away to nothing, so the generic magnitude+IsAllZeroText scheme below (built for
            // plain numeric sections, where every digit in the output really does reflect the
            // value) misfires and leaves a stray "-" in front (R74 vs R79 regression: "-0/2"
            // instead of "0/2"). Route fraction formats through FormatSimpleFraction with the
            // ORIGINAL signed value instead, so its own (already correct/tested) sign-suppression
            // logic runs -- matching what the slower ParseSections/SelectPositionalSection path
            // (still used for multi-section formats) already does for FormatSimpleFraction.
            // Strip "_x"/"*x" spacing/fill directives the same way the multi-section path does
            // (ApplyNumericFormat's PreserveAccountingFillSpace + RemoveSpacingAndFillDirectives
            // call, further below) before handing the format to FormatSimpleFraction -- otherwise
            // FormatSimpleFraction's ExtractNumericAffixes treats '_'/'*' and the character they
            // reserve/repeat as ordinary literal text and renders them visibly (e.g. "_(2 1/2_)"
            // instead of the invisible padding Excel shows).
            var fractionCheckFormat = RemoveSpacingAndFillDirectives(PreserveAccountingFillSpace(effectiveFormat));
            if (IsSimpleFractionFormat(fractionCheckFormat))
            {
                var fractionText = ApplyNativeDigitSubstitution(FormatSimpleFraction(value, fractionCheckFormat), effectiveFormat);
                fractionText = ApplyAccountingTargetWidth(fractionText, effectiveFormat, targetWidthCharacters);
                return new FormatResult(fractionText, colorHex);
            }

            // A single-section format applies to negatives by formatting the MAGNITUDE and prepending a
            // leading minus to the whole result (so "-" sits before any prefix: -¥12.30, not ¥-12.30).
            // For prefix-free formats this is identical to the inline minus, so plain formats are unaffected.
            var sign = "";
            var magnitude = value;
            if (value < 0)
            {
                sign = "-";
                magnitude = -value;
            }
            else if (magnitude == 0)
            {
                // IEEE-754 negative zero (-0.0) fails `value < 0`, so it falls through this
                // branch still carrying its sign bit. TryFormatPlainNumericSection below (and
                // the "long" fast path in FormatNumberGeneral) format magnitude directly via
                // .NET's own ToString, which -- unlike a cast to long -- preserves that sign
                // bit as a literal "-". Normalize it away here so Excel's "never show negative
                // zero" rule holds for the single most common numeric formats (e.g. "0.00").
                magnitude = 0.0;
            }

            var singleSectionText = effectiveFormat == ""
                ? ""
                : TryFormatPlainNumericSection(magnitude, effectiveFormat, out var plainNumericText)
                    ? plainNumericText
                    : ApplyNumericFormat(magnitude, effectiveFormat, uses1904DateSystem: uses1904DateSystem, targetWidthCharacters: targetWidthCharacters);
            singleSectionText = ApplyAccountingTargetWidth(singleSectionText, effectiveFormat, targetWidthCharacters);
            // Excel never displays negative zero: if sign is "-" but the formatted text
            // is all zeros (after magnitude formatting), drop the sign.
            if (sign == "-" && IsAllZeroText(singleSectionText))
                sign = "";
            return new FormatResult(sign + singleSectionText, colorHex);
        }

        var parsedSections = ParseSections(sections, indexedColors, theme, out var hasConditions);

        ParsedSection section;
        double displayValue = value;

        if (hasConditions)
        {
            var selectedIndex = FindParsedSectionIndex(
                parsedSections,
                section => section.Condition is not null && section.Condition.Matches(value));
            if (selectedIndex < 0)
            {
                selectedIndex = FindParsedSectionIndex(parsedSections, section => section.Condition is null);
                if (selectedIndex < 0)
                {
                    // Every section in this format carries an explicit [condition] and none of
                    // them matched the value -- Excel has no section left to fall back to, so it
                    // renders the value with the plain General format (and no color), not the
                    // first conditioned section's pattern/text/color unconditionally.
                    return new FormatResult(FormatNumberGeneral(value, targetWidthCharacters));
                }
            }

            section = parsedSections[selectedIndex];
        }
        else
        {
            (section, displayValue) = SelectPositionalSection(value, parsedSections);
        }

        string text = section.Format == ""
            ? ""
            : ApplyNumericFormat(displayValue, section.Format, uses1904DateSystem: uses1904DateSystem, targetWidthCharacters: targetWidthCharacters);
        text = ApplyAccountingTargetWidth(text, section.Format, targetWidthCharacters);
        return new FormatResult(text, section.ColorHex);
    }

    private static int FindParsedSectionIndex(
        IReadOnlyList<ParsedSection> sections,
        Func<ParsedSection, bool> predicate)
    {
        for (var index = 0; index < sections.Count; index++)
        {
            if (predicate(sections[index]))
                return index;
        }

        return -1;
    }

    private static bool TryFormatPlainNumericSection(double value, string format, out string text)
    {
        text = "";
        // A pattern with irregular (e.g. Indian lakh/crore) comma grouping must go through
        // ApplyNumericFormat, which knows how to apply the pattern-derived NumberGroupSizes --
        // this fast path always formats against the invariant culture's default (uniform
        // 3-digit) grouping, which would silently discard the irregular layout.
        if (!IsPlainNumericSection(format) || TryDeriveIrregularGroupSizes(format, out _))
            return false;

        try
        {
            text = value.ToString(format, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPlainNumericSection(string format)
    {
        var hasPlaceholder = false;
        var lastToken = '\0';
        for (var i = 0; i < format.Length; i++)
        {
            var c = format[i];
            switch (c)
            {
                case '0':
                case '#':
                    hasPlaceholder = true;
                    lastToken = c;
                    break;
                case '.':
                case ',':
                    lastToken = c;
                    break;
                default:
                    return false;
            }
        }

        return hasPlaceholder && lastToken != ',';
    }

    /// <summary>
    /// Derives Excel's irregular (e.g. Indian lakh/crore 3-2-2) thousands-grouping sizes purely
    /// from the literal comma positions in a custom numeric pattern's integer part -- matching
    /// Excel's own behaviour, where a format code like "#,##,##0" groups 3-2-2 from the decimal
    /// point outward with no [$-locale] token involved at all. .NET's custom-format engine does
    /// not derive grouping from the literal segment widths on its own; it only groups digits by
    /// <see cref="NumberFormatInfo.NumberGroupSizes"/>, so without this the comma positions
    /// actually written in the format code would be silently discarded in favor of uniform
    /// Western 3-digit grouping. Returns false (and an empty array) when the pattern has no
    /// grouping commas, or when the commas only ever imply the standard uniform 3-digit
    /// grouping (nothing irregular to report).
    /// </summary>
    private static bool TryDeriveIrregularGroupSizes(string format, out int[] groupSizes)
    {
        groupSizes = [];

        // Only the integer part (before any unquoted decimal point) carries grouping commas —
        // Excel/.NET never group the fractional part.
        var integerPart = format;
        var inQuote = false;
        for (var i = 0; i < format.Length; i++)
        {
            var c = format[i];
            if (c == '"') { inQuote = !inQuote; continue; }
            if (!inQuote && c == '\\' && i + 1 < format.Length) { i++; continue; }
            if (!inQuote && c == '.') { integerPart = format[..i]; break; }
        }

        if (integerPart.IndexOf(',') < 0)
            return false;

        var segments = new List<int>();
        var currentLength = 0;
        inQuote = false;
        for (var i = 0; i < integerPart.Length; i++)
        {
            var c = integerPart[i];
            if (c == '"') { inQuote = !inQuote; continue; }
            if (!inQuote && c == '\\' && i + 1 < integerPart.Length) { i++; continue; }

            if (!inQuote && IsNumericPlaceholder(c))
                currentLength++;
            else if (!inQuote && c == ',')
            {
                segments.Add(currentLength);
                currentLength = 0;
            }
        }
        segments.Add(currentLength);

        // Fewer than two comma-separated groups means there's nothing irregular to derive —
        // leave the caller's existing (default) grouping behaviour alone.
        if (segments.Count < 2)
            return false;

        // .NET's NumberGroupSizes convention reads right-to-left with the last array element
        // repeating for every further-out group. The leftmost (outermost) literal segment is
        // "however many placeholders are left", not a hard cap, so it is dropped in favor of
        // letting the next segment in become the repeating tail — e.g. "#,##,##0" is [1,2,3]
        // left-to-right; reversed to [3,2,1]; dropping the leftmost "1" leaves [3,2].
        segments.Reverse();
        segments.RemoveAt(segments.Count - 1);

        if (segments.Count == 0 || segments.Exists(len => len <= 0))
            return false;

        // A pattern that only ever implies the standard Western 3-digit grouping (e.g. "#,##0",
        // "#,###,##0") isn't irregular — skip so callers keep their existing fast paths/defaults.
        if (segments.TrueForAll(len => len == 3))
            return false;

        groupSizes = segments.ToArray();
        return true;
    }

    private static string ApplyNumericFormat(
        double value,
        string format,
        bool preserveAccountingZeroDashAlignment = false,
        bool uses1904DateSystem = false,
        int? targetWidthCharacters = null)
    {
        // Excel never displays a negative sign for a zero value, in any format. IEEE-754
        // negative zero (-0.0) satisfies none of the `value < 0` checks below, so without this
        // it flows straight into format-specific branches (scientific notation in particular,
        // via FormatScientific) that call .ToString() directly on the raw bit pattern and
        // render the sign as a literal "-" -- bypassing the negative-zero strip further down
        // this method (around IsNegativeZeroRepresentation), which only covers the plain
        // .NET-custom-format path and runs too late for those early-return branches.
        if (value == 0)
            value = 0.0;

        if (TryFormatCjkNativeNumberText(value, format, out var cjkNativeNumberText))
            return cjkNativeNumberText;

        var nativeDigitFormat = format;
        string NativeDigits(string text) => ApplyNativeDigitSubstitution(text, nativeDigitFormat);

        if (string.IsNullOrEmpty(format) || IsGeneralFormat(format))
            return FormatNumberGeneral(value, targetWidthCharacters);

        // A "General" keyword DECORATED with plain literal text and/or a sign character (e.g.
        // "-General" as the negative section of "General;-General", or a quoted-prefixed
        // "\"Value: \"General") is not itself a numeric pattern -- Excel renders the value's
        // General-format text with the surrounding literals applied verbatim, exactly like the
        // exact "General" case just above. This must run before the generic pipeline below,
        // since without any 0/#/? placeholder that pipeline falls through to .NET's custom
        // numeric formatter, which treats the whole string (including the word "General") as
        // literal characters and ignores the value entirely.
        if (TryFormatDecoratedGeneral(value, format, out var decoratedGeneralText))
            return NativeDigits(decoratedGeneralText);

        if (TryResolveSpecialDateTimeLocaleToken(format, out var specialDateTimeToken))
        {
            try
            {
                // Use DateTime.FromOADate for the special locale-token path so that the
                // roundtrip DateTime→ToOADate→FromOADate is lossless for modern dates.
                // ExcelDateSystem.SerialToDate is used for the regular date-format path
                // (where the 1900 phantom-leap-day correction matters), and also here when
                // the workbook uses the 1904 date system (where the epoch itself differs).
                var dt = uses1904DateSystem ? ExcelDateSystem.SerialToDate(value, uses1904DateSystem) : DateTime.FromOADate(value);
                return NativeDigits(FormatSpecialDateTimeLocaleValue(dt, specialDateTimeToken));
            }
            catch { return value.ToString(CultureInfo.InvariantCulture); }
        }
        format = PreserveLocaleCurrencyTokens(format, out var numberFormat, out var dateTimeFormat);

        // Elapsed-time brackets: [h], [m], [s] represent total elapsed hours/minutes/seconds
        // and must be handled before the generic bracket-stripping pass.
        if (format.IndexOf('[') >= 0)
        {
            var directiveFormat = PreprocessBracketFormatDirectives(format);
            if (directiveFormat.ElapsedTimeMatch.Success)
            {
                return NativeDigits(FormatElapsedTime(
                    value,
                    directiveFormat.Format,
                    directiveFormat.ElapsedTimeMatch));
            }

            format = directiveFormat.Format;
        }

        format = PreserveAccountingFillSpace(format);
        format = RemoveSpacingAndFillDirectives(format);
        (format, value) = ApplyTrailingCommaScaling(format, value);

        // Percentage: multiply value by 100 before formatting
        int activePercentCount = CountActivePercentTokens(format);
        if (activePercentCount > 0)
        {
            double pctValue = value * Math.Pow(100, activePercentCount);
            // .NET percentage format (P) multiplies by 100 and adds %; but format string
            // containing literal '%' means we multiply ourselves and use 'F' style.
            // Replace active percent tokens with quoted literals so they stay in-place.
            string numFmt = QuoteActivePercentTokens(format).Trim();
            try
            {
                return NativeDigits(pctValue.ToString(string.IsNullOrEmpty(numFmt) ? "0" : numFmt, numberFormat));
            }
            catch { return NativeDigits(pctValue.ToString("0", numberFormat) + "%"); }
        }

        // Date / time format
        if (IsDateTimeFormat(format))
        {
            // Excel treats a negative value as invalid for a calendar date/time format (see
            // BuildInvalidDateTimeIndicator). This only fires here for a single, unconditioned
            // section that still carries its original sign -- e.g. a bracket-prefixed date
            // format like "[$-409]m/d/yyyy" reaches this call with the raw value -- since the
            // fast single-section path in FormatNumber and the multi-section explicit-negative-
            // section path both already hand this function a non-negative value, making this a
            // no-op for those callers.
            if (value < 0)
                return BuildInvalidDateTimeIndicator(format, targetWidthCharacters);

            // A serial past the representable date range (e.g. year > 9999) is just as
            // fundamentally invalid for a calendar date/time format as a negative one -- Excel
            // shows the same all-hash invalid-value indicator, not the raw number. Use the
            // bounds-checked TrySerialToDate (already relied on elsewhere, e.g.
            // ExcelDateSystem.TrySerialToDate's own callers) instead of catching the
            // ArgumentOutOfRangeException that the unchecked SerialToDate throws here, since that
            // exception was being swallowed by the catch-all below and replaced with the plain
            // numeric text.
            if (!ExcelDateSystem.TrySerialToDate(value, uses1904DateSystem, out var dt))
                return BuildInvalidDateTimeIndicator(format, targetWidthCharacters);

            try
            {
                return NativeDigits(FormatDateTimeValue(dt, format, dateTimeFormat));
            }
            catch { return value.ToString(CultureInfo.InvariantCulture); }
        }

        if (IsSimpleFractionFormat(format))
            return NativeDigits(FormatSimpleFraction(value, format));

        if (IsScientificFormat(format))
            return NativeDigits(FormatScientific(value, format, numberFormat));

        // Accounting / text literals — strip quoted strings to expose the numeric pattern
        var stripped = format.IndexOf('"') >= 0
            ? NumericQuotedTextRegex.Replace(format, "")
            : format;
        var prefix = "";
        var suffix = "";

        // Extract prefix/suffix literal text (not part of numeric pattern)
        if (format != stripped || HasActiveQuestionPlaceholder(format))
        {
            (prefix, format, suffix) = ExtractNumericAffixes(format);

            if (format.All(c => c is '?' || char.IsWhiteSpace(c)) &&
                !ShouldRenderQuestionOnlyFormat(prefix, suffix))
                return NativeDigits(prefix + RenderQuestionOnlyAlignment(format) + suffix);

            if (string.IsNullOrEmpty(format))
                return NativeDigits(prefix + suffix);
        }

        // Pass the cleaned format to .NET — it understands #,##0.00, 0.00, 0, # etc.
        if (HasActiveQuestionPlaceholder(format))
            return NativeDigits(prefix + FormatQuestionPlaceholderNumber(value, format, numberFormat) + suffix);

        // Excel derives irregular (e.g. Indian lakh/crore) thousands grouping purely from the
        // literal comma positions written in the pattern itself, even with no [$-locale] token.
        // .NET's custom-format engine ignores those literal positions and groups only by
        // NumberFormatInfo.NumberGroupSizes, so honor a pattern-implied irregular grouping here
        // unless numberFormat already reflects it (e.g. via an explicit [$-439] locale token).
        if (TryDeriveIrregularGroupSizes(format, out var irregularGroupSizes) &&
            !irregularGroupSizes.SequenceEqual(numberFormat.NumberGroupSizes))
        {
            var groupedNumberFormat = (NumberFormatInfo)numberFormat.Clone();
            groupedNumberFormat.NumberGroupSizes = irregularGroupSizes;
            numberFormat = groupedNumberFormat;
        }

        string numStr;
        try   { numStr = value.ToString(format, numberFormat); }
        catch { numStr = value.ToString(numberFormat); }

        // .NET may produce "-0" (or "-0.00" etc.) when a very small negative number
        // rounds to zero under the format. Excel never displays negative zero — strip the sign.
        // This can ONLY be .NET's own auto-generated sign (never a literal '-' the format author
        // wrote) when the ORIGINAL value passed to this method was itself negative: .NET's custom
        // numeric-format engine treats '-' purely as a literal character and never emits it on its
        // own for a non-negative input. Both callers of this method (the single-section magnitude
        // path and the positional-section path via SelectPositionalSection) already pass an
        // Math.Abs'd, non-negative value for a negative source number, so a leading '-' seen there
        // is always an author-literal minus (e.g. the "-0" in "0;-0") and must be preserved --
        // gating on `value < 0` is what tells the two cases apart.
        if (value < 0 && numStr.Length > 1 && numStr[0] == '-' && IsNegativeZeroRepresentation(numStr, numberFormat))
            numStr = numStr[1..];

        var result = NativeDigits(prefix + numStr + suffix);

        // Same distinction as above, applied to the assembled prefix+numStr+suffix result: only
        // strip when the original value was negative (an auto-generated sign), never when it was
        // already non-negative (any '-' there is an author-literal character in the format's
        // prefix/section text and must be kept, per R55-render-number-format-display-5-1).
        if (value < 0 && result.Length > 1 && result[0] == '-' && IsAllZeroText(result[1..]))
            result = result[1..];

        return result;
    }

    private static int CountActivePercentTokens(string format)
    {
        int count = 0;
        bool inQuote = false;
        for (int i = 0; i < format.Length; i++)
        {
            char c = format[i];
            if (c == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (c == '\\' && i + 1 < format.Length)
            {
                i++;
                continue;
            }

            if (!inQuote && c == '%')
                count++;
        }

        return count;
    }

    private static string QuoteActivePercentTokens(string format)
    {
        var result = new System.Text.StringBuilder(format.Length);
        bool inQuote = false;
        for (int i = 0; i < format.Length; i++)
        {
            char c = format[i];
            if (c == '"')
            {
                inQuote = !inQuote;
                result.Append(c);
                continue;
            }

            if (c == '\\' && i + 1 < format.Length)
            {
                result.Append(c);
                result.Append(format[++i]);
                continue;
            }

            if (!inQuote && c == '%')
            {
                result.Append("\"%\"");
                continue;
            }

            result.Append(c);
        }

        return result.ToString();
    }

    private static (string Format, double Value) ApplyTrailingCommaScaling(string format, double value)
    {
        if (CountTrailingScaleCommas(format) == 0)
            return (format, value);

        var sb = new System.Text.StringBuilder(format);
        bool inQuote = false;
        int scaleCommas = 0;

        for (int i = sb.Length - 1; i >= 0; i--)
        {
            char c = sb[i];
            if (c == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (inQuote)
                continue;

            if (char.IsWhiteSpace(c))
                continue;

            if (c == ',')
            {
                if (IsEscaped(sb, i))
                    break;

                scaleCommas++;
                sb.Remove(i, 1);
                continue;
            }

            break;
        }

        return (sb.ToString(), value / Math.Pow(1000, scaleCommas));
    }

    private static int CountTrailingScaleCommas(string format)
    {
        bool inQuote = false;
        int scaleCommas = 0;

        for (int i = format.Length - 1; i >= 0; i--)
        {
            char c = format[i];
            if (c == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (inQuote)
                continue;

            if (char.IsWhiteSpace(c))
                continue;

            if (c == ',')
            {
                if (IsEscaped(format, i))
                    break;

                scaleCommas++;
                continue;
            }

            break;
        }

        return scaleCommas;
    }

    /// <summary>
    /// Returns true when a formatted string like "-0" or "-0.00" is a negative-zero
    /// representation — all digit characters after the minus sign are the numeric
    /// separator characters or "0". Excel never displays negative zero; callers
    /// should strip the leading "-" in that case.
    /// </summary>
    private static bool IsNegativeZeroRepresentation(string numStr, NumberFormatInfo fmt)
    {
        // numStr starts with "-"; scan the rest for any non-zero digit.
        for (int i = 1; i < numStr.Length; i++)
        {
            char c = numStr[i];
            // A non-zero digit means this is not "negative zero".
            if (char.IsDigit(c) && c != '0')
                return false;
        }

        return true;
    }

    /// <summary>
    /// Returns true when every digit character in <paramref name="text"/> is '0'
    /// (i.e. the text represents a zero value with possible non-digit decoration).
    /// Used to detect "negative zero" situations such as "0", "0.00", "0,000".
    /// </summary>
    private static bool IsAllZeroText(string text)
    {
        bool hasDigit = false;
        foreach (char c in text)
        {
            if (char.IsDigit(c))
            {
                hasDigit = true;
                if (c != '0')
                    return false;
            }
        }
        return hasDigit;
    }

    private static bool IsEscaped(string text, int index)
    {
        int slashCount = 0;
        for (int i = index - 1; i >= 0 && text[i] == '\\'; i--)
            slashCount++;

        return slashCount % 2 == 1;
    }

    private static bool IsEscaped(System.Text.StringBuilder text, int index)
    {
        int slashCount = 0;
        for (int i = index - 1; i >= 0 && text[i] == '\\'; i--)
            slashCount++;

        return slashCount % 2 == 1;
    }

    private static (string Prefix, string NumericFormat, string Suffix) ExtractNumericAffixes(string format)
    {
        var unquotedBuilder = new System.Text.StringBuilder(format.Length);
        int start = -1;
        int end = -1;
        bool inQuote = false;

        for (int i = 0; i < format.Length; i++)
        {
            char c = format[i];
            if (c == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (!inQuote && c == '\\' && i + 1 < format.Length)
            {
                var escaped = format[++i];
                if (!IsNumericPlaceholder(escaped))
                    unquotedBuilder.Append('\\');
                unquotedBuilder.Append(escaped);
                continue;
            }

            int outputIndex = unquotedBuilder.Length;
            unquotedBuilder.Append(c);

            if (!inQuote && IsNumericPlaceholder(c))
            {
                if (start < 0)
                    start = outputIndex;
                end = outputIndex;
            }
        }

        string unquoted = unquotedBuilder.ToString();

        if (start < 0 || end < start)
            return (unquoted, "", "");

        return (unquoted[..start], unquoted[start..(end + 1)], unquoted[(end + 1)..]);
    }

    private static bool IsNumericPlaceholder(char c)
        => c is '0' or '#' or '?';

    private static bool IsGeneralFormat(string format) =>
        string.Equals(format, "General", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Recognizes a "General" keyword that is DECORATED with surrounding plain literal text
    /// and/or a sign character -- e.g. "-General" (the negative section of the common
    /// "General;-General" negative-coloring trick) or "\"Value: \"General" -- and renders it as
    /// the value's General-format text with those literals applied verbatim. Mirrors the
    /// CJK/DBNum path's analogous <see cref="TrySplitNativeGeneralAffixes"/>, which only fires
    /// when a "[DBNum..]"/"[NatNum..]" bracket is present; this covers the ordinary (non-CJK)
    /// case those brackets don't cover. Any leading "[Color]" bracket has already been stripped
    /// out by <see cref="ParseSection"/> by the time a section's format text reaches here, so
    /// only quoted/escaped literal text and bare literal characters remain to split around.
    /// </summary>
    private static bool TryFormatDecoratedGeneral(double value, string format, out string text)
    {
        text = "";
        var visible = UnquoteNativeNumberFormatText(format);

        // Any numeric/fraction placeholder means this is a real custom numeric pattern, not a
        // decorated General keyword -- leave it to the normal formatting pipeline.
        if (visible.IndexOfAny(['0', '#', '?']) >= 0)
            return false;

        foreach (var token in NativeGeneralFormatTokens)
        {
            var index = visible.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                continue;

            var prefix = visible[..index];
            var suffix = visible[(index + token.Length)..];
            text = prefix + FormatNumberGeneral(value) + suffix;
            return true;
        }

        return false;
    }

    // ── Date/time formatting ──────────────────────────────────────────────────

}

