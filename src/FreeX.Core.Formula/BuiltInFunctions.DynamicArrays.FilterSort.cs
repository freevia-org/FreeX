using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    private static ScalarValue Filter(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue arrayError) return arrayError;
        var arr = args[0] is RangeValue arrayRange
            ? arrayRange
            : new RangeValue(new ScalarValue[1, 1] { { args[0] } });
        if (args[1] is ErrorValue includeError) return includeError;
        var include = args[1] is RangeValue includeRange
            ? includeRange
            : new RangeValue(new ScalarValue[1, 1] { { args[1] } });
        var ifEmpty = args.Count > 2 && args[2] is not BlankValue ? args[2] : ErrorValue.Calc;

        if (include.ColCount == 1 && include.RowCount == arr.RowCount)
            return FilterRows(arr, include, ifEmpty);

        if (include.RowCount == 1 && include.ColCount == arr.ColCount)
            return FilterColumns(arr, include, ifEmpty);

        return ErrorValue.Value;
    }

    private static ScalarValue FilterRows(RangeValue arr, RangeValue include, ScalarValue ifEmpty)
    {
        var matchedRows = new List<int>();
        for (int i = 0; i < arr.RowCount; i++)
        {
            var v = include.Cells[i, 0];
            if (v is ErrorValue e) return e;
            if (!TryFilterIncluded(v, out bool included)) return ErrorValue.Value;
            if (included) matchedRows.Add(i);
        }

        if (matchedRows.Count == 0)
            return FilterEmptyResult(ifEmpty);

        var result = new ScalarValue[matchedRows.Count, arr.ColCount];
        for (int ri = 0; ri < matchedRows.Count; ri++)
            for (int c = 0; c < arr.ColCount; c++)
                result[ri, c] = arr.Cells[matchedRows[ri], c];
        return new RangeValue(result);
    }

    private static ScalarValue FilterColumns(RangeValue arr, RangeValue include, ScalarValue ifEmpty)
    {
        var matchedCols = new List<int>();
        for (int c = 0; c < arr.ColCount; c++)
        {
            var v = include.Cells[0, c];
            if (v is ErrorValue e) return e;
            if (!TryFilterIncluded(v, out bool included)) return ErrorValue.Value;
            if (included) matchedCols.Add(c);
        }

        if (matchedCols.Count == 0)
            return FilterEmptyResult(ifEmpty);

        var result = new ScalarValue[arr.RowCount, matchedCols.Count];
        for (int r = 0; r < arr.RowCount; r++)
            for (int ci = 0; ci < matchedCols.Count; ci++)
                result[r, ci] = arr.Cells[r, matchedCols[ci]];
        return new RangeValue(result);
    }

    private static bool TryFilterIncluded(ScalarValue value, out bool included)
    {
        included = false;
        if (value is BlankValue) return true;
        if (value is BoolValue b)
        {
            included = b.Value;
            return true;
        }

        if (TryCellNumber(value, out double number))
        {
            included = number != 0;
            return true;
        }

        return false;
    }

    private static ScalarValue FilterEmptyResult(ScalarValue ifEmpty) =>
        ifEmpty switch
        {
            ErrorValue e => e,
            RangeValue rvEmpty => rvEmpty,
            _ => new RangeValue(new ScalarValue[1, 1] { { ifEmpty } })
        };

    private static ScalarValue Sort(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue arrayError) return arrayError;
        var arr = args[0] is RangeValue arrayRange
            ? arrayRange
            : new RangeValue(new ScalarValue[1, 1] { { args[0] } });

        // by_col must be a single scalar; resolve it first so the sort orientation is known.
        if (!TryGetScalarControlArgument(args.Count > 3 ? args[3] : BlankValue.Instance, out var byColArg, out var byColError)) return byColError;
        bool byCol = byColArg is not BlankValue && ToBool(byColArg);

        // sort_index and sort_order may each be a 1-D array, defining a multi-key sort. A blank
        // sort_index defaults to a single key on the first row/column. sort_order defaults to
        // ascending; a single sort_order is broadcast across all keys.
        if (!TryReadSortControlVector(args.Count > 1 ? args[1] : BlankValue.Instance, out var sortIdxRaws, out var sortIdxError)) return sortIdxError;
        if (!TryReadSortControlVector(args.Count > 2 ? args[2] : BlankValue.Instance, out var sortOrderRaws, out var sortOrderError)) return sortOrderError;

        if (sortIdxRaws.Count == 0) sortIdxRaws = new List<double> { 1 };
        if (sortOrderRaws.Count == 0) sortOrderRaws = new List<double> { 1 };
        if (sortOrderRaws.Count != 1 && sortOrderRaws.Count != sortIdxRaws.Count) return ErrorValue.Value;

        var keyCount = sortIdxRaws.Count;
        int axisLength = byCol ? arr.RowCount : arr.ColCount;
        var keys = new (int Index, int Order)[keyCount];
        for (int k = 0; k < keyCount; k++)
        {
            double idxRaw = sortIdxRaws[k];
            if (!double.IsFinite(idxRaw)) return ErrorValue.Value;
            int idx = (int)idxRaw - 1;
            if (idx < 0 || idx >= axisLength) return ErrorValue.Value;

            double orderRaw = sortOrderRaws.Count == 1 ? sortOrderRaws[0] : sortOrderRaws[k];
            if (!double.IsFinite(orderRaw)) return ErrorValue.Value;
            int order = (int)orderRaw;
            if (order != 1 && order != -1) return ErrorValue.Value;

            keys[k] = (idx, order);
        }

        // Excel's SORT is all-or-nothing: an error anywhere in a sort key column/row makes the
        // whole result that error, mirroring FILTER's `if (v is ErrorValue e) return e;` guard
        // on its deciding array above.
        foreach (var (idx, _) in keys)
        {
            var keyError = FindErrorInSortKey(arr, idx, byCol);
            if (keyError is not null) return keyError;
        }

        if (!byCol)
        {
            var rowIndices = CreateSequentialIndices(arr.RowCount);
            Array.Sort(rowIndices, (a, b) =>
            {
                foreach (var (idx, order) in keys)
                {
                    var cmp = CompareSortKey(arr.Cells[a, idx], arr.Cells[b, idx], order);
                    if (cmp != 0) return cmp;
                }
                return a.CompareTo(b); // stable: preserve original order for ties (Excel SORT is stable)
            });
            var result = new ScalarValue[arr.RowCount, arr.ColCount];
            for (int r = 0; r < arr.RowCount; r++)
                for (int c = 0; c < arr.ColCount; c++)
                    result[r, c] = arr.Cells[rowIndices[r], c];
            return new RangeValue(result);
        }
        else
        {
            var colIndices = CreateSequentialIndices(arr.ColCount);
            Array.Sort(colIndices, (a, b) =>
            {
                foreach (var (idx, order) in keys)
                {
                    var cmp = CompareSortKey(arr.Cells[idx, a], arr.Cells[idx, b], order);
                    if (cmp != 0) return cmp;
                }
                return a.CompareTo(b); // stable: preserve original order for ties (Excel SORT is stable)
            });
            var result = new ScalarValue[arr.RowCount, arr.ColCount];
            for (int r = 0; r < arr.RowCount; r++)
                for (int c = 0; c < arr.ColCount; c++)
                    result[r, c] = arr.Cells[r, colIndices[c]];
            return new RangeValue(result);
        }
    }

    // Reads a SORT control argument (sort_index / sort_order) as a flat list of numbers. A scalar
    // yields a single-element list; a 1-D array (row or column vector) yields its elements in order,
    // enabling multi-key sorts. A blank yields an empty list (caller applies the default). A 2-D
    // array or an embedded error is rejected.
    private static bool TryReadSortControlVector(ScalarValue value, out List<double> numbers, out ScalarValue error)
    {
        numbers = new List<double>();
        error = ErrorValue.Value;

        if (value is ErrorValue directError) { error = directError; return false; }
        if (value is BlankValue) return true;

        if (value is RangeValue range)
        {
            if (range.RowCount != 1 && range.ColCount != 1) return false; // 2-D control argument is invalid
            foreach (var cell in range.Cells)
            {
                if (cell is ErrorValue cellError) { error = cellError; return false; }
                if (cell is BlankValue) continue;
                numbers.Add(ToNumber(cell));
            }
            return true;
        }

        numbers.Add(ToNumber(value));
        return true;
    }

    // Excel's SORT/SORTBY always places blank cells LAST, regardless of ascending/descending order.
    // (Empty cells in the sort key are pushed to the end; only non-blank values honor sort_order.)
    private static int CompareSortKey(ScalarValue va, ScalarValue vb, int sortOrder)
    {
        // Excel always pushes empty cells to the very bottom of a SORT, regardless of
        // ascending/descending order. Only non-empty values honor sort_order.
        bool aBlank = va is BlankValue;
        bool bBlank = vb is BlankValue;
        if (aBlank && bBlank) return 0;
        if (aBlank) return 1;
        if (bBlank) return -1;
        return sortOrder * CompareScalar(va, vb);
    }

    // Scans every value along the sort key at `idx` (a column when sorting rows, a row when sorting
    // columns) for an ErrorValue. Returns the first one found so SORT/SORTBY can propagate it as the
    // whole function result instead of letting CompareScalar's cross-type fallback silently place it.
    private static ErrorValue? FindErrorInSortKey(RangeValue arr, int idx, bool byCol)
    {
        if (byCol)
        {
            for (int c = 0; c < arr.ColCount; c++)
                if (arr.Cells[idx, c] is ErrorValue e) return e;
        }
        else
        {
            for (int r = 0; r < arr.RowCount; r++)
                if (arr.Cells[r, idx] is ErrorValue e) return e;
        }

        return null;
    }

    private static ScalarValue SortBy(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue arrayError) return arrayError;
        var arr = args[0] is RangeValue arrayRange
            ? arrayRange
            : new RangeValue(new ScalarValue[1, 1] { { args[0] } });

        var keys = new List<(RangeValue Range, int Order)>();
        bool? sortRows = null;

        for (int i = 1; i < args.Count; i++)
        {
            if (args[i] is ErrorValue keyError) return keyError;
            var byArray = args[i] is RangeValue byArrayRange
                ? byArrayRange
                : new RangeValue(new ScalarValue[1, 1] { { args[i] } });

            if (!TryGetSortByOrientation(arr, byArray, out bool keySortsRows)) return ErrorValue.Value;
            if (sortRows.HasValue && sortRows.Value != keySortsRows) return ErrorValue.Value;
            sortRows ??= keySortsRows;

            int sortOrder = 1;
            if (i + 1 < args.Count)
            {
                if (!TryGetScalarControlArgument(args[i + 1], out var orderArg, out var orderError)) return orderError;
                if (orderArg is not BlankValue)
                {
                    if (orderArg is ErrorValue orderArgError) return orderArgError;
                    double orderRaw = ToNumber(orderArg);
                    if (!double.IsFinite(orderRaw)) return ErrorValue.Value;
                    sortOrder = (int)orderRaw;
                    if (sortOrder != 1 && sortOrder != -1) return ErrorValue.Value;
                }
                i++;
            }

            keys.Add((byArray, sortOrder));
        }

        if (keys.Count == 0) return ErrorValue.Value;

        // Same all-or-nothing propagation as SORT: an error anywhere in a by_array key means the
        // whole SORTBY result is that error.
        foreach (var key in keys)
            foreach (var cell in key.Range.Cells)
                if (cell is ErrorValue e) return e;

        return sortRows.GetValueOrDefault(true)
            ? SortByRows(arr, keys)
            : SortByColumns(arr, keys);
    }

    private static bool TryGetSortByOrientation(RangeValue arr, RangeValue byArray, out bool sortRows)
    {
        if (byArray.RowCount == arr.RowCount && byArray.ColCount == 1)
        {
            sortRows = true;
            return true;
        }

        if (byArray.RowCount == 1 && byArray.ColCount == arr.ColCount)
        {
            sortRows = false;
            return true;
        }

        sortRows = true;
        return false;
    }

    private static ScalarValue SortByRows(RangeValue arr, IReadOnlyList<(RangeValue Range, int Order)> keys)
    {
        var rowIndices = CreateSequentialIndices(arr.RowCount);
        Array.Sort(rowIndices, (a, b) =>
        {
            foreach (var key in keys)
            {
                int cmp = CompareSortKey(key.Range.Cells[a, 0], key.Range.Cells[b, 0], key.Order);
                if (cmp != 0) return cmp;
            }

            return a.CompareTo(b);
        });

        var result = new ScalarValue[arr.RowCount, arr.ColCount];
        for (int r = 0; r < arr.RowCount; r++)
            for (int c = 0; c < arr.ColCount; c++)
                result[r, c] = arr.Cells[rowIndices[r], c];
        return new RangeValue(result);
    }

    private static ScalarValue SortByColumns(RangeValue arr, IReadOnlyList<(RangeValue Range, int Order)> keys)
    {
        var colIndices = CreateSequentialIndices(arr.ColCount);
        Array.Sort(colIndices, (a, b) =>
        {
            foreach (var key in keys)
            {
                int cmp = CompareSortKey(key.Range.Cells[0, a], key.Range.Cells[0, b], key.Order);
                if (cmp != 0) return cmp;
            }

            return a.CompareTo(b);
        });

        var result = new ScalarValue[arr.RowCount, arr.ColCount];
        for (int r = 0; r < arr.RowCount; r++)
            for (int c = 0; c < arr.ColCount; c++)
                result[r, c] = arr.Cells[r, colIndices[c]];
        return new RangeValue(result);
    }

    private static int[] CreateSequentialIndices(int count)
    {
        var indices = new int[count];
        for (var i = 0; i < indices.Length; i++)
            indices[i] = i;

        return indices;
    }
}

