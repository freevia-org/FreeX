using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    private static ScalarValue Abs(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue err) return err;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, AbsScalar);
        return AbsScalar(args[0]);
    }

    private static ScalarValue AbsScalar(ScalarValue value)
    {
        var n = ToNumber(value);
        if (!double.IsFinite(n)) return ErrorValue.Num;
        return new NumberValue(Math.Abs(n));
    }

    private static ScalarValue Mod(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        return MapBinaryMathArgs(args[0], args[1], (left, right) => ModScalar(left, ToNumber(right)));
    }


    private static ScalarValue ModScalar(ScalarValue value, double d)
    {
        var n = ToNumber(value);
        if (!double.IsFinite(n) || !double.IsFinite(d)) return ErrorValue.Num;
        if (d == 0) return ErrorValue.DivByZero;
        return NumberResult(n - d * Math.Floor(n / d));
    }

    private static ScalarValue Power(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        return MapBinaryMathArgs(args[0], args[1], (left, right) => PowerScalar(left, ToNumber(right)));
    }

    private static ScalarValue PowerScalar(ScalarValue value, double power)
    {
        var number = ToNumber(value);
        if (!double.IsFinite(number) || !double.IsFinite(power)) return ErrorValue.Num;
        if (number == 0 && power < 0) return ErrorValue.DivByZero;
        if (number == 0 && power == 0) return ErrorValue.Num;
        var result = Math.Pow(number, power);
        if (double.IsNaN(result)) return ErrorValue.Num;
        if (double.IsInfinity(result)) return ErrorValue.Num;
        return new NumberValue(result);
    }

    private static ScalarValue Sqrt(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, SqrtScalar);
        return SqrtScalar(args[0]);
    }

    private static ScalarValue SqrtScalar(ScalarValue value)
    {
        var n = ToNumber(value);
        if (!double.IsFinite(n) || n < 0) return ErrorValue.Num;
        return new NumberValue(Math.Sqrt(n));
    }


    private static ScalarValue Rand(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        new NumberValue(Random.Shared.NextDouble());

    private static ScalarValue Randbetween(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        return MapBinaryMathArgs(args[0], args[1], RandbetweenScalar);
    }

    private static ScalarValue RandbetweenScalar(ScalarValue bottomValue, ScalarValue topValue)
    {
        double db = ToNumber(bottomValue);
        double dt = ToNumber(topValue);
        if (!double.IsFinite(db) || !double.IsFinite(dt)) return ErrorValue.Num;
        if (!TryTruncateToLong(db, out long bottom) || !TryTruncateToLong(dt, out long top))
            return ErrorValue.Num;
        if (bottom > top) return ErrorValue.Num;
        // NextInt64(min, max) is [min, max) â€” add 1 to make top inclusive
        long exclusiveTop;
        try { exclusiveTop = checked(top + 1); }
        catch (OverflowException) { return ErrorValue.Num; }
        return new NumberValue(Random.Shared.NextInt64(bottom, exclusiveTop));
    }

    private static ScalarValue Sign(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, SignScalar);
        return SignScalar(args[0]);
    }

    private static ScalarValue SignScalar(ScalarValue value)
    {
        var n = ToNumber(value);
        if (!double.IsFinite(n)) return ErrorValue.Num;
        return new NumberValue(n > 0 ? 1 : n < 0 ? -1 : 0);
    }

    private static ScalarValue Log(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        var baseArg = args.Count > 1 && args[1] is not BlankValue ? args[1] : new NumberValue(10.0);
        return MapBinaryMathArgs(args[0], baseArg, (left, right) => LogScalar(left, ToNumber(right)));
    }

    private static ScalarValue LogScalar(ScalarValue value, double base_)
    {
        var n = ToNumber(value);
        if (!double.IsFinite(n) || !double.IsFinite(base_)) return ErrorValue.Num;
        if (n <= 0 || base_ <= 0) return ErrorValue.Num;
        if (base_ == 1) return ErrorValue.DivByZero;
        return NumberResult(Math.Log(n) / Math.Log(base_));
    }

    private static ScalarValue Log10(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, Log10Scalar);
        return Log10Scalar(args[0]);
    }

    private static ScalarValue Log10Scalar(ScalarValue value)
    {
        var n = ToNumber(value);
        if (!double.IsFinite(n) || n <= 0) return ErrorValue.Num;
        return NumberResult(Math.Log10(n));
    }

    private static ScalarValue Ln(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, LnScalar);
        return LnScalar(args[0]);
    }

    private static ScalarValue LnScalar(ScalarValue value)
    {
        var n = ToNumber(value);
        if (!double.IsFinite(n)) return ErrorValue.Num;
        if (n <= 0) return ErrorValue.Num;
        return NumberResult(Math.Log(n));
    }

    private static ScalarValue Exp(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, ExpScalar);
        return ExpScalar(args[0]);
    }

    private static ScalarValue ExpScalar(ScalarValue value)
    {
        var n = ToNumber(value);
        if (!double.IsFinite(n)) return ErrorValue.Num;
        var result = Math.Exp(n);
        if (double.IsNaN(result) || double.IsInfinity(result)) return ErrorValue.Num;
        return new NumberValue(result);
    }

    private static ScalarValue Pi(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        new NumberValue(Math.PI);

    // CHOOSE(index, val1, val2, ...)
    private static ScalarValue Choose(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var n = ToNumber(args[0]);
        if (!double.IsFinite(n)) return ErrorValue.Value;
        int idx = (int)n;
        if (idx < 1 || idx >= args.Count) return ErrorValue.Value;
        return args[idx];
    }

    private static ScalarValue Quotient(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        return MapBinaryMathArgs(args[0], args[1], (left, right) => QuotientScalar(left, ToNumber(right)));
    }

    private static ScalarValue QuotientScalar(ScalarValue value, double d)
    {
        double n = ToNumber(value);
        if (!double.IsFinite(n) || !double.IsFinite(d)) return ErrorValue.Num;
        if (d == 0) return ErrorValue.DivByZero;
        return NumberResult(Math.Truncate(n / d));
    }

}
