using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

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
        int digits = (int)Math.Ceiling(Math.Log10(f));
        if (digits < 1) digits = 1;
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
        int digits = (int)Math.Ceiling(Math.Log10(f));
        if (digits < 1) digits = 1;
        return NumberResult(intPart + fracPart * f / Math.Pow(10, digits));
    }
}
