using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    // -- C3: Dollar conversion helpers ------------------------------------

    private static ScalarValue Dollarde(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        return MapBinaryMathArgs(args[0], args[1], DollardeScalar);
    }

    private static ScalarValue DollardeScalar(ScalarValue dollarValue, ScalarValue fractionValue)
    {
        double rawFraction = ToNumber(fractionValue);
        return DollardeScalar(dollarValue, rawFraction);
    }

    private static ScalarValue DollardeScalar(ScalarValue dollarValue, double rawFraction)
    {
        double d = ToNumber(dollarValue);
        if (!double.IsFinite(d) || !double.IsFinite(rawFraction)) return ErrorValue.Num;
        if (rawFraction < 0) return ErrorValue.Num;

        double f = Math.Truncate(rawFraction);
        if (f < 0) return ErrorValue.Num;
        if (f == 0) return ErrorValue.DivByZero;
        double intPart  = Math.Truncate(d);
        double fracPart = d - intPart;
        // fraction=1 legitimately yields digits=0 (an unscaled/identity result) — clamping the
        // digit count up to a minimum of 1 corrupted that case by inflating the multiplier from
        // 10^0=1 to 10^1=10 (R50-formula-text-currency-numsys-3-1); f>=1 always holds here
        // (f==0 is rejected above with #DIV/0!), so no lower-bound clamp is needed.
        int digits = (int)Math.Ceiling(Math.Log10(f));
        return NumberResult(intPart + fracPart * Math.Pow(10, digits) / f);
    }

    private static ScalarValue Dollarfr(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        return MapBinaryMathArgs(args[0], args[1], DollarfrScalar);
    }

    private static ScalarValue DollarfrScalar(ScalarValue dollarValue, ScalarValue fractionValue)
    {
        double rawFraction = ToNumber(fractionValue);
        return DollarfrScalar(dollarValue, rawFraction);
    }

    private static ScalarValue DollarfrScalar(ScalarValue dollarValue, double rawFraction)
    {
        double d = ToNumber(dollarValue);
        if (!double.IsFinite(d) || !double.IsFinite(rawFraction)) return ErrorValue.Num;
        if (rawFraction < 0) return ErrorValue.Num;

        double f = Math.Truncate(rawFraction);
        if (f < 0) return ErrorValue.Num;
        if (f == 0) return ErrorValue.DivByZero;
        double intPart  = Math.Truncate(d);
        double fracPart = d - intPart;
        // See DollardeScalar: fraction=1 legitimately yields digits=0 (identity) — do not clamp
        // the digit count to a minimum of 1 (R50-formula-text-currency-numsys-3-1).
        int digits = (int)Math.Ceiling(Math.Log10(f));
        return NumberResult(intPart + fracPart * f / Math.Pow(10, digits));
    }
}
