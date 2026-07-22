using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    // -- Loan payment breakdown helpers -----------------------------------

    private static double CalcPmt(double rate, double nper, double pv, double fv, int type)
    {
        if (Math.Abs(rate) < 1e-14) return -(pv + fv) / nper;
        double r1 = Math.Pow(1 + rate, nper);
        return -(pv * r1 + fv) * rate / ((1 + rate * type) * (r1 - 1));
    }

    private static double CalcIpmt(double rate, double per, double nper, double pv, double fv, int type)
    {
        if (Math.Abs(rate) < 1e-14) return 0.0;
        // Excel: for type=1 (annuity-due), payments occur at the START of each period.
        // The period-1 payment happens before any interest has had a chance to accrue, so
        // its interest component is always 0. For per >= 2, the interest owed is charged on
        // the balance that remained outstanding after the previous (type=1) payment - i.e.
        // the standard annuity-due amortization recursion:
        //   Owed[1] = pv + pmt                      (payment 1 is pure principal)
        //   Owed[j] = Owed[j-1]*(1+rate) + pmt       for j = 2..nper
        // using the type=1 payment amount (already correctly discounted by CalcPmt).
        if (type == 1)
        {
            if (per <= 1) return 0.0;
            double pmt1 = CalcPmt(rate, nper, pv, fv, 1);
            double m = per - 2; // periods of growth since the balance became (pv + pmt1)
            double growth = Math.Pow(1 + rate, m);
            double owedBeforeThisPeriod = (pv + pmt1) * growth + pmt1 * (growth - 1) / rate;
            return -(owedBeforeThisPeriod * rate);
        }
        double pmt = CalcPmt(rate, nper, pv, fv, 0);
        double pvAtPer = pv * Math.Pow(1 + rate, per - 1)
                       + pmt * (Math.Pow(1 + rate, per - 1) - 1) / rate;
        // Interest payment matches PMT sign convention: negative = outflow (borrower)
        return -(pvAtPer * rate);
    }

    private static ScalarValue Ispmt(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        return MapScalarArgs(args, values => IspmtScalar(values[0], values[1], values[2], values[3]));
    }

    private static ScalarValue IspmtScalar(ScalarValue rateValue, ScalarValue periodValue, ScalarValue nperValue, ScalarValue pvValue)
    {
        if (rateValue is ErrorValue rateError) return rateError;
        if (periodValue is ErrorValue periodError) return periodError;
        if (nperValue is ErrorValue nperError) return nperError;
        if (pvValue is ErrorValue pvError) return pvError;

        double rate = ToNumber(rateValue);
        double per = Math.Truncate(ToNumber(periodValue));
        double nper = ToNumber(nperValue);
        double pv = ToNumber(pvValue);
        if (!double.IsFinite(rate) || !double.IsFinite(per) || !double.IsFinite(nper) || !double.IsFinite(pv))
            return ErrorValue.Num;
        if (nper <= 0 || per < 0 || per > nper) return ErrorValue.Num;

        return NumberResult(-pv * rate * (nper - per) / nper);
    }

    private static ScalarValue Ipmt(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var fvArg = args.Count > 4 && args[4] is not BlankValue ? args[4] : new NumberValue(0);
        var typeArg = args.Count > 5 && args[5] is not BlankValue ? args[5] : new NumberValue(0);
        return MapScalarArgs([args[0], args[1], args[2], args[3], fvArg, typeArg], values => IpmtScalar(values[0], values[1], values[2], values[3], values[4], values[5]));
    }

    private static ScalarValue IpmtScalar(ScalarValue rateValue, ScalarValue periodValue, ScalarValue nperValue, ScalarValue pvValue, ScalarValue fvValue, ScalarValue typeValue)
    {
        if (rateValue is ErrorValue rateError) return rateError;
        if (periodValue is ErrorValue periodError) return periodError;
        if (nperValue is ErrorValue nperError) return nperError;
        if (pvValue is ErrorValue pvError) return pvError;
        if (fvValue is ErrorValue fvError) return fvError;
        if (typeValue is ErrorValue typeError) return typeError;
        return IpmtScalar(ToNumber(rateValue), periodValue, ToNumber(nperValue), ToNumber(pvValue), ToNumber(fvValue), ToNumber(typeValue));
    }

    private static ScalarValue IpmtScalar(double rate, ScalarValue periodValue, double nper, double pv, double fv, double type)
    {
        double per = ToNumber(periodValue);
        if (!double.IsFinite(rate) || !double.IsFinite(per) || !double.IsFinite(nper) ||
            !double.IsFinite(pv)   || !double.IsFinite(fv)  || !double.IsFinite(type))
            return ErrorValue.Num;
        // Excel requires type to be exactly 0 or 1 and rejects any other numeric value with
        // #NUM! — validate the RAW value before truncating (R50-formula-financial-loan-3-1),
        // matching IsValidPaymentType's non-truncating check used by PMT/PV/FV/NPER/RATE.
        if (type != 0 && type != 1) return ErrorValue.Num;
        int itype = (int)type;
        if (nper <= 0) return ErrorValue.Num;
        int iper = (int)Math.Truncate(per);
        if (iper < 1 || iper > (int)Math.Truncate(nper)) return ErrorValue.Num;
        return NumberResult(CalcIpmt(rate, iper, nper, pv, fv, itype));
    }

    private static ScalarValue Ppmt(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var fvArg = args.Count > 4 && args[4] is not BlankValue ? args[4] : new NumberValue(0);
        var typeArg = args.Count > 5 && args[5] is not BlankValue ? args[5] : new NumberValue(0);
        return MapScalarArgs([args[0], args[1], args[2], args[3], fvArg, typeArg], values => PpmtScalar(values[0], values[1], values[2], values[3], values[4], values[5]));
    }

    private static ScalarValue PpmtScalar(ScalarValue rateValue, ScalarValue periodValue, ScalarValue nperValue, ScalarValue pvValue, ScalarValue fvValue, ScalarValue typeValue)
    {
        if (rateValue is ErrorValue rateError) return rateError;
        if (periodValue is ErrorValue periodError) return periodError;
        if (nperValue is ErrorValue nperError) return nperError;
        if (pvValue is ErrorValue pvError) return pvError;
        if (fvValue is ErrorValue fvError) return fvError;
        if (typeValue is ErrorValue typeError) return typeError;
        return PpmtScalar(ToNumber(rateValue), periodValue, ToNumber(nperValue), ToNumber(pvValue), ToNumber(fvValue), ToNumber(typeValue));
    }

    private static ScalarValue PpmtScalar(double rate, ScalarValue periodValue, double nper, double pv, double fv, double type)
    {
        double per = ToNumber(periodValue);
        if (!double.IsFinite(rate) || !double.IsFinite(per) || !double.IsFinite(nper) ||
            !double.IsFinite(pv)   || !double.IsFinite(fv)  || !double.IsFinite(type))
            return ErrorValue.Num;
        // Validate the RAW type value before truncating — see IpmtScalar (R50-formula-financial-loan-3-1).
        if (type != 0 && type != 1) return ErrorValue.Num;
        int itype = (int)type;
        if (nper <= 0) return ErrorValue.Num;
        int iper = (int)Math.Truncate(per);
        if (iper < 1 || iper > (int)Math.Truncate(nper)) return ErrorValue.Num;
        double pmt  = CalcPmt(rate, nper, pv, fv, itype);
        double ipmt = CalcIpmt(rate, iper, nper, pv, fv, itype);
        return NumberResult(pmt - ipmt);
    }

    /// <summary>
    /// Closed-form outstanding-principal balance after <paramref name="k"/> payments (k = 0..nper)
    /// for a standard amortization schedule with payment <paramref name="pmt"/> (as produced by
    /// CalcPmt with fv = 0). Used by CUMIPMT/CUMPRINC to avoid an O(nper) per-period loop, which
    /// would hang for a bounds-valid-but-huge nper (e.g. billions of periods) even though the
    /// requested start/end span is small.
    /// For type = 0 (ordinary annuity) this is the standard TVM balance recurrence:
    ///   Balance(k) = pv*(1+rate)^k + pmt*((1+rate)^k - 1)/rate.
    /// For type = 1 (annuity-due) payment 1 is pure principal with no interest accrued before it
    /// (mirrors CalcIpmt's per &lt;= 1 special case), so the same recurrence applies starting from
    /// Balance(1) = pv + pmt instead of Balance(0) = pv.
    /// Because PPMT(per) = pmt - IPMT(per) for every period by construction (see PpmtScalar), and
    /// Balance(per) = Balance(per-1) + PPMT(per) telescopes, the cumulative principal paid over
    /// [start, end] is simply CumulativeBalance(end) - CumulativeBalance(start-1).
    /// </summary>
    private static double CumulativeBalance(double rate, double pmt, double pv, int type, int k)
    {
        if (k <= 0) return pv;
        double g = 1 + rate;
        if (type == 1)
        {
            double gp = Math.Pow(g, k - 1);
            return (pv + pmt) * gp + pmt * (gp - 1) / rate;
        }
        double gpOrdinary = Math.Pow(g, k);
        return pv * gpOrdinary + pmt * (gpOrdinary - 1) / rate;
    }

    private static ScalarValue Cumipmt(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        return MapScalarArgs(args, values => CumipmtScalar(values[0], values[1], values[2], values[3], values[4], values[5]));
    }

    private static ScalarValue CumipmtScalar(ScalarValue rateValue, ScalarValue nperValue, ScalarValue pvValue, ScalarValue startValue, ScalarValue endValue, ScalarValue typeValue)
    {
        if (rateValue is ErrorValue rateError) return rateError;
        if (nperValue is ErrorValue nperError) return nperError;
        if (pvValue is ErrorValue pvError) return pvError;
        if (startValue is ErrorValue startError) return startError;
        if (endValue is ErrorValue endError) return endError;
        if (typeValue is ErrorValue typeError) return typeError;
        return CumipmtScalar(ToNumber(rateValue), ToNumber(nperValue), ToNumber(pvValue), startValue, ToNumber(endValue), ToNumber(typeValue));
    }

    private static ScalarValue CumipmtScalar(double rate, double nper, double pv, ScalarValue startValue, double end, double type)
    {
        double start = ToNumber(startValue);
        if (!double.IsFinite(rate) || !double.IsFinite(nper) || !double.IsFinite(pv) ||
            !double.IsFinite(start) || !double.IsFinite(end) || !double.IsFinite(type))
            return ErrorValue.Num;
        // Validate the RAW type value before truncating — see IpmtScalar (R50-formula-financial-loan-3-1).
        if (type != 0 && type != 1) return ErrorValue.Num;
        int itype  = (int)type;
        if (rate <= 0 || nper <= 0 || pv <= 0) return ErrorValue.Num;
        int is_ = (int)Math.Truncate(start), ie = (int)Math.Truncate(end);
        if (is_ < 1 || ie < is_ || ie > (int)Math.Truncate(nper)) return ErrorValue.Num;
        // Closed form: IPMT(per) + PPMT(per) = pmt for every period by construction, so the
        // cumulative interest over [is_, ie] is just the total payments over the span minus the
        // cumulative principal (see CumulativeBalance) -- this avoids an O(nper) loop that would
        // hang for a bounds-valid-but-huge nper.
        double pmt = CalcPmt(rate, nper, pv, 0, itype);
        double cumPrinc = CumulativeBalance(rate, pmt, pv, itype, ie) - CumulativeBalance(rate, pmt, pv, itype, is_ - 1);
        double count = ie - is_ + 1;
        return NumberResult(count * pmt - cumPrinc);
    }

    private static ScalarValue Cumprinc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        return MapScalarArgs(args, values => CumprincScalar(values[0], values[1], values[2], values[3], values[4], values[5]));
    }

    private static ScalarValue CumprincScalar(ScalarValue rateValue, ScalarValue nperValue, ScalarValue pvValue, ScalarValue startValue, ScalarValue endValue, ScalarValue typeValue)
    {
        if (rateValue is ErrorValue rateError) return rateError;
        if (nperValue is ErrorValue nperError) return nperError;
        if (pvValue is ErrorValue pvError) return pvError;
        if (startValue is ErrorValue startError) return startError;
        if (endValue is ErrorValue endError) return endError;
        if (typeValue is ErrorValue typeError) return typeError;
        return CumprincScalar(ToNumber(rateValue), ToNumber(nperValue), ToNumber(pvValue), startValue, ToNumber(endValue), ToNumber(typeValue));
    }

    private static ScalarValue CumprincScalar(double rate, double nper, double pv, ScalarValue startValue, double end, double type)
    {
        double start = ToNumber(startValue);
        if (!double.IsFinite(rate) || !double.IsFinite(nper) || !double.IsFinite(pv) ||
            !double.IsFinite(start) || !double.IsFinite(end) || !double.IsFinite(type))
            return ErrorValue.Num;
        // Validate the RAW type value before truncating — see IpmtScalar (R50-formula-financial-loan-3-1).
        if (type != 0 && type != 1) return ErrorValue.Num;
        int itype = (int)type;
        if (rate <= 0 || nper <= 0 || pv <= 0) return ErrorValue.Num;
        int is_ = (int)Math.Truncate(start), ie = (int)Math.Truncate(end);
        if (is_ < 1 || ie < is_ || ie > (int)Math.Truncate(nper)) return ErrorValue.Num;
        // Closed form via CumulativeBalance (see its doc comment) -- avoids an O(nper) loop that
        // would hang for a bounds-valid-but-huge nper.
        double pmt = CalcPmt(rate, nper, pv, 0, itype);
        double sum = CumulativeBalance(rate, pmt, pv, itype, ie) - CumulativeBalance(rate, pmt, pv, itype, is_ - 1);
        return NumberResult(sum);
    }
}
