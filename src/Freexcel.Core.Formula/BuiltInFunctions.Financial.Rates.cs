using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

public static partial class BuiltInFunctions
{
    // -- Financial rate conversion helpers --------------------------------

    private static ScalarValue Effect(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        return MapBinaryMathArgs(args[0], args[1], EffectScalar);
    }

    private static ScalarValue EffectScalar(ScalarValue rateValue, ScalarValue nperyValue)
    {
        double npery = Math.Truncate(ToNumber(nperyValue));
        double nomRate = ToNumber(rateValue);
        if (!double.IsFinite(nomRate) || !double.IsFinite(npery)) return ErrorValue.Num;
        if (nomRate <= 0 || npery < 1) return ErrorValue.Num;
        return NumberResult(Math.Pow(1 + nomRate / npery, npery) - 1);
    }

    private static ScalarValue Nominal(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        return MapBinaryMathArgs(args[0], args[1], NominalScalar);
    }

    private static ScalarValue NominalScalar(ScalarValue rateValue, ScalarValue nperyValue)
    {
        double npery = Math.Truncate(ToNumber(nperyValue));
        double effectRate = ToNumber(rateValue);
        if (!double.IsFinite(effectRate) || !double.IsFinite(npery)) return ErrorValue.Num;
        if (effectRate <= 0 || npery < 1) return ErrorValue.Num;
        return NumberResult((Math.Pow(1 + effectRate, 1.0 / npery) - 1) * npery);
    }
}
