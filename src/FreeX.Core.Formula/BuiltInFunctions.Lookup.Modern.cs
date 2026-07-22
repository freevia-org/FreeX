using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    private static ScalarValue Xmatch(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var lookupArr = args[1] is RangeValue lookupRange
            ? lookupRange
            : new RangeValue(new ScalarValue[1, 1] { { args[1] } });
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        if (args.Count > 3 && args[3] is ErrorValue e3) return e3;
        if (lookupArr.RowCount != 1 && lookupArr.ColCount != 1) return ErrorValue.Value;
        if (!LookupRangeVector.TryCreate(lookupArr, out var lookupVector)) return ErrorValue.Value;

        var matchModeArg = args.Count > 2 ? args[2] : BlankValue.Instance;
        var searchModeArg = args.Count > 3 ? args[3] : BlankValue.Instance;
        return MapTernaryTextArgs(args[0], matchModeArg, searchModeArg, (lookupValue, matchModeValue, searchModeValue) => XmatchScalar(lookupValue, lookupVector, matchModeValue, searchModeValue));
    }

    private static ScalarValue XmatchScalar(ScalarValue lookupValue, LookupRangeVector lookupVector, ScalarValue matchModeValue, ScalarValue searchModeValue)
    {
        double rawMatchMode  = matchModeValue is not BlankValue ? ToNumber(matchModeValue) : 0;
        double rawSearchMode = searchModeValue is not BlankValue ? ToNumber(searchModeValue) : 1;
        if (!double.IsFinite(rawMatchMode) || !double.IsFinite(rawSearchMode)) return ErrorValue.Value;
        int matchMode  = (int)rawMatchMode;
        int searchMode = (int)rawSearchMode;
        if (matchMode is not (-1 or 0 or 1 or 2)) return ErrorValue.Value;
        if (searchMode is not (-2 or -1 or 1 or 2)) return ErrorValue.Value;
        return XmatchScalar(lookupValue, lookupVector, matchMode, searchMode);
    }

    private static ScalarValue XmatchScalar(ScalarValue lookupValue, LookupRangeVector lookupVector, int matchMode, int searchMode)
    {
        if (searchMode is 1 or -1)
            return XmatchScalarLinear(lookupValue, lookupVector, matchMode, searchMode);

        if (matchMode != 2)
        {
            var binaryError = TryFindBinaryLookupIndex(lookupVector, lookupValue, matchMode, descending: searchMode == -2, out int binaryIndex);
            if (binaryError is not null) return binaryError;
            return binaryIndex >= 0 ? new NumberValue(binaryIndex + 1) : ErrorValue.NA;
        }

        GetLookupSearchBounds(lookupVector.Count, searchMode, out int start, out int end, out int step);

        string pattern = ToText(lookupValue);
        for (int i = start; i != end; i += step)
        {
            var candidate = lookupVector[i];
            if (candidate is ErrorValue err) return err;
            if (candidate is TextValue tv && WildcardMatch(tv.Value, pattern, ignoreCase: true))
                return new NumberValue(i + 1);
        }
        return ErrorValue.NA;
    }

    private static ScalarValue XmatchScalarLinear(ScalarValue lookupValue, LookupRangeVector lookupVector, int matchMode, int searchMode)
    {
        int start = searchMode == 1 ? 0 : lookupVector.Count - 1;
        int end = searchMode == 1 ? lookupVector.Count : -1;
        int step = searchMode == 1 ? 1 : -1;

        if (matchMode == 0)
        {
            for (int i = start; i != end; i += step)
            {
                var candidate = lookupVector[i];
                if (candidate is ErrorValue err) return err;
                if (ScalarEquals(candidate, lookupValue))
                    return new NumberValue(i + 1);
            }
            return ErrorValue.NA;
        }

        if (matchMode == 2)
        {
            string pattern = ToText(lookupValue);
            for (int i = start; i != end; i += step)
            {
                var candidate = lookupVector[i];
                if (candidate is ErrorValue err) return err;
                if (candidate is TextValue tv && WildcardMatch(tv.Value, pattern, ignoreCase: true))
                    return new NumberValue(i + 1);
            }
            return ErrorValue.NA;
        }

        if (matchMode == -1)
        {
            var error = TryFindApproximateMatchIndexLinear(lookupVector, lookupValue, searchMode, nextSmaller: true, out int best);
            if (error is not null) return error;
            return best >= 0 ? new NumberValue(best + 1) : ErrorValue.NA;
        }

        var nextLargerError = TryFindApproximateMatchIndexLinear(lookupVector, lookupValue, searchMode, nextSmaller: false, out int nextLarger);
        if (nextLargerError is not null) return nextLargerError;
        return nextLarger >= 0 ? new NumberValue(nextLarger + 1) : ErrorValue.NA;
    }

    // Modern lookup: XLOOKUP and shared approximate-match helpers.

    private static ScalarValue Xlookup(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var lookupArr = args[1] is RangeValue lookupRange
            ? lookupRange
            : new RangeValue(new ScalarValue[1, 1] { { args[1] } });
        if (args[2] is ErrorValue e2) return e2;
        var returnArr = args[2] is RangeValue returnRange
            ? returnRange
            : new RangeValue(new ScalarValue[1, 1] { { args[2] } });
        var lookupIsVertical = lookupArr.ColCount == 1;
        var lookupIsHorizontal = lookupArr.RowCount == 1;
        if (!lookupIsVertical && !lookupIsHorizontal) return ErrorValue.Value;
        if (lookupIsVertical && returnArr.RowCount != lookupArr.RowCount) return ErrorValue.Value;
        if (lookupIsHorizontal && returnArr.ColCount != lookupArr.ColCount) return ErrorValue.Value;
        if (!LookupRangeVector.TryCreate(lookupArr, out var lookupVector)) return ErrorValue.Value;

        var lookupValue = args[0];

        // if_not_found (args[3]) is only consulted when the lookup actually fails to find a
        // match -- like IFNA's lazy value_if_na -- so it must NOT be checked for an error (or
        // otherwise short-circuited) up front: a successful lookup returns the found value even
        // when if_not_found itself would evaluate to an error (e.g. a fallback chain of nested
        // XLOOKUPs, or XLOOKUP(key, table, result, NA())). The XlookupScalar/XlookupScalarLinear
        // helpers below already only read `ifNotFound` on their not-found branches.
        ScalarValue ifNotFound = args.Count > 3 ? args[3] : ErrorValue.NA;
        if (args.Count > 4 && args[4] is ErrorValue e4) return e4;
        if (args.Count > 5 && args[5] is ErrorValue e5) return e5;
        var matchModeArg = args.Count > 4 ? args[4] : BlankValue.Instance;
        var searchModeArg = args.Count > 5 ? args[5] : BlankValue.Instance;
        if (args[0] is RangeValue lookupValueRange)
            return XlookupRangeLookupValues(lookupValueRange, lookupVector, returnArr, lookupIsVertical, ifNotFound, matchModeArg, searchModeArg);

        return MapTernaryTextArgs(lookupValue, matchModeArg, searchModeArg,
            (lookupValueScalar, matchModeValue, searchModeValue) =>
                XlookupScalar(lookupValueScalar, lookupVector, returnArr, lookupIsVertical, ifNotFound, matchModeValue, searchModeValue));
    }

    private static ScalarValue XlookupRangeLookupValues(
        RangeValue lookupValues,
        LookupRangeVector lookupVector,
        RangeValue returnArr,
        bool lookupIsVertical,
        ScalarValue ifNotFound,
        ScalarValue matchModeArg,
        ScalarValue searchModeArg)
    {
        var matchModeRange = matchModeArg as RangeValue;
        var searchModeRange = searchModeArg as RangeValue;
        if ((matchModeRange is not null && !CanBroadcastToShape(matchModeRange, lookupValues.RowCount, lookupValues.ColCount)) ||
            (searchModeRange is not null && !CanBroadcastToShape(searchModeRange, lookupValues.RowCount, lookupValues.ColCount)))
            return ErrorValue.Value;

        var results = new ScalarValue[lookupValues.RowCount, lookupValues.ColCount];
        bool hasRangeResult = false;
        for (int r = 0; r < lookupValues.RowCount; r++)
            for (int c = 0; c < lookupValues.ColCount; c++)
            {
                var lookupValue = lookupValues.Cells[r, c];
                var matchModeValue = matchModeRange is null ? matchModeArg : ValueAtBroadcastCell(matchModeRange, r, c);
                var searchModeValue = searchModeRange is null ? searchModeArg : ValueAtBroadcastCell(searchModeRange, r, c);
                var result = lookupValue is ErrorValue e
                    ? e
                    : XlookupScalar(lookupValue, lookupVector, returnArr, lookupIsVertical, ifNotFound, matchModeValue, searchModeValue);
                results[r, c] = result;
                if (result is RangeValue) hasRangeResult = true;
            }

        // The return array's width/height for the non-lookup axis is fixed by returnArr's shape
        // regardless of whether any individual lookup hit or missed -- a miss just yields the
        // scalar ifNotFound (via XlookupScalar) rather than a RangeValue, so hasRangeResult alone
        // can't be trusted to decide whether reshaping is needed (e.g. when EVERY lookup misses,
        // hasRangeResult is false even though returnArr has multiple columns/rows and the spilled
        // result must still be that full width/height, filled with ifNotFound).
        bool needsReshape = lookupIsVertical ? returnArr.ColCount > 1 : returnArr.RowCount > 1;
        if (!hasRangeResult && !needsReshape) return new RangeValue(results);

        // The SHAPE each per-hit result comes back in (a row vs. a column) is decided by
        // lookupIsVertical/XlookupReturnAt -- the lookup_ARRAY's own orientation -- not by the
        // lookup_VALUE's orientation. A horizontal lookup_value queried against a vertical
        // lookup_array (or vice versa) still yields row-shaped (lookupIsVertical) or
        // column-shaped (!lookupIsVertical) hits; only the number/arrangement of query "slots"
        // comes from lookupValues' shape. Reshape using lookupIsVertical to decide the expected
        // hit orientation, independent of whether lookupValues itself is a row or a column.
        if (lookupValues.ColCount == 1 || lookupValues.RowCount == 1)
        {
            int queryCount = lookupValues.ColCount == 1 ? lookupValues.RowCount : lookupValues.ColCount;
            ScalarValue ResultAt(int i) => lookupValues.ColCount == 1 ? results[i, 0] : results[0, i];

            if (lookupIsVertical)
            {
                // Each hit is a ROW (or a scalar when returnArr has a single column); stack one
                // row per query.
                int outputCols = -1;
                for (int i = 0; i < queryCount; i++)
                {
                    if (ResultAt(i) is not RangeValue rv) continue;
                    if (rv.RowCount != 1) return ErrorValue.Value;
                    if (outputCols < 0) outputCols = rv.ColCount;
                    else if (rv.ColCount != outputCols) return ErrorValue.Value;
                }
                if (outputCols < 0) outputCols = returnArr.ColCount;

                var cells = new ScalarValue[queryCount, outputCols];
                for (int i = 0; i < queryCount; i++)
                {
                    var cellResult = ResultAt(i);
                    if (cellResult is RangeValue rv)
                    {
                        for (int c = 0; c < outputCols; c++)
                            cells[i, c] = rv.Cells[0, c];
                    }
                    else
                    {
                        for (int c = 0; c < outputCols; c++)
                            cells[i, c] = cellResult;
                    }
                }

                return new RangeValue(cells);
            }
            else
            {
                // Each hit is a COLUMN (or a scalar when returnArr has a single row); stack one
                // column per query.
                int outputRows = -1;
                for (int i = 0; i < queryCount; i++)
                {
                    if (ResultAt(i) is not RangeValue rv) continue;
                    if (rv.ColCount != 1) return ErrorValue.Value;
                    if (outputRows < 0) outputRows = rv.RowCount;
                    else if (rv.RowCount != outputRows) return ErrorValue.Value;
                }
                if (outputRows < 0) outputRows = returnArr.RowCount;

                var cells = new ScalarValue[outputRows, queryCount];
                for (int i = 0; i < queryCount; i++)
                {
                    var cellResult = ResultAt(i);
                    if (cellResult is RangeValue rv)
                    {
                        for (int r = 0; r < outputRows; r++)
                            cells[r, i] = rv.Cells[r, 0];
                    }
                    else
                    {
                        for (int r = 0; r < outputRows; r++)
                            cells[r, i] = cellResult;
                    }
                }

                return new RangeValue(cells);
            }
        }

        return ErrorValue.Value;
    }

    private static ScalarValue XlookupScalar(
        ScalarValue lookupValue,
        LookupRangeVector lookupVector,
        RangeValue returnArr,
        bool lookupIsVertical,
        ScalarValue ifNotFound,
        ScalarValue matchModeValue,
        ScalarValue searchModeValue)
    {
        double rawXMatchMode  = matchModeValue is not BlankValue ? ToNumber(matchModeValue) : 0;
        double rawXSearchMode = searchModeValue is not BlankValue ? ToNumber(searchModeValue) : 1;
        if (!double.IsFinite(rawXMatchMode) || !double.IsFinite(rawXSearchMode)) return ErrorValue.Value;
        int matchMode  = (int)rawXMatchMode;
        int searchMode = (int)rawXSearchMode;
        if (matchMode is not (-1 or 0 or 1 or 2)) return ErrorValue.Value;
        if (searchMode is not (-2 or -1 or 1 or 2)) return ErrorValue.Value;
        return XlookupScalar(lookupValue, lookupVector, returnArr, lookupIsVertical, ifNotFound, matchMode, searchMode);
    }

    private static ScalarValue XlookupScalar(ScalarValue lookupValue, LookupRangeVector lookupVector, RangeValue returnArr, bool lookupIsVertical, ScalarValue ifNotFound, int matchMode, int searchMode)
    {
        if (searchMode is 1 or -1)
            return XlookupScalarLinear(lookupValue, lookupVector, returnArr, lookupIsVertical, ifNotFound, matchMode, searchMode);

        if (matchMode != 2)
        {
            var binaryError = TryFindBinaryLookupIndex(lookupVector, lookupValue, matchMode, descending: searchMode == -2, out int binaryIndex);
            if (binaryError is not null) return binaryError;
            return binaryIndex >= 0 ? XlookupReturnAt(returnArr, binaryIndex, lookupIsVertical) : ifNotFound;
        }

        GetLookupSearchBounds(lookupVector.Count, searchMode, out int start, out int end, out int step);

        string pattern = ToText(lookupValue);
        for (int i = start; i != end; i += step)
        {
            var candidate = lookupVector[i];
            if (candidate is ErrorValue err) return err;
            if (candidate is TextValue tv && WildcardMatch(tv.Value, pattern, ignoreCase: true))
                return XlookupReturnAt(returnArr, i, lookupIsVertical);
        }
        return ifNotFound;
    }

    private static ScalarValue XlookupScalarLinear(
        ScalarValue lookupValue,
        LookupRangeVector lookupVector,
        RangeValue returnArr,
        bool lookupIsVertical,
        ScalarValue ifNotFound,
        int matchMode,
        int searchMode)
    {
        int start = searchMode == 1 ? 0 : lookupVector.Count - 1;
        int end = searchMode == 1 ? lookupVector.Count : -1;
        int step = searchMode == 1 ? 1 : -1;

        if (matchMode == 0)
        {
            for (int i = start; i != end; i += step)
            {
                var candidate = lookupVector[i];
                if (candidate is ErrorValue err) return err;
                if (ScalarEquals(candidate, lookupValue))
                    return XlookupReturnAt(returnArr, i, lookupIsVertical);
            }
            return ifNotFound;
        }

        if (matchMode == 2)
        {
            string pattern = ToText(lookupValue);
            for (int i = start; i != end; i += step)
            {
                var candidate = lookupVector[i];
                if (candidate is ErrorValue err) return err;
                if (candidate is TextValue tv && WildcardMatch(tv.Value, pattern, ignoreCase: true))
                    return XlookupReturnAt(returnArr, i, lookupIsVertical);
            }
            return ifNotFound;
        }

        if (matchMode == -1)
        {
            var error = TryFindApproximateMatchIndexLinear(lookupVector, lookupValue, searchMode, nextSmaller: true, out int best);
            if (error is not null) return error;
            return best >= 0 ? XlookupReturnAt(returnArr, best, lookupIsVertical) : ifNotFound;
        }

        var nextLargerError = TryFindApproximateMatchIndexLinear(lookupVector, lookupValue, searchMode, nextSmaller: false, out int nextLarger);
        if (nextLargerError is not null) return nextLargerError;
        return nextLarger >= 0 ? XlookupReturnAt(returnArr, nextLarger, lookupIsVertical) : ifNotFound;
    }

    private static ErrorValue? TryFindApproximateMatchIndex(
        LookupRangeVector lookupVector,
        ScalarValue lookupValue,
        int start,
        int end,
        int step,
        bool nextSmaller,
        out int matchIndex)
    {
        matchIndex = -1;
        for (int i = start; i != end; i += step)
        {
            var candidate = lookupVector[i];
            if (candidate is ErrorValue err) return err;
            if (ScalarEquals(candidate, lookupValue))
            {
                matchIndex = i;
                return null;
            }
        }

        int best = -1;
        for (int i = start; i != end; i += step)
        {
            var candidate = lookupVector[i];
            if (candidate is ErrorValue err) return err;
            int candidateVsLookup = CompareScalar(candidate, lookupValue);
            if (nextSmaller)
            {
                if (candidateVsLookup > 0) continue;
                if (best < 0 || CompareScalar(candidate, lookupVector[best]) > 0)
                    best = i;
            }
            else
            {
                if (candidateVsLookup < 0) continue;
                if (best < 0 || CompareScalar(candidate, lookupVector[best]) < 0)
                    best = i;
            }
        }

        matchIndex = best;
        return null;
    }

    private static void GetLookupSearchBounds(int count, int searchMode, out int start, out int end, out int step)
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

    // Binary-search (search_mode 2/-2) lookup, mirroring the direct-range fast path in
    // FormulaEvaluator.LookupFastPaths.cs (TryFindDirectBinaryLookupIndex and helpers) so that
    // wrapped-range arguments (e.g. IF(TRUE,B1:B5)) that fall through to this slow path get the
    // same binary-search semantics as a bare range reference.
    private static ErrorValue? TryFindBinaryLookupIndex(
        LookupRangeVector lookupVector,
        ScalarValue lookupValue,
        int matchMode,
        bool descending,
        out int matchIndex)
    {
        matchIndex = -1;
        var error = TryFindBinaryCompareRange(lookupVector, lookupValue, descending, out int equalStart, out int equalEnd);
        if (error is not null)
            return error;

        error = TryFindScalarEqualInRange(lookupVector, lookupValue, equalStart, equalEnd, descending, out matchIndex);
        if (error is not null || matchIndex >= 0 || matchMode == 0)
            return error;

        if (equalStart < equalEnd)
        {
            matchIndex = descending ? equalEnd - 1 : equalStart;
            return null;
        }

        return TryFindBinaryApproximateLookupIndex(lookupVector, equalStart, descending, nextSmaller: matchMode == -1, out matchIndex);
    }

    private static ErrorValue? TryFindScalarEqualInRange(
        LookupRangeVector lookupVector,
        ScalarValue lookupValue,
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
            for (int index = end - 1; index >= start; index--)
            {
                var candidate = lookupVector[index];
                if (candidate is ErrorValue error) return error;
                if (ScalarEquals(candidate, lookupValue))
                {
                    matchIndex = index;
                    return null;
                }
            }

            return null;
        }

        for (int index = start; index < end; index++)
        {
            var candidate = lookupVector[index];
            if (candidate is ErrorValue error) return error;
            if (ScalarEquals(candidate, lookupValue))
            {
                matchIndex = index;
                return null;
            }
        }

        return null;
    }

    private static ErrorValue? TryFindBinaryApproximateLookupIndex(
        LookupRangeVector lookupVector,
        int boundary,
        bool descending,
        bool nextSmaller,
        out int matchIndex)
    {
        matchIndex = -1;
        int candidateIndex = descending
            ? (nextSmaller ? boundary : boundary - 1)
            : (nextSmaller ? boundary - 1 : boundary);
        if ((uint)candidateIndex >= (uint)lookupVector.Count)
            return null;

        var candidate = lookupVector[candidateIndex];
        if (candidate is ErrorValue error) return error;

        var rangeError = TryFindBinaryCompareRange(lookupVector, candidate, descending, out int candidateStart, out int candidateEnd);
        if (rangeError is not null) return rangeError;

        matchIndex = descending ? candidateEnd - 1 : candidateStart;
        return null;
    }

    private static ErrorValue? TryFindBinaryCompareRange(
        LookupRangeVector lookupVector,
        ScalarValue lookupValue,
        bool descending,
        out int start,
        out int end)
    {
        var error = TryFindBinarySearchBoundary(lookupVector, lookupValue, descending, upperBound: false, out start);
        if (error is not null)
        {
            end = 0;
            return error;
        }

        return TryFindBinarySearchBoundary(lookupVector, lookupValue, descending, upperBound: true, out end);
    }

    private static ErrorValue? TryFindBinarySearchBoundary(
        LookupRangeVector lookupVector,
        ScalarValue lookupValue,
        bool descending,
        bool upperBound,
        out int boundary)
    {
        int low = 0;
        int high = lookupVector.Count;
        while (low < high)
        {
            int mid = low + ((high - low) / 2);
            var candidate = lookupVector[mid];
            if (candidate is ErrorValue error)
            {
                boundary = 0;
                return error;
            }

            int comparison = CompareLookupSortOrder(candidate, lookupValue, descending);
            if (upperBound ? comparison <= 0 : comparison < 0)
                low = mid + 1;
            else
                high = mid;
        }

        boundary = low;
        return null;
    }

    private static int CompareLookupSortOrder(ScalarValue candidate, ScalarValue lookupValue, bool descending)
    {
        int comparison = CompareScalar(candidate, lookupValue);
        if (comparison == 0) return 0;
        if (comparison < 0) return descending ? 1 : -1;
        return descending ? -1 : 1;
    }

    private static ErrorValue? TryFindApproximateMatchIndexLinear(
        LookupRangeVector lookupVector,
        ScalarValue lookupValue,
        int searchMode,
        bool nextSmaller,
        out int matchIndex)
    {
        matchIndex = -1;
        int start = searchMode == 1 ? 0 : lookupVector.Count - 1;
        int end = searchMode == 1 ? lookupVector.Count : -1;
        int step = searchMode == 1 ? 1 : -1;

        for (int i = start; i != end; i += step)
        {
            var candidate = lookupVector[i];
            if (candidate is ErrorValue err) return err;
            if (ScalarEquals(candidate, lookupValue))
            {
                matchIndex = i;
                return null;
            }
        }

        // Approximate (next-smaller/next-larger) matches only ever consider candidates whose
        // type class (number / text / bool) matches the lookup value's own type class -- a
        // text or bool candidate can never be a numeric "next larger/smaller" match, mirroring
        // MATCH/VLOOKUP/HLOOKUP/LOOKUP's own ApproxLookupTypeClass filtering.
        //
        // A genuinely blank candidate cell is let through the type-class filter (mirrors
        // FormulaEvaluator.LookupFastPaths.cs's EvaluateLegacyLookupDirectTable /
        // EvaluateMatchDirectRange, R29-lookup-repass-1) so CompareScalar's own blank-to-0/""
        // coercion gets a chance to run instead of the blank row being skipped like a foreign
        // type (text/logical).
        int lookupClass = ApproxLookupTypeClass(lookupValue);
        int best = -1;
        for (int i = start; i != end; i += step)
        {
            var candidate = lookupVector[i];
            if (candidate is ErrorValue err) return err;
            if (candidate is not BlankValue && ApproxLookupTypeClass(candidate) != lookupClass) continue;
            int candidateVsLookup = CompareScalar(candidate, lookupValue);
            if (nextSmaller)
            {
                if (candidateVsLookup > 0) continue;
                if (best < 0 || CompareScalar(candidate, lookupVector[best]) > 0)
                    best = i;
            }
            else
            {
                if (candidateVsLookup < 0) continue;
                if (best < 0 || CompareScalar(candidate, lookupVector[best]) < 0)
                    best = i;
            }
        }

        matchIndex = best;
        return null;
    }

    private static ScalarValue XlookupReturnAt(RangeValue returnArr, int index, bool lookupIsVertical)
    {
        if (lookupIsVertical)
        {
            if (returnArr.ColCount == 1) return returnArr.Cells[index, 0];
            var row = new ScalarValue[1, returnArr.ColCount];
            for (int c = 0; c < returnArr.ColCount; c++)
                row[0, c] = returnArr.Cells[index, c];
            return new RangeValue(row);
        }

        if (returnArr.RowCount == 1) return returnArr.Cells[0, index];
        var col = new ScalarValue[returnArr.RowCount, 1];
        for (int r = 0; r < returnArr.RowCount; r++)
            col[r, 0] = returnArr.Cells[r, index];
        return new RangeValue(col);
    }
}

