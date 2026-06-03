using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public sealed partial class FormulaEvaluator
{
    private bool TryEvaluateMatchDirectRange(FunctionCallNode node, IEvalContext context, out ScalarValue result)
    {
        result = BlankValue.Instance;
        if (node.Arguments.Count is < 2 or > 3)
        {
            result = ErrorValue.Value;
            return true;
        }

        if (!TryAsRangeRef(node.Arguments[1], out var range))
            return false;

        if (TryAsRangeRef(node.Arguments[0], out _) ||
            (node.Arguments.Count > 2 && TryAsRangeRef(node.Arguments[2], out _)))
            return false;

        if (!TryCreateDirectLookupVector(range, context, ErrorValue.NA, out var vector, out result))
            return true;

        var lookupValue = EvaluateNode(node.Arguments[0], context);
        if (lookupValue is ErrorValue lookupError)
        {
            result = lookupError;
            return true;
        }

        var matchTypeValue = node.Arguments.Count > 2
            ? EvaluateNode(node.Arguments[2], context)
            : BlankValue.Instance;
        if (matchTypeValue is ErrorValue matchTypeError)
        {
            result = matchTypeError;
            return true;
        }

        if (lookupValue is RangeValue || matchTypeValue is RangeValue)
            return false;

        var matchTypeCoerced = matchTypeValue is BlankValue
            ? new NumberValue(1)
            : CoerceToNumber(matchTypeValue);
        if (matchTypeCoerced is ErrorValue matchTypeCoerceError)
        {
            result = matchTypeCoerceError;
            return true;
        }

        var rawMatchType = ((NumberValue)matchTypeCoerced).Value;
        if (!double.IsFinite(rawMatchType))
        {
            result = ErrorValue.NA;
            return true;
        }

        var matchType = (int)rawMatchType;
        if (matchType is not (-1 or 0 or 1))
        {
            result = ErrorValue.NA;
            return true;
        }

        result = EvaluateMatchDirectRange(lookupValue, vector, matchType, context);
        return true;
    }

    private bool TryEvaluateXmatchDirectRange(FunctionCallNode node, IEvalContext context, out ScalarValue result)
    {
        result = BlankValue.Instance;
        if (node.Arguments.Count is < 2 or > 4)
        {
            result = ErrorValue.Value;
            return true;
        }

        if (!TryAsRangeRef(node.Arguments[1], out var range))
            return false;

        if (TryAsRangeRef(node.Arguments[0], out _) ||
            (node.Arguments.Count > 2 && TryAsRangeRef(node.Arguments[2], out _)) ||
            (node.Arguments.Count > 3 && TryAsRangeRef(node.Arguments[3], out _)))
            return false;

        var lookupValue = EvaluateNode(node.Arguments[0], context);
        if (lookupValue is ErrorValue lookupError)
        {
            result = lookupError;
            return true;
        }

        if (lookupValue is RangeValue)
            return false;

        if (!TryCreateDirectLookupVector(range, context, ErrorValue.Value, out var vector, out result))
            return true;

        var matchModeValue = node.Arguments.Count > 2
            ? EvaluateNode(node.Arguments[2], context)
            : BlankValue.Instance;
        if (matchModeValue is ErrorValue matchModeError)
        {
            result = matchModeError;
            return true;
        }

        var searchModeValue = node.Arguments.Count > 3
            ? EvaluateNode(node.Arguments[3], context)
            : BlankValue.Instance;
        if (searchModeValue is ErrorValue searchModeError)
        {
            result = searchModeError;
            return true;
        }

        if (matchModeValue is RangeValue || searchModeValue is RangeValue)
            return false;

        if (!TryCoerceDirectLookupMode(matchModeValue, defaultMode: 0, nonFiniteError: ErrorValue.Value, out var matchMode, out result))
            return true;

        if (!TryCoerceDirectLookupMode(searchModeValue, defaultMode: 1, nonFiniteError: ErrorValue.Value, out var searchMode, out result))
            return true;

        if (matchMode is not (-1 or 0 or 1 or 2) ||
            searchMode is not (-2 or -1 or 1 or 2))
        {
            result = ErrorValue.Value;
            return true;
        }

        result = EvaluateXmatchDirectRange(lookupValue, vector, matchMode, searchMode, context);
        return true;
    }

    private bool TryEvaluateXlookupDirectRanges(FunctionCallNode node, IEvalContext context, out ScalarValue result)
    {
        result = BlankValue.Instance;
        if (node.Arguments.Count is < 3 or > 6)
        {
            result = ErrorValue.Value;
            return true;
        }

        if (!TryAsRangeRef(node.Arguments[1], out var lookupRange) ||
            !TryAsRangeRef(node.Arguments[2], out var returnRange))
            return false;

        if (TryAsRangeRef(node.Arguments[0], out _) ||
            (node.Arguments.Count > 3 && TryAsRangeRef(node.Arguments[3], out _)) ||
            (node.Arguments.Count > 4 && TryAsRangeRef(node.Arguments[4], out _)) ||
            (node.Arguments.Count > 5 && TryAsRangeRef(node.Arguments[5], out _)))
            return false;

        var lookupValue = EvaluateNode(node.Arguments[0], context);
        if (lookupValue is ErrorValue lookupError)
        {
            result = lookupError;
            return true;
        }

        if (lookupValue is RangeValue)
            return false;

        if (!TryCreateDirectLookupVector(lookupRange, context, ErrorValue.Value, out var lookupVector, out result))
            return true;

        if (!TryCreateDirectXlookupReturnVector(returnRange, context, out var returnVector, out result, out var shouldFallback))
            return !shouldFallback;

        if (returnVector.Count != lookupVector.Count)
        {
            result = ErrorValue.Value;
            return true;
        }

        ScalarValue ifNotFound = ErrorValue.NA;
        if (node.Arguments.Count > 3)
        {
            var ifNotFoundValue = EvaluateNode(node.Arguments[3], context);
            if (ifNotFoundValue is ErrorValue ifNotFoundError)
            {
                result = ifNotFoundError;
                return true;
            }

            if (ifNotFoundValue is RangeValue)
                return false;

            if (ifNotFoundValue is not BlankValue)
                ifNotFound = ifNotFoundValue;
        }

        var matchModeValue = node.Arguments.Count > 4
            ? EvaluateNode(node.Arguments[4], context)
            : BlankValue.Instance;
        if (matchModeValue is ErrorValue matchModeError)
        {
            result = matchModeError;
            return true;
        }

        var searchModeValue = node.Arguments.Count > 5
            ? EvaluateNode(node.Arguments[5], context)
            : BlankValue.Instance;
        if (searchModeValue is ErrorValue searchModeError)
        {
            result = searchModeError;
            return true;
        }

        if (matchModeValue is RangeValue || searchModeValue is RangeValue)
            return false;

        if (!TryCoerceDirectLookupMode(matchModeValue, defaultMode: 0, nonFiniteError: ErrorValue.Value, out var matchMode, out result))
            return true;

        if (!TryCoerceDirectLookupMode(searchModeValue, defaultMode: 1, nonFiniteError: ErrorValue.Value, out var searchMode, out result))
            return true;

        if (matchMode is not (-1 or 0 or 1 or 2) ||
            searchMode is not (-2 or -1 or 1 or 2))
        {
            result = ErrorValue.Value;
            return true;
        }

        var matchError = TryFindDirectLookupIndex(
            lookupValue,
            lookupVector,
            matchMode,
            searchMode,
            context,
            out var matchIndex);
        if (matchError is not null)
        {
            result = matchError;
            return true;
        }

        result = matchIndex >= 0
            ? GetDirectLookupValue(returnVector, matchIndex, context)
            : ifNotFound;
        return true;
    }

    private bool TryEvaluateLookupDirectRanges(FunctionCallNode node, IEvalContext context, out ScalarValue result)
    {
        result = BlankValue.Instance;
        if (node.Arguments.Count is < 2 or > 3)
        {
            result = ErrorValue.Value;
            return true;
        }

        if (!TryAsRangeRef(node.Arguments[1], out var lookupRange))
            return false;

        if (TryAsRangeRef(node.Arguments[0], out _))
            return false;

        var lookupValue = EvaluateNode(node.Arguments[0], context);
        if (lookupValue is ErrorValue lookupError)
        {
            result = lookupError;
            return true;
        }

        if (lookupValue is RangeValue)
            return false;

        DirectLookupRangeVector lookupVector;
        DirectLookupRangeVector resultVector;
        if (node.Arguments.Count == 2)
        {
            if (!TryCreateDirectLookupArrayFormVectors(lookupRange, context, out lookupVector, out resultVector, out result))
                return true;
        }
        else
        {
            if (!TryCreateDirectXlookupReturnVector(lookupRange, context, out lookupVector, out result, out var lookupShouldFallback))
                return !lookupShouldFallback;

            if (!TryAsRangeRef(node.Arguments[2], out var resultRange))
                return false;

            if (!TryCreateDirectXlookupReturnVector(resultRange, context, out resultVector, out result, out var resultShouldFallback))
                return !resultShouldFallback;
        }

        result = EvaluateLookupDirectVectors(lookupValue, lookupVector, resultVector, context);
        return true;
    }

    private static ScalarValue EvaluateMatchDirectRange(
        ScalarValue lookupValue,
        DirectLookupRangeVector vector,
        int matchType,
        IEvalContext context)
    {
        if (matchType == 0)
        {
            for (var index = 0; index < vector.Count; index++)
            {
                var candidate = GetDirectLookupValue(vector, index, context);
                if (candidate is ErrorValue error)
                    return error;

                if (BuiltInFunctions.MatchExactValue(candidate, lookupValue))
                    return new NumberValue(index + 1);
            }

            return ErrorValue.NA;
        }

        if (matchType == 1)
        {
            var best = -1;
            for (var index = 0; index < vector.Count; index++)
            {
                var candidate = GetDirectLookupValue(vector, index, context);
                if (candidate is ErrorValue error)
                    return error;

                if (BuiltInFunctions.CompareScalar(candidate, lookupValue) <= 0)
                    best = index;
                else
                    break;
            }

            return best >= 0 ? new NumberValue(best + 1) : ErrorValue.NA;
        }

        var descendingBest = -1;
        for (var index = 0; index < vector.Count; index++)
        {
            var candidate = GetDirectLookupValue(vector, index, context);
            if (candidate is ErrorValue error)
                return error;

            if (BuiltInFunctions.CompareScalar(candidate, lookupValue) >= 0)
                descendingBest = index;
            else
                break;
        }

        return descendingBest >= 0 ? new NumberValue(descendingBest + 1) : ErrorValue.NA;
    }

    private static ScalarValue EvaluateXmatchDirectRange(
        ScalarValue lookupValue,
        DirectLookupRangeVector vector,
        int matchMode,
        int searchMode,
        IEvalContext context)
    {
        var error = TryFindDirectLookupIndex(
            lookupValue,
            vector,
            matchMode,
            searchMode,
            context,
            out var matchIndex);
        if (error is not null)
            return error;

        return matchIndex >= 0 ? new NumberValue(matchIndex + 1) : ErrorValue.NA;
    }

    private static ErrorValue? TryFindDirectLookupIndex(
        ScalarValue lookupValue,
        DirectLookupRangeVector vector,
        int matchMode,
        int searchMode,
        IEvalContext context,
        out int matchIndex)
    {
        GetDirectLookupSearchBounds(vector.Count, searchMode, out var start, out var end, out var step);

        if (matchMode == 0)
            return TryFindDirectExactLookupIndex(lookupValue, vector, start, end, step, context, out matchIndex);

        if (matchMode == 2)
            return TryFindDirectWildcardLookupIndex(lookupValue, vector, start, end, step, context, out matchIndex);

        return TryFindDirectApproximateXmatchIndex(
            lookupValue,
            vector,
            start,
            end,
            step,
            nextSmaller: matchMode == -1,
            context,
            out matchIndex);
    }

    private static ErrorValue? TryFindDirectExactLookupIndex(
        ScalarValue lookupValue,
        DirectLookupRangeVector vector,
        int start,
        int end,
        int step,
        IEvalContext context,
        out int matchIndex)
    {
        matchIndex = -1;
        for (var index = start; index != end; index += step)
        {
            var candidate = GetDirectLookupValue(vector, index, context);
            if (candidate is ErrorValue error)
                return error;

            if (BuiltInFunctions.ScalarEquals(candidate, lookupValue))
            {
                matchIndex = index;
                return null;
            }
        }

        return null;
    }

    private static ErrorValue? TryFindDirectWildcardLookupIndex(
        ScalarValue lookupValue,
        DirectLookupRangeVector vector,
        int start,
        int end,
        int step,
        IEvalContext context,
        out int matchIndex)
    {
        matchIndex = -1;
        var pattern = BuiltInFunctions.ToText(lookupValue);
        for (var index = start; index != end; index += step)
        {
            var candidate = GetDirectLookupValue(vector, index, context);
            if (candidate is ErrorValue error)
                return error;

            if (candidate is TextValue text &&
                BuiltInFunctions.WildcardMatch(text.Value, pattern, ignoreCase: true))
            {
                matchIndex = index;
                return null;
            }
        }

        return null;
    }

    private static ErrorValue? TryFindDirectApproximateXmatchIndex(
        ScalarValue lookupValue,
        DirectLookupRangeVector vector,
        int start,
        int end,
        int step,
        bool nextSmaller,
        IEvalContext context,
        out int matchIndex)
    {
        matchIndex = -1;
        for (var index = start; index != end; index += step)
        {
            var candidate = GetDirectLookupValue(vector, index, context);
            if (candidate is ErrorValue error)
                return error;

            if (BuiltInFunctions.ScalarEquals(candidate, lookupValue))
            {
                matchIndex = index;
                return null;
            }
        }

        var best = -1;
        ScalarValue bestValue = BlankValue.Instance;
        for (var index = start; index != end; index += step)
        {
            var candidate = GetDirectLookupValue(vector, index, context);
            if (candidate is ErrorValue error)
                return error;

            var candidateVsLookup = BuiltInFunctions.CompareScalar(candidate, lookupValue);
            if (nextSmaller)
            {
                if (candidateVsLookup > 0)
                    continue;
                if (best < 0 || BuiltInFunctions.CompareScalar(candidate, bestValue) > 0)
                {
                    best = index;
                    bestValue = candidate;
                }
            }
            else
            {
                if (candidateVsLookup < 0)
                    continue;
                if (best < 0 || BuiltInFunctions.CompareScalar(candidate, bestValue) < 0)
                {
                    best = index;
                    bestValue = candidate;
                }
            }
        }

        matchIndex = best;
        return null;
    }

    private static ScalarValue EvaluateLookupDirectVectors(
        ScalarValue lookupValue,
        DirectLookupRangeVector lookupVector,
        DirectLookupRangeVector resultVector,
        IEvalContext context)
    {
        var matchIndex = -1;
        for (var index = 0; index < lookupVector.Count; index++)
        {
            var candidate = GetDirectLookupValue(lookupVector, index, context);
            if (candidate is ErrorValue)
                continue;

            if (BuiltInFunctions.CompareScalar(candidate, lookupValue) <= 0)
                matchIndex = index;
        }

        if (matchIndex < 0)
            return ErrorValue.NA;

        return matchIndex < resultVector.Count
            ? GetDirectLookupValue(resultVector, matchIndex, context)
            : ErrorValue.NA;
    }

    private static bool TryCreateDirectLookupArrayFormVectors(
        RangeRefNode rawRange,
        IEvalContext context,
        out DirectLookupRangeVector lookupVector,
        out DirectLookupRangeVector resultVector,
        out ScalarValue result)
    {
        lookupVector = default;
        resultVector = default;
        result = BlankValue.Instance;

        if (rawRange.SheetName is not null && !context.SheetExists(rawRange.SheetName))
        {
            result = ErrorValue.Ref;
            return false;
        }

        var range = ClampOpenEndedRangeToUsed(rawRange, context);
        var startRow = Math.Min(range.Start.Row, range.End.Row);
        var endRow = Math.Max(range.Start.Row, range.End.Row);
        var startCol = Math.Min(range.Start.ColumnNumber, range.End.ColumnNumber);
        var endCol = Math.Max(range.Start.ColumnNumber, range.End.ColumnNumber);
        var rowCount = endRow - startRow + 1;
        var colCount = endCol - startCol + 1;

        if (FormulaSafetyLimits.GetRangeCellCount(startRow, startCol, endRow, endCol) >
            FormulaSafetyLimits.MaxMaterializedRangeCells)
        {
            result = ErrorValue.Ref;
            return false;
        }

        if (rowCount > 1 && colCount > 1)
        {
            if (colCount > rowCount)
            {
                lookupVector = new DirectLookupRangeVector(range.SheetName, startRow, startCol, 1, colCount);
                resultVector = new DirectLookupRangeVector(range.SheetName, endRow, startCol, 1, colCount);
            }
            else
            {
                lookupVector = new DirectLookupRangeVector(range.SheetName, startRow, startCol, rowCount, 1);
                resultVector = new DirectLookupRangeVector(range.SheetName, startRow, endCol, rowCount, 1);
            }

            return true;
        }

        lookupVector = new DirectLookupRangeVector(range.SheetName, startRow, startCol, rowCount, colCount);
        resultVector = lookupVector;
        return true;
    }

    private static bool TryCreateDirectXlookupReturnVector(
        RangeRefNode rawRange,
        IEvalContext context,
        out DirectLookupRangeVector vector,
        out ScalarValue result,
        out bool shouldFallback)
    {
        vector = default;
        result = BlankValue.Instance;
        shouldFallback = false;

        if (rawRange.SheetName is not null && !context.SheetExists(rawRange.SheetName))
        {
            result = ErrorValue.Ref;
            return false;
        }

        var range = ClampOpenEndedRangeToUsed(rawRange, context);
        var startRow = Math.Min(range.Start.Row, range.End.Row);
        var endRow = Math.Max(range.Start.Row, range.End.Row);
        var startCol = Math.Min(range.Start.ColumnNumber, range.End.ColumnNumber);
        var endCol = Math.Max(range.Start.ColumnNumber, range.End.ColumnNumber);
        var rowCount = endRow - startRow + 1;
        var colCount = endCol - startCol + 1;

        if (rowCount > 1 && colCount > 1)
        {
            shouldFallback = true;
            return false;
        }

        if (FormulaSafetyLimits.GetRangeCellCount(startRow, startCol, endRow, endCol) >
            FormulaSafetyLimits.MaxMaterializedRangeCells)
        {
            result = ErrorValue.Ref;
            return false;
        }

        vector = new DirectLookupRangeVector(range.SheetName, startRow, startCol, rowCount, colCount);
        return true;
    }

    private static bool TryCreateDirectLookupVector(
        RangeRefNode rawRange,
        IEvalContext context,
        ErrorValue invalidShapeError,
        out DirectLookupRangeVector vector,
        out ScalarValue result)
    {
        vector = default;
        result = BlankValue.Instance;

        if (rawRange.SheetName is not null && !context.SheetExists(rawRange.SheetName))
        {
            result = ErrorValue.Ref;
            return false;
        }

        var range = ClampOpenEndedRangeToUsed(rawRange, context);
        var startRow = Math.Min(range.Start.Row, range.End.Row);
        var endRow = Math.Max(range.Start.Row, range.End.Row);
        var startCol = Math.Min(range.Start.ColumnNumber, range.End.ColumnNumber);
        var endCol = Math.Max(range.Start.ColumnNumber, range.End.ColumnNumber);
        var rowCount = endRow - startRow + 1;
        var colCount = endCol - startCol + 1;

        if (rowCount > 1 && colCount > 1)
        {
            result = invalidShapeError;
            return false;
        }

        if (FormulaSafetyLimits.GetRangeCellCount(startRow, startCol, endRow, endCol) >
            FormulaSafetyLimits.MaxMaterializedRangeCells)
        {
            result = ErrorValue.Ref;
            return false;
        }

        vector = new DirectLookupRangeVector(range.SheetName, startRow, startCol, rowCount, colCount);
        return true;
    }

    private static bool TryCoerceDirectLookupMode(
        ScalarValue value,
        int defaultMode,
        ErrorValue nonFiniteError,
        out int mode,
        out ScalarValue result)
    {
        if (value is BlankValue)
        {
            mode = defaultMode;
            result = BlankValue.Instance;
            return true;
        }

        var coerced = CoerceToNumber(value);
        if (coerced is ErrorValue error)
        {
            mode = 0;
            result = error;
            return false;
        }

        var rawMode = ((NumberValue)coerced).Value;
        if (!double.IsFinite(rawMode))
        {
            mode = 0;
            result = nonFiniteError;
            return false;
        }

        mode = (int)rawMode;
        result = BlankValue.Instance;
        return true;
    }

    private static void GetDirectLookupSearchBounds(int count, int searchMode, out int start, out int end, out int step)
    {
        if (searchMode is 1 or 2)
        {
            start = 0;
            end = count;
            step = 1;
            return;
        }

        start = count - 1;
        end = -1;
        step = -1;
    }

    private static ScalarValue GetDirectLookupValue(
        DirectLookupRangeVector vector,
        int index,
        IEvalContext context)
    {
        var row = vector.IsRow ? vector.StartRow : vector.StartRow + (uint)index;
        var col = vector.IsRow ? vector.StartCol + (uint)index : vector.StartCol;

        if (context is SheetEvalContext sheetContext)
            return sheetContext.ResolveSheetForFastRange(vector.SheetName)?.GetValue(row, col) ?? ErrorValue.Ref;

        return vector.SheetName is null
            ? context.GetCellValue(row, col)
            : context.GetCellValue(vector.SheetName, row, col);
    }

    private readonly record struct DirectLookupRangeVector(
        string? SheetName,
        uint StartRow,
        uint StartCol,
        uint RowCount,
        uint ColCount)
    {
        public bool IsRow => RowCount == 1;

        public int Count => checked((int)(IsRow ? ColCount : RowCount));
    }
}
