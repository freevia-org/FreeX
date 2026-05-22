using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

public static partial class BuiltInFunctions
{
    // Lookup and reference functions.

    private static ScalarValue Vlookup(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var table = args[1] is RangeValue tableRange
            ? tableRange
            : new RangeValue(new ScalarValue[1, 1] { { args[1] } });
        if (args[2] is ErrorValue e2) return e2;

        var lookupValue = args[0];
        double rawCol = ToNumber(args[2]);
        if (!double.IsFinite(rawCol) || rawCol > int.MaxValue) return ErrorValue.Value;
        int colIndex = (int)rawCol;
        if (args.Count > 3 && args[3] is ErrorValue e3) return e3;
        bool rangeLookup = args.Count < 4 || args[3] is BlankValue || ToBool(args[3]); // default TRUE

        if (colIndex < 1 || colIndex > (int)table.ColCount) return ErrorValue.Ref;

        if (rangeLookup)
        {
            // Approximate match – table must be sorted ascending on first column
            // Return last row where first-col value <= lookupValue
            int bestRow = -1;
            for (int r = 1; r <= table.RowCount; r++)
            {
                var cv = table.At(r, 1);
                if (cv is ErrorValue cvErr) return cvErr;
                if (CompareScalar(cv, lookupValue) <= 0)
                    bestRow = r;
                else
                    break;
            }
            if (bestRow < 0) return ErrorValue.NA;
            return table.At(bestRow, colIndex);
        }
        else
        {
            // Exact match — propagate errors encountered in the lookup column
            for (int r = 1; r <= table.RowCount; r++)
            {
                var cv = table.At(r, 1);
                if (cv is ErrorValue ev) return ev;
                if (MatchExactValue(cv, lookupValue))
                    return table.At(r, colIndex);
            }
            return ErrorValue.NA;
        }
    }

    private static ScalarValue Hlookup(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var table = args[1] is RangeValue tableRange
            ? tableRange
            : new RangeValue(new ScalarValue[1, 1] { { args[1] } });
        if (args[2] is ErrorValue e2) return e2;

        var lookupValue = args[0];
        double rawRow = ToNumber(args[2]);
        if (!double.IsFinite(rawRow) || rawRow > int.MaxValue) return ErrorValue.Value;
        int rowIndex = (int)rawRow;
        if (args.Count > 3 && args[3] is ErrorValue e3) return e3;
        bool rangeLookup = args.Count < 4 || args[3] is BlankValue || ToBool(args[3]);

        if (rowIndex < 1 || rowIndex > (int)table.RowCount) return ErrorValue.Ref;

        if (rangeLookup)
        {
            int bestCol = -1;
            for (int c = 1; c <= table.ColCount; c++)
            {
                var cv = table.At(1, c);
                if (cv is ErrorValue cvErr) return cvErr;
                if (CompareScalar(cv, lookupValue) <= 0)
                    bestCol = c;
                else
                    break;
            }
            if (bestCol < 0) return ErrorValue.NA;
            return table.At(rowIndex, bestCol);
        }
        else
        {
            // Exact match — propagate errors encountered in the lookup row
            for (int c = 1; c <= table.ColCount; c++)
            {
                var cv = table.At(1, c);
                if (cv is ErrorValue ev) return ev;
                if (MatchExactValue(cv, lookupValue))
                    return table.At(rowIndex, c);
            }
            return ErrorValue.NA;
        }
    }

    private static ScalarValue Index(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        var table = args[0] is RangeValue tableRange
            ? tableRange
            : new RangeValue(new ScalarValue[1, 1] { { args[0] } });
        if (args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;

        double rawRowNum = ToNumber(args[1]);
        if (!double.IsFinite(rawRowNum) || rawRowNum > int.MaxValue) return ErrorValue.Value;
        int rowNum = (int)rawRowNum;
        double rawColNum = args.Count > 2 ? ToNumber(args[2]) : 1.0;
        if (!double.IsFinite(rawColNum) || rawColNum > int.MaxValue) return ErrorValue.Value;
        int colNum = (int)rawColNum;

        // For a 1-D range with a single index argument, the index selects along the
        // only dimension (column for a 1-row range, row for a 1-column range).
        if (args.Count == 2)
        {
            if (table.RowCount == 1) { colNum = rowNum; rowNum = 1; }
            else if (table.ColCount == 1) { /* rowNum already correct, colNum = 1 */ }
        }

        // Negative indices → #VALUE! (out-of-range positive → #REF! per Excel)
        if (rowNum < 0) return ErrorValue.Value;
        if (colNum < 0) return ErrorValue.Value;
        if (rowNum > table.RowCount) return ErrorValue.Ref;
        if (colNum > table.ColCount) return ErrorValue.Ref;

        if (rowNum == 0 && colNum == 0)
            return table;

        if (rowNum == 0)
        {
            var col = new ScalarValue[table.RowCount, 1];
            for (int r = 0; r < table.RowCount; r++)
                col[r, 0] = table.Cells[r, colNum - 1];
            return new RangeValue(col);
        }

        if (colNum == 0)
        {
            var row = new ScalarValue[1, table.ColCount];
            for (int c = 0; c < table.ColCount; c++)
                row[0, c] = table.Cells[rowNum - 1, c];
            return new RangeValue(row);
        }

        return table.At(rowNum, colNum);
    }

    private static ScalarValue Match(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var table = args[1] is RangeValue tableRange
            ? tableRange
            : new RangeValue(new ScalarValue[1, 1] { { args[1] } });
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        if (table.RowCount > 1 && table.ColCount > 1) return ErrorValue.NA;

        var lookupValue = args[0];
        double rawMatchType = args.Count > 2 && args[2] is not BlankValue ? ToNumber(args[2]) : 1;
        if (!double.IsFinite(rawMatchType)) return ErrorValue.NA;
        int matchType = (int)rawMatchType;
        if (matchType is not (-1 or 0 or 1)) return ErrorValue.NA;

        // Flatten to 1-D (single row or column expected)
        var flat = table.Flatten();

        if (matchType == 0)
        {
            // Exact match — propagate errors encountered in the lookup array
            for (int i = 0; i < flat.Count; i++)
            {
                if (flat[i] is ErrorValue ev) return ev;
                if (MatchExactValue(flat[i], lookupValue))
                    return new NumberValue(i + 1);
            }
            return ErrorValue.NA;
        }
        else if (matchType == 1)
        {
            // Ascending approximate: largest value <= lookupValue
            int best = -1;
            for (int i = 0; i < flat.Count; i++)
            {
                if (flat[i] is ErrorValue fErr) return fErr;
                if (CompareScalar(flat[i], lookupValue) <= 0)
                    best = i;
                else
                    break;
            }
            if (best < 0) return ErrorValue.NA;
            return new NumberValue(best + 1);
        }
        else // matchType == -1
        {
            // Descending approximate: smallest value >= lookupValue.
            // Assumes the lookup vector is sorted descending, matching Excel's contract.
            int best = -1;
            for (int i = 0; i < flat.Count; i++)
            {
                if (flat[i] is ErrorValue fErr) return fErr;
                if (CompareScalar(flat[i], lookupValue) >= 0)
                    best = i;
                else
                    break;
            }
            if (best < 0) return ErrorValue.NA;
            return new NumberValue(best + 1);
        }
    }

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

        var lookupValue = args[0];
        var lookupFlat = lookupArr.Flatten();
        double rawMatchMode  = args.Count > 2 && args[2] is not BlankValue ? ToNumber(args[2]) : 0;
        double rawSearchMode = args.Count > 3 && args[3] is not BlankValue ? ToNumber(args[3]) : 1;
        if (!double.IsFinite(rawMatchMode) || !double.IsFinite(rawSearchMode)) return ErrorValue.Value;
        int matchMode  = (int)rawMatchMode;
        int searchMode = (int)rawSearchMode;
        if (matchMode is not (-1 or 0 or 1 or 2)) return ErrorValue.Value;
        if (searchMode is not (-2 or -1 or 1 or 2)) return ErrorValue.Value;

        var indices = Enumerable.Range(0, lookupFlat.Count).ToList();
        if (searchMode is -1 or -2) indices.Reverse();

        if (matchMode == 0)
        {
            foreach (int i in indices)
                if (ScalarEquals(lookupFlat[i], lookupValue))
                    return new NumberValue(i + 1);
            return ErrorValue.NA;
        }

        if (matchMode == 2)
        {
            string pattern = ToText(lookupValue);
            foreach (int i in indices)
                if (lookupFlat[i] is TextValue tv && WildcardMatch(tv.Value, pattern, ignoreCase: true))
                    return new NumberValue(i + 1);
            return ErrorValue.NA;
        }

        if (matchMode == -1)
        {
            int best = FindApproximateMatchIndex(lookupFlat, lookupValue, indices, nextSmaller: true);
            return best >= 0 ? new NumberValue(best + 1) : ErrorValue.NA;
        }

        int nextLarger = FindApproximateMatchIndex(lookupFlat, lookupValue, indices, nextSmaller: false);
        return nextLarger >= 0 ? new NumberValue(nextLarger + 1) : ErrorValue.NA;
    }

    private static ScalarValue Indirect(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        var refText = ToText(args[0]).Trim();
        bool useA1 = args.Count < 2 || args[1] is BlankValue || ToBool(args[1]);
        string? sheetName = null;
        int bangIdx = refText.IndexOf('!');
        if (bangIdx >= 0)
        {
            var sheetPart = refText[..bangIdx];
            if (sheetPart.StartsWith('\'') && sheetPart.EndsWith('\'') && sheetPart.Length >= 2)
                sheetName = sheetPart[1..^1].Replace("''", "'");   // strip outer quotes and unescape ''→'
            else
                sheetName = sheetPart;
            refText = refText[(bangIdx + 1)..];
        }
        if (useA1
                ? !TryParseA1Ref(refText, out uint row, out uint col)
                : !TryParseR1C1Ref(refText, out row, out col))
            return ErrorValue.Ref;
        return sheetName is not null
            ? ctx.GetCellValue(sheetName, row, col)
            : ctx.GetCellValue(row, col);
    }

    private static ScalarValue Address(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        if (args.Count > 3 && args[3] is ErrorValue e3) return e3;
        if (args.Count > 4 && args[4] is ErrorValue e4) return e4;
        double dRow = ToNumber(args[0]); double dCol = ToNumber(args[1]);
        if (!double.IsFinite(dRow) || !double.IsFinite(dCol)) return ErrorValue.Num;
        int rowNum = (int)dRow; int colNum = (int)dCol;
        if (rowNum < 1 || rowNum > (int)CellAddress.MaxRow ||
            colNum < 1 || colNum > (int)CellAddress.MaxCol) return ErrorValue.Value;
        double rawAbsNum = args.Count > 2 && args[2] is not BlankValue ? ToNumber(args[2]) : 1;
        if (!double.IsFinite(rawAbsNum)) return ErrorValue.Value;
        int absNum = (int)rawAbsNum;
        if (absNum is not (1 or 2 or 3 or 4)) return ErrorValue.Value;
        bool useA1 = args.Count < 4 || args[3] is BlankValue || ToBool(args[3]);
        string? sheetText = args.Count > 4 && args[4] is not BlankValue ? ToText(args[4]) : null;
        string colLetter = CellAddress.NumberToColumnName((uint)colNum);
        bool colAbs = absNum is 1 or 3;
        bool rowAbs = absNum is 1 or 2;
        string addr = useA1
            ? $"{(colAbs ? "$" : "")}{colLetter}{(rowAbs ? "$" : "")}{rowNum}"
            : $"{(rowAbs ? $"R{rowNum}" : $"R[{rowNum}]")}{(colAbs ? $"C{colNum}" : $"C[{colNum}]")}";
        if (!string.IsNullOrEmpty(sheetText))
            addr = $"'{sheetText}'!{addr}";
        return new TextValue(addr);
    }

    private static ScalarValue Lookup(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[1] is ErrorValue e1) return e1;
        var lookupVec = args[1] is RangeValue lookupRange
            ? lookupRange
            : new RangeValue(new ScalarValue[1, 1] { { args[1] } });
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;

        if (args.Count == 2 && lookupVec.RowCount > 1 && lookupVec.ColCount > 1)
            return LookupArrayForm(args[0], lookupVec);

        var lookupFlat = lookupVec.Flatten();
        var resultFlat = args.Count > 2
            ? (args[2] is RangeValue rv
                ? rv.Flatten()
                : new[] { args[2] })
            : lookupFlat;
        var lookupVal = args[0];
        int matchIdx = -1;
        for (int i = 0; i < lookupFlat.Count; i++)
        {
            if (lookupFlat[i] is ErrorValue lErr) return lErr;
            if (CompareScalar(lookupFlat[i], lookupVal) <= 0)
                matchIdx = i;
        }
        if (matchIdx < 0) return ErrorValue.NA;
        return matchIdx < resultFlat.Count ? resultFlat[matchIdx] : ErrorValue.NA;
    }

    private static ScalarValue LookupArrayForm(ScalarValue lookupVal, RangeValue array)
    {
        bool searchFirstRow = array.ColCount > array.RowCount;
        var lookupVector = searchFirstRow ? array.GetRow(1) : array.GetColumn(1);
        var resultVector = searchFirstRow ? array.GetRow(array.RowCount) : array.GetColumn(array.ColCount);

        int matchIdx = -1;
        for (int i = 0; i < lookupVector.Count; i++)
        {
            if (lookupVector[i] is ErrorValue lErr) return lErr;
            if (CompareScalar(lookupVector[i], lookupVal) <= 0)
                matchIdx = i;
        }

        if (matchIdx < 0) return ErrorValue.NA;
        return matchIdx < resultVector.Count ? resultVector[matchIdx] : ErrorValue.NA;
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

        var lookupValue = args[0];
        var lookupFlat = lookupArr.Flatten();

        if (args.Count > 3 && args[3] is ErrorValue e3) return e3;
        ScalarValue ifNotFound = args.Count > 3 && args[3] is not BlankValue ? args[3] : ErrorValue.NA;
        if (args.Count > 4 && args[4] is ErrorValue e4) return e4;
        if (args.Count > 5 && args[5] is ErrorValue e5) return e5;
        double rawXMatchMode  = args.Count > 4 ? ToNumber(args[4]) : 0;
        double rawXSearchMode = args.Count > 5 ? ToNumber(args[5]) : 1;
        if (!double.IsFinite(rawXMatchMode) || !double.IsFinite(rawXSearchMode)) return ErrorValue.Value;
        int matchMode  = (int)rawXMatchMode;  // 0=exact
        int searchMode = (int)rawXSearchMode; // 1=first-to-last
        if (matchMode is not (-1 or 0 or 1 or 2)) return ErrorValue.Value;
        if (searchMode is not (-2 or -1 or 1 or 2)) return ErrorValue.Value;

        var indices = Enumerable.Range(0, lookupFlat.Count).ToList();
        if (searchMode is -1 or -2) indices.Reverse();

        if (matchMode == 0)
        {
            // Exact match
            foreach (int i in indices)
                if (ScalarEquals(lookupFlat[i], lookupValue))
                    return XlookupReturnAt(returnArr, i, lookupIsVertical);
            return ifNotFound;
        }
        else if (matchMode == 2)
        {
            string pattern = ToText(lookupValue);
            foreach (int i in indices)
                if (lookupFlat[i] is TextValue tv && WildcardMatch(tv.Value, pattern, ignoreCase: true))
                    return XlookupReturnAt(returnArr, i, lookupIsVertical);
            return ifNotFound;
        }
        else if (matchMode == -1)
        {
            int best = FindApproximateMatchIndex(lookupFlat, lookupValue, indices, nextSmaller: true);
            return best >= 0 ? XlookupReturnAt(returnArr, best, lookupIsVertical) : ifNotFound;
        }
        else
        {
            int best = FindApproximateMatchIndex(lookupFlat, lookupValue, indices, nextSmaller: false);
            return best >= 0 ? XlookupReturnAt(returnArr, best, lookupIsVertical) : ifNotFound;
        }
    }

    private static int FindApproximateMatchIndex(
        IReadOnlyList<ScalarValue> lookupFlat,
        ScalarValue lookupValue,
        IReadOnlyList<int> searchIndices,
        bool nextSmaller)
    {
        foreach (int i in searchIndices)
            if (ScalarEquals(lookupFlat[i], lookupValue))
                return i;

        int best = -1;
        foreach (int i in searchIndices)
        {
            int candidateVsLookup = CompareScalar(lookupFlat[i], lookupValue);
            if (nextSmaller)
            {
                if (candidateVsLookup > 0) continue;
                if (best < 0 || CompareScalar(lookupFlat[i], lookupFlat[best]) > 0)
                    best = i;
            }
            else
            {
                if (candidateVsLookup < 0) continue;
                if (best < 0 || CompareScalar(lookupFlat[i], lookupFlat[best]) < 0)
                    best = i;
            }
        }

        return best;
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

