using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
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
            // Approximate match: table is expected to be sorted ascending on the first column, but
            // Excel does not verify this and still returns a deterministic (non-error) result for
            // genuinely unsorted data rather than aborting on the first out-of-order value. Scan the
            // full column and keep the last row where first-col value <= lookupValue (matches the
            // no-early-break scan already used by the LOOKUP() vector form below).
            // Excel skips entries whose type class differs from the lookup value's type class
            // (text headers above numeric data do not abort the scan), but a genuinely blank cell
            // is coerced to 0/"" (like any other blank participating in a comparison) rather than
            // excluded outright, so it stays eligible as a candidate between real values.
            int lookupClass = ApproxLookupTypeClass(lookupValue);
            int bestRow = -1;
            for (int r = 1; r <= table.RowCount; r++)
            {
                var cv = table.At(r, 1);
                if (cv is ErrorValue cvErr) return cvErr;
                if (cv is not BlankValue && ApproxLookupTypeClass(cv) != lookupClass) continue;
                if (CompareScalar(cv, lookupValue) <= 0)
                    bestRow = r;
            }
            if (bestRow < 0) return ErrorValue.NA;
            return table.At(bestRow, colIndex);
        }
        else
        {
            // Exact match: propagate errors encountered in the lookup column.
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
            // Approximate match: scan first row ascending, without aborting on the first
            // out-of-order value (see VlookupScalar for why: Excel still returns a deterministic
            // result for genuinely unsorted data instead of erroring on the first descending cell).
            // Skip entries whose type class differs from the lookup value's type class, but let a
            // genuinely blank cell through (see VlookupScalar) so it coerces to 0/"" instead of
            // being excluded outright.
            int lookupClass = ApproxLookupTypeClass(lookupValue);
            int bestCol = -1;
            for (int c = 1; c <= table.ColCount; c++)
            {
                var cv = table.At(1, c);
                if (cv is ErrorValue cvErr) return cvErr;
                if (cv is not BlankValue && ApproxLookupTypeClass(cv) != lookupClass) continue;
                if (CompareScalar(cv, lookupValue) <= 0)
                    bestCol = c;
            }
            if (bestCol < 0) return ErrorValue.NA;
            return table.At(rowIndex, bestCol);
        }
        else
        {
            // Exact match: propagate errors encountered in the lookup row.
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
        var areaArg = args.Count > 3 ? args[3] : BlankValue.Instance;
        return MapScalarArgs([args[1], columnArg, areaArg],
            values => IndexScalar(table, values[0], values[1], values[2], args.Count == 2));
    }

    private static ScalarValue IndexScalar(RangeValue table, ScalarValue rowValue, ScalarValue columnValue, ScalarValue areaValue, bool singleIndexArgument)
    {
        if (rowValue is ErrorValue e1) return e1;
        if (columnValue is ErrorValue e2) return e2;
        if (areaValue is ErrorValue e4) return e4;

        // area_num (4th, optional arg) selects among multiple unioned ranges in `reference`.
        // FreeX's RangeValue only ever represents a single area, so area 1 (or omitted) is the
        // only valid selector here; any other area_num is out of range per Excel's documented
        // #REF! behaviour for INDEX's reference form.
        if (areaValue is not BlankValue)
        {
            double rawAreaNum = ToNumber(areaValue);
            if (!double.IsFinite(rawAreaNum)) return ErrorValue.Value;
            if ((int)rawAreaNum != 1) return ErrorValue.Ref;
        }

        double rawRowNum = ToNumber(rowValue);
        if (!double.IsFinite(rawRowNum) || rawRowNum > int.MaxValue) return ErrorValue.Value;
        int rowNum = (int)rawRowNum;
        double rawColNum = columnValue is BlankValue ? 1.0 : ToNumber(columnValue);
        if (!double.IsFinite(rawColNum) || rawColNum > int.MaxValue) return ErrorValue.Value;
        int colNum = (int)rawColNum;

        // For a 1-D range with a single index argument, the index selects along the
        // only dimension (column for a 1-row range, row for a 1-column range).
        // For a genuine 2-D array (more than one row AND more than one column) with the
        // column_num argument omitted, modern Excel does NOT collapse to a single cell in
        // column 1 — it spills the whole selected row as a 1xN array (mirrors the explicit
        // INDEX(array, row_num, 0) / INDEX(array, row_num,) form below).
        if (singleIndexArgument)
        {
            if (table.RowCount == 1) { colNum = rowNum; rowNum = 1; }
            else if (table.ColCount == 1) { /* rowNum already correct, colNum = 1 */ }
            else { colNum = 0; }
        }

        // Negative indices â†’ #VALUE! (out-of-range positive â†’ #REF! per Excel)
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
            // If the source table is a genuine worksheet reference, the selected column's
            // coordinates map to real cells too, so carry StartRow/StartCol/SheetName forward
            // (offset to the selected column) and mark it so ROW()/COLUMN() and
            // SUBTOTAL/AGGREGATE's hidden-row exclusion see the true position (mirrors OFFSET's
            // construction in FormulaEvaluator.References.cs). A computed-array base has no real
            // coordinates, so it must stay position-less.
            return table.IsSheetReference
                ? new RangeValue(col, table.StartRow, table.StartCol + (uint)(colNum - 1)) { SheetName = table.SheetName, IsSheetReference = true }
                : new RangeValue(col);
        }

        if (colNum == 0)
        {
            var row = new ScalarValue[1, table.ColCount];
            for (int c = 0; c < table.ColCount; c++)
                row[0, c] = table.Cells[rowNum - 1, c];
            // Same reasoning as the whole-column branch above, offset to the selected row.
            return table.IsSheetReference
                ? new RangeValue(row, table.StartRow + (uint)(rowNum - 1), table.StartCol) { SheetName = table.SheetName, IsSheetReference = true }
                : new RangeValue(row);
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
        if (!LookupRangeVector.TryCreate(table, out var vector)) return ErrorValue.NA;

        if (matchType == 0)
        {
            // Exact match: propagate errors encountered in the lookup array.
            for (int i = 0; i < vector.Count; i++)
            {
                var candidate = vector[i];
                if (candidate is ErrorValue ev) return ev;
                if (MatchExactValue(candidate, lookupValue))
                    return new NumberValue(i + 1);
            }
            return ErrorValue.NA;
        }
        else if (matchType == 1)
        {
            // Ascending approximate: largest value <= lookupValue. Scan the whole vector without
            // aborting on the first out-of-order value (see VlookupScalar for why).
            // Skip entries whose type class differs from the lookup value's type class, but let a
            // genuinely blank entry through so it coerces to 0/"" instead of being excluded outright.
            int lookupClass = ApproxLookupTypeClass(lookupValue);
            int best = -1;
            for (int i = 0; i < vector.Count; i++)
            {
                var candidate = vector[i];
                if (candidate is ErrorValue fErr) return fErr;
                if (candidate is not BlankValue && ApproxLookupTypeClass(candidate) != lookupClass) continue;
                if (CompareScalar(candidate, lookupValue) <= 0)
                    best = i;
            }
            if (best < 0) return ErrorValue.NA;
            return new NumberValue(best + 1);
        }
        else // matchType == -1
        {
            // Descending approximate: smallest value >= lookupValue.
            // Assumes the lookup vector is sorted descending, matching Excel's contract, but scans
            // the whole vector without aborting on the first out-of-order value (see VlookupScalar).
            // Skip entries whose type class differs from the lookup value's type class, but let a
            // genuinely blank entry through so it coerces to 0/"" instead of being excluded outright.
            int lookupClass = ApproxLookupTypeClass(lookupValue);
            int best = -1;
            for (int i = 0; i < vector.Count; i++)
            {
                var candidate = vector[i];
                if (candidate is ErrorValue fErr) return fErr;
                if (candidate is not BlankValue && ApproxLookupTypeClass(candidate) != lookupClass) continue;
                if (CompareScalar(candidate, lookupValue) >= 0)
                    best = i;
            }
            if (best < 0) return ErrorValue.NA;
            return new NumberValue(best + 1);
        }
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

        if (LookupRangeVector.TryCreate(lookupVec, out var lookupVector))
        {
            var resultVector = args.Count > 2
                ? LookupValueVector.FromValue(args[2])
                : LookupValueVector.FromRangeVector(lookupVector);
            return LookupVectorForm(args[0], lookupVector, resultVector);
        }

        var lookupFlat = lookupVec.Flatten();
        var resultFlat = args.Count > 2
            ? (args[2] is RangeValue rv
                ? rv.Flatten()
                : new[] { args[2] })
            : lookupFlat;
        var lookupVal = args[0];
        int lookupClass = ApproxLookupTypeClass(lookupVal);
        int matchIdx = -1;
        for (int i = 0; i < lookupFlat.Count; i++)
        {
            if (lookupFlat[i] is ErrorValue) continue;
            if (lookupFlat[i] is not BlankValue && ApproxLookupTypeClass(lookupFlat[i]) != lookupClass) continue;
            if (CompareScalar(lookupFlat[i], lookupVal) <= 0)
                matchIdx = i;
        }
        if (matchIdx < 0) return ErrorValue.NA;
        return matchIdx < resultFlat.Count ? resultFlat[matchIdx] : ErrorValue.NA;
    }

    private static ScalarValue LookupVectorForm(ScalarValue lookupVal, LookupRangeVector lookupVector, LookupValueVector resultVector)
    {
        int lookupClass = ApproxLookupTypeClass(lookupVal);
        int matchIdx = -1;
        for (int i = 0; i < lookupVector.Count; i++)
        {
            var candidate = lookupVector[i];
            if (candidate is ErrorValue) continue;
            if (candidate is not BlankValue && ApproxLookupTypeClass(candidate) != lookupClass) continue;
            if (CompareScalar(candidate, lookupVal) <= 0)
                matchIdx = i;
        }

        if (matchIdx < 0) return ErrorValue.NA;
        return matchIdx < resultVector.Count ? resultVector[matchIdx] : ErrorValue.NA;
    }

    private static ScalarValue LookupArrayForm(ScalarValue lookupVal, RangeValue array)
    {
        bool searchFirstRow = array.ColCount > array.RowCount;
        var lookupVector = searchFirstRow
            ? LookupRangeVector.Row(array, 0)
            : LookupRangeVector.Column(array, 0);
        var resultVector = searchFirstRow
            ? LookupRangeVector.Row(array, array.RowCount - 1)
            : LookupRangeVector.Column(array, array.ColCount - 1);

        return LookupVectorForm(lookupVal, lookupVector, LookupValueVector.FromRangeVector(resultVector));
    }
}

