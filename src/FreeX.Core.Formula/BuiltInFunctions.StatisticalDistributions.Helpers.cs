using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    // ── Helper: collect two parallel arrays from two args (range or scalar) ─

    private static (List<double>? A, List<double>? B, ErrorValue? Err)
        CollectPair(ScalarValue argA, ScalarValue argB)
    {
        var (a, ea) = argA is RangeValue rva ? CollectRangeNumbers(rva) : CollectNumbers(new[] { argA });
        if (ea is not null) return (null, null, ea);
        var (b, eb) = argB is RangeValue rvb ? CollectRangeNumbers(rvb) : CollectNumbers(new[] { argB });
        if (eb is not null) return (null, null, eb);
        return (a, b, null);
    }
}
