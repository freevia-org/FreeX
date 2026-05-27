using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

public static partial class BuiltInFunctions
{
    // -- Financial growth helpers -----------------------------------------

    private static ScalarValue Rri(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        return MapTernaryTextArgs(args[0], args[1], args[2], RriScalar);
    }

    private static ScalarValue RriScalar(ScalarValue nperValue, ScalarValue pvValue, ScalarValue fvValue)
    {
        double pv = ToNumber(pvValue);
        double fv = ToNumber(fvValue);
        return RriScalar(nperValue, pv, fv);
    }

    private static ScalarValue RriScalar(ScalarValue nperValue, double pv, double fv)
    {
        double nper = ToNumber(nperValue);
        if (!double.IsFinite(nper) || !double.IsFinite(pv) || !double.IsFinite(fv)) return ErrorValue.Num;
        if (nper <= 0 || pv == 0) return ErrorValue.Num;
        if ((pv > 0 && fv < 0) || (pv < 0 && fv > 0)) return ErrorValue.Num;
        double result = Math.Pow(fv / pv, 1.0 / nper) - 1;
        return NumberResult(result);
    }

    private static ScalarValue Pduration(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        return MapTernaryTextArgs(args[0], args[1], args[2], PdurationScalar);
    }

    private static ScalarValue PdurationScalar(ScalarValue rateValue, ScalarValue pvValue, ScalarValue fvValue)
    {
        double pv = ToNumber(pvValue);
        double fv = ToNumber(fvValue);
        return PdurationScalar(rateValue, pv, fv);
    }

    private static ScalarValue PdurationScalar(ScalarValue rateValue, double pv, double fv)
    {
        double rate = ToNumber(rateValue);
        if (!double.IsFinite(rate) || !double.IsFinite(pv) || !double.IsFinite(fv)) return ErrorValue.Num;
        if (rate <= 0 || pv <= 0 || fv <= 0) return ErrorValue.Num;
        return NumberResult((Math.Log(fv) - Math.Log(pv)) / Math.Log(1 + rate));
    }

    private static ScalarValue Fvschedule(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var schedRange = args[1] is RangeValue scheduleRange
            ? scheduleRange
            : SingleCellArray(args[1]);
        if (args[0] is RangeValue principalRange)
            return MapUnaryTextRange(principalRange, principalValue => FvscheduleScalar(principalValue, schedRange));
        return FvscheduleScalar(args[0], schedRange);
    }

    private static ScalarValue FvscheduleScalar(ScalarValue principalValue, RangeValue schedRange)
    {
        if (principalValue is ErrorValue principalError) return principalError;
        double principal = ToNumber(principalValue);
        if (!double.IsFinite(principal)) return ErrorValue.Num;
        var (rates, re) = CollectRangeNumbers(schedRange);
        if (re is not null) return re;
        double result = principal;
        foreach (double r in rates!)
        {
            if (!double.IsFinite(r)) return ErrorValue.Num;
            result *= (1 + r);
        }
        return NumberResult(result);
    }
}
