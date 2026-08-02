using FreeX.Core.Model;

namespace FreeX.Core.Formula;

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

    // Coerces a DIRECT argument to AND/OR/XOR the way Excel does: numbers/booleans by value and a NUMERIC
    // string by its value (<>0 = TRUE). A direct blank (an omitted argument, e.g. the trailing comma in
    // =AND(TRUE,)) coerces to FALSE, same as ToBool/NOT already do — Excel does not error on it. Non-numeric
    // text — including the words "TRUE"/"FALSE" — and other inputs that are not valid direct logical values
    // return false via `result`/`false` return, so the caller yields #VALUE!. (Text inside a referenced range
    // is ignored separately, not via this method.)
    internal static bool TryDirectLogicalBool(ScalarValue value, out bool result)
    {
        result = false;
        switch (value)
        {
            case BoolValue b: result = b.Value; return true;
            case NumberValue n: result = n.Value != 0.0; return true;
            case DateTimeValue d: result = d.Value != 0.0; return true;
            case BlankValue: result = false; return true;
            case DirectTextLiteralValue t when ExcelTextNumberParser.TryParse(t.Value, out var dn): result = dn != 0.0; return true;
            case TextValue t when ExcelTextNumberParser.TryParse(t.Value, out var tn): result = tn != 0.0; return true;
            // A 1×1 RangeValue collapses to its single cell for logical evaluation (implicit intersection).
            // This handles cases like OR(cell<namedRange,...) where the comparison returns RangeValue(1×1).
            case RangeValue rv when rv.RowCount == 1 && rv.ColCount == 1:
                return TryDirectLogicalBool(rv.Cells[0, 0], out result);
            default: return false;
        }
    }

    internal static string ToText(ScalarValue v) => v switch
    {
        DirectTextLiteralValue t => t.Value,
        TextValue t => t.Value,
        NumberValue n => NumberToExcelText(n.Value),
        DateTimeValue d => NumberToExcelText(d.Value),
        BoolValue b => b.Value ? "TRUE" : "FALSE",
        BlankValue => "",
        ErrorValue e => e.Code,
        _ => v.ToString() ?? ""
    };

    /// <summary>
    /// Converts a number to its Excel General text representation: 15 significant digits
    /// (matching Excel's &amp; concatenation and CONCATENATE/TEXTJOIN text coercion), with
    /// integers serialized without a decimal point and trailing zeros stripped.
    /// </summary>
    internal static string NumberToExcelText(double value) =>
        value.ToString("G15", System.Globalization.CultureInfo.InvariantCulture);

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
        if (TryToExcelDecimal(number, out var decimalNumber) && digits <= 28)
        {
            if (digits >= 0)
                return (double)Math.Round(decimalNumber, digits, MidpointRounding.AwayFromZero);

            var decimalFactor = DecimalPower10(-digits);
            if (decimalFactor is not null)
                return (double)(Math.Round(decimalNumber / decimalFactor.Value, 0, MidpointRounding.AwayFromZero) * decimalFactor.Value);
        }

        if (digits >= 0)
            return Math.Round(number, digits, MidpointRounding.AwayFromZero);

        double factor = Math.Pow(10, -digits);
        // A very-negative digits underflows factor to a *finite* 0.0 (e.g. when the
        // caller's raw digits value itself overflowed int and saturated to
        // int.MinValue, whose negation also overflows back to int.MinValue), which
        // would otherwise turn the final division into Infinity/0 = NaN -> #NUM!.
        // Excel treats an extreme negative num_digits as simply zeroing out the
        // number's magnitude, matching ROUNDUP/ROUNDDOWN's behavior for the same
        // inputs (see TruncateWithExcelDigits/RoundupWithExcelDigits), so guard on
        // factor == 0 too and return 0 directly.
        if (!double.IsFinite(factor) || factor == 0) return 0.0;
        return Math.Round(number / factor, 0, MidpointRounding.AwayFromZero) * factor;
    }

    private static decimal? DecimalPower10(int exponent)
    {
        if (exponent is < 0 or > 28) return null;
        decimal result = 1m;
        for (var i = 0; i < exponent; i++)
            result *= 10m;
        return result;
    }

    internal static int CompareScalar(ScalarValue a, ScalarValue b)
    {
        // Blank coercion: coerce blank to match the other operand's type class,
        // consistent with CompareValues in FormulaEvaluator.Operators.cs.
        bool aBlank = a is BlankValue;
        bool bBlank = b is BlankValue;
        if (aBlank && !bBlank) a = CoerceBlankForCompare(b);
        else if (bBlank && !aBlank) b = CoerceBlankForCompare(a);
        // blank vs blank falls through — both become blank, TypeOrderForCompare gives 0==0.

        // Numbers and dates compare as numbers (dates are OADate serial numbers).
        bool aIsNum = a is NumberValue or DateTimeValue;
        bool bIsNum = b is NumberValue or DateTimeValue;
        if (aIsNum && bIsNum)
        {
            double av = a is DateTimeValue ad ? ad.Value : ((NumberValue)a).Value;
            double bv = b is DateTimeValue bd ? bd.Value : ((NumberValue)b).Value;
            // Round both operands to 15 significant digits before comparing, matching
            // CompareValues in FormulaEvaluator.Operators.cs (the worksheet =,<,>,<=,>=,<>
            // operators). Without this, a value that displays/compares as equal via the
            // worksheet operators (e.g. a raw STDEV.S result vs the value the user typed
            // in matching its General-format text) can fail to be found by MATCH/VLOOKUP/
            // HLOOKUP/XLOOKUP/XMATCH approximate-match ordering and SORT/SORTBY, purely
            // because of noise in the 16th+ significant digit.
            av = FormulaEvaluator.RoundTo15SignificantDigits(av);
            bv = FormulaEvaluator.RoundTo15SignificantDigits(bv);
            return av.CompareTo(bv);
        }
        if (a is TextValue ta && b is TextValue tb)
            return string.Compare(ta.Value, tb.Value, StringComparison.OrdinalIgnoreCase);
        if (a is BoolValue ba && b is BoolValue bb)
            return ba.Value.CompareTo(bb.Value);

        // Mixed types: numbers/dates < text < booleans (Excel sort/compare convention).
        return TypeOrderForCompare(a).CompareTo(TypeOrderForCompare(b));
    }

    // Coerces blank to the zero/empty/false of the other value's type class,
    // mirroring FormulaEvaluator.CoerceBlankTo so CompareScalar and CompareValues agree.
    private static ScalarValue CoerceBlankForCompare(ScalarValue other) => other switch
    {
        NumberValue or DateTimeValue => new NumberValue(0),
        TextValue => new TextValue(""),
        BoolValue => new BoolValue(false),
        _ => BlankValue.Instance
    };

    // Excel type ordering for cross-type comparisons: number/date < text < bool.
    // Blank is treated as the lowest possible value (coercion handles the normal blank case above).
    private static int TypeOrderForCompare(ScalarValue v) => v switch
    {
        BlankValue => 0,
        NumberValue or DateTimeValue => 1,
        TextValue => 2,
        BoolValue => 3,
        _ => 4
    };

    // Returns the type class for approximate-lookup type-matching purposes.
    // number/date → 1, text → 2, bool → 3, blank → 0 (always skipped in approximate match).
    internal static int ApproxLookupTypeClass(ScalarValue v) => v switch
    {
        BlankValue => 0,
        NumberValue or DateTimeValue => 1,
        TextValue => 2,
        BoolValue => 3,
        _ => -1
    };

    internal static bool ScalarEquals(ScalarValue a, ScalarValue b)
    {
        if (a is BlankValue && b is BlankValue) return true;
        // Coerce blank to the zero/empty/false of the OTHER operand's type class -- including
        // BoolValue, matching CompareScalar's CoerceBlankForCompare (a blank cell must equal
        // FALSE for XLOOKUP/XMATCH exact-match purposes, same as it compares equal via CompareScalar).
        if (a is BlankValue) a = CoerceBlankForCompare(b);
        if (b is BlankValue) b = CoerceBlankForCompare(a);
        if (TryCellNumber(a, out double aNumber) && TryCellNumber(b, out double bNumber))
        {
            // Round both operands to 15 significant digits before comparing, matching
            // CompareValues in FormulaEvaluator.Operators.cs (the worksheet = operator).
            // Otherwise MATCH/VLOOKUP/HLOOKUP/XLOOKUP/XMATCH exact match (via MatchExactValue)
            // disagree with '=' on the exact same values whenever the looked-up value comes
            // from a function whose raw double result differs from its own displayed/typed
            // text only in the 16th+ significant digit (e.g. STDEV.S/VAR and similar).
            aNumber = FormulaEvaluator.RoundTo15SignificantDigits(aNumber);
            bNumber = FormulaEvaluator.RoundTo15SignificantDigits(bNumber);
            return aNumber == bNumber;
        }
        if (a is TextValue ta && b is TextValue tb)
            return string.Equals(ta.Value, tb.Value, StringComparison.OrdinalIgnoreCase);
        if (a is BoolValue ba && b is BoolValue bb)
            return ba.Value == bb.Value;
        return false;
    }
}
