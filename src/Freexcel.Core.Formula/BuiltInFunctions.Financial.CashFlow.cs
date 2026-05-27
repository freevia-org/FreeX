using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

public static partial class BuiltInFunctions
{
    // -- Financial cash-flow helpers --------------------------------------

    private static ScalarValue Mirr(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var valRange = args[0] is RangeValue valuesRange
            ? valuesRange
            : SingleCellArray(args[0]);
        return MapBinaryMathArgs(args[1], args[2], (financeRateValue, reinvestRateValue) => MirrScalar(valRange, financeRateValue, reinvestRateValue));
    }

    private static ScalarValue MirrScalar(RangeValue valRange, ScalarValue financeRateValue, ScalarValue reinvestRateValue)
    {
        double financeRate  = ToNumber(financeRateValue);
        double reinvestRate = ToNumber(reinvestRateValue);
        if (!double.IsFinite(financeRate) || !double.IsFinite(reinvestRate)) return ErrorValue.Num;
        var (values, err) = CollectRangeNumbers(valRange);
        if (err is not null) return err;
        var cf = values!;
        int n = cf.Count;
        if (n < 2) return ErrorValue.DivByZero;

        double npvNeg = 0;
        for (int i = 0; i < n; i++)
            if (cf[i] < 0) npvNeg += cf[i] / Math.Pow(1 + financeRate, i);

        double npvPos = 0;
        for (int i = 0; i < n; i++)
            if (cf[i] > 0) npvPos += cf[i] / Math.Pow(1 + reinvestRate, i);

        if (npvNeg == 0 || npvPos == 0) return ErrorValue.DivByZero;
        double mirr = Math.Pow((-npvPos * Math.Pow(1 + reinvestRate, n - 1)) / npvNeg, 1.0 / (n - 1)) - 1;
        return NumberResult(mirr);
    }

    private static ScalarValue Xirr(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var valRange = args[0] is RangeValue valuesRange
            ? valuesRange
            : SingleCellArray(args[0]);
        var dateRange = args[1] is RangeValue datesRange
            ? datesRange
            : SingleCellArray(args[1]);
        var guessArg = args.Count > 2 ? args[2] : new NumberValue(0.1);
        if (guessArg is RangeValue guessRange)
            return MapUnaryTextRange(guessRange, guessValue => XirrScalar(valRange, dateRange, guessValue));
        return XirrScalar(valRange, dateRange, guessArg);
    }

    private static ScalarValue XirrScalar(RangeValue valRange, RangeValue dateRange, ScalarValue guessValue)
    {
        if (guessValue is ErrorValue guessError) return guessError;
        double guess = guessValue is not BlankValue ? ToNumber(guessValue) : 0.1;
        var (vals, ve) = CollectRangeNumbers(valRange);
        var (datesRaw, de) = CollectRangeNumbers(dateRange);
        if (ve is not null) return ve;
        if (de is not null) return de;
        var cf = vals!;
        var ds = datesRaw!;
        if (cf.Count < 2) return ErrorValue.NA;
        if (cf.Count != ds.Count) return ErrorValue.Num;
        var dates = ds.Select(SerialToDate).ToList();
        DateTime d0 = dates[0];
        double r = guess;
        for (int iter = 0; iter < 200; iter++)
        {
            double f = 0, df = 0;
            for (int i = 0; i < cf.Count; i++)
            {
                double t = (dates[i] - d0).TotalDays / 365.0;
                double denom = Math.Pow(1 + r, t);
                f  += cf[i] / denom;
                df -= t * cf[i] / (denom * (1 + r));
            }
            if (Math.Abs(df) < 1e-14) break;
            double delta = f / df;
            r -= delta;
            if (Math.Abs(delta) < 1e-10) break;
        }
        if (!double.IsFinite(r)) return ErrorValue.Num;
        return NumberResult(r);
    }

    private static ScalarValue Xnpv(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var valRange = args[1] is RangeValue valuesRange
            ? valuesRange
            : SingleCellArray(args[1]);
        var dateRange = args[2] is RangeValue datesRange
            ? datesRange
            : SingleCellArray(args[2]);
        if (args[0] is RangeValue rateRange)
            return MapUnaryTextRange(rateRange, rateValue => XnpvScalar(rateValue, valRange, dateRange));
        return XnpvScalar(args[0], valRange, dateRange);
    }

    private static ScalarValue XnpvScalar(ScalarValue rateValue, RangeValue valRange, RangeValue dateRange)
    {
        double rate = ToNumber(rateValue);
        if (!double.IsFinite(rate) || rate <= -1) return ErrorValue.Num;
        var (vals, ve) = CollectRangeNumbers(valRange);
        var (datesRaw, de) = CollectRangeNumbers(dateRange);
        if (ve is not null) return ve;
        if (de is not null) return de;
        var cf = vals!;
        var ds = datesRaw!;
        if (cf.Count != ds.Count || cf.Count == 0) return ErrorValue.Num;
        var dates = ds.Select(SerialToDate).ToList();
        DateTime d0 = dates[0];
        double result = 0;
        for (int i = 0; i < cf.Count; i++)
        {
            double t = (dates[i] - d0).TotalDays / 365.0;
            result += cf[i] / Math.Pow(1 + rate, t);
        }
        return NumberResult(result);
    }
}
