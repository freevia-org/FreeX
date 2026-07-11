using System.Globalization;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public static class CellEntryParser
{
    public static Cell CreateCell(string text, CellAddress address, bool useR1C1ReferenceStyle)
    {
        if (text.StartsWith("=", StringComparison.Ordinal))
        {
            var formula = text[1..];
            if (useR1C1ReferenceStyle)
                formula = FormulaReferenceStyleService.ToA1(formula, address);

            return Cell.FromFormula(formula);
        }

        return Cell.FromValue(ParseScalarValue(text));
    }

    public static ScalarValue ParseScalarValue(string text)
    {
        if (text.Length == 0)
        {
            return BlankValue.Instance;
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
            return new NumberValue(percentValue);
        }

        if (TryParseCurrency(text, out var currencyValue))
        {
            return new NumberValue(currencyValue);
        }

        if (TryParseMixedFraction(text, out var fractionValue))
        {
            return new NumberValue(fractionValue);
        }

        if (TryParseCurrentCultureDate(text, out var dateTime))
        {
            return DateTimeValue.FromDateTime(dateTime);
        }

        return new TextValue(text);
    }

    // Float + AllowThousands so a comma-decimal locale's grouped integer (e.g. de-DE "1.234"
    // meaning 1234, '.' as thousands separator) is honored, not silently misread as a decimal.
    private const NumberStyles NumberEntryStyles = NumberStyles.Float | NumberStyles.AllowThousands;

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
        // Math.Round(double, int) only accepts digits in [0, 15] and throws for negative values
        // (which occur whenever |value| >= 10^digits); clamp to [0, 15] rather than [-15, 15]
        // since a value that already has more integer digits than the cap has nothing left to
        // round off.
        scale = Math.Clamp(scale, 0, 15);
        return Math.Round(value, scale, MidpointRounding.AwayFromZero);
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

        return DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.NoCurrentDateDefault, out dateTime) &&
               dateTime.Date != DateTime.MinValue.Date;
    }

    private static bool LooksLikeDateCandidate(string text)
    {
        // '/' and '-' are universally treated by Excel as date separators regardless of locale;
        // '.' only counts when it is the current culture's own actual date separator (e.g.
        // de-DE/it-IT), otherwise a plain decimal-looking string like "1.2.3" under en-US (whose
        // date separator is '/') would be misread as a date instead of staying text.
        var cultureDateSeparator = CultureInfo.CurrentCulture.DateTimeFormat.DateSeparator;

        var digitGroups = 0;
        var inDigitGroup = false;
        var hasDateSeparator = false;
        var hasLetter = false;

        foreach (var c in text)
        {
            if (char.IsDigit(c))
            {
                if (!inDigitGroup)
                {
                    digitGroups++;
                    inDigitGroup = true;
                }

                continue;
            }

            inDigitGroup = false;
            hasDateSeparator |= c is '/' or '-' ||
                (cultureDateSeparator.Length == 1 && c == cultureDateSeparator[0]);
            hasLetter |= char.IsLetter(c);
        }

        if (digitGroups < 2)
        {
            return false;
        }

        return (hasDateSeparator && digitGroups >= 3) || hasLetter;
    }
}
