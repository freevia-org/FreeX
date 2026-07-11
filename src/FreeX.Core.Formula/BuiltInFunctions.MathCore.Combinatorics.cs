using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    private static ScalarValue Fact(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, FactScalar);
        return FactScalar(args[0]);
    }

    private static ScalarValue FactScalar(ScalarValue value)
    {
        var n = ToNumber(value);
        if (!double.IsFinite(n) || n < 0) return ErrorValue.Num;
        int ni = (int)Math.Truncate(n);
        if (ni > 170) return ErrorValue.Num; // Excel limit; 171! overflows double
        double result = 1;
        for (int i = 2; i <= ni; i++) result *= i;
        return new NumberValue(result);
    }

    private static ScalarValue FactDouble(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, FactDoubleScalar);
        return FactDoubleScalar(args[0]);
    }

    private static ScalarValue FactDoubleScalar(ScalarValue value)
    {
        var raw = ToNumber(value);
        if (!double.IsFinite(raw) || raw < 0 || raw > int.MaxValue) return ErrorValue.Num;
        var n = (int)Math.Truncate(raw);
        double result = 1;
        for (var i = n; i > 1; i -= 2)
        {
            result *= i;
            if (!double.IsFinite(result)) return ErrorValue.Num;
        }

        return new NumberValue(result);
    }


    private static ScalarValue Combin(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        return MapBinaryMathArgs(args[0], args[1], CombinScalar);
    }

    private static ScalarValue CombinScalar(ScalarValue numberValue, ScalarValue chosenValue)
    {
        double dn = ToNumber(numberValue); double dk = ToNumber(chosenValue);
        if (!double.IsFinite(dn) || !double.IsFinite(dk)) return ErrorValue.Num;
        if (dn < 0 || dk < 0) return ErrorValue.Num;
        dn = Math.Truncate(dn); dk = Math.Truncate(dk);
        if (dk > dn) return ErrorValue.Num;
        if (dk == 0) return new NumberValue(1);
        if (dk == 1) return new NumberValue(dn);
        if (dn > int.MaxValue || dk > int.MaxValue) return ErrorValue.Num;
        return CombinPositiveIntegers((int)dn, (int)dk);
    }

    private static ScalarValue Combina(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        return MapBinaryMathArgs(args[0], args[1], CombinaScalar);
    }

    private static ScalarValue CombinaScalar(ScalarValue numberValue, ScalarValue chosenValue)
    {
        double dn = ToNumber(numberValue); double dk = ToNumber(chosenValue);
        if (!double.IsFinite(dn) || !double.IsFinite(dk)) return ErrorValue.Num;
        if (dn < 0 || dk < 0) return ErrorValue.Num;
        dn = Math.Truncate(dn); dk = Math.Truncate(dk);
        if (dn == 0 && dk > 0) return ErrorValue.Num;
        if (dk == 0) return new NumberValue(1);
        if (dk == 1) return new NumberValue(dn);
        if (dn > int.MaxValue || dk > int.MaxValue) return ErrorValue.Num;
        int n = (int)dn;
        int k = (int)dk;
        // CombinPositiveIntegers(n+k-1, k) internally minimizes k to min(k, (n+k-1)-k) = min(k, n-1)
        // and rejects when that minimized value exceeds 1029; pre-check the same quantity so we
        // don't reject cases (e.g. large n with small k) that CombinPositiveIntegers would happily compute.
        if (Math.Min(k, n - 1) > 1029) return ErrorValue.Num;
        return CombinPositiveIntegers(n + k - 1, k);
    }

    private static ScalarValue Permut(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        return MapBinaryMathArgs(args[0], args[1], PermutScalar);
    }

    private static ScalarValue PermutScalar(ScalarValue numberValue, ScalarValue chosenValue)
    {
        double dn = ToNumber(numberValue); double dk = ToNumber(chosenValue);
        if (!double.IsFinite(dn) || !double.IsFinite(dk)) return ErrorValue.Num;
        if (dn < 0 || dk < 0) return ErrorValue.Num;
        dn = Math.Truncate(dn); dk = Math.Truncate(dk);
        if (dk > dn) return ErrorValue.Num;
        if (dk == 0) return new NumberValue(1);
        if (dk == 1) return new NumberValue(dn);
        if (dn > int.MaxValue || dk > int.MaxValue) return ErrorValue.Num;
        int n = (int)dn; int k = (int)dk;
        double result = 1;
        for (int i = 0; i < k; i++)
            result *= (n - i);
        return NumberResult(result);
    }

    private static ScalarValue PermutationA(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        return MapBinaryMathArgs(args[0], args[1], PermutationAScalar);
    }

    private static ScalarValue PermutationAScalar(ScalarValue numberValue, ScalarValue chosenValue)
    {
        double dn = ToNumber(numberValue); double dk = ToNumber(chosenValue);
        if (!double.IsFinite(dn) || !double.IsFinite(dk)) return ErrorValue.Num;
        if (dn < 0 || dk < 0) return ErrorValue.Num;
        dn = Math.Truncate(dn); dk = Math.Truncate(dk);
        if (dn == 0 && dk > 0) return ErrorValue.Num;
        if (dk == 0) return new NumberValue(1);
        if (dk == 1) return new NumberValue(dn);
        if (dn > int.MaxValue || dk > int.MaxValue) return ErrorValue.Num;
        int n = (int)dn;
        int k = (int)dk;
        return NumberResult(Math.Pow(n, k));
    }

    private static ScalarValue CombinPositiveIntegers(int n, int k)
    {
        if (n < 0 || k < 0 || k > n) return ErrorValue.Num;
        if (k > n - k) k = n - k;
        if (k > 1029) return ErrorValue.Num;
        double result = 1;
        for (int i = 0; i < k; i++)
            result = result * (n - i) / (i + 1);
        return NumberResult(Math.Round(result));
    }
}
