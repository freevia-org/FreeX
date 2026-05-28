using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

public static partial class BuiltInFunctions
{
    // ═══════════════════════════════════════════════════════════════════
    // Phase 4b  –  Dynamic arrays
    // ═══════════════════════════════════════════════════════════════════

    private static ScalarValue Sequence(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryGetScalarControlArgument(args[0], out var rowsArg, out var rowsError)) return rowsError;
        if (!TryGetScalarControlArgument(args.Count > 1 ? args[1] : BlankValue.Instance, out var colsArg, out var colsError)) return colsError;
        if (!TryGetScalarControlArgument(args.Count > 2 ? args[2] : BlankValue.Instance, out var startArg, out var startError)) return startError;
        if (!TryGetScalarControlArgument(args.Count > 3 ? args[3] : BlankValue.Instance, out var stepArg, out var stepError)) return stepError;
        double rawRows = rowsArg is not BlankValue ? ToNumber(rowsArg) : 1;
        double rawCols = colsArg is not BlankValue ? ToNumber(colsArg) : 1;
        double start = startArg is not BlankValue ? ToNumber(startArg) : 1;
        double step  = stepArg is not BlankValue ? ToNumber(stepArg) : 1;
        if (!double.IsFinite(rawRows) || !double.IsFinite(rawCols)) return ErrorValue.Value;
        if (!double.IsFinite(start) || !double.IsFinite(step)) return ErrorValue.Num;
        int rows = (int)rawRows;
        int cols = (int)rawCols;
        if (rows < 1 || cols < 1) return ErrorValue.Value;
        if ((long)rows * cols > 1_000_000) return ErrorValue.Value;
        var cells = new ScalarValue[rows, cols];
        double val = start;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                if (!double.IsFinite(val)) return ErrorValue.Num;
                cells[r, c] = new NumberValue(val);
                val += step;
        }
        return new RangeValue(cells);
    }

    private static ScalarValue RandArray(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryGetScalarControlArgument(args.Count > 0 ? args[0] : BlankValue.Instance, out var rowsArg, out var rowsError)) return rowsError;
        if (!TryGetScalarControlArgument(args.Count > 1 ? args[1] : BlankValue.Instance, out var colsArg, out var colsError)) return colsError;
        if (!TryGetScalarControlArgument(args.Count > 2 ? args[2] : BlankValue.Instance, out var minArg, out var minError)) return minError;
        if (!TryGetScalarControlArgument(args.Count > 3 ? args[3] : BlankValue.Instance, out var maxArg, out var maxError)) return maxError;
        if (!TryGetScalarControlArgument(args.Count > 4 ? args[4] : BlankValue.Instance, out var wholeNumberArg, out var wholeNumberError)) return wholeNumberError;

        double rowsD = rowsArg is not BlankValue ? ToNumber(rowsArg) : 1;
        double colsD = colsArg is not BlankValue ? ToNumber(colsArg) : 1;
        double min = minArg is not BlankValue ? ToNumber(minArg) : 0;
        double max = maxArg is not BlankValue ? ToNumber(maxArg) : 1;
        bool wholeNumber = wholeNumberArg is not BlankValue && ToBool(wholeNumberArg);

        if (!double.IsFinite(rowsD) || !double.IsFinite(colsD)) return ErrorValue.Value;
        int rows = (int)rowsD;
        int cols = (int)colsD;
        if (rows < 1 || cols < 1) return ErrorValue.Value;
        if ((long)rows * cols > 1_000_000) return ErrorValue.Value;
        if (!double.IsFinite(min) || !double.IsFinite(max) || min > max) return ErrorValue.Value;

        if (wholeNumber)
        {
            if (!TryTruncateToLong(Math.Ceiling(min), out long bottom) ||
                !TryTruncateToLong(Math.Floor(max), out long top))
                return ErrorValue.Value;
            if (bottom > top) return ErrorValue.Value;

            long randExclusiveTop;
            try { randExclusiveTop = checked(top + 1); }
            catch (OverflowException) { return ErrorValue.Value; }
            var integers = new ScalarValue[rows, cols];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    integers[r, c] = new NumberValue(Random.Shared.NextInt64(bottom, randExclusiveTop));
            return new RangeValue(integers);
        }

        double width = max - min;
        if (!double.IsFinite(width)) return ErrorValue.Value;
        var result = new ScalarValue[rows, cols];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var value = min + Random.Shared.NextDouble() * width;
                if (!double.IsFinite(value)) return ErrorValue.Value;
                result[r, c] = new NumberValue(value);
            }
        return new RangeValue(result);
    }

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
            var rowIndices = Enumerable.Range(0, arr.RowCount).ToList();
            rowIndices.Sort((a, b) =>
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
            var colIndices = Enumerable.Range(0, arr.ColCount).ToList();
            colIndices.Sort((a, b) =>
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
        var rowIndices = Enumerable.Range(0, arr.RowCount).ToList();
        rowIndices.Sort((a, b) =>
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
        var colIndices = Enumerable.Range(0, arr.ColCount).ToList();
        colIndices.Sort((a, b) =>
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

    private static ScalarValue Take(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue arrayError) return arrayError;
        var arr = args[0] is RangeValue arrayRange
            ? arrayRange
            : new RangeValue(new ScalarValue[1, 1] { { args[0] } });
        int rowStart = 0;
        int rowCount = arr.RowCount;
        if (args[1] is not BlankValue &&
            !TryGetArraySliceCount(args[1], arr.RowCount, isTake: true, out rowStart, out rowCount, out var rowSliceError))
            return rowSliceError;

        int colStart = 0;
        int colCount = arr.ColCount;
        if (args.Count > 2 && args[2] is not BlankValue)
        {
            if (!TryGetArraySliceCount(args[2], arr.ColCount, isTake: true, out colStart, out colCount, out var colSliceError))
                return colSliceError;
        }

        return SliceRange(arr, rowStart, colStart, rowCount, colCount);
    }

    private static ScalarValue Drop(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue arrayError) return arrayError;
        var arr = args[0] is RangeValue arrayRange
            ? arrayRange
            : new RangeValue(new ScalarValue[1, 1] { { args[0] } });
        int rowStart = 0;
        int rowCount = arr.RowCount;
        if (args[1] is not BlankValue &&
            !TryGetArraySliceCount(args[1], arr.RowCount, isTake: false, out rowStart, out rowCount, out var rowSliceError))
            return rowSliceError;

        int colStart = 0;
        int colCount = arr.ColCount;
        if (args.Count > 2 && args[2] is not BlankValue)
        {
            if (!TryGetArraySliceCount(args[2], arr.ColCount, isTake: false, out colStart, out colCount, out var colSliceError))
                return colSliceError;
        }

        return SliceRange(arr, rowStart, colStart, rowCount, colCount);
    }

    private static bool TryGetArraySliceCount(
        ScalarValue countValue,
        int dimensionLength,
        bool isTake,
        out int start,
        out int count,
        out ScalarValue error)
    {
        error = ErrorValue.Value;
        if (!TryGetScalarControlArgument(countValue, out var scalarCountValue, out error))
        {
            start = 0;
            count = 0;
            return false;
        }

        countValue = scalarCountValue;
        double raw = ToNumber(countValue);
        if (!double.IsFinite(raw))
        {
            start = 0;
            count = 0;
            return false;
        }
        if (raw > int.MaxValue || raw <= int.MinValue)
        {
            start = 0;
            count = 0;
            return false;
        }

        int requested = (int)raw;
        if (requested == 0)
        {
            start = 0;
            count = 0;
            error = ErrorValue.Calc;
            return false;
        }

        if (isTake)
        {
            count = Math.Min(Math.Abs(requested), dimensionLength);
            start = requested > 0 ? 0 : dimensionLength - count;
            return count > 0;
        }

        if (Math.Abs(requested) >= dimensionLength)
        {
            start = 0;
            count = 0;
            error = ErrorValue.Calc;
            return false;
        }

        if (requested > 0)
        {
            start = requested;
            count = dimensionLength - requested;
        }
        else
        {
            start = 0;
            count = dimensionLength + requested;
        }

        return count > 0;
    }

    private static bool TryGetScalarControlArgument(ScalarValue value, out ScalarValue scalar, out ScalarValue error)
    {
        error = ErrorValue.Value;
        if (value is RangeValue range)
        {
            if (range.RowCount != 1 || range.ColCount != 1)
            {
                scalar = ErrorValue.Value;
                return false;
            }

            scalar = range.Cells[0, 0];
            if (scalar is ErrorValue scalarError)
            {
                error = scalarError;
                return false;
            }

            return true;
        }

        scalar = value;
        if (value is ErrorValue directError)
        {
            error = directError;
            return false;
        }

        return true;
    }

    private static RangeValue SliceRange(RangeValue arr, int rowStart, int colStart, int rowCount, int colCount)
    {
        var result = new ScalarValue[rowCount, colCount];
        for (int r = 0; r < rowCount; r++)
            for (int c = 0; c < colCount; c++)
                result[r, c] = arr.Cells[rowStart + r, colStart + c];
        return new RangeValue(result);
    }

    private static ScalarValue ChooseRows(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue arrayError) return arrayError;
        var arr = args[0] is RangeValue range
            ? range
            : new RangeValue(new[,] { { args[0] } });
        if (!TryResolveChoiceIndexes(args, arr.RowCount, out var rowIndexes, out var error)) return error;

        var result = new ScalarValue[rowIndexes.Count, arr.ColCount];
        for (int r = 0; r < rowIndexes.Count; r++)
            for (int c = 0; c < arr.ColCount; c++)
                result[r, c] = arr.Cells[rowIndexes[r], c];
        return new RangeValue(result);
    }

    private static ScalarValue ChooseCols(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue arrayError) return arrayError;
        var arr = args[0] is RangeValue range
            ? range
            : new RangeValue(new[,] { { args[0] } });
        if (!TryResolveChoiceIndexes(args, arr.ColCount, out var colIndexes, out var error)) return error;

        var result = new ScalarValue[arr.RowCount, colIndexes.Count];
        for (int r = 0; r < arr.RowCount; r++)
            for (int c = 0; c < colIndexes.Count; c++)
                result[r, c] = arr.Cells[r, colIndexes[c]];
        return new RangeValue(result);
    }

    private static bool TryResolveChoiceIndexes(
        IReadOnlyList<ScalarValue> args,
        int dimensionLength,
        out List<int> indexes,
        out ScalarValue error)
    {
        indexes = new List<int>();
        error = ErrorValue.Value;

        for (int i = 1; i < args.Count; i++)
        {
            if (args[i] is ErrorValue e)
            {
                error = e;
                return false;
            }

            if (args[i] is RangeValue range)
            {
                for (int r = 0; r < range.RowCount; r++)
                    for (int c = 0; c < range.ColCount; c++)
                        if (!TryAddChoiceIndex(range.Cells[r, c], dimensionLength, indexes, out error))
                            return false;

                continue;
            }

            if (!TryAddChoiceIndex(args[i], dimensionLength, indexes, out error))
                return false;
        }

        return indexes.Count > 0;
    }

    private static bool TryAddChoiceIndex(
        ScalarValue value,
        int dimensionLength,
        List<int> indexes,
        out ScalarValue error)
    {
        error = ErrorValue.Value;
        if (value is ErrorValue e)
        {
            error = e;
            return false;
        }

        double raw = ToNumber(value);
        if (!double.IsFinite(raw)) return false;

        int requested = (int)raw;
        if (requested == 0) return false;

        int zeroBased = requested > 0
            ? requested - 1
            : dimensionLength + requested;
        if (zeroBased < 0 || zeroBased >= dimensionLength) return false;

        indexes.Add(zeroBased);
        return true;
    }

    private static ScalarValue VStack(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryCollectStackArrays(args, out var arrays, out var error)) return error;

        long rowCountL = 0;
        foreach (var a in arrays) rowCountL += a.RowCount;
        int colCount = arrays.Max(a => a.ColCount);
        if (rowCountL * colCount > 1_000_000) return ErrorValue.Value;
        int rowCount = (int)rowCountL;
        var result = CreateFilledRange(rowCount, colCount, ErrorValue.NA);

        int rowOffset = 0;
        foreach (var arr in arrays)
        {
            for (int r = 0; r < arr.RowCount; r++)
                for (int c = 0; c < arr.ColCount; c++)
                    result[rowOffset + r, c] = arr.Cells[r, c];
            rowOffset += arr.RowCount;
        }

        return new RangeValue(result);
    }

    private static ScalarValue HStack(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryCollectStackArrays(args, out var arrays, out var error)) return error;

        int rowCount = arrays.Max(a => a.RowCount);
        long colCountL = 0;
        foreach (var a in arrays) colCountL += a.ColCount;
        if ((long)rowCount * colCountL > 1_000_000) return ErrorValue.Value;
        int colCount = (int)colCountL;
        var result = CreateFilledRange(rowCount, colCount, ErrorValue.NA);

        int colOffset = 0;
        foreach (var arr in arrays)
        {
            for (int r = 0; r < arr.RowCount; r++)
                for (int c = 0; c < arr.ColCount; c++)
                    result[r, colOffset + c] = arr.Cells[r, c];
            colOffset += arr.ColCount;
        }

        return new RangeValue(result);
    }

    private static bool TryCollectStackArrays(
        IReadOnlyList<ScalarValue> args,
        out List<RangeValue> arrays,
        out ScalarValue error)
    {
        arrays = new List<RangeValue>();
        error = ErrorValue.Value;

        foreach (var arg in args)
        {
            arrays.Add(arg is RangeValue arr
                ? arr
                : new RangeValue(new[,] { { arg } }));
        }

        return arrays.Count > 0;
    }

    private static ScalarValue[,] CreateFilledRange(int rowCount, int colCount, ScalarValue value)
    {
        var result = new ScalarValue[rowCount, colCount];
        for (int r = 0; r < rowCount; r++)
            for (int c = 0; c < colCount; c++)
                result[r, c] = value;
        return result;
    }

    private static ScalarValue ToRow(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryFlattenArray(args, out var values, out var error)) return error;
        if (values.Count == 0) return ErrorValue.Calc;
        if (values.Count > 1_000_000) return ErrorValue.Value;

        var result = new ScalarValue[1, values.Count];
        for (int c = 0; c < values.Count; c++)
            result[0, c] = values[c];
        return new RangeValue(result);
    }

    private static ScalarValue ToCol(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryFlattenArray(args, out var values, out var error)) return error;
        if (values.Count == 0) return ErrorValue.Calc;
        if (values.Count > 1_000_000) return ErrorValue.Value;

        var result = new ScalarValue[values.Count, 1];
        for (int r = 0; r < values.Count; r++)
            result[r, 0] = values[r];
        return new RangeValue(result);
    }

    private static bool TryFlattenArray(
        IReadOnlyList<ScalarValue> args,
        out List<ScalarValue> values,
        out ScalarValue error)
    {
        values = new List<ScalarValue>();
        error = ErrorValue.Value;

        if (args[0] is ErrorValue arrayError)
        {
            error = arrayError;
            return false;
        }

        int ignore = 0;
        if (args.Count > 1 && args[1] is not BlankValue)
        {
            if (!TryGetScalarControlArgument(args[1], out var ignoreArg, out error)) return false;
            double rawIgnore = ToNumber(ignoreArg);
            if (!double.IsFinite(rawIgnore)) return false;
            ignore = (int)rawIgnore;
            if (ignore is < 0 or > 3) return false;
        }

        bool scanByColumn = false;
        if (args.Count > 2 && args[2] is not BlankValue)
        {
            if (!TryGetScalarControlArgument(args[2], out var scanArg, out error)) return false;
            scanByColumn = ToBool(scanArg);
        }

        bool ignoreBlanks = (ignore & 1) != 0;
        bool ignoreErrors = (ignore & 2) != 0;

        if (args[0] is not RangeValue arr)
        {
            AddFlattenedValue(args[0], ignoreBlanks, ignoreErrors, values);
            return true;
        }

        if (scanByColumn)
        {
            for (int c = 0; c < arr.ColCount; c++)
                for (int r = 0; r < arr.RowCount; r++)
                    AddFlattenedValue(arr.Cells[r, c], ignoreBlanks, ignoreErrors, values);
        }
        else
        {
            for (int r = 0; r < arr.RowCount; r++)
                for (int c = 0; c < arr.ColCount; c++)
                    AddFlattenedValue(arr.Cells[r, c], ignoreBlanks, ignoreErrors, values);
        }

        return true;
    }

    private static void AddFlattenedValue(
        ScalarValue value,
        bool ignoreBlanks,
        bool ignoreErrors,
        List<ScalarValue> values)
    {
        if (ignoreBlanks && value is BlankValue) return;
        if (ignoreErrors && value is ErrorValue) return;
        values.Add(value);
    }

    private static ScalarValue WrapRows(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryGetWrapArgs(args, out var values, out int wrapCount, out var padWith, out var error)) return error;

        int rowCount = (values.Count + wrapCount - 1) / wrapCount;
        if ((long)rowCount * wrapCount > 1_000_000) return ErrorValue.Value;
        var result = CreateFilledRange(rowCount, wrapCount, padWith);
        for (int i = 0; i < values.Count; i++)
            result[i / wrapCount, i % wrapCount] = values[i];
        return new RangeValue(result);
    }

    private static ScalarValue WrapCols(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryGetWrapArgs(args, out var values, out int wrapCount, out var padWith, out var error)) return error;

        int colCount = (values.Count + wrapCount - 1) / wrapCount;
        if ((long)wrapCount * colCount > 1_000_000) return ErrorValue.Value;
        var result = CreateFilledRange(wrapCount, colCount, padWith);
        for (int i = 0; i < values.Count; i++)
            result[i % wrapCount, i / wrapCount] = values[i];
        return new RangeValue(result);
    }

    private static bool TryGetWrapArgs(
        IReadOnlyList<ScalarValue> args,
        out List<ScalarValue> values,
        out int wrapCount,
        out ScalarValue padWith,
        out ScalarValue error)
    {
        values = new List<ScalarValue>();
        wrapCount = 0;
        padWith = ErrorValue.NA;
        error = ErrorValue.Value;

        if (args[0] is ErrorValue arrayError)
        {
            error = arrayError;
            return false;
        }

        if (!TryGetScalarControlArgument(args[1], out var wrapCountArg, out error)) return false;
        double rawWrapCount = ToNumber(wrapCountArg);
        if (!double.IsFinite(rawWrapCount)) return false;
        if (rawWrapCount > int.MaxValue || rawWrapCount <= int.MinValue)
        {
            error = ErrorValue.Num;
            return false;
        }
        wrapCount = (int)rawWrapCount;
        if (wrapCount < 1)
        {
            error = ErrorValue.Num;
            return false;
        }

        if (args[0] is RangeValue arr)
        {
            if (!TryReadVector(arr, values)) return false;
        }
        else
        {
            values.Add(args[0]);
        }

        if (args.Count > 2 && args[2] is not BlankValue)
        {
            if (!TryGetScalarControlArgument(args[2], out padWith, out error)) return false;
        }

        return values.Count > 0;
    }

    private static bool TryReadVector(RangeValue arr, List<ScalarValue> values)
    {
        if (arr.RowCount == 1)
        {
            for (int c = 0; c < arr.ColCount; c++)
                values.Add(arr.Cells[0, c]);
            return true;
        }

        if (arr.ColCount == 1)
        {
            for (int r = 0; r < arr.RowCount; r++)
                values.Add(arr.Cells[r, 0]);
            return true;
        }

        return false;
    }

    private static ScalarValue Expand(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue arrayError) return arrayError;
        var arr = args[0] is RangeValue range
            ? range
            : new RangeValue(new[,] { { args[0] } });
        if (!TryGetExpandDimension(args[1], arr.RowCount, out int rowCount, out var rowError)) return rowError;
        int colCount = arr.ColCount;
        if (args.Count > 2 && args[2] is not BlankValue)
        {
            if (!TryGetExpandDimension(args[2], arr.ColCount, out colCount, out var colError)) return colError;
        }

        if (rowCount < arr.RowCount || colCount < arr.ColCount) return ErrorValue.Value;
        if ((long)rowCount * colCount > 1_000_000) return ErrorValue.Value;

        var padWith = (ScalarValue)ErrorValue.NA;
        if (args.Count > 3 && args[3] is not BlankValue)
        {
            if (!TryGetScalarControlArgument(args[3], out padWith, out var padError)) return padError;
        }

        var result = CreateFilledRange(rowCount, colCount, padWith);
        for (int r = 0; r < arr.RowCount; r++)
            for (int c = 0; c < arr.ColCount; c++)
                result[r, c] = arr.Cells[r, c];
        return new RangeValue(result);
    }

    private static bool TryGetExpandDimension(ScalarValue value, int originalLength, out int dimension, out ScalarValue error)
    {
        dimension = originalLength;
        error = ErrorValue.Value;
        if (value is BlankValue) return true;

        if (!TryGetScalarControlArgument(value, out var scalarValue, out error)) return false;
        double raw = ToNumber(scalarValue);
        if (!double.IsFinite(raw) || raw > int.MaxValue) return false;
        dimension = (int)raw;
        return dimension >= 1;
    }

    private static ScalarValue Unique(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue arrayError) return arrayError;
        var arr = args[0] is RangeValue arrayRange
            ? arrayRange
            : new RangeValue(new ScalarValue[1, 1] { { args[0] } });
        if (!TryGetScalarControlArgument(args.Count > 1 ? args[1] : BlankValue.Instance, out var byColArg, out var byColError)) return byColError;
        if (!TryGetScalarControlArgument(args.Count > 2 ? args[2] : BlankValue.Instance, out var exactlyOnceArg, out var exactlyOnceError)) return exactlyOnceError;
        bool byCol       = byColArg is not BlankValue && ToBool(byColArg);
        bool exactlyOnce = exactlyOnceArg is not BlankValue && ToBool(exactlyOnceArg);

        if (!byCol)
        {
            var keyOrder  = new List<string>();
            var keyIndex  = new Dictionary<string, int>();
            var keyCounts = new List<int>();
            var rowOfKey  = new List<int>();

            var keySb = new System.Text.StringBuilder();
            for (int r = 0; r < arr.RowCount; r++)
            {
                keySb.Clear();
                for (int c = 0; c < arr.ColCount; c++)
                {
                    if (c > 0) keySb.Append('\0');
                    AppendUniqueKey(keySb, arr.Cells[r, c]);
                }
                var key = keySb.ToString();
                if (keyIndex.TryGetValue(key, out int idx))
                {
                    keyCounts[idx]++;
                }
                else
                {
                    keyIndex[key] = keyOrder.Count;
                    keyOrder.Add(key);
                    keyCounts.Add(1);
                    rowOfKey.Add(r);
                }
            }

            var selected = keyOrder
                .Select((k, i) => (key: k, idx: i))
                .Where(t => !exactlyOnce || keyCounts[t.idx] == 1)
                .Select(t => rowOfKey[t.idx])
                .ToList();

            if (selected.Count == 0) return ErrorValue.Calc;
            var result = new ScalarValue[selected.Count, arr.ColCount];
            for (int ri = 0; ri < selected.Count; ri++)
                for (int c = 0; c < arr.ColCount; c++)
                    result[ri, c] = arr.Cells[selected[ri], c];
            return new RangeValue(result);
        }
        else
        {
            var keyOrder  = new List<string>();
            var keyIndex  = new Dictionary<string, int>();
            var keyCounts = new List<int>();
            var colOfKey  = new List<int>();

            var colKeySb = new System.Text.StringBuilder();
            for (int c = 0; c < arr.ColCount; c++)
            {
                colKeySb.Clear();
                for (int r = 0; r < arr.RowCount; r++)
                {
                    if (r > 0) colKeySb.Append('\0');
                    AppendUniqueKey(colKeySb, arr.Cells[r, c]);
                }
                var key = colKeySb.ToString();
                if (keyIndex.TryGetValue(key, out int idx))
                {
                    keyCounts[idx]++;
                }
                else
                {
                    keyIndex[key] = keyOrder.Count;
                    keyOrder.Add(key);
                    keyCounts.Add(1);
                    colOfKey.Add(c);
                }
            }

            var selected = keyOrder
                .Select((k, i) => (key: k, idx: i))
                .Where(t => !exactlyOnce || keyCounts[t.idx] == 1)
                .Select(t => colOfKey[t.idx])
                .ToList();

            if (selected.Count == 0) return ErrorValue.Calc;
            var result = new ScalarValue[arr.RowCount, selected.Count];
            for (int r = 0; r < arr.RowCount; r++)
                for (int ci = 0; ci < selected.Count; ci++)
                    result[r, ci] = arr.Cells[r, selected[ci]];
            return new RangeValue(result);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    private static void AppendUniqueKey(System.Text.StringBuilder sb, ScalarValue value)
    {
        switch (value)
        {
            case BlankValue:
                sb.Append("blank");
                break;
            case NumberValue n:
                sb.Append("number:").Append(n.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                break;
            case DateTimeValue dt:
                sb.Append("number:").Append(dt.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                break;
            case TextValue t:
                sb.Append("text:").Append(t.Value.ToUpperInvariant());
                break;
            case BoolValue b:
                sb.Append("bool:").Append(b.Value ? '1' : '0');
                break;
            case ErrorValue e:
                sb.Append("error:").Append(e.Code);
                break;
            default:
                sb.Append("other:").Append(ToText(value));
                break;
        }
    }

    private static ScalarValue TrimRange(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue arrayError) return arrayError;
        var range = args[0] is RangeValue rv
            ? rv
            : new RangeValue(new ScalarValue[1, 1] { { args[0] } });

        if (!TryGetTrimRangeMode(args, 1, out int trimRows, out var rowsError)) return rowsError;
        if (!TryGetTrimRangeMode(args, 2, out int trimCols, out var colsError)) return colsError;

        int rowStart = 0;
        int rowEnd = range.RowCount - 1;
        if ((trimRows & 1) != 0)
            while (rowStart <= rowEnd && IsTrimRangeBlankRow(range, rowStart)) rowStart++;
        if ((trimRows & 2) != 0)
            while (rowEnd >= rowStart && IsTrimRangeBlankRow(range, rowEnd)) rowEnd--;

        int colStart = 0;
        int colEnd = range.ColCount - 1;
        if ((trimCols & 1) != 0)
            while (colStart <= colEnd && IsTrimRangeBlankColumn(range, colStart, rowStart, rowEnd)) colStart++;
        if ((trimCols & 2) != 0)
            while (colEnd >= colStart && IsTrimRangeBlankColumn(range, colEnd, rowStart, rowEnd)) colEnd--;

        if (rowStart > rowEnd || colStart > colEnd)
            return ErrorValue.Calc;

        return SliceRange(range, rowStart, colStart, rowEnd - rowStart + 1, colEnd - colStart + 1);
    }

    private static bool TryGetTrimRangeMode(
        IReadOnlyList<ScalarValue> args,
        int index,
        out int mode,
        out ScalarValue error)
    {
        mode = 3;
        error = ErrorValue.Value;
        if (args.Count <= index || args[index] is BlankValue) return true;
        if (!TryGetScalarControlArgument(args[index], out var scalar, out error)) return false;

        var raw = ToNumber(scalar);
        if (!double.IsFinite(raw)) return false;

        mode = (int)raw;
        return mode is >= 0 and <= 3;
    }

    private static bool IsTrimRangeBlankRow(RangeValue range, int row)
    {
        for (int col = 0; col < range.ColCount; col++)
            if (range.Cells[row, col] is not BlankValue)
                return false;

        return true;
    }

    private static bool IsTrimRangeBlankColumn(RangeValue range, int col, int rowStart, int rowEnd)
    {
        for (int row = rowStart; row <= rowEnd; row++)
            if (range.Cells[row, col] is not BlankValue)
                return false;

        return true;
    }

}
