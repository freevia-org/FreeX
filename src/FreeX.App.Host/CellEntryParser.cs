using System.Globalization;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

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

    private static bool TryParseFiniteNumber(string text, out double number) =>
        (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out number) ||
         double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) &&
        double.IsFinite(number);
}
