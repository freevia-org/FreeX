using FreeX.Core.Formula;

namespace FreeX.Core.Calc;

/// <summary>Owns the volatile function catalog and Excel's constant CELL/INFO exceptions.</summary>
public static class FormulaVolatilityPolicy
{
    private static readonly HashSet<string> NonVolatileInfoTypes =
        ["directory", "numfile", "origin", "osversion", "recalc", "release", "system"];

    public static bool IsVolatileFunctionName(string name) =>
        name is "NOW" or "TODAY" or "RAND" or "RANDBETWEEN" or "RANDARRAY" or "INDIRECT" or "OFFSET" or "CELL" or "INFO";

    public static bool IsVolatileCall(FunctionCallNode function) =>
        IsVolatileFunctionName(function.FunctionName) && !IsConstantNonVolatileCellOrInfoCall(function);

    public static bool IsCurrentCellSensitiveCall(FunctionCallNode function) =>
        IsVolatileCall(function)
        || function.Arguments.Count == 0 && function.FunctionName is "ROW" or "COLUMN";

    public static bool IsConstantNonVolatileCellOrInfoCall(FunctionCallNode function)
    {
        if (function.Arguments.Count == 0 || function.Arguments[0] is not StringNode { Value: var infoTypeArg })
            return false;

        var infoType = infoTypeArg.Trim();
        return function.FunctionName switch
        {
            "CELL" => string.Equals(infoType, "width", StringComparison.OrdinalIgnoreCase),
            "INFO" => NonVolatileInfoTypes.Contains(infoType.ToLowerInvariant()),
            _ => false,
        };
    }
}
