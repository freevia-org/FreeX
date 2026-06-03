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
        double exp = Math.Exp(2 * y);
        return NumberResult((exp - 1) / (exp + 1));
    }

    private static ScalarValue Prob(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        if (args.Count > 3 && args[3] is ErrorValue e3) return e3;

        var xRange = args[0] is RangeValue xr ? xr : SingleCellArray(args[0]);
        var probRange = args[1] is RangeValue pr ? pr : SingleCellArray(args[1]);
        var upperArg = args.Count > 3 ? args[3] : args[2];
        return MapBinaryMathArgs(args[2], upperArg, (lowerValue, upperValue) => ProbScalar(xRange, probRange, lowerValue, upperValue));
    }

    private static ScalarValue ProbScalar(RangeValue xRange, RangeValue probRange, ScalarValue lowerValue, ScalarValue upperValue)
    {
        if (xRange.RowCount != probRange.RowCount || xRange.ColCount != probRange.ColCount) return ErrorValue.NA;

        double lower = ToNumber(lowerValue);
        double upper = ToNumber(upperValue);
        if (!double.IsFinite(lower) || !double.IsFinite(upper)) return ErrorValue.Num;

        double probabilitySum = 0;
        double result = 0;
        for (int r = 0; r < xRange.RowCount; r++)
        {
            for (int c = 0; c < xRange.ColCount; c++)
            {
                var xCell = xRange.Cells[r, c];
                var probCell = probRange.Cells[r, c];
                if (xCell is ErrorValue xError) return xError;
                if (probCell is ErrorValue pError) return pError;
                if (!TryCellNumber(xCell, out double x) || !TryCellNumber(probCell, out double p)) return ErrorValue.Value;
                if (!double.IsFinite(x) || !double.IsFinite(p) || p <= 0 || p > 1) return ErrorValue.Num;

                probabilitySum += p;
                if (x >= lower && x <= upper) result += p;
            }
        }

        return Math.Abs(probabilitySum - 1.0) <= 1e-12 ? NumberResult(result) : ErrorValue.Num;
    }
}
