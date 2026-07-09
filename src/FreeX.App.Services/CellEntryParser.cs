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

        return double.TryParse(text, NumberEntryStyles, CultureInfo.InvariantCulture, out number) &&
               double.IsFinite(number);
    }
}
