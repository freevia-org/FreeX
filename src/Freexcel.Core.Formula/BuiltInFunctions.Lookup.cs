using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

public static partial class BuiltInFunctions
{
    // Lookup and reference functions.

    private static ScalarValue Vlookup(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[1] is ErrorValue e1) return e1;
        var table = args[1] is RangeValue tableRange
            ? tableRange
            : new RangeValue(new ScalarValue[1, 1] { { args[1] } });
        var rangeLookupArg = args.Count > 3 ? args[3] : BlankValue.Instance;
        return MapScalarArgs([args[0], args[2], rangeLookupArg],
            values => VlookupScalar(values[0], table, values[1], values[2]));
    }

    private static ScalarValue VlookupScalar(ScalarValue lookupValue, RangeValue table, ScalarValue columnIndexValue, ScalarValue rangeLookupValue)
    {
        if (lookupValue is ErrorValue e0) return e0;
        if (columnIndexValue is ErrorValue e2) return e2;
        if (rangeLookupValue is ErrorValue e3) return e3;
        double rawCol = ToNumber(columnIndexValue);
        if (!double.IsFinite(rawCol) || rawCol > int.MaxValue) return ErrorValue.Value;
        int colIndex = (int)rawCol;
        bool rangeLookup = rangeLookupValue is BlankValue || ToBool(rangeLookupValue); // default TRUE

        if (colIndex < 1) return ErrorValue.Value;
        if (colIndex > (int)table.ColCount) return ErrorValue.Ref;

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
        if (args[1] is ErrorValue e1) return e1;
        var table = args[1] is RangeValue tableRange
            ? tableRange
            : new RangeValue(new ScalarValue[1, 1] { { args[1] } });
        var rangeLookupArg = args.Count > 3 ? args[3] : BlankValue.Instance;
        return MapScalarArgs([args[0], args[2], rangeLookupArg],
            values => HlookupScalar(values[0], table, values[1], values[2]));
    }

    private static ScalarValue HlookupScalar(ScalarValue lookupValue, RangeValue table, ScalarValue rowIndexValue, ScalarValue rangeLookupValue)
    {
        if (lookupValue is ErrorValue e0) return e0;
        if (rowIndexValue is ErrorValue e2) return e2;
        if (rangeLookupValue is ErrorValue e3) return e3;
        double rawRow = ToNumber(rowIndexValue);
        if (!double.IsFinite(rawRow) || rawRow > int.MaxValue) return ErrorValue.Value;
        int rowIndex = (int)rawRow;
        bool rangeLookup = rangeLookupValue is BlankValue || ToBool(rangeLookupValue);

        if (rowIndex < 1) return ErrorValue.Value;
        if (rowIndex > (int)table.RowCount) return ErrorValue.Ref;

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
        var columnArg = args.Count > 2 ? args[2] : BlankValue.Instance;
        return MapScalarArgs([args[1], columnArg],
            values => IndexScalar(table, values[0], values[1], args.Count == 2));
    }

    private static ScalarValue IndexScalar(RangeValue table, ScalarValue rowValue, ScalarValue columnValue, bool singleIndexArgument)
    {
        if (rowValue is ErrorValue e1) return e1;
        if (columnValue is ErrorValue e2) return e2;
        double rawRowNum = ToNumber(rowValue);
        if (!double.IsFinite(rawRowNum) || rawRowNum > int.MaxValue) return ErrorValue.Value;
        int rowNum = (int)rawRowNum;
        double rawColNum = columnValue is BlankValue ? 1.0 : ToNumber(columnValue);
        if (!double.IsFinite(rawColNum) || rawColNum > int.MaxValue) return ErrorValue.Value;
        int colNum = (int)rawColNum;

        // For a 1-D range with a single index argument, the index selects along the
        // only dimension (column for a 1-row range, row for a 1-column range).
        if (singleIndexArgument)
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
        if (args[1] is ErrorValue e1) return e1;
        var table = args[1] is RangeValue tableRange
            ? tableRange
            : new RangeValue(new ScalarValue[1, 1] { { args[1] } });
        if (table.RowCount > 1 && table.ColCount > 1) return ErrorValue.NA;
        var matchTypeArg = args.Count > 2 ? args[2] : BlankValue.Instance;
        return MapScalarArgs([args[0], matchTypeArg],
            values => MatchScalar(values[0], table, values[1]));
    }

    private static ScalarValue MatchScalar(ScalarValue lookupValue, RangeValue table, ScalarValue matchTypeValue)
    {
        if (lookupValue is ErrorValue e0) return e0;
        if (matchTypeValue is ErrorValue e2) return e2;
        double rawMatchType = matchTypeValue is not BlankValue ? ToNumber(matchTypeValue) : 1;
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

        var lookupFlat = lookupArr.Flatten();
        var matchModeArg = args.Count > 2 ? args[2] : BlankValue.Instance;
        var searchModeArg = args.Count > 3 ? args[3] : BlankValue.Instance;
        return MapTernaryTextArgs(args[0], matchModeArg, searchModeArg, (lookupValue, matchModeValue, searchModeValue) => XmatchScalar(lookupValue, lookupFlat, matchModeValue, searchModeValue));
    }

    private static ScalarValue XmatchScalar(ScalarValue lookupValue, IReadOnlyList<ScalarValue> lookupFlat, ScalarValue matchModeValue, ScalarValue searchModeValue)
    {
        double rawMatchMode  = matchModeValue is not BlankValue ? ToNumber(matchModeValue) : 0;
        double rawSearchMode = searchModeValue is not BlankValue ? ToNumber(searchModeValue) : 1;
        if (!double.IsFinite(rawMatchMode) || !double.IsFinite(rawSearchMode)) return ErrorValue.Value;
        int matchMode  = (int)rawMatchMode;
        int searchMode = (int)rawSearchMode;
        if (matchMode is not (-1 or 0 or 1 or 2)) return ErrorValue.Value;
        if (searchMode is not (-2 or -1 or 1 or 2)) return ErrorValue.Value;
        return XmatchScalar(lookupValue, lookupFlat, matchMode, searchMode);
    }

    private static ScalarValue XmatchScalar(ScalarValue lookupValue, IReadOnlyList<ScalarValue> lookupFlat, int matchMode, int searchMode)
    {
        if (searchMode is 1 or -1)
            return XmatchScalarLinear(lookupValue, lookupFlat, matchMode, searchMode);

        GetLookupSearchBounds(lookupFlat.Count, searchMode, out int start, out int end, out int step);

        if (matchMode == 0)
        {
            for (int i = start; i != end; i += step)
            {
                if (lookupFlat[i] is ErrorValue err) return err;
                if (ScalarEquals(lookupFlat[i], lookupValue))
                    return new NumberValue(i + 1);
            }
            return ErrorValue.NA;
        }

        if (matchMode == 2)
        {
            string pattern = ToText(lookupValue);
            for (int i = start; i != end; i += step)
            {
                if (lookupFlat[i] is ErrorValue err) return err;
                if (lookupFlat[i] is TextValue tv && WildcardMatch(tv.Value, pattern, ignoreCase: true))
                    return new NumberValue(i + 1);
            }
            return ErrorValue.NA;
        }

        if (matchMode == -1)
        {
            var error = TryFindApproximateMatchIndex(lookupFlat, lookupValue, start, end, step, nextSmaller: true, out int best);
            if (error is not null) return error;
            return best >= 0 ? new NumberValue(best + 1) : ErrorValue.NA;
        }

        var nextLargerError = TryFindApproximateMatchIndex(lookupFlat, lookupValue, start, end, step, nextSmaller: false, out int nextLarger);
        if (nextLargerError is not null) return nextLargerError;
        return nextLarger >= 0 ? new NumberValue(nextLarger + 1) : ErrorValue.NA;
    }

    private static ScalarValue XmatchScalarLinear(ScalarValue lookupValue, IReadOnlyList<ScalarValue> lookupFlat, int matchMode, int searchMode)
    {
        int start = searchMode == 1 ? 0 : lookupFlat.Count - 1;
        int end = searchMode == 1 ? lookupFlat.Count : -1;
        int step = searchMode == 1 ? 1 : -1;

        if (matchMode == 0)
        {
            for (int i = start; i != end; i += step)
            {
                if (lookupFlat[i] is ErrorValue err) return err;
                if (ScalarEquals(lookupFlat[i], lookupValue))
                    return new NumberValue(i + 1);
            }
            return ErrorValue.NA;
        }

        if (matchMode == 2)
        {
            string pattern = ToText(lookupValue);
            for (int i = start; i != end; i += step)
            {
                if (lookupFlat[i] is ErrorValue err) return err;
                if (lookupFlat[i] is TextValue tv && WildcardMatch(tv.Value, pattern, ignoreCase: true))
                    return new NumberValue(i + 1);
            }
            return ErrorValue.NA;
        }

        if (matchMode == -1)
        {
            var error = TryFindApproximateMatchIndexLinear(lookupFlat, lookupValue, searchMode, nextSmaller: true, out int best);
            if (error is not null) return error;
            return best >= 0 ? new NumberValue(best + 1) : ErrorValue.NA;
        }

        var nextLargerError = TryFindApproximateMatchIndexLinear(lookupFlat, lookupValue, searchMode, nextSmaller: false, out int nextLarger);
        if (nextLargerError is not null) return nextLargerError;
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
        if (useA1 && TryParseA1RangeRef(refText, out var startRow, out var startCol, out var endRow, out var endCol))
            return BuildIndirectRange(ctx, sheetName, startRow, startCol, endRow, endCol);

        if (useA1 && sheetName is null && ctx.TryResolveNamedRange(refText) is { } namedRange)
        {
            var namedSheetName = ctx.TryGetSheetName(namedRange.Start.Sheet);
            return namedSheetName is null
                ? ErrorValue.Ref
                : BuildIndirectRange(
                    ctx,
                    namedSheetName,
                    namedRange.Start.Row,
                    namedRange.Start.Col,
                    namedRange.End.Row,
                    namedRange.End.Col);
        }

        if (useA1
                ? !TryParseA1Ref(refText, out uint row, out uint col)
                : !TryParseR1C1Ref(refText, ctx.CurrentCellAddress, out row, out col))
            return ErrorValue.Ref;
        return sheetName is not null
            ? ctx.GetCellValue(sheetName, row, col)
            : ctx.GetCellValue(row, col);
    }

    private static ScalarValue BuildIndirectRange(
        IEvalContext ctx,
        string? sheetName,
        uint startRow,
        uint startCol,
        uint endRow,
        uint endCol)
    {
        if (sheetName is not null && !ctx.SheetExists(sheetName)) return ErrorValue.Ref;

        uint r0 = Math.Min(startRow, endRow);
        uint r1 = Math.Max(startRow, endRow);
        uint c0 = Math.Min(startCol, endCol);
        uint c1 = Math.Max(startCol, endCol);
        var cells = new ScalarValue[r1 - r0 + 1, c1 - c0 + 1];
        for (uint r = r0; r <= r1; r++)
            for (uint c = c0; c <= c1; c++)
                cells[r - r0, c - c0] = sheetName is not null
                    ? ctx.GetCellValue(sheetName, r, c)
                    : ctx.GetCellValue(r, c);

        return new RangeValue(cells, r0, c0) { SheetName = sheetName };
    }

    private static bool TryParseA1RangeRef(string refText, out uint startRow, out uint startCol, out uint endRow, out uint endCol)
    {
        startRow = startCol = endRow = endCol = 0;
        int colon = refText.IndexOf(':');
        if (colon < 0 || colon != refText.LastIndexOf(':')) return false;

        return TryParseA1Ref(refText[..colon], out startRow, out startCol)
            && TryParseA1Ref(refText[(colon + 1)..], out endRow, out endCol);
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
            addr = $"'{sheetText.Replace("'", "''")}'!{addr}";
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
        var matchModeArg = args.Count > 4 ? args[4] : BlankValue.Instance;
        var searchModeArg = args.Count > 5 ? args[5] : BlankValue.Instance;
        if (args[0] is RangeValue lookupValueRange)
            return XlookupRangeLookupValues(lookupValueRange, lookupFlat, returnArr, lookupIsVertical, ifNotFound, matchModeArg, searchModeArg);

        return MapTernaryTextArgs(lookupValue, matchModeArg, searchModeArg,
            (lookupValueScalar, matchModeValue, searchModeValue) =>
                XlookupScalar(lookupValueScalar, lookupFlat, returnArr, lookupIsVertical, ifNotFound, matchModeValue, searchModeValue));
    }

    private static ScalarValue XlookupRangeLookupValues(
        RangeValue lookupValues,
        IReadOnlyList<ScalarValue> lookupFlat,
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
                    : XlookupScalar(lookupValue, lookupFlat, returnArr, lookupIsVertical, ifNotFound, matchModeValue, searchModeValue);
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
        IReadOnlyList<ScalarValue> lookupFlat,
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
        return XlookupScalar(lookupValue, lookupFlat, returnArr, lookupIsVertical, ifNotFound, matchMode, searchMode);
    }

    private static ScalarValue XlookupScalar(ScalarValue lookupValue, IReadOnlyList<ScalarValue> lookupFlat, RangeValue returnArr, bool lookupIsVertical, ScalarValue ifNotFound, int matchMode, int searchMode)
    {
        if (searchMode is 1 or -1)
            return XlookupScalarLinear(lookupValue, lookupFlat, returnArr, lookupIsVertical, ifNotFound, matchMode, searchMode);

        GetLookupSearchBounds(lookupFlat.Count, searchMode, out int start, out int end, out int step);

        if (matchMode == 0)
        {
            // Exact match
            for (int i = start; i != end; i += step)
            {
                if (lookupFlat[i] is ErrorValue err) return err;
                if (ScalarEquals(lookupFlat[i], lookupValue))
                    return XlookupReturnAt(returnArr, i, lookupIsVertical);
            }
            return ifNotFound;
        }
        else if (matchMode == 2)
        {
            string pattern = ToText(lookupValue);
            for (int i = start; i != end; i += step)
            {
                if (lookupFlat[i] is ErrorValue err) return err;
                if (lookupFlat[i] is TextValue tv && WildcardMatch(tv.Value, pattern, ignoreCase: true))
                    return XlookupReturnAt(returnArr, i, lookupIsVertical);
            }
            return ifNotFound;
        }
        else if (matchMode == -1)
        {
            var error = TryFindApproximateMatchIndex(lookupFlat, lookupValue, start, end, step, nextSmaller: true, out int best);
            if (error is not null) return error;
            return best >= 0 ? XlookupReturnAt(returnArr, best, lookupIsVertical) : ifNotFound;
        }
        else
        {
            var error = TryFindApproximateMatchIndex(lookupFlat, lookupValue, start, end, step, nextSmaller: false, out int best);
            if (error is not null) return error;
            return best >= 0 ? XlookupReturnAt(returnArr, best, lookupIsVertical) : ifNotFound;
        }
    }

    private static ScalarValue XlookupScalarLinear(
        ScalarValue lookupValue,
        IReadOnlyList<ScalarValue> lookupFlat,
        RangeValue returnArr,
        bool lookupIsVertical,
        ScalarValue ifNotFound,
        int matchMode,
        int searchMode)
    {
        int start = searchMode == 1 ? 0 : lookupFlat.Count - 1;
        int end = searchMode == 1 ? lookupFlat.Count : -1;
        int step = searchMode == 1 ? 1 : -1;

        if (matchMode == 0)
        {
            for (int i = start; i != end; i += step)
            {
                if (lookupFlat[i] is ErrorValue err) return err;
                if (ScalarEquals(lookupFlat[i], lookupValue))
                    return XlookupReturnAt(returnArr, i, lookupIsVertical);
            }
            return ifNotFound;
        }

        if (matchMode == 2)
        {
            string pattern = ToText(lookupValue);
            for (int i = start; i != end; i += step)
            {
                if (lookupFlat[i] is ErrorValue err) return err;
                if (lookupFlat[i] is TextValue tv && WildcardMatch(tv.Value, pattern, ignoreCase: true))
                    return XlookupReturnAt(returnArr, i, lookupIsVertical);
            }
            return ifNotFound;
        }

        if (matchMode == -1)
        {
            var error = TryFindApproximateMatchIndexLinear(lookupFlat, lookupValue, searchMode, nextSmaller: true, out int best);
            if (error is not null) return error;
            return best >= 0 ? XlookupReturnAt(returnArr, best, lookupIsVertical) : ifNotFound;
        }

        var nextLargerError = TryFindApproximateMatchIndexLinear(lookupFlat, lookupValue, searchMode, nextSmaller: false, out int nextLarger);
        if (nextLargerError is not null) return nextLargerError;
        return nextLarger >= 0 ? XlookupReturnAt(returnArr, nextLarger, lookupIsVertical) : ifNotFound;
    }

    private static ErrorValue? TryFindApproximateMatchIndex(
        IReadOnlyList<ScalarValue> lookupFlat,
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
            if (lookupFlat[i] is ErrorValue err) return err;
            if (ScalarEquals(lookupFlat[i], lookupValue))
            {
                matchIndex = i;
                return null;
            }
        }

        int best = -1;
        for (int i = start; i != end; i += step)
        {
            if (lookupFlat[i] is ErrorValue err) return err;
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
        IReadOnlyList<ScalarValue> lookupFlat,
        ScalarValue lookupValue,
        int searchMode,
        bool nextSmaller,
        out int matchIndex)
    {
        matchIndex = -1;
        int start = searchMode == 1 ? 0 : lookupFlat.Count - 1;
        int end = searchMode == 1 ? lookupFlat.Count : -1;
        int step = searchMode == 1 ? 1 : -1;

        for (int i = start; i != end; i += step)
        {
            if (lookupFlat[i] is ErrorValue err) return err;
            if (ScalarEquals(lookupFlat[i], lookupValue))
            {
                matchIndex = i;
                return null;
            }
        }

        int best = -1;
        for (int i = start; i != end; i += step)
        {
            if (lookupFlat[i] is ErrorValue err) return err;
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

