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

        result = EvaluateMatchDirectRange(lookupValue, CreateDirectLookupReader(vector, context), matchType);
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

        result = EvaluateXmatchDirectRange(lookupValue, CreateDirectLookupReader(vector, context), matchMode, searchMode);
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

        // Excel's XLOOKUP requires the return array's shape to match the lookup array's
        // shape on the matching axis (row-count for a vertical lookup array, column-count
        // for a horizontal one) -- not merely the same total element count. A same-count
        // but orientation-mismatched pair (e.g. a 5-row/1-col lookup array paired with a
        // 1-row/5-col return array) must fall through to #VALUE!, matching the slow path
        // in BuiltInFunctions.Lookup.Modern.cs.
        var lookupIsVertical = lookupVector.ColCount == 1;
        var lookupIsHorizontal = lookupVector.RowCount == 1;
        if ((!lookupIsVertical && !lookupIsHorizontal) ||
            (lookupIsVertical && returnVector.RowCount != lookupVector.RowCount) ||
            (lookupIsHorizontal && returnVector.ColCount != lookupVector.ColCount))
        {
            result = ErrorValue.Value;
            return true;
        }

        var lookupReader = CreateDirectLookupReader(lookupVector, context);
        var returnReader = CreateDirectLookupReader(returnVector, context);

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
            lookupReader,
            matchMode,
            searchMode,
            out var matchIndex);
        if (matchError is not null)
        {
            result = matchError;
            return true;
        }

        if (matchIndex >= 0)
        {
            result = returnReader.GetValue(matchIndex);
            return true;
        }

        // if_not_found (arg[3]) is evaluated lazily -- only when the lookup actually fails
        // to find a match -- so a found lookup value is returned even when if_not_found
        // would itself evaluate to an error (e.g. a fallback chain of nested XLOOKUPs, or
        // XLOOKUP(key, table, result, NA())). This mirrors IFNA's lazy value_if_na handling
        // and the slow path in BuiltInFunctions.Lookup.Modern.cs.
        if (node.Arguments.Count <= 3)
        {
            result = ErrorValue.NA;
            return true;
        }

        var ifNotFoundValue = EvaluateNode(node.Arguments[3], context);
        if (ifNotFoundValue is ErrorValue ifNotFoundError)
        {
            result = ifNotFoundError;
            return true;
        }

        if (ifNotFoundValue is RangeValue)
            return false;

        // Mirror the slow path (Xlookup in BuiltInFunctions.Lookup.Modern.cs): an
        // explicitly-supplied if_not_found argument is returned verbatim -- including
        // when it evaluates to blank -- and must not be coerced to #N/A.
        result = ifNotFoundValue;
        return true;
    }

    private bool TryEvaluateLegacyLookupDirectTable(
        FunctionCallNode node,
        IEvalContext context,
        bool horizontal,
        out ScalarValue result)
    {
        result = BlankValue.Instance;
        if (node.Arguments.Count is < 3 or > 4)
        {
            result = ErrorValue.Value;
            return true;
        }

        if (!TryAsRangeRef(node.Arguments[1], out var rawTableRange))
            return false;

        if (TryAsRangeRef(node.Arguments[0], out _) ||
            TryAsRangeRef(node.Arguments[2], out _) ||
            (node.Arguments.Count > 3 && TryAsRangeRef(node.Arguments[3], out _)))
            return false;

        if (!TryCreateDirectLegacyLookupTable(
                rawTableRange,
                context,
                out var tableSheetName,
                out var startRow,
                out var startCol,
                out var rowCount,
                out var colCount,
                out result))
        {
            return true;
        }

        var lookupValue = EvaluateNode(node.Arguments[0], context);
        if (lookupValue is ErrorValue lookupError)
        {
            result = lookupError;
            return true;
        }
        if (lookupValue is RangeValue)
            return false;

        var indexValue = EvaluateNode(node.Arguments[2], context);
        if (indexValue is ErrorValue indexError)
        {
            result = indexError;
            return true;
        }
        if (indexValue is RangeValue)
            return false;

        var rangeLookupValue = node.Arguments.Count > 3
            ? EvaluateNode(node.Arguments[3], context)
            : BlankValue.Instance;
        if (rangeLookupValue is ErrorValue rangeLookupError)
        {
            result = rangeLookupError;
            return true;
        }
        if (rangeLookupValue is RangeValue)
            return false;

        var indexCoerced = CoerceToNumber(indexValue);
        if (indexCoerced is ErrorValue indexCoerceError)
        {
            result = indexCoerceError;
            return true;
        }

        var rawIndex = ((NumberValue)indexCoerced).Value;
        if (!double.IsFinite(rawIndex) || rawIndex < int.MinValue || rawIndex > int.MaxValue)
        {
            result = ErrorValue.Value;
            return true;
        }

        var lookupIndex = (int)rawIndex;
        bool approximate;
        if (node.Arguments.Count <= 3)
        {
            // Genuinely omitted (no 4th argument node at all) -> Excel's friendly TRUE default.
            approximate = true;
        }
        else if (rangeLookupValue is BlankValue)
        {
            // Present but blank -- a trailing comma (VLOOKUP(A1,B:D,2,)) or a genuinely blank-cell
            // reference -- coerces to the logical natural-zero FALSE (exact match), mirroring the
            // slow path's VlookupScalar/HlookupScalar (BuiltInFunctions.Lookup.Legacy.cs). This is
            // NOT the same as omitted, even though both evaluate to BlankValue.Instance.
            approximate = false;
        }
        else
        {
            // Coerce via the SAME helper the slow path (VlookupScalar/HlookupScalar in
            // BuiltInFunctions.Lookup.Legacy.cs) uses, not the generic ToBool -- ToBool throws
            // #VALUE! on a literal "TRUE"/"FALSE" text argument, which Excel (and the slow path)
            // coerces to the corresponding logical value instead (R75-formula-lookup-vhx-4-1).
            var coercedRangeLookup = BuiltInFunctions.TryCoerceRangeLookupBool(rangeLookupValue);
            if (coercedRangeLookup is null)
            {
                result = ErrorValue.Value;
                return true;
            }
            approximate = coercedRangeLookup.Value;
        }

        if (lookupIndex < 1)
        {
            result = ErrorValue.Value;
            return true;
        }

        var maxIndex = horizontal ? rowCount : colCount;
        if (lookupIndex > maxIndex)
        {
            result = ErrorValue.Ref;
            return true;
        }

        var lookupVector = horizontal
            ? new DirectLookupRangeVector(tableSheetName, startRow, startCol, 1, colCount)
            : new DirectLookupRangeVector(tableSheetName, startRow, startCol, rowCount, 1);
        var resultVector = horizontal
            ? new DirectLookupRangeVector(tableSheetName, startRow + (uint)lookupIndex - 1, startCol, 1, colCount)
            : new DirectLookupRangeVector(tableSheetName, startRow, startCol + (uint)lookupIndex - 1, rowCount, 1);

        result = EvaluateLegacyLookupDirectTable(
            lookupValue,
            CreateDirectLookupReader(lookupVector, context),
            CreateDirectLookupReader(resultVector, context),
            approximate);
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

        result = EvaluateLookupDirectVectors(
            lookupValue,
            CreateDirectLookupReader(lookupVector, context),
            CreateDirectLookupReader(resultVector, context));
        return true;
    }

    private static ScalarValue EvaluateLegacyLookupDirectTable(
        ScalarValue lookupValue,
        DirectLookupRangeReader lookupReader,
        DirectLookupRangeReader resultReader,
        bool approximate)
    {
        if (approximate)
        {
            // R75-formula-lookup-vhx-4-2: use the same blank-lookup-value coercion as the slow
            // path (BuiltInFunctions.Lookup.Legacy.cs's ApproxLookupClassForLookupValue) -- this
            // fast path is reached even for a bare cell reference lookup_value (e.g. "A1"), since
            // TryAsRangeRef only bails to the slow path for a multi-cell range, not a single cell.
            var lookupClass = BuiltInFunctions.ApproxLookupClassForLookupValue(lookupValue);
            var best = -1;
            for (var index = 0; index < lookupReader.Count; index++)
            {
                var candidate = lookupReader.GetValue(index);
                if (candidate is ErrorValue error)
                    return error;

                if (candidate is not BlankValue && BuiltInFunctions.ApproxLookupTypeClass(candidate) != lookupClass) continue;
                // Full scan keeping the last qualifying candidate (no early break): Excel's
                // approximate match does not verify sortedness, so an out-of-order row that
                // already exceeds the lookup value must not abort the scan and yield #N/A when a
                // valid match exists later. Mirrors BuiltInFunctions.Lookup.Legacy's slow path
                // (R29-lookup-repass-1) so the literal-range fast path agrees with it.
                //
                // A genuinely blank candidate cell is let through the type-class filter (mirrors
                // BuiltInFunctions.Lookup.Legacy.cs) so CompareScalar's own blank-to-0/""
                // coercion gets a chance to run instead of the blank row being skipped like a
                // foreign type (text/logical).
                if (BuiltInFunctions.CompareScalar(candidate, lookupValue) <= 0)
                    best = index;
            }

            return best >= 0 ? resultReader.GetValue(best) : ErrorValue.NA;
        }

        for (var index = 0; index < lookupReader.Count; index++)
        {
            var candidate = lookupReader.GetValue(index);
            if (candidate is ErrorValue error)
                return error;

            if (BuiltInFunctions.MatchExactValue(candidate, lookupValue))
                return resultReader.GetValue(index);
        }

        return ErrorValue.NA;
    }

    private static ScalarValue EvaluateMatchDirectRange(
        ScalarValue lookupValue,
        DirectLookupRangeReader reader,
        int matchType)
    {
        if (matchType == 0)
        {
            for (var index = 0; index < reader.Count; index++)
            {
                var candidate = reader.GetValue(index);
                if (candidate is ErrorValue error)
                    return error;

                if (BuiltInFunctions.MatchExactValue(candidate, lookupValue))
                    return new NumberValue(index + 1);
            }

            return ErrorValue.NA;
        }

        if (matchType == 1)
        {
            // R75-formula-lookup-vhx-4-2: see EvaluateLegacyLookupDirectTable above for why the
            // blank-lookup-value coercion is needed here too.
            var lookupClass = BuiltInFunctions.ApproxLookupClassForLookupValue(lookupValue);
            var best = -1;
            for (var index = 0; index < reader.Count; index++)
            {
                var candidate = reader.GetValue(index);
                if (candidate is ErrorValue error)
                    return error;

                if (candidate is not BlankValue && BuiltInFunctions.ApproxLookupTypeClass(candidate) != lookupClass) continue;
                // Full scan keeping the last qualifying candidate (no early break) -- see the
                // rationale on EvaluateLegacyLookupDirectTable above (R29-lookup-repass-1). A
                // genuinely blank candidate is let through the type-class filter for the same
                // reason as above.
                if (BuiltInFunctions.CompareScalar(candidate, lookupValue) <= 0)
                    best = index;
            }

            return best >= 0 ? new NumberValue(best + 1) : ErrorValue.NA;
        }

        {
            // R75-formula-lookup-vhx-4-2: see EvaluateLegacyLookupDirectTable above for why the
            // blank-lookup-value coercion is needed here too.
            var lookupClass = BuiltInFunctions.ApproxLookupClassForLookupValue(lookupValue);
            var descendingBest = -1;
            for (var index = 0; index < reader.Count; index++)
            {
                var candidate = reader.GetValue(index);
                if (candidate is ErrorValue error)
                    return error;

                if (candidate is not BlankValue && BuiltInFunctions.ApproxLookupTypeClass(candidate) != lookupClass) continue;
                // Full scan keeping the last qualifying candidate (no early break) -- descending
                // MATCH mirror of the R29-lookup-repass-1 fix; do not abort on the first row that
                // falls below the lookup value. A genuinely blank candidate is let through the
                // type-class filter for the same reason as above.
                if (BuiltInFunctions.CompareScalar(candidate, lookupValue) >= 0)
                    descendingBest = index;
            }

            return descendingBest >= 0 ? new NumberValue(descendingBest + 1) : ErrorValue.NA;
        }
    }

    private static ScalarValue EvaluateXmatchDirectRange(
        ScalarValue lookupValue,
        DirectLookupRangeReader reader,
        int matchMode,
        int searchMode)
    {
        var error = TryFindDirectLookupIndex(
            lookupValue,
            reader,
            matchMode,
            searchMode,
            out var matchIndex);
        if (error is not null)
            return error;

        return matchIndex >= 0 ? new NumberValue(matchIndex + 1) : ErrorValue.NA;
    }

    private static ErrorValue? TryFindDirectLookupIndex(
        ScalarValue lookupValue,
        DirectLookupRangeReader reader,
        int matchMode,
        int searchMode,
        out int matchIndex)
    {
        if (searchMode is 2 or -2 && matchMode != 2)
            return TryFindDirectBinaryLookupIndex(lookupValue, reader, matchMode, searchMode == -2, out matchIndex);

        GetDirectLookupSearchBounds(reader.Count, searchMode, out var start, out var end, out var step);

        if (matchMode == 0)
            return TryFindDirectExactLookupIndex(lookupValue, reader, start, end, step, out matchIndex);

        if (matchMode == 2)
            return TryFindDirectWildcardLookupIndex(lookupValue, reader, start, end, step, out matchIndex);

        return TryFindDirectApproximateXmatchIndex(
            lookupValue,
            reader,
            start,
            end,
            step,
            nextSmaller: matchMode == -1,
            out matchIndex);
    }

    private static ErrorValue? TryFindDirectBinaryLookupIndex(
        ScalarValue lookupValue,
        DirectLookupRangeReader reader,
        int matchMode,
        bool descending,
        out int matchIndex)
    {
        matchIndex = -1;
        var error = TryFindDirectCompareRange(lookupValue, reader, descending, out var equalStart, out var equalEnd);
        if (error is not null)
            return error;

        error = TryFindDirectScalarEqualInRange(lookupValue, reader, equalStart, equalEnd, descending, out matchIndex);
        if (error is not null || matchIndex >= 0 || matchMode == 0)
            return error;

        if (equalStart < equalEnd)
        {
            matchIndex = descending ? equalEnd - 1 : equalStart;
            return null;
        }

        return TryFindDirectBinaryApproximateLookupIndex(
            reader,
            equalStart,
            descending,
            nextSmaller: matchMode == -1,
            out matchIndex);
    }

    private static ErrorValue? TryFindDirectScalarEqualInRange(
        ScalarValue lookupValue,
        DirectLookupRangeReader reader,
        int start,
        int end,
        bool descending,
        out int matchIndex)
    {
        matchIndex = -1;
        if (start >= end)
            return null;

        if (descending)
        {
            for (var index = end - 1; index >= start; index--)
            {
                var candidate = reader.GetValue(index);
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

        for (var index = start; index < end; index++)
        {
            var candidate = reader.GetValue(index);
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

    private static ErrorValue? TryFindDirectBinaryApproximateLookupIndex(
        DirectLookupRangeReader reader,
        int boundary,
        bool descending,
        bool nextSmaller,
        out int matchIndex)
    {
        matchIndex = -1;
        var candidateIndex = descending
            ? (nextSmaller ? boundary : boundary - 1)
            : (nextSmaller ? boundary - 1 : boundary);
        if ((uint)candidateIndex >= (uint)reader.Count)
            return null;

        var candidate = reader.GetValue(candidateIndex);
        if (candidate is ErrorValue error)
            return error;

        var rangeError = TryFindDirectCompareRange(candidate, reader, descending, out var candidateStart, out var candidateEnd);
        if (rangeError is not null)
            return rangeError;

        matchIndex = descending ? candidateEnd - 1 : candidateStart;
        return null;
    }

    private static ErrorValue? TryFindDirectCompareRange(
        ScalarValue lookupValue,
        DirectLookupRangeReader reader,
        bool descending,
        out int start,
        out int end)
    {
        var error = TryFindDirectBinarySearchBoundary(
            lookupValue,
            reader,
            descending,
            upperBound: false,
            out start);
        if (error is not null)
        {
            end = 0;
            return error;
        }

        return TryFindDirectBinarySearchBoundary(
            lookupValue,
            reader,
            descending,
            upperBound: true,
            out end);
    }

    private static ErrorValue? TryFindDirectBinarySearchBoundary(
        ScalarValue lookupValue,
        DirectLookupRangeReader reader,
        bool descending,
        bool upperBound,
        out int boundary)
    {
        var low = 0;
        var high = reader.Count;
        while (low < high)
        {
            var mid = low + ((high - low) / 2);
            var candidate = reader.GetValue(mid);
            if (candidate is ErrorValue error)
            {
                boundary = 0;
                return error;
            }

            var comparison = CompareDirectLookupSortOrder(candidate, lookupValue, descending);
            if (upperBound ? comparison <= 0 : comparison < 0)
                low = mid + 1;
            else
                high = mid;
        }

        boundary = low;
        return null;
    }

    private static int CompareDirectLookupSortOrder(ScalarValue candidate, ScalarValue lookupValue, bool descending)
    {
        var comparison = BuiltInFunctions.CompareScalar(candidate, lookupValue);
        if (comparison == 0)
            return 0;

        if (comparison < 0)
            return descending ? 1 : -1;

        return descending ? -1 : 1;
    }

    private static ErrorValue? TryFindDirectExactLookupIndex(
        ScalarValue lookupValue,
        DirectLookupRangeReader reader,
        int start,
        int end,
        int step,
        out int matchIndex)
    {
        matchIndex = -1;
        for (var index = start; index != end; index += step)
        {
            var candidate = reader.GetValue(index);
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
        DirectLookupRangeReader reader,
        int start,
        int end,
        int step,
        out int matchIndex)
    {
        matchIndex = -1;
        for (var index = start; index != end; index += step)
        {
            var candidate = reader.GetValue(index);
            if (candidate is ErrorValue error)
                return error;

            if (BuiltInFunctions.MatchExactValue(candidate, lookupValue))
            {
                matchIndex = index;
                return null;
            }
        }

        return null;
    }

    private static ErrorValue? TryFindDirectApproximateXmatchIndex(
        ScalarValue lookupValue,
        DirectLookupRangeReader reader,
        int start,
        int end,
        int step,
        bool nextSmaller,
        out int matchIndex)
    {
        matchIndex = -1;
        for (var index = start; index != end; index += step)
        {
            var candidate = reader.GetValue(index);
            if (candidate is ErrorValue error)
                return error;

            if (BuiltInFunctions.ScalarEquals(candidate, lookupValue))
            {
                matchIndex = index;
                return null;
            }
        }

        // Approximate (next-smaller/next-larger) matches only ever consider candidates whose
        // type class (number / text / bool) matches the lookup value's own type class -- a
        // text or bool candidate can never be a numeric "next larger/smaller" match, mirroring
        // BuiltInFunctions.Lookup.Modern.cs's TryFindApproximateMatchIndexLinear (the general,
        // non-fast-path XMATCH/XLOOKUP implementation).
        //
        // A genuinely blank lookup_value must be coerced to the numeric class here too (Excel
        // treats a blank lookup_value as 0 for approximate match), mirroring
        // ApproxLookupClassForLookupValue's use in the legacy VLOOKUP/HLOOKUP/MATCH/LOOKUP fast
        // paths (R75-formula-lookup-vhx-4-2 / R106).
        var lookupClass = BuiltInFunctions.ApproxLookupClassForLookupValue(lookupValue);
        var best = -1;
        ScalarValue bestValue = BlankValue.Instance;
        for (var index = start; index != end; index += step)
        {
            var candidate = reader.GetValue(index);
            if (candidate is ErrorValue error)
                return error;

            if (candidate is not BlankValue && BuiltInFunctions.ApproxLookupTypeClass(candidate) != lookupClass) continue;

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
        DirectLookupRangeReader lookupReader,
        DirectLookupRangeReader resultReader)
    {
        // R75-formula-lookup-vhx-4-2: see EvaluateLegacyLookupDirectTable above for why the
        // blank-lookup-value coercion is needed here too (LOOKUP's direct-vector fast path).
        var lookupClass = BuiltInFunctions.ApproxLookupClassForLookupValue(lookupValue);
        var matchIndex = -1;
        for (var index = 0; index < lookupReader.Count; index++)
        {
            var candidate = lookupReader.GetValue(index);
            // Match VLOOKUP/HLOOKUP/MATCH's direct-range fast paths (EvaluateLegacyLookupDirectTable
            // / EvaluateMatchDirectRange above): an error encountered while scanning the lookup
            // vector poisons the whole lookup and must be returned immediately, not skipped.
            if (candidate is ErrorValue error)
                return error;

            if (candidate is not BlankValue && BuiltInFunctions.ApproxLookupTypeClass(candidate) != lookupClass) continue;
            if (BuiltInFunctions.CompareScalar(candidate, lookupValue) <= 0)
                matchIndex = index;
        }

        if (matchIndex < 0)
            return ErrorValue.NA;

        return matchIndex < resultReader.Count
            ? resultReader.GetValue(matchIndex)
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

        // Unlike BuildRangeValue/OFFSET, this path never allocates a rowCount x colCount array --
        // it only ever wraps ONE dimension (the smaller of rowCount/colCount is always 1 once split
        // below, or the whole rectangle degenerates to a single vector) into a DirectLookupVector,
        // which lazily reads cells one at a time via DirectLookupRangeReader.GetValue. Gating the
        // raw rowCount*colCount PRODUCT against the materialization cap here rejected perfectly
        // ordinary explicit tables like A1:C500000 (3 cols x 500,000 rows = 1,500,000 cells) even
        // though the actual vector this method ever touches is bounded by a single sheet axis
        // (<= CellAddress.MaxRow or <= CellAddress.MaxCol cells) -- there is no real cap to enforce.
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

    private static bool TryCreateDirectLegacyLookupTable(
        RangeRefNode rawRange,
        IEvalContext context,
        out string? sheetName,
        out uint startRow,
        out uint startCol,
        out uint rowCount,
        out uint colCount,
        out ScalarValue result)
    {
        sheetName = null;
        startRow = 0;
        startCol = 0;
        rowCount = 0;
        colCount = 0;
        result = BlankValue.Instance;

        if (rawRange.SheetName is not null && !context.SheetExists(rawRange.SheetName))
        {
            result = ErrorValue.Ref;
            return false;
        }

        var range = ClampOpenEndedRangeToUsed(rawRange, context);
        var endRow = Math.Max(range.Start.Row, range.End.Row);
        var endCol = Math.Max(range.Start.ColumnNumber, range.End.ColumnNumber);
        startRow = Math.Min(range.Start.Row, range.End.Row);
        startCol = Math.Min(range.Start.ColumnNumber, range.End.ColumnNumber);
        rowCount = endRow - startRow + 1;
        colCount = endCol - startCol + 1;

        // See the matching comment in TryCreateDirectLookupArrayFormVectors above: this path
        // never allocates a rowCount x colCount array either -- the caller only ever reads a
        // single lookup vector plus a single index-offset vector, each lazily via cell-by-cell
        // access, so gating the raw product against the materialization cap here rejected
        // ordinary explicit bounded tables (e.g. A1:C500000) for no real memory-safety reason.
        sheetName = range.SheetName;
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

        // A single-vector shape is guaranteed above (rowCount == 1 or colCount == 1), so the
        // vector length is always bounded by a single sheet axis (<= CellAddress.MaxRow or
        // <= CellAddress.MaxCol) regardless of the other (raw/unclamped) dimension -- and
        // DirectLookupRangeReader never allocates an array, it reads lazily. No materialization
        // cap is needed here; see the matching comment on TryCreateDirectLookupArrayFormVectors.
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

        // See the matching comment on TryCreateDirectXlookupReturnVector: a single-vector shape is
        // guaranteed above, so the vector length is always bounded by a single sheet axis and no
        // materialization cap applies (the reader never allocates an array).
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

    private static DirectLookupRangeReader CreateDirectLookupReader(
        DirectLookupRangeVector vector,
        IEvalContext context)
    {
        var sheet = context is SheetEvalContext sheetContext
            ? sheetContext.ResolveSheetForFastRange(vector.SheetName)
            : null;

        return new DirectLookupRangeReader(vector, context, sheet);
    }

    /// <summary>
    /// INDIRECT("Sheet2!Name") support for a name whose RefersTo is a formula/dynamic expression
    /// AND whose scope is explicitly the sheet named in the qualifier -- the sheet-qualified
    /// counterpart of <see cref="TryResolveIndirectNamedFormula"/> (R75-meta-2). That method
    /// always resolves <paramref name="name"/> against <c>context.CurrentSheet</c>'s own scope (via
    /// <c>context.TryGetNamedFormulaText</c>, see <see cref="TryEvaluateNamedFormula"/>), so it
    /// cannot see a formula scoped to a DIFFERENT sheet than the one currently evaluating --
    /// exactly the case a sheet-qualified INDIRECT reference like "Sheet2!GrownName" needs when
    /// GrownName is a formula scoped to Sheet2 but the formula containing INDIRECT lives on
    /// Sheet1. Looks up <paramref name="sheetId"/>'s scoped named-formula dictionary directly and
    /// evaluates it through the same cycle-guarded <see cref="EvaluateNamedFormulaText"/> body
    /// <see cref="TryResolveSheetQualifiedName"/> already uses for the direct-formula-reference
    /// form (e.g. a plain <c>=Sheet2!GrownName</c> formula), so both syntaxes agree.
    /// </summary>
    internal static bool TryResolveIndirectNamedFormulaScoped(
        string name,
        FreeX.Core.Model.SheetId sheetId,
        IEvalContext context,
        out RangeValue range,
        out ScalarValue? error)
    {
        range = null!;
        error = null;

        var workbook = context.CurrentWorkbook;
        if (workbook is null || !workbook.ScopedNamedFormulas.TryGetValue((name, sheetId), out var formulaText))
            return false;

        var result = EvaluateNamedFormulaText(name, formulaText, context, sheetId);
        if (result is RangeValue rangeValue)
        {
            range = rangeValue;
            return true;
        }

        if (result is ErrorValue namedFormulaError)
            error = namedFormulaError;

        return false;
    }

    private readonly record struct DirectLookupRangeReader(
        DirectLookupRangeVector Vector,
        IEvalContext Context,
        Sheet? Sheet)
    {
        public int Count => Vector.Count;

        public ScalarValue GetValue(int index)
        {
            var row = Vector.IsRow ? Vector.StartRow : Vector.StartRow + (uint)index;
            var col = Vector.IsRow ? Vector.StartCol + (uint)index : Vector.StartCol;

            if (Sheet is not null)
                return Sheet.GetValue(row, col);

            if (Context is SheetEvalContext)
                return ErrorValue.Ref;

            return Vector.SheetName is null
                ? Context.GetCellValue(row, col)
                : Context.GetCellValue(Vector.SheetName, row, col);
        }
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
