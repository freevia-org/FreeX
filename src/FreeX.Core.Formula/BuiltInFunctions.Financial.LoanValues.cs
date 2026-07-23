using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    // -- Core loan value helpers ------------------------------------------

    private static bool IsValidPaymentType(double type) =>
        double.IsFinite(type) && (type == 0 || type == 1);

    private static ScalarValue Pmt(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var fvArg = args.Count > 3 && args[3] is not BlankValue ? args[3] : new NumberValue(0);
        var typeArg = args.Count > 4 && args[4] is not BlankValue ? args[4] : new NumberValue(0);
        return MapScalarArgs([args[0], args[1], args[2], fvArg, typeArg], values => PmtScalar(values[0], values[1], values[2], values[3], values[4]));
    }

    private static ScalarValue PmtScalar(ScalarValue rateValue, ScalarValue nperValueArg, ScalarValue pvValue, ScalarValue fvValue, ScalarValue typeValue)
    {
        if (rateValue is ErrorValue rateError) return rateError;
        if (nperValueArg is ErrorValue nperError) return nperError;
        if (pvValue is ErrorValue pvError) return pvError;
        if (fvValue is ErrorValue fvError) return fvError;
        if (typeValue is ErrorValue typeError) return typeError;
        return PmtScalar(rateValue, ToNumber(nperValueArg), ToNumber(pvValue), ToNumber(fvValue), ToNumber(typeValue));
    }

    private static ScalarValue PmtScalar(ScalarValue rateValue, double nperValue, double pv, double fv, double type)
    {
        double rate = ToNumber(rateValue);
        if (!double.IsFinite(rate) || !double.IsFinite(nperValue) || !double.IsFinite(pv) || !double.IsFinite(fv) || !double.IsFinite(type))
            return ErrorValue.Num;
        if (!IsValidPaymentType(type)) return ErrorValue.Num;
        double nper = nperValue;
        if (nper == 0) return ErrorValue.DivByZero;
        if (Math.Abs(rate) < 1e-10)
            return NumberResult(-(pv + fv) / nper);
        double rn  = Math.Pow(1 + rate, nper);
        double pmt = -(pv * rn + fv) * rate / ((1 + rate * type) * (rn - 1));
        return NumberResult(pmt);
    }

    private static ScalarValue Pv(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var fvArg = args.Count > 3 && args[3] is not BlankValue ? args[3] : new NumberValue(0);
        var typeArg = args.Count > 4 && args[4] is not BlankValue ? args[4] : new NumberValue(0);
        return MapScalarArgs([args[0], args[1], args[2], fvArg, typeArg], values => PvScalar(values[0], values[1], values[2], values[3], values[4]));
    }

    private static ScalarValue PvScalar(ScalarValue rateValue, ScalarValue nperValueArg, ScalarValue pmtValue, ScalarValue fvValue, ScalarValue typeValue)
    {
        if (rateValue is ErrorValue rateError) return rateError;
        if (nperValueArg is ErrorValue nperError) return nperError;
        if (pmtValue is ErrorValue pmtError) return pmtError;
        if (fvValue is ErrorValue fvError) return fvError;
        if (typeValue is ErrorValue typeError) return typeError;
        return PvScalar(rateValue, ToNumber(nperValueArg), ToNumber(pmtValue), ToNumber(fvValue), ToNumber(typeValue));
    }

    private static ScalarValue PvScalar(ScalarValue rateValue, double nperValue, double pmt, double fv, double type)
    {
        double rate = ToNumber(rateValue);
        if (!double.IsFinite(rate) || !double.IsFinite(nperValue) || !double.IsFinite(pmt) || !double.IsFinite(fv) || !double.IsFinite(type))
            return ErrorValue.Num;
        if (!IsValidPaymentType(type)) return ErrorValue.Num;
        double nper = nperValue;
        // Unlike PMT (where nper == 0 is a genuine division by zero), PV has no such
        // singularity: when nper == 0, rn == (1+rate)^0 == 1 and the pmt term vanishes
        // ((rn - 1) == 0), so the closed form below correctly falls through to -fv
        // (matching Excel's PV(rate, 0, pmt, fv) == -fv) instead of #DIV/0!.
        if (Math.Abs(rate) < 1e-10)
            return NumberResult(-pmt * nper - fv);
        double rn = Math.Pow(1 + rate, nper);
        double pv = (-pmt * (1 + rate * type) * (rn - 1) / rate - fv) / rn;
        return NumberResult(pv);
    }

    private static ScalarValue Fv(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var pvArg = args.Count > 3 && args[3] is not BlankValue ? args[3] : new NumberValue(0);
        var typeArg = args.Count > 4 && args[4] is not BlankValue ? args[4] : new NumberValue(0);
        return MapScalarArgs([args[0], args[1], args[2], pvArg, typeArg], values => FvScalar(values[0], values[1], values[2], values[3], values[4]));
    }

    private static ScalarValue FvScalar(ScalarValue rateValue, ScalarValue nperValueArg, ScalarValue pmtValue, ScalarValue pvValue, ScalarValue typeValue)
    {
        if (rateValue is ErrorValue rateError) return rateError;
        if (nperValueArg is ErrorValue nperError) return nperError;
        if (pmtValue is ErrorValue pmtError) return pmtError;
        if (pvValue is ErrorValue pvError) return pvError;
        if (typeValue is ErrorValue typeError) return typeError;
        return FvScalar(rateValue, ToNumber(nperValueArg), ToNumber(pmtValue), ToNumber(pvValue), ToNumber(typeValue));
    }

    private static ScalarValue FvScalar(ScalarValue rateValue, double nperValue, double pmt, double pv, double type)
    {
        double rate = ToNumber(rateValue);
        if (!double.IsFinite(rate) || !double.IsFinite(nperValue) || !double.IsFinite(pmt) || !double.IsFinite(pv) || !double.IsFinite(type))
            return ErrorValue.Num;
        if (!IsValidPaymentType(type)) return ErrorValue.Num;
        double nper = nperValue;
        if (Math.Abs(rate) < 1e-10)
            return NumberResult(-pv - pmt * nper);
        double rn = Math.Pow(1 + rate, nper);
        return NumberResult(-pv * rn - pmt * (1 + rate * type) * (rn - 1) / rate);
    }

    private static ScalarValue Nper(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var fvArg = args.Count > 3 && args[3] is not BlankValue ? args[3] : new NumberValue(0);
        var typeArg = args.Count > 4 && args[4] is not BlankValue ? args[4] : new NumberValue(0);
        return MapScalarArgs([args[0], args[1], args[2], fvArg, typeArg], values => NperScalar(values[0], values[1], values[2], values[3], values[4]));
    }

    private static ScalarValue NperScalar(ScalarValue rateValue, ScalarValue pmtValue, ScalarValue pvValue, ScalarValue fvValue, ScalarValue typeValue)
    {
        if (rateValue is ErrorValue rateError) return rateError;
        if (pmtValue is ErrorValue pmtError) return pmtError;
        if (pvValue is ErrorValue pvError) return pvError;
        if (fvValue is ErrorValue fvError) return fvError;
        if (typeValue is ErrorValue typeError) return typeError;
        return NperScalar(rateValue, ToNumber(pmtValue), ToNumber(pvValue), ToNumber(fvValue), ToNumber(typeValue));
    }

    private static ScalarValue NperScalar(ScalarValue rateValue, double pmt, double pv, double fv, double type)
    {
        double rate = ToNumber(rateValue);
        if (!double.IsFinite(rate) || !double.IsFinite(pmt) || !double.IsFinite(pv) || !double.IsFinite(fv) || !double.IsFinite(type))
            return ErrorValue.Num;
        if (!IsValidPaymentType(type)) return ErrorValue.Num;
        if (Math.Abs(rate) < 1e-10)
        {
            if (Math.Abs(pmt) < 1e-10) return ErrorValue.DivByZero;
            return NumberResult(-(pv + fv) / pmt);
        }
        double pmtAdj = pmt * (1 + rate * type);
        double ratio  = (pmtAdj - fv * rate) / (pmtAdj + pv * rate);
        if (ratio <= 0) return ErrorValue.Num;
        return NumberResult(Math.Log(ratio) / Math.Log(1 + rate));
    }

    private static ScalarValue Rate(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var fvArg = args.Count > 3 && args[3] is not BlankValue ? args[3] : new NumberValue(0);
        var typeArg = args.Count > 4 && args[4] is not BlankValue ? args[4] : new NumberValue(0);
        // A genuinely-omitted 6th argument defaults to 0.1 (Excel's RATE default guess), but an
        // explicitly-supplied argument that merely evaluates to blank (e.g. a reference to an
        // empty cell) must flow through normal numeric coercion instead -- BlankValue coerces to
        // 0.0 (BuiltInFunctions.Coercion.cs ToNumber), matching Excel's blank-cell-as-0 rule.
        // args.Count reflects the actual number of arguments written in the formula (see
        // FormulaEvaluator.Functions.cs, which sizes expandedArgs from node.Arguments.Count with
        // no arity padding), so it reliably distinguishes "omitted" from "explicit but blank".
        var guessArg = args.Count > 5 ? args[5] : new NumberValue(0.1);
        return MapScalarArgs([args[0], args[1], args[2], fvArg, typeArg, guessArg], values => RateScalar(values[0], values[1], values[2], values[3], values[4], values[5]));
    }

    private static ScalarValue RateScalar(ScalarValue nperValueArg, ScalarValue pmtValue, ScalarValue pvValue, ScalarValue fvValue, ScalarValue typeValue, ScalarValue guessValue)
    {
        if (nperValueArg is ErrorValue nperError) return nperError;
        if (pmtValue is ErrorValue pmtError) return pmtError;
        if (pvValue is ErrorValue pvError) return pvError;
        if (fvValue is ErrorValue fvError) return fvError;
        if (typeValue is ErrorValue typeError) return typeError;
        if (guessValue is ErrorValue guessError) return guessError;
        return RateScalar(nperValueArg, ToNumber(pmtValue), ToNumber(pvValue), ToNumber(fvValue), ToNumber(typeValue), ToNumber(guessValue));
    }

    private static ScalarValue RateScalar(ScalarValue nperValueArg, double pmt, double pv, double fv, double type, double guess)
    {
        double nperValue = ToNumber(nperValueArg);
        if (!double.IsFinite(nperValue) || !double.IsFinite(pmt) || !double.IsFinite(pv) || !double.IsFinite(fv) || !double.IsFinite(type) || !double.IsFinite(guess))
            return ErrorValue.Num;
        if (!IsValidPaymentType(type)) return ErrorValue.Num;
        if (nperValue == 0) return ErrorValue.DivByZero;
        double nper = nperValue;
        double r = guess;
        for (int i = 0; i < 100; i++)
        {
            double rn   = Math.Pow(1 + r, nper);
            double rn1  = nper * Math.Pow(1 + r, nper - 1);
            double f, df;
            if (Math.Abs(r) < 1e-10)
            {
                f  = pv + pmt * nper + fv;
                df = pv * nper + pmt * nper * (nper - 1) / 2.0;
            }
            else
            {
                f  = pv * rn + pmt * (1 + r * type) * (rn - 1) / r + fv;
                df = pv * rn1
                   + pmt * type * (rn - 1) / r
                   + pmt * (1 + r * type) * (rn1 * r - (rn - 1)) / (r * r);
            }
            if (Math.Abs(df) < 1e-15) break;
            double delta = f / df;
            r -= delta;
            if (Math.Abs(delta) < 1e-10) break;
        }
        if (double.IsNaN(r) || double.IsInfinity(r)) return ErrorValue.Num;
        // Verify the solution actually satisfies the TVM equation; non-converged iterates
        // can land far from any root and must return #NUM! (matches Excel behaviour).
        double rFinal = r, fResidual;
        if (Math.Abs(rFinal) < 1e-10)
            fResidual = pv + pmt * nper + fv;
        else
        {
            double rnF = Math.Pow(1 + rFinal, nper);
            fResidual = pv * rnF + pmt * (1 + rFinal * type) * (rnF - 1) / rFinal + fv;
        }
        // Scale the convergence tolerance by the dominant cash-flow magnitude so that
        // large-loan inputs (pv ~ 1e7) are not wrongly rejected due to absolute residual
        // exceeding 1e-7, while genuinely non-converged iterates are still caught.
        double scale = Math.Max(1.0, Math.Max(Math.Abs(pv), Math.Max(Math.Abs(fv), Math.Abs(pmt * nper))));
        return Math.Abs(fResidual) > 1e-7 * scale ? ErrorValue.Num : new NumberValue(r);
    }
}
