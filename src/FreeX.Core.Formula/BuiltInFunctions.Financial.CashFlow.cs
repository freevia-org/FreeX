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

        // Measured against Excel: no negative flow is #DIV/0!, but all-negative flows (no positive) give -100%.
        if (npvNeg == 0) return ErrorValue.DivByZero;
        if (npvPos == 0) return new NumberValue(-1);
        if (1 + reinvestRate == 0) return ErrorValue.DivByZero;
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
        // A genuinely-omitted guess argument defaults to 0.1 (Excel's XIRR default guess), but
        // an explicitly-supplied argument that merely evaluates to blank (e.g. a reference to
        // an empty cell) must flow through normal numeric coercion instead -- BlankValue
        // coerces to 0.0 (ToNumber), matching Excel's blank-cell-as-0 rule and mirroring RATE's
        // guessArg handling in BuiltInFunctions.Financial.LoanValues.cs. Defaulting guessArg to
        // NumberValue(0.1) when omitted (rather than leaving it BlankValue) means the single
        // ToNumber(guessValue) below naturally yields 0.1 for "omitted" and 0.0 for "explicit
        // blank".
        var guessArg = args.Count > 2 ? args[2] : new NumberValue(0.1);
        if (guessArg is RangeValue guessRange)
            return MapUnaryTextRange(guessRange, guessValue => XirrScalar(valRange, dateRange, guessValue));
        return XirrScalar(valRange, dateRange, guessArg);
    }

    private static ScalarValue XirrScalar(RangeValue valRange, RangeValue dateRange, ScalarValue guessValue)
    {
        if (guessValue is ErrorValue guessError) return guessError;
        if (guessValue is BoolValue) return ErrorValue.Value; // measured: a boolean guess is #VALUE!
        double guess = ToNumber(guessValue);
        // 1 + guess must be > 0 for the Newton iteration to make sense (mirrors IRR's guard).
        if (!double.IsFinite(guess) || guess <= -1) return ErrorValue.Num;
        if (RangeHasTextOrBoolean(valRange) || RangeHasTextOrBoolean(dateRange)) return ErrorValue.Value;
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
        // Measured against Excel: XIRR does NOT enforce the documented "no date before the first" rule; it
        // truncates each date to a whole serial and uses (serial - first serial) / 365 (negative allowed).
        // Only a negative serial is #NUM!.
        if (!NormalizeDateSerialsToYearFractions(ds)) return ErrorValue.Num;
        // Measured: a zero first cash flow makes Excel's iteration collapse onto a fixed near-zero value.
        if (cf[0] == 0) return NumberResult(2.98023223877E-09);
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
            if (df == 0 || !double.IsFinite(df) || !double.IsFinite(f)) { r = double.NaN; break; }
            double delta = f / df;
            double next = r - delta;
            // A Newton step that would leave the domain (rate <= -100%) is damped back inside it, which lets
            // the iteration reach the (very negative) roots Excel finds for flows like -1000, 1, 1, 1.
            if (next <= -1) next = (r - 1) / 2;
            delta = r - next;
            r = next;
            // Relative tolerance: Excel finds enormous roots (e.g. 1.28E15 for two flows a day apart), which an
            // absolute 1e-10 step can never satisfy in double precision.
            if (Math.Abs(delta) < 1e-10 * Math.Max(1.0, Math.Abs(r))) { converged = true; break; }
        }

        // Newton must actually converge to a verified root within the iteration budget
        // (see IrrCashFlows for the identical rationale/reference to Excel's documented
        // give-up-and-return-#NUM! behavior). A value that merely stayed finite after
        // exhausting the iteration cap, or after Newton diverged, must not be silently
        // replaced by an unrelated root from an unbounded, guess-blind global bisection.
        if (!converged || !double.IsFinite(r)) return ErrorValue.Num;
        // Measured: Excel reaches rates of about 1E15 (two flows a day apart) but gives up on the astronomically
        // larger roots of flows a few days apart, e.g. -100,50,50,50 over consecutive days (1E33).
        if (Math.Abs(r) > 1E16) return ErrorValue.Num;
        // Measured against Excel (guess sweeps over positive- and negative-root flows): its solver only reaches a
        // positive root from a non-negative guess, and it never reaches a negative root from a guess of 1 or more.
        if (r > 1e-6 && guess < 0) return ErrorValue.Num;
        if (r < -1e-6 && guess >= 1) return ErrorValue.Num;
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
        if (rateValue is BoolValue) return ErrorValue.Value;
        double rate = ToNumber(rateValue);
        // Measured against Excel: XNPV(0, ...) and any negative rate are #NUM!.
        if (!double.IsFinite(rate) || rate <= 0) return ErrorValue.Num;
        if (RangeHasTextOrBoolean(valRange) || RangeHasTextOrBoolean(dateRange)) return ErrorValue.Value;

        var (valueCount, valueError) = CountRangeNumbers(valRange);
        if (valueError is not null) return valueError;

        var (dateCount, dateError) = CountRangeNumbers(dateRange);
        if (dateError is not null) return dateError;

        if (valueCount != dateCount || valueCount == 0) return ErrorValue.Num;

        var dateRow = 0;
        var dateCol = 0;
        if (!TryReadNextRangeNumber(dateRange, ref dateRow, ref dateCol, out var firstDateSerial))
            return ErrorValue.Num;

        firstDateSerial = Math.Floor(firstDateSerial);
        if (firstDateSerial < 1) return ErrorValue.Num;
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

            // Excel's documented XNPV contract: "If any number in dates precedes the starting
            // date, XNPV returns the #NUM! error value."
            dateSerial = Math.Floor(dateSerial);
            if (dateSerial < firstDateSerial) return ErrorValue.Num;

            var yearFraction = (dateSerial - firstDateSerial) / 365.0;
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

    /// <summary>
    /// Converts date serials in-place to year-fractions relative to the first (anchor) date.
    /// Returns false if any date precedes the anchor date, matching Excel's documented XIRR
    /// contract that dates must not precede the starting date.
    /// </summary>
    private static bool NormalizeDateSerialsToYearFractions(List<double> serials)
    {
        var firstSerial = Math.Floor(serials[0]);
        for (var i = 0; i < serials.Count; i++)
        {
            var serial = Math.Floor(serials[i]);
            if (serial < 0) return false;
            // Raw serial arithmetic, like Excel: converting through DateTime would collapse serials 59
            // and 60 (the phantom 1900 leap day) into the same calendar day.
            serials[i] = (serial - firstSerial) / 365.0;
        }
        return true;
    }

    /// <summary>True when the range holds a text or boolean cell (Excel answers #VALUE! for these in XIRR/XNPV/FVSCHEDULE).</summary>
    private static bool RangeHasTextOrBoolean(RangeValue range)
    {
        for (int r = 0; r < range.RowCount; r++)
            for (int c = 0; c < range.ColCount; c++)
                if (range.Cells[r, c] is TextValue or BoolValue) return true;
        return false;
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
        {
            double denom = Math.Pow(1 + rate, i + 1);
            // rate == -1 makes every discount factor exactly zero: Excel surfaces this as a plain
            // #DIV/0! (its standard x/0 propagation rule), distinct from XNPV/XIRR's rate<=-1
            // boundary, which those functions document/return as #NUM! instead.
            if (denom == 0) return ErrorValue.DivByZero;
            result += values[i] / denom;
        }
        return NumberResult(result);
    }

    private static ScalarValue Irr(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var valRange = args[0] is RangeValue valuesRange
            ? valuesRange
            : SingleCellArray(args[0]);
        // A genuinely-omitted guess argument defaults to 0.1 (Excel's IRR default guess), but an
        // explicitly-supplied argument that merely evaluates to blank (e.g. a reference to an
        // empty cell) must flow through normal numeric coercion instead -- BlankValue coerces to
        // 0.0 (ToNumber), matching Excel's blank-cell-as-0 rule and mirroring RATE's guessArg
        // handling in BuiltInFunctions.Financial.LoanValues.cs.
        double guess = args.Count > 1 ? ToNumber(args[1]) : 0.1;
        if (!double.IsFinite(guess)) return ErrorValue.Num;
        if (guess <= -1) return ErrorValue.Value; // Excel reports #VALUE! (not #NUM!) for a guess at or below -100%
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
            // Relative tolerance: Excel finds enormous roots (e.g. 1.28E15 for two flows a day apart), which an
            // absolute 1e-10 step can never satisfy in double precision.
            if (Math.Abs(delta) < 1e-10 * Math.Max(1.0, Math.Abs(r))) { converged = true; break; }
        }

        // Newton must actually converge to a verified root within the iteration budget.
        // Matching Excel's documented behavior ("If IRR can't find a result that works
        // after 20 tries, the #NUM! error value is returned"), a value that merely stayed
        // finite after the iteration cap was exhausted (never satisfied the residual/delta
        // convergence checks above), or that came from Newton overshooting past r = -1 /
        // a vanishing derivative, is not a verified root and must return #NUM! — it must
        // never be silently replaced by an unrelated root from an unbounded, guess-blind
        // global bisection over the whole domain.
        if (!converged || !double.IsFinite(r))
        {
            // Excel keeps going where a plain Newton walk from the guess diverges (IRR({-100,10,10,10}) is
            // -0.4244, IRR(E1:E5,10) is 0.1532): scan the log-rate axis for sign changes of the NPV, bisect the
            // brackets and answer with the lowest root (IRR({-1000,3900,-5030,2145},0.405) is 0.1 in Excel).
            var scanned = FindIrrRootByScan(cashflows, guess);
            return scanned is { } root ? new NumberValue(root) : ErrorValue.Num;
        }

        return new NumberValue(r);
    }

    private static double? FindIrrRootByScan(IReadOnlyList<double> cashflows, double guess)
    {
        static double Npv(IReadOnlyList<double> flows, double rate)
        {
            double sum = 0;
            for (int i = 0; i < flows.Count; i++) sum += flows[i] / Math.Pow(1 + rate, i);
            return sum;
        }

        double? best = null;
        double previousRate = Math.Exp(-25d) - 1d;
        double previousValue = Npv(cashflows, previousRate);
        for (double s = -24.95; s <= 12d; s += 0.05)
        {
            double rate = Math.Exp(s) - 1d;
            double value = Npv(cashflows, rate);
            if (double.IsFinite(previousValue) && double.IsFinite(value) &&
                (previousValue == 0d || (previousValue < 0d) != (value < 0d)))
            {
                double lo = previousRate, hi = rate, fLo = previousValue;
                for (int i = 0; i < 200; i++)
                {
                    double mid = (lo + hi) / 2d;
                    double fMid = Npv(cashflows, mid);
                    if ((fMid < 0d) == (fLo < 0d)) { lo = mid; fLo = fMid; } else hi = mid;
                }

                double root = (lo + hi) / 2d;
                if (best is null) best = root; // the lowest root (the scan climbs the rate axis)
            }

            previousRate = rate;
            previousValue = value;
        }

        return best;
    }
}
