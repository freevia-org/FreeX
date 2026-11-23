using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    private static ScalarValue Fisher(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, FisherScalar);
        return FisherScalar(args[0]);
    }

    private static ScalarValue FisherScalar(ScalarValue value)
    {
        double x = ToNumber(value);
        if (!double.IsFinite(x)) return ErrorValue.Num;
        if (x <= -1 || x >= 1) return ErrorValue.Num;
        return NumberResult(0.5 * Math.Log((1 + x) / (1 - x)));
    }

    private static ScalarValue FisherInv(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, FisherInvScalar);
        return FisherInvScalar(args[0]);
    }

    private static ScalarValue FisherInvScalar(ScalarValue value)
    {
        double y = ToNumber(value);
        if (!double.IsFinite(y)) return ErrorValue.Num;
        return NumberResult(Math.Tanh(y)); // exp(2y) overflows long before the result stops being 1
    }

    private static ScalarValue Prob(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        if (args.Count > 3 && args[3] is ErrorValue e3) return e3;

        var xRange = args[0] is RangeValue xr ? xr : SingleCellArray(args[0]);
        var probRange = args[1] is RangeValue pr ? pr : SingleCellArray(args[1]);
        // An omitted or empty upper limit means "exactly the lower limit".
        var upperArg = args.Count > 3 && args[3] is not BlankValue ? args[3] : args[2];
        return MapBinaryMathArgs(args[2], upperArg, (lowerValue, upperValue) => ProbScalar(xRange, probRange, lowerValue, upperValue));
    }

    private static ScalarValue ProbScalar(RangeValue xRange, RangeValue probRange, ScalarValue lowerValue, ScalarValue upperValue)
    {
        // Measured against Excel: only the number of values has to agree (a 1x4 against a 4x1 is fine).
        if (xRange.RowCount * xRange.ColCount != probRange.RowCount * probRange.ColCount) return ErrorValue.NA;
        var xFlat = xRange.Flatten().ToArray();
        var probFlat = probRange.Flatten().ToArray();

        double lower = ToNumber(lowerValue);
        double upper = ToNumber(upperValue);
        if (!double.IsFinite(lower) || !double.IsFinite(upper)) return ErrorValue.Num;

        double probabilitySum = 0;
        double result = 0;
        for (int i = 0; i < xFlat.Length; i++)
        {
            var xCell = xFlat[i];
            var probCell = probFlat[i];
            if (xCell is ErrorValue xError) return xError;
            if (probCell is ErrorValue pError) return pError;
            // Measured against Excel: a non-numeric cell is #NUM! (not #VALUE!), zero and even negative
            // probabilities are accepted as long as every one is <= 1 and the total is exactly 1.
            if (!TryCellNumber(xCell, out double x) || !TryCellNumber(probCell, out double p)) return ErrorValue.Num;
            if (!double.IsFinite(x) || !double.IsFinite(p) || p > 1) return ErrorValue.Num;

            probabilitySum += p;
            if (x >= lower && x <= upper) result += p;
        }

        return Math.Abs(probabilitySum - 1.0) <= 5e-14 ? NumberResult(result) : ErrorValue.Num;
    }
}
