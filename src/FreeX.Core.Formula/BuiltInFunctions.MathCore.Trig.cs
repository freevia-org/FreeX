using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    private static ScalarValue Sin(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, value => TrigScalar(value, Math.Sin));
        return TrigScalar(args[0], Math.Sin);
    }

    private static ScalarValue Sinh(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, value => HyperbolicScalar(value, Math.Sinh));
        return HyperbolicScalar(args[0], Math.Sinh);
    }

    private static ScalarValue Cos(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, value => TrigScalar(value, Math.Cos));
        return TrigScalar(args[0], Math.Cos);
    }

    private static ScalarValue Cosh(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, value => HyperbolicScalar(value, Math.Cosh));
        return HyperbolicScalar(args[0], Math.Cosh);
    }

    private static ScalarValue Tan(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, value => TrigScalar(value, Math.Tan));
        return TrigScalar(args[0], Math.Tan);
    }

    private static ScalarValue Tanh(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, value => HyperbolicScalar(value, Math.Tanh));
        return HyperbolicScalar(args[0], Math.Tanh);
    }

    private static ScalarValue Sec(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, SecScalar);
        return SecScalar(args[0]);
    }

    private static ScalarValue SecScalar(ScalarValue value)
    {
        var n = ToNumber(value);
        if (!double.IsFinite(n) || Math.Abs(n) >= TrigInputLimit) return ErrorValue.Num;
        var denominator = Math.Cos(n);
        if (denominator == 0) return ErrorValue.DivByZero;
        return NumberResult(1.0 / denominator);
    }

    private static ScalarValue Csc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, CscScalar);
        return CscScalar(args[0]);
    }

    private static ScalarValue CscScalar(ScalarValue value)
    {
        var n = ToNumber(value);
        if (!double.IsFinite(n) || Math.Abs(n) >= TrigInputLimit) return ErrorValue.Num;
        var denominator = Math.Sin(n);
        if (denominator == 0) return ErrorValue.DivByZero;
        return NumberResult(1.0 / denominator);
    }

    private static ScalarValue Cot(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, CotScalar);
        return CotScalar(args[0]);
    }

    private static ScalarValue CotScalar(ScalarValue value)
    {
        var n = ToNumber(value);
        if (!double.IsFinite(n) || Math.Abs(n) >= TrigInputLimit) return ErrorValue.Num;
        var denominator = Math.Tan(n);
        if (denominator == 0) return ErrorValue.DivByZero;
        return NumberResult(1.0 / denominator);
    }

    private static ScalarValue Sech(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, SechScalar);
        return SechScalar(args[0]);
    }

    private static ScalarValue SechScalar(ScalarValue value)
    {
        var n = ToNumber(value);
        if (!double.IsFinite(n) || Math.Abs(n) >= TrigInputLimit) return ErrorValue.Num;
        return NumberResult(1.0 / Math.Cosh(n));
    }

    private static ScalarValue Csch(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, CschScalar);
        return CschScalar(args[0]);
    }

    private static ScalarValue CschScalar(ScalarValue value)
    {
        var n = ToNumber(value);
        if (!double.IsFinite(n) || Math.Abs(n) >= TrigInputLimit) return ErrorValue.Num;
        var denominator = Math.Sinh(n);
        if (denominator == 0) return ErrorValue.DivByZero;
        return NumberResult(1.0 / denominator);
    }

    private static ScalarValue Coth(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, CothScalar);
        return CothScalar(args[0]);
    }

    private static ScalarValue CothScalar(ScalarValue value)
    {
        var n = ToNumber(value);
        if (!double.IsFinite(n) || Math.Abs(n) >= TrigInputLimit) return ErrorValue.Num;
        var denominator = Math.Tanh(n);
        if (denominator == 0) return ErrorValue.DivByZero;
        return NumberResult(1.0 / denominator);
    }

    private static ScalarValue TrigScalar(ScalarValue value, Func<double, double> func)
    {
        var n = ToNumber(value);
        if (!double.IsFinite(n) || Math.Abs(n) >= TrigInputLimit) return ErrorValue.Num;
        return new NumberValue(func(n));
    }

    private static ScalarValue HyperbolicScalar(ScalarValue value, Func<double, double> func)
    {
        var n = ToNumber(value);
        if (!double.IsFinite(n)) return ErrorValue.Num;
        return NumberResult(func(n));
    }

    private static ScalarValue Asin(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, AsinScalar);
        return AsinScalar(args[0]);
    }

    private static ScalarValue AsinScalar(ScalarValue value)
    {
        double n = ToNumber(value);
        if (!double.IsFinite(n) || n < -1 || n > 1) return ErrorValue.Num;
        return new NumberValue(Math.Asin(n));
    }

    private static ScalarValue Asinh(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, AsinhScalar);
        return AsinhScalar(args[0]);
    }

    private static ScalarValue AsinhScalar(ScalarValue value)
    {
        var n = ToNumber(value);
        if (!double.IsFinite(n)) return ErrorValue.Num;
        return NumberResult(Math.Asinh(n));
    }

    private static ScalarValue Acos(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, AcosScalar);
        return AcosScalar(args[0]);
    }

    private static ScalarValue AcosScalar(ScalarValue value)
    {
        double n = ToNumber(value);
        if (!double.IsFinite(n) || n < -1 || n > 1) return ErrorValue.Num;
        return new NumberValue(Math.Acos(n));
    }

    private static ScalarValue Acosh(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, AcoshScalar);
        return AcoshScalar(args[0]);
    }

    private static ScalarValue AcoshScalar(ScalarValue value)
    {
        var n = ToNumber(value);
        if (!double.IsFinite(n) || n < 1) return ErrorValue.Num;
        return NumberResult(Math.Acosh(n));
    }

    private static ScalarValue Atan(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, AtanScalar);
        return AtanScalar(args[0]);
    }

    private static ScalarValue AtanScalar(ScalarValue value)
    {
        var n = ToNumber(value);
        if (!double.IsFinite(n)) return ErrorValue.Num;
        return new NumberValue(Math.Atan(n));
    }

    private static ScalarValue Atanh(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, AtanhScalar);
        return AtanhScalar(args[0]);
    }

    private static ScalarValue AtanhScalar(ScalarValue value)
    {
        var n = ToNumber(value);
        if (!double.IsFinite(n) || n <= -1 || n >= 1) return ErrorValue.Num;
        return NumberResult(Math.Atanh(n));
    }

    private static ScalarValue Acot(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, AcotScalar);
        return AcotScalar(args[0]);
    }

    private static ScalarValue AcotScalar(ScalarValue value)
    {
        var n = ToNumber(value);
        if (!double.IsFinite(n)) return ErrorValue.Num;
        if (n == 0) return new NumberValue(Math.PI / 2.0);
        var result = Math.Atan(1.0 / n);
        return new NumberValue(n < 0 ? result + Math.PI : result);
    }

    private static ScalarValue Acoth(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, AcothScalar);
        return AcothScalar(args[0]);
    }

    private static ScalarValue AcothScalar(ScalarValue value)
    {
        var n = ToNumber(value);
        if (!double.IsFinite(n) || Math.Abs(n) <= 1) return ErrorValue.Num;
        return NumberResult(0.5 * Math.Log((n + 1.0) / (n - 1.0)));
    }

    private static ScalarValue Atan2Func(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        return MapBinaryMathArgs(args[0], args[1], Atan2Scalar);
    }

    private static ScalarValue Atan2Scalar(ScalarValue xValue, ScalarValue yValue)
    {
        double x = ToNumber(xValue);
        double y = ToNumber(yValue);
        if (!double.IsFinite(x) || !double.IsFinite(y)) return ErrorValue.Num;
        if (x == 0 && y == 0) return ErrorValue.DivByZero;
        return new NumberValue(Math.Atan2(y, x));
    }

    private const double TrigInputLimit = 134217728.0;

    private static ScalarValue Degrees(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, DegreesScalar);
        return DegreesScalar(args[0]);
    }

    private static ScalarValue DegreesScalar(ScalarValue value)
    {
        var n = ToNumber(value);
        if (!double.IsFinite(n)) return ErrorValue.Num;
        return NumberResult(n * 180.0 / Math.PI);
    }

    private static ScalarValue Radians(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, RadiansScalar);
        return RadiansScalar(args[0]);
    }

    private static ScalarValue RadiansScalar(ScalarValue value)
    {
        var n = ToNumber(value);
        if (!double.IsFinite(n)) return ErrorValue.Num;
        return NumberResult(n * Math.PI / 180.0);
    }
}
