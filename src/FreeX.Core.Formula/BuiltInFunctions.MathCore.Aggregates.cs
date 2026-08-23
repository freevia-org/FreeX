using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
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
                // SUMPRODUCT multiplies booleans as 1/0 (unlike SUM, which ignores booleans
                // encountered inside a range); non-numeric text still coerces to 0.
                double term = v is BoolValue bv ? (bv.Value ? 1.0 : 0.0)
                    : TryCellNumber(v, out double value) ? value : 0;
                product *= term;
                if (!double.IsFinite(product)) return ErrorValue.Num;
            }
            total += product;
            if (!double.IsFinite(total)) return ErrorValue.Num;
        }
        // Match the 15-significant-digit rounding applied after every +,-,*,/,^ binary
        // arithmetic result (FormulaEvaluator.Operators.cs) for consistency with SUM and
        // with the arithmetic-operator path.
        return NumberResult(FormulaEvaluator.RoundTo15SignificantDigits(total));
    }

    private static ScalarValue Product(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        double result = 1.0;
        bool sawNumeric = false;
        foreach (var a in args)
        {
            if (a is ErrorValue e) return e;
            if (a is ReferencedScalarValue referenced)
            {
                if (TryReferencedNumber(referenced, out double value, out var refError)) { result *= value; sawNumeric = true; }
                else if (refError is not null) return refError;
                continue;
            }
            if (a is DirectTextLiteralValue direct)
            {
                if (!TryDirectTextNumber(direct, out double value)) return ErrorValue.Value;
                if (!double.IsFinite(value)) return ErrorValue.Num;
                result *= value;
                sawNumeric = true;
            }
            else if (a is NumberValue or BoolValue or DateTimeValue) { result *= ToNumber(a); sawNumeric = true; }
        }
        // Match the 15-significant-digit rounding applied after every +,-,*,/,^ binary
        // arithmetic result (FormulaEvaluator.Operators.cs), mirroring the fix already applied
        // to Sum()/Sumproduct() above, so PRODUCT(range) stays interchangeable with the
        // textually-expanded chain of * over the same cells.
        return NumberResult(sawNumeric ? FormulaEvaluator.RoundTo15SignificantDigits(result) : 0);
    }

    private static ScalarValue SumSq(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        double total = 0;
        foreach (var arg in args)
        {
            if (arg is ErrorValue e) return e;
            if (arg is ReferencedScalarValue referenced)
            {
                if (TryReferencedNumber(referenced, out var referencedNumber, out var referencedError))
                {
                    total += referencedNumber * referencedNumber;
                    if (!double.IsFinite(total)) return ErrorValue.Num;
                }
                else if (referencedError is not null)
                {
                    return referencedError;
                }

                continue;
            }
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
        var firstIsReferenceArray = first is RangeValue;
        var secondIsReferenceArray = second is RangeValue;
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
                if (!TrySumXPairNumber(left, firstIsReferenceArray, out var x, out var leftErrorValue))
                {
                    if (leftErrorValue is not null) return leftErrorValue;
                    continue;
                }

                if (!TrySumXPairNumber(right, secondIsReferenceArray, out var y, out var rightErrorValue))
                {
                    if (rightErrorValue is not null) return rightErrorValue;
                    continue;
                }

                total += map(x, y);
                if (!double.IsFinite(total)) return ErrorValue.Num;
            }

        return NumberResult(total);
    }

    private static bool TrySumXPairNumber(ScalarValue value, bool isReferenceArray, out double number, out ErrorValue? error)
    {
        number = 0;
        error = null;

        if (isReferenceArray)
        {
            if (!TryCellNumber(value, out number))
                return false;

            if (double.IsFinite(number))
                return true;

            error = ErrorValue.Num;
            return false;
        }

        if (IsNonFiniteDirectTextNumber(value))
        {
            error = ErrorValue.Num;
            return false;
        }

        if (TryMathAggregateNumber(value, out number))
            return true;

        error = ErrorValue.Value;
        return false;
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
}
