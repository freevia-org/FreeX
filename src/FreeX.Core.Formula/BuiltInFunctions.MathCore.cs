using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    // Core math, rounding, trigonometry, and combinatoric functions.

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

    private static ScalarValue MapBinaryMathArgs(
        ScalarValue left,
        ScalarValue right,
        Func<ScalarValue, ScalarValue, ScalarValue> map)
    {
        if (left is RangeValue leftRange && right is RangeValue rightRange)
        {
            var shape = leftRange.RowCount == 1 && leftRange.ColCount == 1 ? rightRange : leftRange;
            if (!CanBroadcastToShape(leftRange, shape.RowCount, shape.ColCount) ||
                !CanBroadcastToShape(rightRange, shape.RowCount, shape.ColCount))
                return ErrorValue.Value;

            var cells = new ScalarValue[shape.RowCount, shape.ColCount];
            for (int r = 0; r < shape.RowCount; r++)
                for (int c = 0; c < shape.ColCount; c++)
                    cells[r, c] = map(ValueAtBroadcastCell(leftRange, r, c), ValueAtBroadcastCell(rightRange, r, c));
            return new RangeValue(cells);
        }

        if (left is RangeValue lRange)
            return MapUnaryTextRange(lRange, value => map(value, right));
        if (right is RangeValue rRange)
            return MapUnaryTextRange(rRange, value => map(left, value));
        return map(left, right);
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
        // NextInt64(min, max) is [min, max) — add 1 to make top inclusive
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

    private static ScalarValue Fact(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, FactScalar);
        return FactScalar(args[0]);
    }

    private static ScalarValue FactScalar(ScalarValue value)
    {
        var n = ToNumber(value);
        if (!double.IsFinite(n)) return ErrorValue.Num;
        int ni = (int)Math.Truncate(n);
        if (ni < 0 || ni > 170) return ErrorValue.Num; // Excel limit; 171! overflows double
        double result = 1;
        for (int i = 2; i <= ni; i++) result *= i;
        return new NumberValue(result);
    }

    private static ScalarValue FactDouble(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, FactDoubleScalar);
        return FactDoubleScalar(args[0]);
    }

    private static ScalarValue FactDoubleScalar(ScalarValue value)
    {
        var raw = ToNumber(value);
        if (!double.IsFinite(raw) || raw < 0 || raw > int.MaxValue) return ErrorValue.Num;
        var n = (int)Math.Truncate(raw);
        double result = 1;
        for (var i = n; i > 1; i -= 2)
        {
            result *= i;
            if (!double.IsFinite(result)) return ErrorValue.Num;
        }

        return new NumberValue(result);
    }

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

    // SUMPRODUCT(array1, [array2, ...])
    private static ScalarValue Sumproduct(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var arrays = new List<IReadOnlyList<ScalarValue>>();
        int firstRows = -1, firstCols = -1;
        foreach (var a in args)
        {
            if (a is ErrorValue e) return e;
            if (a is RangeValue rv)
            {
                if (firstRows == -1) { firstRows = rv.RowCount; firstCols = rv.ColCount; }
                else if (rv.RowCount != firstRows || rv.ColCount != firstCols) return ErrorValue.Value;
                arrays.Add(rv.Flatten());
            }
            else if (a is NumberValue nv) arrays.Add([nv]);
            else arrays.Add([a]);
        }
        if (arrays.Count == 0) return new NumberValue(0);
        int len = arrays[0].Count;
        for (int k = 1; k < arrays.Count; k++)
            if (arrays[k].Count != len) return ErrorValue.Value;
        double total = 0;
        for (int i = 0; i < len; i++)
        {
            double product = 1;
            for (int k = 0; k < arrays.Count; k++)
            {
                var v = arrays[k][i];
                if (v is ErrorValue ev) return ev;
                product *= TryCellNumber(v, out double value) ? value : 0;
                if (!double.IsFinite(product)) return ErrorValue.Num;
            }
            total += product;
            if (!double.IsFinite(total)) return ErrorValue.Num;
        }
        return NumberResult(total);
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

    private static ScalarValue Product(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        double result = 1.0;
        foreach (var a in args)
        {
            if (a is ErrorValue e) return e;
            if (a is ReferencedScalarValue referenced)
            {
                if (TryReferencedNumber(referenced, out double value, out var refError)) result *= value;
                else if (refError is not null) return refError;
                continue;
            }
            if (a is DirectTextLiteralValue direct)
            {
                if (!TryDirectTextNumber(direct, out double value)) return ErrorValue.Value;
                if (!double.IsFinite(value)) return ErrorValue.Num;
                result *= value;
            }
            else if (a is NumberValue or BoolValue or DateTimeValue) result *= ToNumber(a);
        }
        return NumberResult(result);
    }

    private static ScalarValue SumSq(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        double total = 0;
        foreach (var arg in args)
        {
            if (arg is ErrorValue e) return e;
            if (arg is RangeValue range)
            {
                foreach (var value in range.Flatten())
                {
                    if (value is ErrorValue cellError) return cellError;
                    if (!TryCellNumber(value, out var number)) continue;
                    total += number * number;
                    if (!double.IsFinite(total)) return ErrorValue.Num;
                }

                continue;
            }

            foreach (var value in FlattenMathArguments(arg))
            {
                if (value is ErrorValue cellError) return cellError;
                if (!TryMathAggregateNumber(value, out var number)) continue;
                total += number * number;
                if (!double.IsFinite(total)) return ErrorValue.Num;
            }
        }

        return NumberResult(total);
    }

    private static ScalarValue SumX2My2(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        SumXPair(args[0], args[1], (x, y) => x * x - y * y);

    private static ScalarValue SumX2Py2(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        SumXPair(args[0], args[1], (x, y) => x * x + y * y);

    private static ScalarValue SumXMy2(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        SumXPair(args[0], args[1], (x, y) =>
        {
            var difference = x - y;
            return difference * difference;
        });

    private static ScalarValue SumXPair(ScalarValue first, ScalarValue second, Func<double, double, double> map)
    {
        if (first is ErrorValue e0) return e0;
        if (second is ErrorValue e1) return e1;
        var firstRange = first is RangeValue range0 ? range0 : SingleCellArray(first);
        var secondRange = second is RangeValue range1 ? range1 : SingleCellArray(second);
        if (firstRange.RowCount != secondRange.RowCount || firstRange.ColCount != secondRange.ColCount)
            return ErrorValue.NA;

        double total = 0;
        for (var row = 0; row < firstRange.RowCount; row++)
            for (var col = 0; col < firstRange.ColCount; col++)
            {
                var left = firstRange.Cells[row, col];
                var right = secondRange.Cells[row, col];
                if (left is ErrorValue leftError) return leftError;
                if (right is ErrorValue rightError) return rightError;
                if (IsNonFiniteDirectTextNumber(left) || IsNonFiniteDirectTextNumber(right))
                    return ErrorValue.Num;
                if (!TryMathAggregateNumber(left, out var x) || !TryMathAggregateNumber(right, out var y))
                    return ErrorValue.Value;
                total += map(x, y);
                if (!double.IsFinite(total)) return ErrorValue.Num;
            }

        return NumberResult(total);
    }

    private static IEnumerable<ScalarValue> FlattenMathArguments(ScalarValue value)
    {
        if (value is RangeValue range)
        {
            foreach (var cell in range.Flatten())
                yield return cell;
        }
        else
        {
            yield return value;
        }
    }

    private static bool TryMathAggregateNumber(ScalarValue value, out double number)
    {
        number = 0;
        if (TryCellNumber(value, out number)) return double.IsFinite(number);
        if (value is BoolValue b)
        {
            number = b.Value ? 1 : 0;
            return true;
        }
        if (value is DirectTextLiteralValue direct && TryDirectTextNumber(direct, out number))
            return double.IsFinite(number);
        return false;
    }

    private static bool IsNonFiniteDirectTextNumber(ScalarValue value) =>
        value is DirectTextLiteralValue direct &&
        TryDirectTextNumber(direct, out var number) &&
        !double.IsFinite(number);

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

    private static ScalarValue Gcd(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        long result = 0;
        foreach (var a in args)
        {
            if (a is ErrorValue e) return e;
            if (a is ReferencedScalarValue referenced)
            {
                if (TryReferencedNumber(referenced, out double value, out var refError))
                {
                    if (!double.IsFinite(value) || value < 0 || value > long.MaxValue) return ErrorValue.Num;
                    result = GcdCalc(result, (long)value);
                }
                else if (refError is not null) return refError;
                continue;
            }
            double d = ToNumber(a);
            if (!double.IsFinite(d) || d < 0 || d > long.MaxValue) return ErrorValue.Num;
            long n = (long)d;
            result = GcdCalc(result, n);
        }
        return new NumberValue(result);
    }

    private static long GcdCalc(long a, long b)
    {
        while (b != 0) { long t = b; b = a % b; a = t; }
        return a;
    }

    private static ScalarValue Lcm(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        long result = 1;
        foreach (var a in args)
        {
            if (a is ErrorValue e) return e;
            if (a is ReferencedScalarValue referenced)
            {
                if (TryReferencedNumber(referenced, out double value, out var refError))
                {
                    if (!double.IsFinite(value) || value < 0 || value > long.MaxValue) return ErrorValue.Num;
                    long referencedNumber = (long)value;
                    if (referencedNumber == 0) return new NumberValue(0);
                    long referencedGcd = GcdCalc(result, referencedNumber);
                    if (result / referencedGcd > long.MaxValue / referencedNumber) return ErrorValue.Num;
                    result = result / referencedGcd * referencedNumber;
                }
                else if (refError is not null) return refError;
                continue;
            }
            double d = ToNumber(a);
            if (!double.IsFinite(d) || d < 0 || d > long.MaxValue) return ErrorValue.Num;
            long n = (long)d;
            if (n == 0) return new NumberValue(0);
            long g = GcdCalc(result, n);
            // Check overflow before multiplying
            if (result / g > long.MaxValue / n) return ErrorValue.Num;
            result = result / g * n;
        }
        return new NumberValue(result);
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

    private static ScalarValue Combin(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        return MapBinaryMathArgs(args[0], args[1], CombinScalar);
    }

    private static ScalarValue CombinScalar(ScalarValue numberValue, ScalarValue chosenValue)
    {
        double dn = ToNumber(numberValue); double dk = ToNumber(chosenValue);
        if (!double.IsFinite(dn) || !double.IsFinite(dk)) return ErrorValue.Num;
        if (dn < 0 || dn > int.MaxValue || dk < 0 || dk > int.MaxValue) return ErrorValue.Num;
        int n = (int)Math.Truncate(dn); int k = (int)Math.Truncate(dk);
        if (n < 0 || k < 0 || k > n) return ErrorValue.Num;
        if (k == 0) return new NumberValue(1);
        if (k == 1) return new NumberValue(n);
        if (n > 1029) return ErrorValue.Num;
        if (k > n - k) k = n - k;
        double result = 1;
        for (int i = 0; i < k; i++)
            result = result * (n - i) / (i + 1);
        return NumberResult(Math.Round(result));
    }

    private static ScalarValue Combina(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        return MapBinaryMathArgs(args[0], args[1], CombinaScalar);
    }

    private static ScalarValue CombinaScalar(ScalarValue numberValue, ScalarValue chosenValue)
    {
        double dn = ToNumber(numberValue); double dk = ToNumber(chosenValue);
        if (!double.IsFinite(dn) || !double.IsFinite(dk)) return ErrorValue.Num;
        if (dn < 0 || dn > int.MaxValue || dk < 0 || dk > int.MaxValue) return ErrorValue.Num;
        int n = (int)Math.Truncate(dn);
        int k = (int)Math.Truncate(dk);
        if (n == 0 && k > 0) return ErrorValue.Num;
        if (k == 0) return new NumberValue(1);
        if (k == 1) return new NumberValue(n);
        if (n > 1029) return ErrorValue.Num;
        if (k > 0 && n > 1029 - k + 1) return ErrorValue.Num;
        return CombinPositiveIntegers(n + k - 1, k);
    }

    private static ScalarValue Permut(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        return MapBinaryMathArgs(args[0], args[1], PermutScalar);
    }

    private static ScalarValue PermutScalar(ScalarValue numberValue, ScalarValue chosenValue)
    {
        double dn = ToNumber(numberValue); double dk = ToNumber(chosenValue);
        if (!double.IsFinite(dn) || !double.IsFinite(dk)) return ErrorValue.Num;
        if (dn < 0 || dn > int.MaxValue || dk < 0 || dk > int.MaxValue) return ErrorValue.Num;
        int n = (int)Math.Truncate(dn); int k = (int)Math.Truncate(dk);
        if (n <= 0 || k < 0 || k > n) return ErrorValue.Num;
        double result = 1;
        for (int i = 0; i < k; i++)
            result *= (n - i);
        return NumberResult(result);
    }

    private static ScalarValue PermutationA(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        return MapBinaryMathArgs(args[0], args[1], PermutationAScalar);
    }

    private static ScalarValue PermutationAScalar(ScalarValue numberValue, ScalarValue chosenValue)
    {
        double dn = ToNumber(numberValue); double dk = ToNumber(chosenValue);
        if (!double.IsFinite(dn) || !double.IsFinite(dk)) return ErrorValue.Num;
        if (dn < 0 || dk < 0 || dn > int.MaxValue || dk > int.MaxValue) return ErrorValue.Num;
        int n = (int)Math.Truncate(dn);
        int k = (int)Math.Truncate(dk);
        if (n == 0 && k > 0) return ErrorValue.Num;
        return NumberResult(Math.Pow(n, k));
    }

    private static ScalarValue CombinPositiveIntegers(int n, int k)
    {
        if (n < 0 || k < 0 || k > n) return ErrorValue.Num;
        if (k > n - k) k = n - k;
        double result = 1;
        for (int i = 0; i < k; i++)
            result = result * (n - i) / (i + 1);
        return NumberResult(Math.Round(result));
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
