using System.Buffers;
using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public sealed partial class FormulaEvaluator
{
    private static bool IsDirectSelectionFunction(string functionName) =>
        functionName is "LARGE" or "SMALL" or
            "PERCENTILE" or "PERCENTILE.INC" or "PERCENTILE.EXC" or
            "QUARTILE" or "QUARTILE.INC" or "QUARTILE.EXC";

    private bool TryEvaluateStatisticalSelectionDirectRange(
        string functionName,
        FunctionCallNode node,
        IEvalContext context,
        out ScalarValue result)
    {
        result = BlankValue.Instance;
        if (node.Arguments.Count != 2)
            return false;

        var rangeState = TryCreateDirectRangeArgument(node.Arguments[0], context, out var range, out result);
        if (rangeState == DirectRangeFastPathState.Unsupported)
            return false;
        if (rangeState == DirectRangeFastPathState.Error)
            return true;

        if (TryAsRangeRef(node.Arguments[1], out _))
            return false;

        var kState = TryEvaluateFastScalarControl(node.Arguments[1], context, out var kValue);
        if (kState == DirectRangeFastPathState.Unsupported)
            return false;
        if (kValue is ErrorValue kError)
        {
            result = kError;
            return true;
        }

        if (kValue is RangeValue)
            return false;

        var coerced = CoerceToNumber(kValue);
        if (coerced is ErrorValue coercionError)
        {
            result = coercionError;
            return true;
        }

        var k = ((NumberValue)coerced).Value;
        if (!double.IsFinite(k))
        {
            result = ErrorValue.Num;
            return true;
        }

        using var numbers = CreateDirectSelectionBuffer([range], context);
        var collectError = CollectDirectRangeNumbers(context, range, numbers);
        if (collectError is not null)
        {
            result = collectError;
            return true;
        }

        result = EvaluateDirectSelectionFunction(functionName, numbers, k, topLevelFunction: true);
        return true;
    }

    private bool TryEvaluateAggregateSelectionDirectRanges(
        FunctionCallNode node,
        IEvalContext context,
        int functionNumber,
        int options,
        out ScalarValue result)
    {
        result = BlankValue.Instance;

        var needsK = functionNumber is >= 14 and <= 19;
        if (needsK && node.Arguments.Count < 4)
        {
            result = ErrorValue.Value;
            return true;
        }

        var k = 0.0;
        var kIndex = needsK ? node.Arguments.Count - 1 : -1;
        if (needsK)
        {
            if (TryAsRangeRef(node.Arguments[kIndex], out _))
                return false;

            var kState = TryEvaluateFastScalarControl(node.Arguments[kIndex], context, out var kValue);
            if (kState == DirectRangeFastPathState.Unsupported)
                return false;
            if (kValue is ErrorValue kError)
            {
                result = kError;
                return true;
            }

            if (kValue is RangeValue)
                return false;

            var coerced = CoerceToNumber(kValue);
            if (coerced is ErrorValue coercionError)
            {
                result = coercionError;
                return true;
            }

            k = ((NumberValue)coerced).Value;
            if (!double.IsFinite(k))
            {
                result = ErrorValue.Num;
                return true;
            }
        }

        var ranges = new List<DirectRangeArgument>(node.Arguments.Count - 2);
        for (var index = 2; index < node.Arguments.Count; index++)
        {
            if (index == kIndex)
                continue;

            var rangeState = TryCreateDirectRangeArgument(node.Arguments[index], context, out var range, out result);
            if (rangeState == DirectRangeFastPathState.Unsupported)
                return false;
            if (rangeState == DirectRangeFastPathState.Error)
                return true;

            ranges.Add(range);
        }

        var ignoreErrors = options is 2 or 3 or 6 or 7;
        var ignoreHiddenRows = options is 1 or 3 or 5 or 7;
        var ignoreNestedAggregates = options <= 3;

        if (functionNumber == 13)
        {
            result = EvaluateAggregateModeDirectRanges(
                ranges,
                context,
                ignoreErrors,
                ignoreHiddenRows,
                ignoreNestedAggregates);
            return true;
        }

        using var numbers = CreateDirectSelectionBuffer(ranges, context);
        foreach (var range in ranges)
        {
            var collectError = CollectDirectAggregateNumbers(
                context,
                range,
                ignoreErrors,
                ignoreHiddenRows,
                ignoreNestedAggregates,
                numbers);
            if (collectError is not null)
            {
                result = collectError;
                return true;
            }
        }

        result = functionNumber switch
        {
            12 => EvaluateDirectMedian(numbers),
            14 => EvaluateDirectLarge(numbers, k, topLevelFunction: false),
            15 => EvaluateDirectSmall(numbers, k, topLevelFunction: false),
            16 => EvaluateDirectPercentileInc(numbers, k),
            17 => EvaluateDirectQuartileInc(numbers, k),
            18 => EvaluateDirectPercentileExc(numbers, k),
            19 => EvaluateDirectQuartileExc(numbers, k),
            _ => ErrorValue.Value
        };
        return true;
    }

    private static DirectSelectionBuffer CreateDirectSelectionBuffer(
        IReadOnlyList<DirectRangeArgument> ranges,
        IEvalContext context)
    {
        long cellCount = 0;
        long populatedEstimate = 0;
        foreach (var range in ranges)
        {
            cellCount += FormulaSafetyLimits.GetRangeCellCount(
                range.StartRow,
                range.StartCol,
                range.EndRow,
                range.EndCol);

            populatedEstimate += EstimateDirectSelectionPopulatedCellCount(range, context);
        }

        if (cellCount is <= 0 or > FormulaSafetyLimits.MaxMaterializedRangeCells)
            return new DirectSelectionBuffer(0);

        // CollectDirectRangeNumbers/CollectDirectAggregateNumbers still scan every nominal cell
        // in `ranges` below -- correctness is unchanged. Only the buffer's INITIAL size changes:
        // rather than eagerly renting the full nominal rowCount*colCount up front (an 80MB
        // double[10_000_000] rent for e.g. =LARGE(A1:J1000000,1) even when the sheet has 5
        // populated numbers), start from the sheet's actual populated extent within the requested
        // range. The existing Grow() doubling logic (already exercised by the "unsupported
        // context" and "estimate undershoots" cases) makes up any shortfall, so a sparse estimate
        // never drops a legitimately populated cell -- it only avoids the wasted allocation for
        // the common mostly-empty-large-range case this defect targets.
        var initial = (int)Math.Clamp(populatedEstimate, 0, cellCount);
        return new DirectSelectionBuffer(initial);
    }

    // Estimate how many cells in `range` could plausibly hold a number, without changing what
    // CollectDirectRangeNumbers/CollectDirectAggregateNumbers actually scan. Every cell outside
    // the sheet's used-range bounding box is guaranteed blank (Sheet.GetUsedRange's box covers
    // every populated cell on the sheet), so intersecting the requested rectangle with it gives a
    // safe upper bound on the populated count -- these direct-selection functions (LARGE, SMALL,
    // PERCENTILE(.INC/.EXC), QUARTILE(.INC/.EXC), MEDIAN/AGGREGATE's direct-range mode) only ever
    // flatten their range into an unordered bag of numbers (see the "shape-agnostic" family noted
    // in FormulaEvaluator.FunctionClassification.cs), so shrinking the estimate never changes the
    // result -- unlike BuildRangeValue's 2-D array (INDEX/MMULT/SUMPRODUCT), which is NOT safe to
    // clamp this way because those consumers index by position or require matching dimensions
    // across arguments.
    private static long EstimateDirectSelectionPopulatedCellCount(DirectRangeArgument range, IEvalContext context)
    {
        var nominal = FormulaSafetyLimits.GetRangeCellCount(range.StartRow, range.StartCol, range.EndRow, range.EndCol);

        if (context is not SheetEvalContext sheetContext)
            return nominal;

        var sheet = sheetContext.ResolveSheetForFastRange(range.SheetName);
        if (sheet is null)
            return nominal;

        if (sheet.GetUsedRange() is not { } used)
            return 0;

        var startRow = Math.Max(range.StartRow, used.Start.Row);
        var endRow = Math.Min(range.EndRow, used.End.Row);
        var startCol = Math.Max(range.StartCol, used.Start.Col);
        var endCol = Math.Min(range.EndCol, used.End.Col);
        if (startRow > endRow || startCol > endCol)
            return 0;

        return FormulaSafetyLimits.GetRangeCellCount(startRow, startCol, endRow, endCol);
    }

    private static ErrorValue? CollectDirectRangeNumbers(
        IEvalContext context,
        DirectRangeArgument range,
        DirectSelectionBuffer numbers)
    {
        var values = numbers.Values;
        var count = numbers.Count;
        for (var row = range.StartRow; row <= range.EndRow; row++)
        {
            for (var col = range.StartCol; col <= range.EndCol; col++)
            {
                var value = GetFastRangeCellValue(context, range, row, col);
                if (TryDirectRangeNumber(value, out var number, out var error))
                {
                    if (count == values.Length)
                    {
                        numbers.Count = count;
                        values = numbers.Grow();
                    }

                    values[count++] = number;
                }
                else if (error is not null)
                {
                    numbers.Count = count;
                    return error;
                }
            }
        }

        numbers.Count = count;
        return null;
    }

    private static ErrorValue? CollectDirectAggregateNumbers(
        IEvalContext context,
        DirectRangeArgument range,
        bool ignoreErrors,
        bool ignoreHiddenRows,
        bool ignoreNestedAggregates,
        DirectSelectionBuffer numbers)
    {
        var values = numbers.Values;
        var count = numbers.Count;
        for (var row = range.StartRow; row <= range.EndRow; row++)
        {
            if (ignoreHiddenRows && IsFastAggregateRowHidden(context, range, row))
                continue;

            for (var col = range.StartCol; col <= range.EndCol; col++)
            {
                if (ignoreNestedAggregates && IsFastNestedSubtotalOrAggregateCell(context, range, row, col))
                    continue;

                var value = GetFastRangeCellValue(context, range, row, col);
                if (TryDirectRangeNumber(value, out var number, out var error))
                {
                    if (count == values.Length)
                    {
                        numbers.Count = count;
                        values = numbers.Grow();
                    }

                    values[count++] = number;
                }
                else if (error is not null)
                {
                    if (ignoreErrors)
                        continue;

                    numbers.Count = count;
                    return error;
                }
            }
        }

        numbers.Count = count;
        return null;
    }

    private static ScalarValue EvaluateAggregateModeDirectRanges(
        IReadOnlyList<DirectRangeArgument> ranges,
        IEvalContext context,
        bool ignoreErrors,
        bool ignoreHiddenRows,
        bool ignoreNestedAggregates)
    {
        using var mode = CreateDirectRangeModeBuffer(ranges, context);
        foreach (var range in ranges)
        {
            for (var row = range.StartRow; row <= range.EndRow; row++)
            {
                if (ignoreHiddenRows && IsFastAggregateRowHidden(context, range, row))
                    continue;

                for (var col = range.StartCol; col <= range.EndCol; col++)
                {
                    if (ignoreNestedAggregates && IsFastNestedSubtotalOrAggregateCell(context, range, row, col))
                        continue;

                    var value = GetFastRangeCellValue(context, range, row, col);
                    if (TryDirectRangeNumber(value, out var number, out var error))
                        mode.Add(number);
                    else if (error is not null)
                    {
                        if (ignoreErrors)
                            continue;

                        return error;
                    }
                }
            }
        }

        return mode.TryGetValue(out var result)
            ? FastNumberResult(result)
            : ErrorValue.NA;
    }

    private static DirectRangeModeBuffer CreateDirectRangeModeBuffer(
        IReadOnlyList<DirectRangeArgument> ranges,
        IEvalContext context)
    {
        long cellCount = 0;
        long populatedEstimate = 0;
        foreach (var range in ranges)
        {
            cellCount += FormulaSafetyLimits.GetRangeCellCount(
                range.StartRow,
                range.StartCol,
                range.EndRow,
                range.EndCol);

            populatedEstimate += EstimateDirectSelectionPopulatedCellCount(range, context);
        }

        if (cellCount is <= 0 or > FormulaSafetyLimits.MaxMaterializedRangeCells)
            return new DirectRangeModeBuffer(0);

        // Mirrors CreateDirectSelectionBuffer's sparse-allocation fix (see its comment above): the
        // table's INITIAL capacity is sized from the sheet's populated extent within the requested
        // range rather than the raw nominal rowCount*colCount, so e.g. =AGGREGATE(13,0,A1:J1000000)
        // (MODE.SNGL over a mostly-empty huge range) no longer rents three ~2x-oversized ArrayPool
        // arrays up front. Add()'s existing Grow() doubling logic (already exercised whenever
        // `context` isn't a SheetEvalContext, or the estimate undershoots) makes up any shortfall,
        // so a sparse estimate never drops a legitimately populated cell.
        var initial = (int)Math.Clamp(populatedEstimate, 0, cellCount);
        return new DirectRangeModeBuffer(initial);
    }

    private static ScalarValue EvaluateDirectSelectionFunction(
        string functionName,
        DirectSelectionBuffer numbers,
        double k,
        bool topLevelFunction)
    {
        return functionName switch
        {
            "LARGE" => EvaluateDirectLarge(numbers, k, topLevelFunction),
            "SMALL" => EvaluateDirectSmall(numbers, k, topLevelFunction),
            "PERCENTILE" or "PERCENTILE.INC" => EvaluateDirectPercentileInc(numbers, k),
            "PERCENTILE.EXC" => EvaluateDirectPercentileExc(numbers, k),
            "QUARTILE" or "QUARTILE.INC" => EvaluateDirectQuartileInc(numbers, k),
            "QUARTILE.EXC" => EvaluateDirectQuartileExc(numbers, k),
            _ => ErrorValue.Value
        };
    }

    private static ScalarValue EvaluateDirectMedian(DirectSelectionBuffer numbers)
    {
        if (numbers.Count == 0)
            return ErrorValue.Num;

        var mid = numbers.Count / 2;
        if ((numbers.Count & 1) == 1)
            return FastNumberResult(SelectDirectKthSmallest(numbers, mid));

        var lower = SelectDirectKthSmallest(numbers, mid - 1);
        var upper = SelectDirectKthSmallest(numbers, mid);
        return FastNumberResult((lower + upper) / 2.0);
    }

    private static ScalarValue EvaluateDirectLarge(DirectSelectionBuffer numbers, double k, bool topLevelFunction)
    {
        var ordinal = (int)k;
        if (ordinal < 1 || ordinal > numbers.Count)
            return ErrorValue.Num;

        var value = SelectDirectKthSmallest(numbers, numbers.Count - ordinal);
        return topLevelFunction ? new NumberValue(value) : FastNumberResult(value);
    }

    private static ScalarValue EvaluateDirectSmall(DirectSelectionBuffer numbers, double k, bool topLevelFunction)
    {
        var ordinal = (int)k;
        if (ordinal < 1 || ordinal > numbers.Count)
            return ErrorValue.Num;

        var value = SelectDirectKthSmallest(numbers, ordinal - 1);
        return topLevelFunction ? new NumberValue(value) : FastNumberResult(value);
    }

    private static ScalarValue EvaluateDirectPercentileInc(DirectSelectionBuffer numbers, double percentile)
    {
        if (numbers.Count == 0 || percentile < 0 || percentile > 1)
            return ErrorValue.Num;

        if (numbers.Count == 1)
            return FastNumberResult(numbers.Values[0]);

        var position = percentile * (numbers.Count - 1);
        var lowerIndex = (int)Math.Floor(position);
        var upperIndex = (int)Math.Ceiling(position);
        var lower = SelectDirectKthSmallest(numbers, lowerIndex);
        if (lowerIndex == upperIndex)
            return FastNumberResult(lower);

        var upper = SelectDirectKthSmallest(numbers, upperIndex);
        return FastNumberResult(lower + (position - lowerIndex) * (upper - lower));
    }

    private static ScalarValue EvaluateDirectPercentileExc(DirectSelectionBuffer numbers, double percentile)
    {
        if (numbers.Count == 0 || percentile <= 0 || percentile >= 1)
            return ErrorValue.Num;

        var position = percentile * (numbers.Count + 1) - 1;
        if (position < 0 || position > numbers.Count - 1)
            return ErrorValue.Num;

        var lowerIndex = (int)Math.Floor(position);
        var upperIndex = (int)Math.Ceiling(position);
        var lower = SelectDirectKthSmallest(numbers, lowerIndex);
        if (lowerIndex == upperIndex)
            return FastNumberResult(lower);

        var upper = SelectDirectKthSmallest(numbers, upperIndex);
        return FastNumberResult(lower + (position - lowerIndex) * (upper - lower));
    }

    private static ScalarValue EvaluateDirectQuartileInc(DirectSelectionBuffer numbers, double quartile)
    {
        var rawQuartile = (int)Math.Truncate(quartile);
        if (rawQuartile < 0 || rawQuartile > 4)
            return ErrorValue.Num;

        return EvaluateDirectPercentileInc(numbers, rawQuartile / 4.0);
    }

    private static ScalarValue EvaluateDirectQuartileExc(DirectSelectionBuffer numbers, double quartile)
    {
        var rawQuartile = (int)Math.Truncate(quartile);
        if (rawQuartile < 1 || rawQuartile > 3)
            return ErrorValue.Num;

        return EvaluateDirectPercentileExc(numbers, rawQuartile / 4.0);
    }

    private static double SelectDirectKthSmallest(DirectSelectionBuffer values, int k)
    {
        var items = values.Values;
        var left = 0;
        var right = values.Count - 1;
        var comparer = Comparer<double>.Default;

        while (left < right)
        {
            var pivotIndex = left + ((right - left) / 2);
            var (equalStart, equalEnd) = PartitionDirectSelection(items, left, right, pivotIndex, comparer);

            if (k < equalStart)
                right = equalStart - 1;
            else if (k > equalEnd)
                left = equalEnd + 1;
            else
                break;
        }

        return items[k];
    }

    private static (int EqualStart, int EqualEnd) PartitionDirectSelection(
        double[] values,
        int left,
        int right,
        int pivotIndex,
        Comparer<double> comparer)
    {
        var pivotValue = values[pivotIndex];
        var less = left;
        var current = left;
        var greater = right;

        while (current <= greater)
        {
            var comparison = comparer.Compare(values[current], pivotValue);
            if (comparison < 0)
            {
                SwapDirectSelection(values, less, current);
                less++;
                current++;
            }
            else if (comparison > 0)
            {
                SwapDirectSelection(values, current, greater);
                greater--;
            }
            else
            {
                current++;
            }
        }

        return (less, greater);
    }

    private static void SwapDirectSelection(double[] values, int first, int second)
    {
        if (first == second)
            return;

        (values[first], values[second]) = (values[second], values[first]);
    }

    private sealed class DirectSelectionBuffer : IDisposable
    {
        private double[] _values;
        private bool _pooled;

        public DirectSelectionBuffer(int capacity)
        {
            if (capacity > 0)
            {
                _values = ArrayPool<double>.Shared.Rent(capacity);
                _pooled = true;
            }
            else
            {
                _values = [];
            }
        }

        public int Count { get; set; }

        public double[] Values => _values;

        public void Dispose()
        {
            ReturnArray();
            Count = 0;
        }

        public double[] Grow()
        {
            var newCapacity = _values.Length == 0 ? 4 : _values.Length * 2;
            var newValues = ArrayPool<double>.Shared.Rent(newCapacity);
            if (Count > 0)
                Array.Copy(_values, newValues, Count);

            ReturnArray();
            _values = newValues;
            _pooled = true;
            return _values;
        }

        private void ReturnArray()
        {
            if (!_pooled)
                return;

            ArrayPool<double>.Shared.Return(_values);
            _values = [];
            _pooled = false;
        }
    }

    // Pooled open-addressing table avoids allocating Dictionary buckets on repeated large MODE/AGGREGATE scans.
    private sealed class DirectRangeModeBuffer : IDisposable
    {
        private double[] _keys = [];
        private int[] _counts = [];
        private int[] _firstOrdinals = [];
        private int _capacity;
        private int _mask;
        private int _used;
        private int _ordinal;
        private int _bestCount;
        private int _bestOrdinal = int.MaxValue;
        private double _bestValue;

        public DirectRangeModeBuffer(int capacity)
        {
            if (capacity > 0)
                RentTable(GetDirectRangeModeTableCapacity(capacity));
        }

        public void Add(double value)
        {
            if (_capacity == 0)
                RentTable(4);

            if ((_used + 1) * 4 >= _capacity * 3)
                Grow();

            AddToTable(value, _ordinal);
            _ordinal++;
        }

        public bool TryGetValue(out double value)
        {
            value = _bestValue;
            return _bestCount >= 2;
        }

        public void Dispose()
        {
            if (_capacity == 0)
                return;

            ArrayPool<double>.Shared.Return(_keys);
            ArrayPool<int>.Shared.Return(_counts);
            ArrayPool<int>.Shared.Return(_firstOrdinals);
            _keys = [];
            _counts = [];
            _firstOrdinals = [];
            _capacity = 0;
            _mask = 0;
            _used = 0;
        }

        private void Grow()
        {
            var oldKeys = _keys;
            var oldCounts = _counts;
            var oldFirstOrdinals = _firstOrdinals;
            var oldCapacity = _capacity;

            RentTable(_capacity == 0 ? 4 : _capacity * 2);

            for (var i = 0; i < oldCapacity; i++)
            {
                var count = oldCounts[i];
                if (count == 0)
                    continue;

                AddExistingToTable(oldKeys[i], count, oldFirstOrdinals[i]);
            }

            if (oldCapacity == 0)
                return;

            ArrayPool<double>.Shared.Return(oldKeys);
            ArrayPool<int>.Shared.Return(oldCounts);
            ArrayPool<int>.Shared.Return(oldFirstOrdinals);
        }

        private void RentTable(int capacity)
        {
            _keys = ArrayPool<double>.Shared.Rent(capacity);
            _counts = ArrayPool<int>.Shared.Rent(capacity);
            _firstOrdinals = ArrayPool<int>.Shared.Rent(capacity);
            _capacity = capacity;
            _mask = capacity - 1;
            _used = 0;
            Array.Clear(_counts, 0, capacity);
        }

        private void AddToTable(double value, int ordinal)
        {
            var slot = value.GetHashCode() & _mask;
            while (true)
            {
                var count = _counts[slot];
                if (count == 0)
                {
                    _keys[slot] = value;
                    _counts[slot] = 1;
                    _firstOrdinals[slot] = ordinal;
                    _used++;
                    return;
                }

                if (_keys[slot].Equals(value))
                {
                    count++;
                    _counts[slot] = count;
                    UpdateBest(value, count, _firstOrdinals[slot]);
                    return;
                }

                slot = (slot + 1) & _mask;
            }
        }

        private void AddExistingToTable(double value, int count, int firstOrdinal)
        {
            var slot = value.GetHashCode() & _mask;
            while (_counts[slot] != 0)
                slot = (slot + 1) & _mask;

            _keys[slot] = value;
            _counts[slot] = count;
            _firstOrdinals[slot] = firstOrdinal;
            _used++;
        }

        private void UpdateBest(double value, int count, int firstOrdinal)
        {
            if (count >= 2 &&
                (count > _bestCount ||
                 (count == _bestCount && firstOrdinal < _bestOrdinal)))
            {
                _bestCount = count;
                _bestOrdinal = firstOrdinal;
                _bestValue = value;
            }
        }

        private static int GetDirectRangeModeTableCapacity(int valueCount)
        {
            var desired = Math.Max(4, valueCount * 2);
            desired--;
            desired |= desired >> 1;
            desired |= desired >> 2;
            desired |= desired >> 4;
            desired |= desired >> 8;
            desired |= desired >> 16;
            desired++;
            return desired;
        }
    }

}
