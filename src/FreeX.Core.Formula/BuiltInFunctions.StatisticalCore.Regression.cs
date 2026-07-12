using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    private static ScalarValue Correl(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var (xs, ys, pairErr) = CollectPairedNumbers(args[0], args[1]);
        if (pairErr is not null) return pairErr;
        return Correlation(xs, ys);
    }

    private static ScalarValue Pearson(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        Correl(args, ctx);

    private static ScalarValue Rsq(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var correlation = Correl(args, ctx);
        return correlation is NumberValue nv ? NumberResult(nv.Value * nv.Value) : correlation;
    }

    private static ScalarValue CovarianceP(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        Covariance(args, sample: false);

    private static ScalarValue CovarianceS(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        Covariance(args, sample: true);

    private static ScalarValue Covariance(IReadOnlyList<ScalarValue> args, bool sample)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var (xs, ys, pairErr) = CollectPairedNumbers(args[0], args[1]);
        if (pairErr is not null) return pairErr;
        var stats = RegressionStats(xs, ys);
        if (stats.Error is not null) return stats.Error;
        if (sample && stats.Count < 2) return ErrorValue.DivByZero;
        return NumberResult(stats.Sxy / (sample ? stats.Count - 1 : stats.Count));
    }

    private static ScalarValue Forecast(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        double x    = ToNumber(args[0]);
        if (!double.IsFinite(x)) return ErrorValue.Num;
        var (ys, xs, pairErr) = CollectPairedNumbers(args[1], args[2]);
        if (pairErr is not null) return pairErr;
        var fit = LinearFit(ys, xs);
        if (fit.Error is not null) return fit.Error;
        return NumberResult(fit.Intercept + fit.Slope * x);
    }

    private static ScalarValue Slope(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var (ys, xs, pairErr) = CollectPairedNumbers(args[0], args[1]);
        if (pairErr is not null) return pairErr;
        var fit = LinearFit(ys, xs);
        return fit.Error is not null ? fit.Error : NumberResult(fit.Slope);
    }

    private static ScalarValue Intercept(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var (ys, xs, pairErr) = CollectPairedNumbers(args[0], args[1]);
        if (pairErr is not null) return pairErr;
        var fit = LinearFit(ys, xs);
        return fit.Error is not null ? fit.Error : NumberResult(fit.Intercept);
    }

    private static ScalarValue Steyx(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var (ys, xs, pairErr) = CollectPairedNumbers(args[0], args[1]);
        if (pairErr is not null) return pairErr;
        var fit = LinearFit(ys, xs);
        if (fit.Error is not null) return fit.Error;
        if (xs.Count < 3) return ErrorValue.DivByZero;

        double residualSumSquares = 0.0;
        for (var i = 0; i < xs.Count; i++)
        {
            var predicted = fit.Intercept + fit.Slope * xs[i];
            var residual = ys[i] - predicted;
            residualSumSquares += residual * residual;
        }

        return NumberResult(Math.Sqrt(residualSumSquares / (xs.Count - 2)));
    }

    private static (List<double> Left, List<double> Right, ErrorValue? Error) CollectPairedRangeNumbers(RangeValue left, RangeValue right)
    {
        return CollectPairedNumbers(left, right);
    }

    private static (List<double> Left, List<double> Right, ErrorValue? Error) CollectPairedNumbers(ScalarValue left, ScalarValue right)
    {
        var leftSource = BuildPairedSource(left);
        if (leftSource.Error is not null) return ([], [], leftSource.Error);
        var rightSource = BuildPairedSource(right);
        if (rightSource.Error is not null) return ([], [], rightSource.Error);

        if (leftSource.Count != rightSource.Count)
            return ([], [], ErrorValue.NA);

        var leftValues = new List<double>();
        var rightValues = new List<double>();
        for (int i = 0; i < leftSource.Count; i++)
        {
            if (leftSource.Values[i] is double leftNumber &&
                rightSource.Values[i] is double rightNumber)
            {
                leftValues.Add(leftNumber);
                rightValues.Add(rightNumber);
            }
        }

        return (leftValues, rightValues, null);
    }

    private static (int Count, List<double?> Values, ErrorValue? Error) BuildPairedSource(ScalarValue value)
    {
        if (value is ErrorValue error) return (0, [], error);
        if (value is RangeValue range)
        {
            var values = new List<double?>(range.RowCount * range.ColCount);
            for (var r = 0; r < range.RowCount; r++)
            {
                for (var c = 0; c < range.ColCount; c++)
                {
                    var cell = range.Cells[r, c];
                    if (cell is ErrorValue cellError) return (0, [], cellError);
                    values.Add(TryCellNumber(cell, out var number) ? number : null);
                }
            }

            return (values.Count, values, null);
        }

        if (value is ReferencedScalarValue referenced)
        {
            // A bare single-cell reference (e.g. A1, as opposed to a colon-range like A1:A1)
            // arrives wrapped in ReferencedScalarValue. Unwrap it the same way every other
            // ReferenceProvenanceAggregate function does (see TryReferencedNumber usages in
            // BuiltInFunctions.StatisticalCore.Aggregates.cs) instead of calling ToNumber on
            // the wrapper itself, which has no case for it and throws #VALUE!.
            if (TryReferencedNumber(referenced, out var refNumber, out var refError))
                return (1, [refNumber], null);
            if (refError is not null) return (0, [], refError);
            return (1, [null], null);
        }

        return (1, [ToNumber(value)], null);
    }

    private static ScalarValue Correlation(List<double> xs, List<double> ys)
    {
        var stats = RegressionStats(xs, ys);
        if (stats.Error is not null) return stats.Error;
        if (stats.Count < 2) return ErrorValue.DivByZero;
        if (stats.Sxx == 0 || stats.Syy == 0) return ErrorValue.DivByZero;
        return NumberResult(stats.Sxy / Math.Sqrt(stats.Sxx * stats.Syy));
    }

    private static (int Count, double Sxx, double Sxy, double Syy, ErrorValue? Error) RegressionStats(List<double> xs, List<double> ys)
    {
        var n = xs.Count;
        if (n == 0) return (0, 0, 0, 0, ErrorValue.DivByZero);
        var xMean = xs.Average();
        var yMean = ys.Average();
        double sxx = 0, sxy = 0, syy = 0;
        for (var i = 0; i < n; i++)
        {
            var dx = xs[i] - xMean;
            var dy = ys[i] - yMean;
            sxx += dx * dx;
            sxy += dx * dy;
            syy += dy * dy;
        }

        return (n, sxx, sxy, syy, null);
    }

    private static (double Slope, double Intercept, ErrorValue? Error) LinearFit(List<double> ys, List<double> xs)
    {
        var stats = RegressionStats(xs, ys);
        if (stats.Error is not null) return (0, 0, stats.Error);
        if (stats.Count < 2 || stats.Sxx == 0) return (0, 0, ErrorValue.DivByZero);
        var slope = stats.Sxy / stats.Sxx;
        var intercept = ys.Average() - slope * xs.Average();
        return (slope, intercept, null);
    }
}
