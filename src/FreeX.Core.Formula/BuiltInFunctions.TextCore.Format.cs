using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    // Inline number, date/time, fixed, and currency text formatting.

    private static ScalarValue TextFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[1] is ErrorValue formatError) return formatError;
        var uses1904DateSystem = ctx.Uses1904DateSystem;
        return MapBinaryMathArgs(args[0], args[1], (value, formatValue) => TextScalarWithFormat(value, formatValue, uses1904DateSystem));
    }

    private static ScalarValue TextScalarWithFormat(ScalarValue value, ScalarValue formatValue, bool uses1904DateSystem)
    {
        if (value is ErrorValue valueError) return valueError;
        if (formatValue is ErrorValue formatError) return formatError;
        return TextFormatValue(value, ToText(formatValue), uses1904DateSystem);
    }

    private static RangeValue MapTextFuncRange(RangeValue range, string fmt, bool uses1904DateSystem)
    {
        var cells = new ScalarValue[range.RowCount, range.ColCount];
        for (int r = 0; r < range.RowCount; r++)
            for (int c = 0; c < range.ColCount; c++)
            {
                var value = range.Cells[r, c];
                cells[r, c] = value is ErrorValue e ? e : TextFormatValue(value, fmt, uses1904DateSystem);
            }

        return new RangeValue(cells);
    }

    private static ScalarValue TextFormatValue(ScalarValue val, string fmt, bool uses1904DateSystem = false)
    {
        // Numbers and dates format through the same Excel number-format engine the grid uses, so TEXT() and
        // cell display stay consistent — including the '?' digit placeholder, grouping, and scaling that a
        // naive .NET ToString renders as literal characters. Other values pass through as text.
        if (val is NumberValue or DateTimeValue)
            return TextResult(NumberFormatter.Format(val, fmt, uses1904DateSystem));
        return TextResult(ToText(val));
    }

    private static ScalarValue Fixed(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        var decimalsArg = args.Count > 1 ? args[1] : new NumberValue(2);
        var noCommasArg = args.Count > 2 ? args[2] : BlankValue.Instance;
        return MapTernaryTextArgs(args[0], decimalsArg, noCommasArg, FixedScalarWithArgs);
    }

    private static ScalarValue FixedScalarWithArgs(ScalarValue value, ScalarValue decimalsValue, ScalarValue noCommasValue)
    {
        if (noCommasValue is ErrorValue noCommasError) return noCommasError;
        bool noCommas = noCommasValue is not BlankValue && ToBool(noCommasValue);
        return FixedScalarWithDecimals(value, decimalsValue, noCommas);
    }

    private static ScalarValue FixedScalarWithDecimals(ScalarValue value, ScalarValue decimalsValue, bool noCommas)
    {
        if (value is ErrorValue valueError) return valueError;
        if (decimalsValue is ErrorValue decimalsError) return decimalsError;
        int dec = 2;
        if (decimalsValue is not BlankValue)
        {
            double rawDec = ToNumber(decimalsValue);
            if (!double.IsFinite(rawDec) || rawDec > int.MaxValue || rawDec < int.MinValue) return ErrorValue.Num;
            dec = (int)rawDec;
        }
        else
        {
            // BlankValue decimals arg: Excel treats a blank cell reference as 0 for numeric coercion.
            // This matches =FIXED(n,) and =FIXED(n, blank_cell) → 0 decimal places.
            // The omitted-arg case is handled upstream via new NumberValue(2) default in Fixed().
            dec = 0;
        }
        return FixedScalar(value, dec, noCommas);
    }

    private static ScalarValue FixedScalar(ScalarValue value, int dec, bool noCommas)
    {
        double n = ToNumber(value);
        return TextResult(FormatRoundedNumber(n, dec, useCommas: !noCommas));
    }

    private static ScalarValue Dollar(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        var decimalsArg = args.Count > 1 ? args[1] : new NumberValue(2);
        return MapBinaryMathArgs(args[0], decimalsArg, DollarScalarWithDecimals);
    }

    private static ScalarValue DollarScalarWithDecimals(ScalarValue value, ScalarValue decimalsValue)
    {
        if (value is ErrorValue valueError) return valueError;
        if (decimalsValue is ErrorValue decimalsError) return decimalsError;
        int dec = 2;
        if (decimalsValue is BlankValue)
        {
            dec = 0;
        }
        else
        {
            double rawDec = ToNumber(decimalsValue);
            if (!double.IsFinite(rawDec) || rawDec > int.MaxValue || rawDec < int.MinValue) return ErrorValue.Num;
            dec = (int)rawDec;
        }
        return DollarScalar(value, dec);
    }

    private static ScalarValue DollarScalar(ScalarValue value, int dec)
    {
        double n = ToNumber(value);
        var numberText = FormatRoundedNumber(Math.Abs(n), dec, useCommas: true);
        var formatted = "$" + numberText;
        return TextResult(n < 0 && (dec >= 0 || numberText != "0") ? "(" + formatted + ")" : formatted);
    }

    private static string FormatRoundedNumber(double value, int decimals, bool useCommas)
    {
        if (!double.IsFinite(value)) throw new FormulaEvalException("#NUM!", "Invalid number");
        if (decimals > 32767) throw new FormulaEvalException("#VALUE!", "Formatted text exceeds Excel cell text limit");

        double rounded = decimals <= 15 ? RoundWithExcelDigits(value, decimals) : value;
        int displayDecimals = Math.Clamp(decimals, 0, 99); // .NET "N"/"F" format supports 0-99 only
        string format = (useCommas ? "N" : "F") + displayDecimals;
        return rounded.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
    }

}
