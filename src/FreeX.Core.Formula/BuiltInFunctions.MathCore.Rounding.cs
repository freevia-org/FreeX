using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    private static ScalarValue Round(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue err0) return err0;
        if (args[1] is ErrorValue err1) return err1;
        return MapBinaryMathArgs(args[0], args[1], RoundScalarWithDigits);
    }

    private static ScalarValue RoundScalarWithDigits(ScalarValue value, ScalarValue digitsValue)
    {
        if (value is ErrorValue valueError) return valueError;
        if (digitsValue is ErrorValue digitsError) return digitsError;
        var rawDigits = ToNumber(digitsValue);
        if (!double.IsFinite(rawDigits)) return ErrorValue.Num;
        return RoundScalar(value, (int)Math.Truncate(rawDigits));
    }

    private static ScalarValue RoundScalar(ScalarValue value, int digits)
    {
        var number = ToNumber(value);
        if (!double.IsFinite(number)) return ErrorValue.Num;
        if (digits > 15) return new NumberValue(number);
        return NumberResult(RoundWithExcelDigits(number, digits));
    }

    private static ScalarValue IntFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, IntScalar);
        return IntScalar(args[0]);
    }

    private static ScalarValue IntScalar(ScalarValue value)
    {
        var n = ToNumber(value);
        if (!double.IsFinite(n)) return ErrorValue.Num;
        return new NumberValue(Math.Floor(n));
    }

    private static ScalarValue Ceiling(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        return MapBinaryMathArgs(args[0], args[1], (left, right) => CeilingScalar(left, ToNumber(right)));
    }

    private static ScalarValue CeilingScalar(ScalarValue value, double sig)
    {
        var n = ToNumber(value);
        if (!double.IsFinite(n) || !double.IsFinite(sig)) return ErrorValue.Num;
        if (sig == 0) return new NumberValue(0);
        if (n != 0 && (n < 0) != (sig < 0)) return ErrorValue.Num;
        return NumberResult(CeilingToMultiple(n, sig));
    }

    private static ScalarValue IsoCeiling(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        var significance = SignificanceArgOrDefault(args, 1);
        return MapBinaryMathArgs(args[0], significance, IsoCeilingScalar);
    }

    private static ScalarValue IsoCeilingScalar(ScalarValue value, ScalarValue significanceValue)
    {
        var n = ToNumber(value);
        var significance = ToNumber(significanceValue);
        if (!double.IsFinite(n) || !double.IsFinite(significance)) return ErrorValue.Num;
        if (n == 0 || significance == 0) return new NumberValue(0);
        var multiple = Math.Abs(significance);
        return NumberResult(CeilingToMultiple(n, multiple));
    }

    // ISO.CEILING/CEILING.PRECISE/CEILING.MATH/FLOOR.PRECISE/FLOOR.MATH default their
    // significance argument to 1 only when the argument SLOT itself is omitted (i.e. the
    // call has no second argument at all, so args.Count <= index). A present-but-blank
    // slot (an explicit trailing comma with nothing after it, or a reference to an empty
    // cell) still occupies the slot -- args.Count > index -- but Excel coerces that blank
    // to 0, not 1, so CEILING.MATH(4.3,) etc. evaluate their significance as 0.
    private static ScalarValue SignificanceArgOrDefault(IReadOnlyList<ScalarValue> args, int index)
    {
        if (args.Count <= index) return new NumberValue(1);
        return args[index] is BlankValue ? new NumberValue(0) : args[index];
    }

    private static ScalarValue CeilingPrecise(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        IsoCeiling(args, ctx);

    private static ScalarValue CeilingMath(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        var significance = SignificanceArgOrDefault(args, 1);
        var mode = args.Count > 2 && args[2] is not BlankValue ? args[2] : new NumberValue(0);
        return MapTernaryTextArgs(args[0], significance, mode, CeilingMathScalar);
    }

    private static ScalarValue CeilingMathScalar(ScalarValue value, ScalarValue significanceValue, ScalarValue modeValue)
    {
        var n = ToNumber(value);
        var significance = ToNumber(significanceValue);
        var mode = ToNumber(modeValue);
        if (!double.IsFinite(n) || !double.IsFinite(significance) || !double.IsFinite(mode)) return ErrorValue.Num;
        if (n == 0 || significance == 0) return new NumberValue(0);
        var multiple = Math.Abs(significance);
        var rounded = n < 0 && mode != 0
            ? FloorToMultiple(n, multiple)
            : CeilingToMultiple(n, multiple);
        return NumberResult(rounded);
    }

    private static ScalarValue Floor(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        return MapBinaryMathArgs(args[0], args[1], (left, right) => FloorScalar(left, ToNumber(right)));
    }

    private static ScalarValue FloorScalar(ScalarValue value, double sig)
    {
        var n = ToNumber(value);
        if (!double.IsFinite(n) || !double.IsFinite(sig)) return ErrorValue.Num;
        if (sig == 0) return new NumberValue(0);
        if (n != 0 && (n < 0) != (sig < 0)) return ErrorValue.Num;
        return NumberResult(FloorToMultiple(n, sig));
    }

    private static ScalarValue FloorPrecise(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        var significance = SignificanceArgOrDefault(args, 1);
        return MapBinaryMathArgs(args[0], significance, FloorPreciseScalar);
    }

    private static ScalarValue FloorPreciseScalar(ScalarValue value, ScalarValue significanceValue)
    {
        var n = ToNumber(value);
        var significance = ToNumber(significanceValue);
        if (!double.IsFinite(n) || !double.IsFinite(significance)) return ErrorValue.Num;
        if (n == 0 || significance == 0) return new NumberValue(0);
        var multiple = Math.Abs(significance);
        return NumberResult(FloorToMultiple(n, multiple));
    }

    private static ScalarValue FloorMath(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        var significance = SignificanceArgOrDefault(args, 1);
        var mode = args.Count > 2 && args[2] is not BlankValue ? args[2] : new NumberValue(0);
        return MapTernaryTextArgs(args[0], significance, mode, FloorMathScalar);
    }

    private static ScalarValue FloorMathScalar(ScalarValue value, ScalarValue significanceValue, ScalarValue modeValue)
    {
        var n = ToNumber(value);
        var significance = ToNumber(significanceValue);
        var mode = ToNumber(modeValue);
        if (!double.IsFinite(n) || !double.IsFinite(significance) || !double.IsFinite(mode)) return ErrorValue.Num;
        if (n == 0 || significance == 0) return new NumberValue(0);
        var multiple = Math.Abs(significance);
        var rounded = n < 0 && mode != 0
            ? TruncateToMultiple(n, multiple)
            : FloorToMultiple(n, multiple);
        return NumberResult(rounded);
    }


    private static ScalarValue Rounddown(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        return MapBinaryMathArgs(args[0], args[1], RounddownScalarWithDigits);
    }

    private static ScalarValue RounddownScalarWithDigits(ScalarValue value, ScalarValue digitsValue)
    {
        if (value is ErrorValue valueError) return valueError;
        if (digitsValue is ErrorValue digitsError) return digitsError;
        var rawDigits = ToNumber(digitsValue);
        if (!double.IsFinite(rawDigits)) return ErrorValue.Num;
        return RounddownScalar(value, (int)Math.Truncate(rawDigits));
    }

    private static ScalarValue RounddownScalar(ScalarValue value, int digits)
    {
        var n = ToNumber(value);
        if (!double.IsFinite(n)) return ErrorValue.Num;
        if (digits > 15) return new NumberValue(n);
        return NumberResult(TruncateWithExcelDigits(n, digits));
    }

    private static ScalarValue Roundup(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        return MapBinaryMathArgs(args[0], args[1], RoundupScalarWithDigits);
    }

    private static ScalarValue RoundupScalarWithDigits(ScalarValue value, ScalarValue digitsValue)
    {
        if (value is ErrorValue valueError) return valueError;
        if (digitsValue is ErrorValue digitsError) return digitsError;
        var rawDigits = ToNumber(digitsValue);
        if (!double.IsFinite(rawDigits)) return ErrorValue.Num;
        return RoundupScalar(value, (int)Math.Truncate(rawDigits));
    }

    private static ScalarValue RoundupScalar(ScalarValue value, int digits)
    {
        var n = ToNumber(value);
        if (!double.IsFinite(n)) return ErrorValue.Num;
        if (digits > 15) return new NumberValue(n);
        return NumberResult(RoundupWithExcelDigits(n, digits));
    }

    private static ScalarValue Trunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var digitsArg = args.Count > 1 ? args[1] : new NumberValue(0);
        if (digitsArg is ErrorValue e1) return e1;
        return MapBinaryMathArgs(args[0], digitsArg, TruncScalarWithDigits);
    }

    private static ScalarValue TruncScalarWithDigits(ScalarValue value, ScalarValue digitsValue)
    {
        if (value is ErrorValue valueError) return valueError;
        if (digitsValue is ErrorValue digitsError) return digitsError;
        var rawDigits = ToNumber(digitsValue);
        if (!double.IsFinite(rawDigits)) return ErrorValue.Num;
        return TruncScalar(value, (int)Math.Truncate(rawDigits));
    }

    private static ScalarValue TruncScalar(ScalarValue value, int digits) =>
        RounddownScalar(value, digits);


    private static ScalarValue Mround(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        return MapBinaryMathArgs(args[0], args[1], (left, right) => MroundScalar(left, ToNumber(right)));
    }

    private static ScalarValue MroundScalar(ScalarValue value, double m)
    {
        double n = ToNumber(value);
        if (!double.IsFinite(n) || !double.IsFinite(m)) return ErrorValue.Num;
        if (m == 0) return new NumberValue(0);
        if (n != 0 && (n < 0) != (m < 0)) return ErrorValue.Num;
        return NumberResult(MroundWithExcelDigits(n, m));
    }

    private static ScalarValue Odd(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, OddScalar);
        return OddScalar(args[0]);
    }

    private static ScalarValue OddScalar(ScalarValue value)
    {
        double n = ToNumber(value);
        if (!double.IsFinite(n)) return ErrorValue.Num;
        if (n == 0) return new NumberValue(1);
        double sign = n > 0 ? 1 : -1;
        double abs = Math.Ceiling(Math.Abs(n));
        // Adjust to the next odd magnitude using double arithmetic throughout so
        // magnitudes beyond int.MaxValue (Excel supports up to ~9.9e307) don't
        // spuriously error via a narrowing (int) cast.
        if (abs % 2 == 0) abs++;
        if (!double.IsFinite(abs)) return ErrorValue.Num;
        return new NumberValue(sign * abs);
    }

    private static ScalarValue Even(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, EvenScalar);
        return EvenScalar(args[0]);
    }

    private static ScalarValue EvenScalar(ScalarValue value)
    {
        double n = ToNumber(value);
        if (!double.IsFinite(n)) return ErrorValue.Num;
        if (n == 0) return new NumberValue(0);
        double sign = n > 0 ? 1 : -1;
        double abs = Math.Ceiling(Math.Abs(n));
        // Adjust to the next even magnitude using double arithmetic throughout so
        // magnitudes beyond int.MaxValue (Excel supports up to ~9.9e307) don't
        // spuriously error via a narrowing (int) cast.
        if (abs % 2 != 0) abs++;
        if (!double.IsFinite(abs)) return ErrorValue.Num;
        return new NumberValue(sign * abs);
    }

    // Truncate toward zero using Excel's 15-significant-digit correction to avoid
    // binary representation error (e.g. 4.35×100 = 434.99999... in raw double).
    private static double TruncateWithExcelDigits(double number, int digits)
    {
        if (TryToExcelDecimal(number, out var decimalNumber) && digits <= 28)
        {
            if (digits >= 0)
            {
                var decimalFactor = DecimalPower10(digits);
                if (decimalFactor is not null)
                    return (double)(Math.Truncate(decimalNumber * decimalFactor.Value) / decimalFactor.Value);
            }
            else
            {
                var decimalFactor = DecimalPower10(-digits);
                if (decimalFactor is not null)
                    return (double)(Math.Truncate(decimalNumber / decimalFactor.Value) * decimalFactor.Value);
            }
        }

        double factor = Math.Pow(10, digits);
        // A very-negative digits underflows factor to a *finite* 0.0 (rather than
        // overflowing to Infinity like ROUND's mirrored -digits exponent does), which
        // would otherwise turn the final division into 0/0 = NaN -> #NUM!. Excel treats
        // an extreme negative num_digits as simply zeroing out the number's magnitude,
        // matching RoundWithExcelDigits's behavior for the same inputs, so guard on
        // factor == 0 too and return 0 directly.
        if (!double.IsFinite(factor) || factor == 0) return 0.0;
        return (number >= 0 ? Math.Floor(number * factor) : Math.Ceiling(number * factor)) / factor;
    }

    // Round away from zero using Excel's 15-significant-digit correction.
    private static double RoundupWithExcelDigits(double number, int digits)
    {
        if (TryToExcelDecimal(number, out var decimalNumber) && digits <= 28)
        {
            if (digits >= 0)
            {
                var decimalFactor = DecimalPower10(digits);
                if (decimalFactor is not null)
                {
                    var shifted = decimalNumber * decimalFactor.Value;
                    var rounded = decimalNumber >= 0 ? Math.Ceiling(shifted) : Math.Floor(shifted);
                    return (double)(rounded / decimalFactor.Value);
                }
            }
            else
            {
                var decimalFactor = DecimalPower10(-digits);
                if (decimalFactor is not null)
                {
                    var shifted = decimalNumber / decimalFactor.Value;
                    var rounded = decimalNumber >= 0 ? Math.Ceiling(shifted) : Math.Floor(shifted);
                    return (double)(rounded * decimalFactor.Value);
                }
            }
        }

        double factor = Math.Pow(10, digits);
        // See the matching comment in TruncateWithExcelDigits: a very-negative digits
        // underflows factor to a finite 0.0, which would otherwise produce 0/0 = NaN.
        if (!double.IsFinite(factor) || factor == 0) return 0.0;
        return (number >= 0 ? Math.Ceiling(number * factor) : Math.Floor(number * factor)) / factor;
    }

    private static double MroundWithExcelDigits(double number, double multiple)
    {
        if (TryToExcelDecimal(number, out var n) && TryToExcelDecimal(multiple, out var m) && m != 0m)
        {
            try
            {
                var quotient = n / m;
                var roundedQuotient = Math.Round(quotient, 0, MidpointRounding.AwayFromZero);
                return (double)(roundedQuotient * m);
            }
            catch (OverflowException)
            {
                // Magnitude gap between number and multiple exceeds decimal's ~7.9e28
                // range (e.g. 1e15 vs 1e-15) even though each individually fits —
                // fall back to double math, same as CeilingToMultiple/FloorToMultiple/
                // TruncateToMultiple above.
            }
        }

        return Math.Round(number / multiple, MidpointRounding.AwayFromZero) * multiple;
    }

    private static bool TryToExcelDecimal(double value, out decimal result)
    {
        result = 0m;
        if (!double.IsFinite(value)) return false;

        return decimal.TryParse(
            value.ToString("G15", System.Globalization.CultureInfo.InvariantCulture),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out result);
    }

    // FLOOR/CEILING and their *.MATH/ISO/PRECISE variants all bucket a value by a
    // significance/multiple. Doing that with raw double division (Math.Ceiling(n/sig)*sig)
    // means an exact multiple like 4.2/0.3 == 14.000000000000002 (IEEE-754 double rounding
    // error) lands a full bucket wrong (4.5 instead of Excel's 4.2). These helpers use the
    // same 15-significant-digit decimal correction as MroundWithExcelDigits above: convert
    // both operands to decimal (via TryToExcelDecimal), do the division/rounding/multiply
    // entirely in decimal, and only cast back to double at the very end so no intermediate
    // double-precision artifact can leak into the result.
    private static double CeilingToMultiple(double n, double multiple)
    {
        if (TryToExcelDecimal(n, out var dn) && TryToExcelDecimal(multiple, out var dm) && dm != 0m)
        {
            try
            {
                return (double)(Math.Ceiling(dn / dm) * dm);
            }
            catch (OverflowException)
            {
                // Magnitude outside decimal's range (~7.9e28) — fall back to double math.
            }
        }

        return Math.Ceiling(n / multiple) * multiple;
    }

    private static double FloorToMultiple(double n, double multiple)
    {
        if (TryToExcelDecimal(n, out var dn) && TryToExcelDecimal(multiple, out var dm) && dm != 0m)
        {
            try
            {
                return (double)(Math.Floor(dn / dm) * dm);
            }
            catch (OverflowException)
            {
            }
        }

        return Math.Floor(n / multiple) * multiple;
    }

    private static double TruncateToMultiple(double n, double multiple)
    {
        if (TryToExcelDecimal(n, out var dn) && TryToExcelDecimal(multiple, out var dm) && dm != 0m)
        {
            try
            {
                return (double)(Math.Truncate(dn / dm) * dm);
            }
            catch (OverflowException)
            {
            }
        }

        return Math.Truncate(n / multiple) * multiple;
    }

    // Same decimal-precision correction as above, but for QUOTIENT which returns the
    // truncated quotient itself rather than a value re-multiplied by the divisor.
    private static double TruncateExcelQuotient(double n, double d)
    {
        if (TryToExcelDecimal(n, out var dn) && TryToExcelDecimal(d, out var dd) && dd != 0m)
        {
            try
            {
                return (double)Math.Truncate(dn / dd);
            }
            catch (OverflowException)
            {
            }
        }

        return Math.Truncate(n / d);
    }
}
