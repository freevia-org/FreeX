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
        double pmt = CalcPmt(rate, nper, pv, fv, type);
        if (Math.Abs(rate) < 1e-14) return 0.0;
        double pvAtPer = pv * Math.Pow(1 + rate, per - 1)
                       + pmt * (1 + rate * type) * (Math.Pow(1 + rate, per - 1) - 1) / rate;
        // Interest payment matches PMT sign convention: negative = outflow (borrower)
        return type == 0 ? -(pvAtPer * rate) : -((pvAtPer - pmt) * rate);
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
        int itype = (int)Math.Truncate(type);
        if (itype != 0 && itype != 1) return ErrorValue.Num;
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
        int itype = (int)Math.Truncate(type);
        if (itype != 0 && itype != 1) return ErrorValue.Num;
        if (nper <= 0) return ErrorValue.Num;
        int iper = (int)Math.Truncate(per);
        if (iper < 1 || iper > (int)Math.Truncate(nper)) return ErrorValue.Num;
        double pmt  = CalcPmt(rate, nper, pv, fv, itype);
        double ipmt = CalcIpmt(rate, iper, nper, pv, fv, itype);
        return NumberResult(pmt - ipmt);
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
        int itype  = (int)Math.Truncate(type);
        if (itype != 0 && itype != 1) return ErrorValue.Num;
        if (rate <= 0 || nper <= 0 || pv <= 0) return ErrorValue.Num;
        int is_ = (int)Math.Truncate(start), ie = (int)Math.Truncate(end);
        if (is_ < 1 || ie < is_ || ie > (int)Math.Truncate(nper)) return ErrorValue.Num;
        double sum = 0;
        for (int per = is_; per <= ie; per++)
            sum += CalcIpmt(rate, per, nper, pv, 0, itype);
        return NumberResult(sum);
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
        int itype = (int)Math.Truncate(type);
        if (itype != 0 && itype != 1) return ErrorValue.Num;
        if (rate <= 0 || nper <= 0 || pv <= 0) return ErrorValue.Num;
        int is_ = (int)Math.Truncate(start), ie = (int)Math.Truncate(end);
        if (is_ < 1 || ie < is_ || ie > (int)Math.Truncate(nper)) return ErrorValue.Num;
        double pmt = CalcPmt(rate, nper, pv, 0, itype);
        double sum = 0;
        for (int per = is_; per <= ie; per++)
            sum += pmt - CalcIpmt(rate, per, nper, pv, 0, itype);
        return NumberResult(sum);
    }
}
