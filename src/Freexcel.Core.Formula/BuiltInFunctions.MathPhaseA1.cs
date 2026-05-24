using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

public static partial class BuiltInFunctions
{
    // Phase A1 math functions: SQRTPI, MULTINOMIAL, SERIESSUM.

    private static ScalarValue Sqrtpi(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, SqrtpiScalar);
        return SqrtpiScalar(args[0]);
    }

    private static ScalarValue SqrtpiScalar(ScalarValue value)
    {
        var n = ToNumber(value);
        if (!double.IsFinite(n) || n < 0) return ErrorValue.Num;
        return NumberResult(Math.Sqrt(n * Math.PI));
    }

    private static ScalarValue Multinomial(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var values = new List<int>();
        long sum = 0;
        foreach (var arg in args)
        {
            if (arg is ErrorValue err) return err;
            if (arg is RangeValue rv)
            {
                foreach (var cell in rv.Flatten())
                {
                    if (cell is ErrorValue ec) return ec;
                    if (cell is BlankValue) continue;
                    if (!TryCellNumber(cell, out double d)) return ErrorValue.Value;
                    if (!double.IsFinite(d) || d < 0) return ErrorValue.Num;
                    int n = (int)Math.Truncate(d);
                    values.Add(n);
                    sum += n;
                }
            }
            else
            {
                if (arg is BlankValue) continue;
                double d;
                if (!TryCellNumber(arg, out d))
                {
                    d = ToNumber(arg);
                }
                if (!double.IsFinite(d) || d < 0) return ErrorValue.Num;
                int n = (int)Math.Truncate(d);
                values.Add(n);
                sum += n;
            }
        }
        if (values.Count == 0) return ErrorValue.Value;

        // Use log-gamma to avoid overflow: log(sum!) - sum(log(n_i!))
        double logResult = LogGamma(sum + 1.0);
        foreach (var v in values)
            logResult -= LogGamma(v + 1.0);

        if (logResult > Math.Log(1e308)) return ErrorValue.Num;
        double result = Math.Round(Math.Exp(logResult));
        return NumberResult(result);
    }

    private static ScalarValue SeriesSum(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        if (args[3] is ErrorValue e3) return e3;

        double n = ToNumber(args[1]);
        double m = ToNumber(args[2]);
        if (!double.IsFinite(n) || !double.IsFinite(m))
            return ErrorValue.Num;

        var coeffs = args[3] is RangeValue coeffRange
            ? coeffRange
            : SingleCellArray(args[3]);

        if (args[0] is RangeValue xRange) return MapUnaryTextRange(xRange, value => SeriesSumScalar(value, n, m, coeffs));
        return SeriesSumScalar(args[0], n, m, coeffs);
    }

    private static ScalarValue SeriesSumScalar(ScalarValue xValue, double n, double m, RangeValue coeffs)
    {
        double x = ToNumber(xValue);
        if (!double.IsFinite(x)) return ErrorValue.Num;

        double sum = 0;
        int i = 0;
        foreach (var cell in coeffs.Flatten())
        {
            if (cell is ErrorValue ec) return ec;
            if (cell is BlankValue) { i++; continue; }
            if (!TryCellNumber(cell, out double a)) return ErrorValue.Value;
            sum += a * Math.Pow(x, n + i * m);
            i++;
        }
        return NumberResult(sum);
    }
}
