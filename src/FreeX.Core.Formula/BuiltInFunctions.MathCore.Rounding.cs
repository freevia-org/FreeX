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
        if (n > 0 && sig < 0) return ErrorValue.Num;
        return NumberResult(Math.Ceiling(n / sig) * sig);
    }

    private static ScalarValue IsoCeiling(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        var significance = args.Count > 1 && args[1] is not BlankValue ? args[1] : new NumberValue(1);
        return MapBinaryMathArgs(args[0], significance, IsoCeilingScalar);
    }

    private static ScalarValue IsoCeilingScalar(ScalarValue value, ScalarValue significanceValue)
    {
        var n = ToNumber(value);
        var significance = ToNumber(significanceValue);
        if (!double.IsFinite(n) || !double.IsFinite(significance)) return ErrorValue.Num;
        if (n == 0 || significance == 0) return new NumberValue(0);
        var multiple = Math.Abs(significance);
        return NumberResult(Math.Ceiling(n / multiple) * multiple);
    }

    private static ScalarValue CeilingPrecise(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        IsoCeiling(args, ctx);

    private static ScalarValue CeilingMath(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        var significance = args.Count > 1 && args[1] is not BlankValue ? args[1] : new NumberValue(1);
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
            ? Math.Floor(n / multiple) * multiple
            : Math.Ceiling(n / multiple) * multiple;
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
        if (n * sig < 0) return ErrorValue.Num;
        return NumberResult(Math.Floor(n / sig) * sig);
    }

    private static ScalarValue FloorPrecise(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        var significance = args.Count > 1 && args[1] is not BlankValue ? args[1] : new NumberValue(1);
        return MapBinaryMathArgs(args[0], significance, FloorPreciseScalar);
    }

    private static ScalarValue FloorPreciseScalar(ScalarValue value, ScalarValue significanceValue)
    {
        var n = ToNumber(value);
        var significance = ToNumber(significanceValue);
        if (!double.IsFinite(n) || !double.IsFinite(significance)) return ErrorValue.Num;
        if (n == 0 || significance == 0) return new NumberValue(0);
        var multiple = Math.Abs(significance);
        return NumberResult(Math.Floor(n / multiple) * multiple);
    }

    private static ScalarValue FloorMath(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        var significance = args.Count > 1 && args[1] is not BlankValue ? args[1] : new NumberValue(1);
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
            ? Math.Truncate(n / multiple) * multiple
            : Math.Floor(n / multiple) * multiple;
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
        double factor = Math.Pow(10, digits);
        if (factor == 0) return new NumberValue(0);
        return NumberResult((n >= 0 ? Math.Floor(n * factor) : Math.Ceiling(n * factor)) / factor);
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
        double factor = Math.Pow(10, digits);
        if (factor == 0) return new NumberValue(0);
        return NumberResult((n >= 0 ? Math.Ceiling(n * factor) : Math.Floor(n * factor)) / factor);
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

    private static ScalarValue TruncScalar(ScalarValue value, int digits)
    {
        var n = ToNumber(value);
        if (!double.IsFinite(n)) return ErrorValue.Num;
        if (digits > 15) return new NumberValue(n);
        double factor = Math.Pow(10, digits);
        if (factor == 0) return new NumberValue(0);
        return NumberResult(Math.Truncate(n * factor) / factor);
    }


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
        if (abs > int.MaxValue) return ErrorValue.Num;
        int iabs = (int)abs;
        if (iabs % 2 == 0) iabs++;
        return new NumberValue(sign * iabs);
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
        if (abs > int.MaxValue - 1) return ErrorValue.Num;
        int iabs = (int)abs;
        if (iabs % 2 != 0) iabs++;
        return new NumberValue(sign * iabs);
    }

    private static double MroundWithExcelDigits(double number, double multiple)
    {
        if (!TryToExcelDecimal(number, out var n) || !TryToExcelDecimal(multiple, out var m) || m == 0m)
            return Math.Round(number / multiple, MidpointRounding.AwayFromZero) * multiple;

        var quotient = n / m;
        var roundedQuotient = Math.Round(quotient, 0, MidpointRounding.AwayFromZero);
        return (double)(roundedQuotient * m);
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
}
