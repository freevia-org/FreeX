using System.Globalization;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public static class CellEntryParser
{
    /// <summary>
    /// Parses a typed cell-entry string into a <see cref="Cell"/>. When <paramref name="workbook"/>
    /// is supplied, the target <paramref name="address"/>'s current number format is honored the
    /// same way real Excel does: a Text ("@") formatted destination keeps the literal input
    /// verbatim (no numeric/date/formula coercion, matching PasteCommandFactory
    /// .IsDestinationTextFormatted's identical rule for the paste path), and a General-formatted
    /// destination that receives a percent/currency/fraction/time-shaped literal has that shape's
    /// matching number format auto-applied, mirroring Excel's own typed-entry auto-formatting
    /// (R87-formula-number-parse-locale-5-2, R87-formula-number-parse-locale-5-3). Callers that
    /// omit <paramref name="workbook"/> keep the original workbook-agnostic coercion behavior.
    /// </summary>
    public static Cell CreateCell(string text, CellAddress address, bool useR1C1ReferenceStyle, Workbook? workbook = null)
    {
        if (workbook is not null && IsTargetTextFormatted(workbook, address))
        {
            return Cell.FromValue(new TextValue(text));
        }

        if (text.StartsWith("=", StringComparison.Ordinal))
        {
            var formula = text[1..];
            if (useR1C1ReferenceStyle)
                formula = FormulaReferenceStyleService.ToA1(formula, address);

            // Real Excel refuses to leave edit mode for genuinely malformed formula syntax (e.g.
            // an unbalanced "=SUM(A1"), offering its "we found an error in this formula" correction
            // prompt instead of committing the broken text. Validate the syntax up front so a
            // parse failure rejects the entry outright (FormulaParseException propagates to the
            // caller, matching the DataValidation-block contract those callers already implement)
            // rather than silently committing unparseable formula text that would otherwise only
            // ever surface as a #VALUE! error later, during recalculation -- RecalcEngine.cs's own
            // FormulaParseException catches exist for the DIFFERENT case of a cell whose formula
            // text was already committed (e.g. loaded from a file whose formula this parser can't
            // handle, such as an external-workbook reference), not for a fresh interactive entry.
            FormulaEvaluator.ParseFormula(formula);

            return Cell.FromFormula(formula);
        }

        var value = ParseScalarValue(text, out var inferredNumberFormat);
        var cell = Cell.FromValue(value);

        if (inferredNumberFormat is not null && workbook is not null && IsTargetFormatGeneral(workbook, address))
        {
            var style = workbook.GetStyle(GetTargetStyleId(workbook, address)).Clone();
            style.NumberFormat = inferredNumberFormat;
            cell.StyleId = workbook.RegisterStyle(style);
        }

        return cell;
    }

    private static StyleId GetTargetStyleId(Workbook workbook, CellAddress address)
    {
        var sheet = workbook.GetSheet(address.Sheet);
        if (sheet is null)
            return StyleId.Default;

        return sheet.GetCell(address)?.StyleId ??
            sheet.GetStyleOnly(address.Row, address.Col) ??
            StyleId.Default;
    }

    // Mirrors PasteCommandFactory.IsDestinationTextFormatted / FindReplaceService's identical
    // "@"-format check for the paste and find/replace-re-parse paths respectively.
    private static bool IsTargetTextFormatted(Workbook workbook, CellAddress address) =>
        workbook.GetStyle(GetTargetStyleId(workbook, address)).NumberFormat == "@";

    private static bool IsTargetFormatGeneral(Workbook workbook, CellAddress address) =>
        workbook.GetStyle(GetTargetStyleId(workbook, address)).NumberFormat == "General";

    public static ScalarValue ParseScalarValue(string text) => ParseScalarValue(text, out _);

    private static ScalarValue ParseScalarValue(string text, out string? inferredNumberFormat)
    {
        inferredNumberFormat = null;

        if (text.Length == 0)
        {
            return BlankValue.Instance;
        }

        // Excel's text-escape convention: a leading apostrophe forces the typed entry to be kept
        // as text (apostrophe stripped), e.g. '007 -> "007". Mirrors PasteCommandFactory
        // .ParseClipboardValue's identical rule for the paste path; must be checked before any
        // numeric/boolean/date coercion below.
        if (text.StartsWith('\''))
        {
            return new TextValue(text[1..]);
        }

        if (TryParseFiniteNumber(text, out var number))
        {
            return new NumberValue(number);
        }

        if (text.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
        {
            return new BoolValue(text.Equals("TRUE", StringComparison.OrdinalIgnoreCase));
        }

        // Excel auto-converts a handful of other common typed literal shapes into a numeric/date
        // value even though the plain-number parse above never sees '%', '$', or a date/fraction
        // separator. Fraction is checked before date because a strict "<int> <int>/<int>" shape
        // (e.g. "1 1/2") is otherwise also accepted by DateTime.TryParse below as a nonsensical
        // date - claim the unambiguous fraction shape first.
        if (TryParsePercent(text, out var percentValue))
        {
            inferredNumberFormat = "0%";
            return new NumberValue(percentValue);
        }

        if (TryParseCurrency(text, out var currencyValue))
        {
            inferredNumberFormat = NumberFormatShortcutService.GetFormat(NumberFormatShortcut.Currency);
            return new NumberValue(currencyValue);
        }

        if (TryParseMixedFraction(text, out var fractionValue))
        {
            inferredNumberFormat = "# ?/?";
            return new NumberValue(fractionValue);
        }

        // Excel's fictitious 1900 leap day ("2/29/1900" / "1900-02-29") cannot be represented as
        // a real .NET DateTime (1900 is not a leap year), so it must be special-cased directly
        // to serial 60 before the general DateTime-based parse below - which would otherwise
        // fail for this literal and leave the cell as plain text. Mirrors the same special-case
        // already applied to DATEVALUE()/TIMEVALUE() in BuiltInFunctions.
        if (BuiltInFunctions.TryParseExcelFakeLeapDayText(text, out var fakeLeapDaySerial))
        {
            return new DateTimeValue(fakeLeapDaySerial);
        }

        if (TryParseCurrentCultureDate(text, out var dateTime))
        {
            // A time-only literal (e.g. "15:30", "3:30 PM") has no date component;
            // TryParseCurrentCultureDate's NoCurrentDateDefault parse synthesizes
            // DateTime.MinValue's date (0001-01-01) for the missing date part rather than
            // today's date. Such a value is an Excel time-of-day serial (< 1, e.g. 0.645833...
            // for 15:30), not a real date, so it must bypass DateTimeValue.FromDateTime's OADate
            // conversion entirely -- DateTime.ToOADate() throws for years before 100 AD, and even
            // if it didn't, the corrected serial math only applies to genuine dates.
            if (dateTime.Date == DateTime.MinValue.Date)
            {
                inferredNumberFormat = NumberFormatShortcutService.GetFormat(NumberFormatShortcut.Time);
                return new DateTimeValue(dateTime.TimeOfDay.TotalDays);
            }

            return DateTimeValue.FromDateTime(dateTime);
        }

        return new TextValue(text);
    }

    // Float + AllowThousands so a comma-decimal locale's grouped integer (e.g. de-DE "1.234"
    // meaning 1234, '.' as thousands separator) is honored, not silently misread as a decimal.
    // AllowParentheses recognizes Excel's Lotus-style negative notation (e.g. "(123)" -> -123)
    // for a plain (no currency-symbol) numeric literal, not just the currency-marked form
    // ("($123)") that NumberStyles.Currency already handled via TryParseCurrency below.
    private const NumberStyles NumberEntryStyles =
        NumberStyles.Float | NumberStyles.AllowThousands | NumberStyles.AllowParentheses;

    private static bool TryParseFiniteNumber(string text, out double number)
    {
        if (double.TryParse(text, NumberEntryStyles, CultureInfo.CurrentCulture, out number) &&
            double.IsFinite(number))
        {
            number = RoundToSignificantDigits(number, 15);
            return true;
        }

        // Only reinterpret using invariant separators when the current-culture attempt failed
        // and the text doesn't contain the current culture's own (non-'.') decimal separator -
        // otherwise a locale-typed value that merely failed to parse could be misread as an
        // invariant-formatted one.
        var currentDecimalSeparator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        if (currentDecimalSeparator != "." && text.Contains(currentDecimalSeparator, StringComparison.Ordinal))
        {
            number = 0;
            return false;
        }

        if (double.TryParse(text, NumberEntryStyles, CultureInfo.InvariantCulture, out number) &&
            double.IsFinite(number))
        {
            number = RoundToSignificantDigits(number, 15);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Round <paramref name="value"/> to at most <paramref name="digits"/> significant decimal
    /// digits, matching Excel's storage precision cap (any typed/pasted literal number is capped
    /// at 15 significant digits, unconditionally). Mirrors RecalcEngine's and
    /// DelimitedTextWorkbookReader's own RoundToSignificantDigits helper (this project cannot
    /// reference FreeX.Core.Calc's internal copy, so the identical logic is duplicated here).
    /// </summary>
    private static double RoundToSignificantDigits(double value, int digits)
    {
        if (value == 0)
            return 0;

        var scale = digits - (int)Math.Floor(Math.Log10(Math.Abs(value))) - 1;
        if (scale < 0)
        {
            // The value has more integer digits than the significant-digit cap (e.g. an 18-digit
            // integer). Excel does not round such values to the nearest 10^-scale -- it truncates
            // (chops) the excess low-order digits to zero, matching its 15-significant-digit storage
            // cap. Math.Round(double, int) only accepts digits in [0, 15] and cannot express a
            // negative scale, so replicate the truncation directly instead of clamping to a no-op.
            var divisor = Math.Pow(10, -scale);
            return Math.Truncate(value / divisor) * divisor;
        }

        // Math.Round(double,int) only accepts digits in [0, 15]; a small-magnitude value (|value| <
        // 0.1) gives scale > 15, which would throw. A double already carries at most ~15-17
        // significant digits, so rounding at the 15th place is a safe no-op for those values.
        return Math.Round(value, Math.Min(scale, 15), MidpointRounding.AwayFromZero);
    }

    // Trailing '%' (e.g. "50%") -> Excel stores the underlying fraction (0.5), not the literal 50.
    private static bool TryParsePercent(string text, out double value)
    {
        value = default;
        if (text.Length < 2 || text[^1] != '%')
            return false;

        if (!TryParseFiniteNumber(text[..^1], out var number))
            return false;

        value = number / 100d;
        return true;
    }

    // A '$' sign is Excel's ASCII currency-entry marker (e.g. "$5") regardless of the current
    // culture's own currency symbol, so it is always parsed against en-US currency formatting.
    private static bool TryParseCurrency(string text, out double value)
    {
        value = default;
        if (!text.Contains('$'))
            return false;

        if (!double.TryParse(text, NumberStyles.Currency, CultureInfo.GetCultureInfo("en-US"), out value) ||
            !double.IsFinite(value))
        {
            value = default;
            return false;
        }

        value = RoundToSignificantDigits(value, 15);
        return true;
    }

    // Mixed-number fraction entry (e.g. "1 1/2" -> 1.5, "0 1/2" -> 0.5). Requires a whole part
    // plus a space before the "n/d", matching Excel's own typed-entry convention - a bare "n/d"
    // with no leading whole part/space (e.g. "1/2") is a date to Excel, not a fraction.
    private static bool TryParseMixedFraction(string text, out double value)
    {
        value = default;
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            return false;

        var slashIndex = parts[1].IndexOf('/');
        if (slashIndex <= 0 || slashIndex == parts[1].Length - 1)
            return false;

        if (!long.TryParse(parts[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole))
            return false;

        if (!long.TryParse(parts[1].AsSpan(0, slashIndex), NumberStyles.None, CultureInfo.InvariantCulture, out var numerator) ||
            !long.TryParse(parts[1].AsSpan(slashIndex + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var denominator) ||
            denominator == 0)
        {
            return false;
        }

        var fraction = (double)numerator / denominator;
        value = whole < 0 ? whole - fraction : whole + fraction;
        return true;
    }

    // Only attempt a date parse when the text already "looks like" a date (at least two digit
    // groups, plus either a recognized date separator with 3+ groups or a letter, e.g. a month
    // name) - otherwise DateTime.TryParse is lenient enough to misread plain numbers/fractions.
    private static bool TryParseCurrentCultureDate(string text, out DateTime dateTime)
    {
        dateTime = default;
        if (string.IsNullOrEmpty(CultureInfo.CurrentCulture.Name) || !LooksLikeDateCandidate(text))
            return false;

        // Clone so the two-digit-year window can be overridden to Excel's documented 1930-2029
        // rule (30-99 -> 19xx, 00-29 -> 20xx). .NET's default Calendar.TwoDigitYearMax is 2049,
        // which would misdate e.g. "6/15/45" to 2045 instead of Excel's 1945.
        var culture = (CultureInfo)CultureInfo.CurrentCulture.Clone();
        culture.DateTimeFormat.Calendar.TwoDigitYearMax = 2029;

        if (!DateTime.TryParse(text, culture, DateTimeStyles.NoCurrentDateDefault, out dateTime))
            return false;

        // A time-only literal (e.g. "15:30", "3:30 PM") has no date component at all;
        // NoCurrentDateDefault synthesizes DateTime.MinValue's date (0001-01-01) for the missing
        // date part rather than today's date. Judge these by their time-of-day instead of the
        // synthesized date -- the pre-1900 guard below exists to reject genuine out-of-range date
        // literals (e.g. "1/1/1850"), not every time-only entry, which would otherwise always
        // synthesize a date far earlier than 1900.
        if (dateTime.Date == DateTime.MinValue.Date)
            return true;

        // Excel's earliest representable date is 1/1/1900 (serial 1); text that parses to an
        // earlier date is left as plain text instead of becoming a negative-serial DateTimeValue.
        return dateTime.Date >= new DateTime(1900, 1, 1);
    }

    // '/' and '-' are universally treated by Excel as date separators regardless of locale; '.'
    // only counts when it is the current culture's own actual date separator (e.g. de-DE/it-IT),
    // otherwise a plain decimal-looking string like "1.2.3" under en-US (whose date separator is
    // '/') would be misread as a date instead of staying text. ':' is Excel's universal time
    // separator regardless of locale (e.g. "15:30"), so a bare "H:MM"/"H:MM:SS" literal with no
    // AM/PM letter must still be treated as a date/time candidate -- otherwise a 24-hour time-only
    // entry never even reaches the DateTime.TryParse attempt below (colonAlwaysQualifies: true).
    // See DateEntryShapeRecognizer for the shared, single-source implementation of the underlying
    // digit-group/date-separator shape check (also used by DelimitedTextWorkbookReader's CSV
    // import and TextToColumnsValueConverter's Text-to-Columns "General" column conversion) --
    // including its year-less two-digit-group "M/d"/"M-d" rule (e.g. a bare "3/4" is a date to
    // Excel, matching CSV import; a bare "1/2" with no leading whole part/space is likewise a
    // date, not a fraction -- see TryParseMixedFraction's own comment for that distinction).
    private static bool LooksLikeDateCandidate(string text)
    {
        var cultureDateSeparator = CultureInfo.CurrentCulture.DateTimeFormat.DateSeparator;
        var dotCountsAsDateSeparator = cultureDateSeparator.Length == 1 && cultureDateSeparator[0] == '.';

        return DateEntryShapeRecognizer.LooksLikeDateCandidate(
            text.AsSpan(),
            dotCountsAsDateSeparator,
            colonAlwaysQualifies: true);
    }
}
