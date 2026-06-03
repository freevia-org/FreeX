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
        if (!TryGetScalarControlArgument(args.Count > 1 ? args[1] : BlankValue.Instance, out var sortIdxArg, out var sortIdxError)) return sortIdxError;
        if (!TryGetScalarControlArgument(args.Count > 2 ? args[2] : BlankValue.Instance, out var sortOrderArg, out var sortOrderError)) return sortOrderError;
        if (!TryGetScalarControlArgument(args.Count > 3 ? args[3] : BlankValue.Instance, out var byColArg, out var byColError)) return byColError;
        double sortIdxRaw   = sortIdxArg is not BlankValue ? ToNumber(sortIdxArg) : 1;
        double sortOrderRaw = sortOrderArg is not BlankValue ? ToNumber(sortOrderArg) : 1;
        if (!double.IsFinite(sortIdxRaw) || !double.IsFinite(sortOrderRaw)) return ErrorValue.Value;
        int sortIdx   = (int)sortIdxRaw - 1;
        if (sortIdx < 0) return ErrorValue.Value;
        int sortOrder = (int)sortOrderRaw;
        if (sortOrder != 1 && sortOrder != -1) return ErrorValue.Value;
        bool byCol    = byColArg is not BlankValue && ToBool(byColArg);
        if (!byCol && sortIdx >= arr.ColCount) return ErrorValue.Value;
        if (byCol && sortIdx >= arr.RowCount) return ErrorValue.Value;

        if (!byCol)
        {
            var rowIndices = CreateSequentialIndices(arr.RowCount);
            Array.Sort(rowIndices, (a, b) =>
            {
                var va = sortIdx < arr.ColCount ? arr.Cells[a, sortIdx] : BlankValue.Instance;
                var vb = sortIdx < arr.ColCount ? arr.Cells[b, sortIdx] : BlankValue.Instance;
                return sortOrder * CompareScalar(va, vb);
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
                var va = sortIdx < arr.RowCount ? arr.Cells[sortIdx, a] : BlankValue.Instance;
                var vb = sortIdx < arr.RowCount ? arr.Cells[sortIdx, b] : BlankValue.Instance;
                return sortOrder * CompareScalar(va, vb);
            });
            var result = new ScalarValue[arr.RowCount, arr.ColCount];
            for (int r = 0; r < arr.RowCount; r++)
                for (int c = 0; c < arr.ColCount; c++)
                    result[r, c] = arr.Cells[r, colIndices[c]];
            return new RangeValue(result);
        }
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
                int cmp = CompareScalar(key.Range.Cells[a, 0], key.Range.Cells[b, 0]);
                if (cmp != 0) return key.Order * cmp;
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
                int cmp = CompareScalar(key.Range.Cells[0, a], key.Range.Cells[0, b]);
                if (cmp != 0) return key.Order * cmp;
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

