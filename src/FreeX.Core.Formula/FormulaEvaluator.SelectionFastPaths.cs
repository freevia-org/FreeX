using System.Buffers;
using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public sealed partial class FormulaEvaluator
{
    private const int MaxDirectRangeModeCapacity = 50_000;

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

        using var numbers = CreateDirectSelectionBuffer([range]);
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

        using var numbers = CreateDirectSelectionBuffer(ranges);
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

    private static DirectSelectionBuffer CreateDirectSelectionBuffer(IReadOnlyList<DirectRangeArgument> ranges)
    {
        long cellCount = 0;
        foreach (var range in ranges)
        {
            cellCount += FormulaSafetyLimits.GetRangeCellCount(
                range.StartRow,
                range.StartCol,
                range.EndRow,
                range.EndCol);
        }

        return new DirectSelectionBuffer(
            cellCount is > 0 and <= FormulaSafetyLimits.MaxMaterializedRangeCells
                ? (int)cellCount
                : 0);
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
        var mode = new DirectRangeModeAccumulator(EstimateDirectRangeModeCapacity(ranges));
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

    private static int EstimateDirectRangeModeCapacity(IReadOnlyList<DirectRangeArgument> ranges)
    {
        long count = 0;
        foreach (var range in ranges)
        {
            count += FormulaSafetyLimits.GetRangeCellCount(
                range.StartRow,
                range.StartCol,
                range.EndRow,
                range.EndCol);
            if (count >= MaxDirectRangeModeCapacity)
                return MaxDirectRangeModeCapacity;
        }

        return (int)count;
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

    private sealed class DirectRangeModeAccumulator
    {
        private readonly int _capacity;
        private Dictionary<double, DirectRangeModeCount>? _counts;
        private int _ordinal;
        private int _bestCount;
        private int _bestOrdinal;
        private double _bestValue;

        public DirectRangeModeAccumulator(int capacity)
        {
            _capacity = capacity;
        }

        public void Add(double value)
        {
            var counts = _counts ??= _capacity > 0
                ? new Dictionary<double, DirectRangeModeCount>(_capacity)
                : [];
            ref var entry = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(
                counts,
                value,
                out var exists);
            if (exists)
                entry.Count++;
            else
                entry = new DirectRangeModeCount(1, _ordinal);

            if (entry.Count >= 2 &&
                (entry.Count > _bestCount ||
                 (entry.Count == _bestCount && entry.FirstOrdinal < _bestOrdinal)))
            {
                _bestCount = entry.Count;
                _bestOrdinal = entry.FirstOrdinal;
                _bestValue = value;
            }

            _ordinal++;
        }

        public bool TryGetValue(out double value)
        {
            value = _bestValue;
            return _bestCount >= 2;
        }
    }

    private struct DirectRangeModeCount
    {
        public int Count;
        public int FirstOrdinal;

        public DirectRangeModeCount(int count, int firstOrdinal)
        {
            Count = count;
            FirstOrdinal = firstOrdinal;
        }
    }

}
