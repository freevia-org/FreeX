using FreeX.Core.Model;

namespace FreeX.Core.Formula;

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
        // 1 + guess must be > 0 for the Newton iteration to make sense (mirrors IRR's guard).
        if (!double.IsFinite(guess) || guess <= -1) return ErrorValue.Num;
        var (vals, ve) = CollectRangeNumbers(valRange);
        var (datesRaw, de) = CollectRangeNumbers(dateRange);
        if (ve is not null) return ve;
        if (de is not null) return de;
        var cf = vals!;
        var ds = datesRaw!;
        if (cf.Count < 2) return ErrorValue.NA;
        if (cf.Count != ds.Count) return ErrorValue.Num;
        // Excel requires at least one positive and one negative cash flow.
        bool xirrHasPositive = false, xirrHasNegative = false;
        for (int i = 0; i < cf.Count; i++)
        {
            if (cf[i] > 0) xirrHasPositive = true;
            else if (cf[i] < 0) xirrHasNegative = true;
        }
        if (!xirrHasPositive || !xirrHasNegative) return ErrorValue.Num;
        NormalizeDateSerialsToYearFractions(ds);
        double r = guess;
        bool converged = false;
        for (int iter = 0; iter < 200; iter++)
        {
            double f = 0, df = 0;
            for (int i = 0; i < cf.Count; i++)
            {
                double t = ds[i];
                double denom = Math.Pow(1 + r, t);
                f  += cf[i] / denom;
                df -= t * cf[i] / (denom * (1 + r));
            }
            if (Math.Abs(df) < 1e-14) { r = double.NaN; break; }
            double delta = f / df;
            r -= delta;
            if (r <= -1) { r = double.NaN; break; }
            if (Math.Abs(delta) < 1e-10) { converged = true; break; }
        }

        // Newton must actually converge to a verified root within the iteration budget
        // (see IrrCashFlows for the identical rationale/reference to Excel's documented
        // give-up-and-return-#NUM! behavior). A value that merely stayed finite after
        // exhausting the iteration cap, or after Newton diverged, must not be silently
        // replaced by an unrelated root from an unbounded, guess-blind global bisection.
        if (!converged || !double.IsFinite(r)) return ErrorValue.Num;
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

        var (valueCount, valueError) = CountRangeNumbers(valRange);
        if (valueError is not null) return valueError;

        var (dateCount, dateError) = CountRangeNumbers(dateRange);
        if (dateError is not null) return dateError;

        if (valueCount != dateCount || valueCount == 0) return ErrorValue.Num;

        var dateRow = 0;
        var dateCol = 0;
        if (!TryReadNextRangeNumber(dateRange, ref dateRow, ref dateCol, out var firstDateSerial))
            return ErrorValue.Num;

        var firstDate = SerialToDate(firstDateSerial);
        var valueRow = 0;
        var valueCol = 0;
        dateRow = 0;
        dateCol = 0;

        double result = 0;
        for (int i = 0; i < valueCount; i++)
        {
            if (!TryReadNextRangeNumber(valRange, ref valueRow, ref valueCol, out var cashFlow) ||
                !TryReadNextRangeNumber(dateRange, ref dateRow, ref dateCol, out var dateSerial))
                return ErrorValue.Num;

            var yearFraction = (SerialToDate(dateSerial) - firstDate).TotalDays / 365.0;
            result += cashFlow / Math.Pow(1 + rate, yearFraction);
        }

        return NumberResult(result);
    }

    private static bool TryReadNextRangeNumber(RangeValue range, ref int row, ref int col, out double number)
    {
        for (; row < range.RowCount; row++)
        {
            for (; col < range.ColCount; col++)
            {
                var value = range.Cells[row, col];
                if (value is NumberValue n)
                {
                    number = n.Value;
                    col++;
                    return true;
                }

                if (value is DateTimeValue d)
                {
                    number = d.Value;
                    col++;
                    return true;
                }
            }

            col = 0;
        }

        number = 0;
        return false;
    }

    private static void NormalizeDateSerialsToYearFractions(List<double> serials)
    {
        var firstDate = SerialToDate(serials[0]);
        for (var i = 0; i < serials.Count; i++)
            serials[i] = (SerialToDate(serials[i]) - firstDate).TotalDays / 365.0;
    }

    private static ScalarValue Npv(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        double rate = ToNumber(args[0]);
        if (!double.IsFinite(rate)) return ErrorValue.Num;
        var (values, err) = CollectNumbers(args, start: 1);
        if (err is not null) return err;

        double result = 0;
        for (int i = 0; i < values!.Count; i++)
            result += values[i] / Math.Pow(1 + rate, i + 1);
        return NumberResult(result);
    }

    private static ScalarValue Irr(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var valRange = args[0] is RangeValue valuesRange
            ? valuesRange
            : SingleCellArray(args[0]);
        double guess = args.Count > 1 && args[1] is not BlankValue ? ToNumber(args[1]) : 0.1;
        if (!double.IsFinite(guess) || guess <= -1) return ErrorValue.Num;
        var (values, err) = CollectRangeNumbers(valRange);
        if (err is not null) return err;
        return IrrCashFlows(values!, guess);
    }

    internal static ScalarValue IrrCashFlows(IReadOnlyList<double> cashflows, double guess)
    {
        if (cashflows.Count < 2) return ErrorValue.Num;
        // Excel requires at least one positive and one negative cashflow.
        bool hasPositive = false, hasNegative = false;
        for (int i = 0; i < cashflows.Count; i++)
        {
            if (cashflows[i] > 0) hasPositive = true;
            else if (cashflows[i] < 0) hasNegative = true;
        }
        if (!hasPositive || !hasNegative) return ErrorValue.Num;

        double r = guess;
        bool converged = false;
        for (int iter = 0; iter < 100; iter++)
        {
            double f = 0, df = 0;
            for (int i = 0; i < cashflows.Count; i++)
            {
                double denom = Math.Pow(1 + r, i);
                f += cashflows[i] / denom;
                if (i > 0) df -= i * cashflows[i] / (denom * (1 + r));
            }
            if (Math.Abs(f) < 1e-10) { converged = true; break; }
            if (Math.Abs(df) < 1e-15) { r = double.NaN; break; }
            double delta = f / df;
            r -= delta;
            if (r <= -1) { r = double.NaN; break; }
            if (Math.Abs(delta) < 1e-10) { converged = true; break; }
        }

        // Newton must actually converge to a verified root within the iteration budget.
        // Matching Excel's documented behavior ("If IRR can't find a result that works
        // after 20 tries, the #NUM! error value is returned"), a value that merely stayed
        // finite after the iteration cap was exhausted (never satisfied the residual/delta
        // convergence checks above), or that came from Newton overshooting past r = -1 /
        // a vanishing derivative, is not a verified root and must return #NUM! — it must
        // never be silently replaced by an unrelated root from an unbounded, guess-blind
        // global bisection over the whole domain.
        if (!converged || !double.IsFinite(r)) return ErrorValue.Num;
        return new NumberValue(r);
    }
}
