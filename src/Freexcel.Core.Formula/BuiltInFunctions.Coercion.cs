using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

public static partial class BuiltInFunctions
{
    private static double ToNumber(ScalarValue v) => v switch
    {
        NumberValue n => n.Value,
        DateTimeValue d => d.Value,
        BoolValue b => b.Value ? 1.0 : 0.0,
        BlankValue => 0.0,
        DirectTextLiteralValue t when ExcelTextNumberParser.TryParse(t.Value, out var d) => d,
        TextValue t when ExcelTextNumberParser.TryParse(t.Value, out var d) => d,
        _ => throw new FormulaEvalException("#VALUE!", $"Cannot convert {v} to number")
    };

    internal static bool ToBool(ScalarValue v) => v switch
    {
        BoolValue b => b.Value,
        NumberValue n => n.Value != 0.0,
        DateTimeValue d => d.Value != 0.0,
        BlankValue => false,
        _ => throw new FormulaEvalException("#VALUE!", $"Cannot convert {v} to boolean")
    };

    private static string ToText(ScalarValue v) => v switch
    {
        DirectTextLiteralValue t => t.Value,
        TextValue t => t.Value,
        NumberValue n => n.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        DateTimeValue d => d.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        BoolValue b => b.Value ? "TRUE" : "FALSE",
        BlankValue => "",
        ErrorValue e => e.Code,
        _ => v.ToString() ?? ""
    };

    private static bool TryDirectTextNumber(DirectTextLiteralValue value, out double number) =>
        ExcelTextNumberParser.TryParse(value.Value, out number);

    private static bool TryCellNumber(ScalarValue value, out double number)
    {
        switch (value)
        {
            case NumberValue n:
                number = n.Value;
                return true;
            case DateTimeValue d:
                number = d.Value;
                return true;
            default:
                number = 0;
                return false;
        }
    }

    private static bool SameShape(RangeValue left, RangeValue right) =>
        left.RowCount == right.RowCount && left.ColCount == right.ColCount;

    private static bool TryReferencedNumber(ReferencedScalarValue value, out double number, out ErrorValue? error)
    {
        number = 0;
        error = null;
        switch (value.Value)
        {
            case ErrorValue e:
                error = e;
                return false;
            case NumberValue n:
                number = n.Value;
                return true;
            case DateTimeValue d:
                number = d.Value;
                return true;
            default:
                return false;
        }
    }

    private static bool TryReferencedBool(ReferencedScalarValue value, out bool boolean, out ErrorValue? error)
    {
        boolean = false;
        error = null;
        switch (value.Value)
        {
            case ErrorValue e:
                error = e;
                return false;
            case BoolValue b:
                boolean = b.Value;
                return true;
            case NumberValue n:
                boolean = n.Value != 0.0;
                return true;
            case DateTimeValue d:
                boolean = d.Value != 0.0;
                return true;
            default:
                return false;
        }
    }

    private static ErrorValue? FirstError(IReadOnlyList<ScalarValue> args)
    {
        foreach (var arg in args)
            if (arg is ErrorValue e) return e;
        return null;
    }

    private static ScalarValue NumberResult(double value) =>
        double.IsFinite(value) ? new NumberValue(value) : ErrorValue.Num;

    private static bool TryTruncateToLong(double value, out long result)
    {
        result = 0;
        if (!double.IsFinite(value) || value < long.MinValue || value >= 9223372036854775808.0)
            return false;
        result = (long)Math.Truncate(value);
        return true;
    }

    private static double RoundWithExcelDigits(double number, int digits)
    {
        if (digits >= 0)
            return Math.Round(number, digits, MidpointRounding.AwayFromZero);

        double factor = Math.Pow(10, -digits);
        if (!double.IsFinite(factor)) return 0.0;
        return Math.Round(number / factor, 0, MidpointRounding.AwayFromZero) * factor;
    }

    private static int CompareScalar(ScalarValue a, ScalarValue b)
    {
        if (a is BlankValue && TryCellNumber(b, out _)) a = new NumberValue(0);
        if (b is BlankValue && TryCellNumber(a, out _)) b = new NumberValue(0);

        var aIsNumber = TryCellNumber(a, out double aNumber);
        var bIsNumber = TryCellNumber(b, out double bNumber);
        if (aIsNumber && bIsNumber)
            return aNumber.CompareTo(bNumber);
        if (a is TextValue ta && b is TextValue tb)
            return string.Compare(ta.Value, tb.Value, StringComparison.OrdinalIgnoreCase);
        return (aIsNumber ? 0 : 1) - (bIsNumber ? 0 : 1);
    }

    internal static bool ScalarEquals(ScalarValue a, ScalarValue b)
    {
        if (a is BlankValue && b is BlankValue) return true;
        if (a is BlankValue) a = b is TextValue ? new TextValue("") : (ScalarValue)new NumberValue(0);
        if (b is BlankValue) b = a is TextValue ? new TextValue("") : (ScalarValue)new NumberValue(0);
        if (TryCellNumber(a, out double aNumber) && TryCellNumber(b, out double bNumber))
            return aNumber == bNumber;
        if (a is TextValue ta && b is TextValue tb)
            return string.Equals(ta.Value, tb.Value, StringComparison.OrdinalIgnoreCase);
        if (a is BoolValue ba && b is BoolValue bb)
            return ba.Value == bb.Value;
        return false;
    }
}
