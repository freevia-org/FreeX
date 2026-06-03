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

        GetLookupSearchBounds(lookupVector.Count, searchMode, out int start, out int end, out int step);

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
            var error = TryFindApproximateMatchIndex(lookupVector, lookupValue, start, end, step, nextSmaller: true, out int best);
            if (error is not null) return error;
            return best >= 0 ? new NumberValue(best + 1) : ErrorValue.NA;
        }

        var nextLargerError = TryFindApproximateMatchIndex(lookupVector, lookupValue, start, end, step, nextSmaller: false, out int nextLarger);
        if (nextLargerError is not null) return nextLargerError;
        return nextLarger >= 0 ? new NumberValue(nextLarger + 1) : ErrorValue.NA;
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

        if (args.Count > 3 && args[3] is ErrorValue e3) return e3;
        ScalarValue ifNotFound = args.Count > 3 && args[3] is not BlankValue ? args[3] : ErrorValue.NA;
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
        if ((matchModeRange is not null && (matchModeRange.RowCount != lookupValues.RowCount || matchModeRange.ColCount != lookupValues.ColCount)) ||
            (searchModeRange is not null && (searchModeRange.RowCount != lookupValues.RowCount || searchModeRange.ColCount != lookupValues.ColCount)))
            return ErrorValue.Value;

        var results = new ScalarValue[lookupValues.RowCount, lookupValues.ColCount];
        bool hasRangeResult = false;
        for (int r = 0; r < lookupValues.RowCount; r++)
            for (int c = 0; c < lookupValues.ColCount; c++)
            {
                var lookupValue = lookupValues.Cells[r, c];
                var matchModeValue = matchModeRange is null ? matchModeArg : matchModeRange.Cells[r, c];
                var searchModeValue = searchModeRange is null ? searchModeArg : searchModeRange.Cells[r, c];
                var result = lookupValue is ErrorValue e
                    ? e
                    : XlookupScalar(lookupValue, lookupVector, returnArr, lookupIsVertical, ifNotFound, matchModeValue, searchModeValue);
                results[r, c] = result;
                if (result is RangeValue) hasRangeResult = true;
            }

        if (!hasRangeResult) return new RangeValue(results);

        if (lookupValues.ColCount == 1)
        {
            int outputCols = -1;
            for (int r = 0; r < lookupValues.RowCount; r++)
            {
                if (results[r, 0] is not RangeValue rv) return ErrorValue.Value;
                if (rv.RowCount != 1) return ErrorValue.Value;
                if (outputCols < 0) outputCols = rv.ColCount;
                else if (rv.ColCount != outputCols) return ErrorValue.Value;
            }

            var cells = new ScalarValue[lookupValues.RowCount, outputCols];
            for (int r = 0; r < lookupValues.RowCount; r++)
            {
                var rv = (RangeValue)results[r, 0];
                for (int c = 0; c < outputCols; c++)
                    cells[r, c] = rv.Cells[0, c];
            }

            return new RangeValue(cells);
        }

        if (lookupValues.RowCount == 1)
        {
            int outputRows = -1;
            for (int c = 0; c < lookupValues.ColCount; c++)
            {
                if (results[0, c] is not RangeValue rv) return ErrorValue.Value;
                if (rv.ColCount != 1) return ErrorValue.Value;
                if (outputRows < 0) outputRows = rv.RowCount;
                else if (rv.RowCount != outputRows) return ErrorValue.Value;
            }

            var cells = new ScalarValue[outputRows, lookupValues.ColCount];
            for (int c = 0; c < lookupValues.ColCount; c++)
            {
                var rv = (RangeValue)results[0, c];
                for (int r = 0; r < outputRows; r++)
                    cells[r, c] = rv.Cells[r, 0];
            }

            return new RangeValue(cells);
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

        GetLookupSearchBounds(lookupVector.Count, searchMode, out int start, out int end, out int step);

        if (matchMode == 0)
        {
            // Exact match
            for (int i = start; i != end; i += step)
            {
                var candidate = lookupVector[i];
                if (candidate is ErrorValue err) return err;
                if (ScalarEquals(candidate, lookupValue))
                    return XlookupReturnAt(returnArr, i, lookupIsVertical);
            }
            return ifNotFound;
        }
        else if (matchMode == 2)
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
        else if (matchMode == -1)
        {
            var error = TryFindApproximateMatchIndex(lookupVector, lookupValue, start, end, step, nextSmaller: true, out int best);
            if (error is not null) return error;
            return best >= 0 ? XlookupReturnAt(returnArr, best, lookupIsVertical) : ifNotFound;
        }
        else
        {
            var error = TryFindApproximateMatchIndex(lookupVector, lookupValue, start, end, step, nextSmaller: false, out int best);
            if (error is not null) return error;
            return best >= 0 ? XlookupReturnAt(returnArr, best, lookupIsVertical) : ifNotFound;
        }
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

