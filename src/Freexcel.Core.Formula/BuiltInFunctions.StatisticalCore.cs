using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

public static partial class BuiltInFunctions
{
    // Core aggregate, ranking, descriptive-statistical, and regression functions.

    private static ScalarValue Sum(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        double total = 0;
        foreach (var arg in args)
        {
            if (arg is ErrorValue err) return err;
            if (arg is ReferencedScalarValue referenced)
            {
                if (TryReferencedNumber(referenced, out double value, out var refError)) total += value;
                else if (refError is not null) return refError;
                continue;
            }
            if (arg is DirectTextLiteralValue direct)
            {
                if (!TryDirectTextNumber(direct, out double value)) return ErrorValue.Value;
                if (!double.IsFinite(value)) return ErrorValue.Num;
                total += value;
                continue;
            }
            if (arg is BlankValue or TextValue) continue; // SUM ignores text and blanks in ranges
            total += ToNumber(arg);
        }
        return NumberResult(total);
    }

    private static ScalarValue PercentOf(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var subset = PercentOfSum(args[0]);
        if (subset.Error is not null) return subset.Error;

        var total = PercentOfSum(args[1]);
        if (total.Error is not null) return total.Error;
        if (total.Sum == 0) return ErrorValue.DivByZero;

        return NumberResult(subset.Sum / total.Sum);
    }

    private static (double Sum, ErrorValue? Error) PercentOfSum(ScalarValue value)
    {
        if (value is ErrorValue e) return (0, e);
        if (value is RangeValue range)
        {
            double total = 0;
            foreach (var cell in range.Flatten())
            {
                if (cell is ErrorValue cellError) return (0, cellError);
                if (TryCellNumber(cell, out var number)) total += number;
            }

            return (total, null);
        }

        return (ToNumber(value), null);
    }

    private static ScalarValue Fisher(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, FisherScalar);
        return FisherScalar(args[0]);
    }

    private static ScalarValue FisherScalar(ScalarValue value)
    {
        double x = ToNumber(value);
        if (!double.IsFinite(x)) return ErrorValue.Num;
        if (x <= -1 || x >= 1) return ErrorValue.Num;
        return NumberResult(0.5 * Math.Log((1 + x) / (1 - x)));
    }

    private static ScalarValue FisherInv(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, FisherInvScalar);
        return FisherInvScalar(args[0]);
    }

    private static ScalarValue FisherInvScalar(ScalarValue value)
    {
        double y = ToNumber(value);
        if (!double.IsFinite(y)) return ErrorValue.Num;
        double exp = Math.Exp(2 * y);
        return NumberResult((exp - 1) / (exp + 1));
    }

    private static ScalarValue Prob(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        if (args.Count > 3 && args[3] is ErrorValue e3) return e3;

        var xRange = args[0] is RangeValue xr ? xr : SingleCellArray(args[0]);
        var probRange = args[1] is RangeValue pr ? pr : SingleCellArray(args[1]);
        var upperArg = args.Count > 3 ? args[3] : args[2];
        return MapBinaryMathArgs(args[2], upperArg, (lowerValue, upperValue) => ProbScalar(xRange, probRange, lowerValue, upperValue));
    }

    private static ScalarValue ProbScalar(RangeValue xRange, RangeValue probRange, ScalarValue lowerValue, ScalarValue upperValue)
    {
        if (xRange.RowCount != probRange.RowCount || xRange.ColCount != probRange.ColCount) return ErrorValue.NA;

        double lower = ToNumber(lowerValue);
        double upper = ToNumber(upperValue);
        if (!double.IsFinite(lower) || !double.IsFinite(upper)) return ErrorValue.Num;

        double probabilitySum = 0;
        double result = 0;
        for (int r = 0; r < xRange.RowCount; r++)
        {
            for (int c = 0; c < xRange.ColCount; c++)
            {
                var xCell = xRange.Cells[r, c];
                var probCell = probRange.Cells[r, c];
                if (xCell is ErrorValue xError) return xError;
                if (probCell is ErrorValue pError) return pError;
                if (!TryCellNumber(xCell, out double x) || !TryCellNumber(probCell, out double p)) return ErrorValue.Value;
                if (!double.IsFinite(x) || !double.IsFinite(p) || p <= 0 || p > 1) return ErrorValue.Num;

                probabilitySum += p;
                if (x >= lower && x <= upper) result += p;
            }
        }

        return Math.Abs(probabilitySum - 1.0) <= 1e-12 ? NumberResult(result) : ErrorValue.Num;
    }

    private static ScalarValue Average(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        double total = 0;
        int count = 0;
        foreach (var arg in args)
        {
            if (arg is ErrorValue err) return err;
            if (arg is ReferencedScalarValue referenced)
            {
                if (TryReferencedNumber(referenced, out double value, out var refError))
                {
                    total += value;
                    count++;
                }
                else if (refError is not null) return refError;
                continue;
            }
            if (arg is DirectTextLiteralValue direct)
            {
                if (!TryDirectTextNumber(direct, out double value)) return ErrorValue.Value;
                if (!double.IsFinite(value)) return ErrorValue.Num;
                total += value;
                count++;
                continue;
            }
            if (arg is BlankValue or TextValue) continue;
            total += ToNumber(arg);
            count++;
        }
        return count == 0 ? ErrorValue.DivByZero : NumberResult(total / count);
    }

    private static ScalarValue AverageA(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        double total = 0;
        int count = 0;
        foreach (var arg in args)
        {
            var (values, error) = CollectAValues(arg);
            if (error is not null) return error;
            foreach (var value in values)
            {
                total += value;
                count++;
            }
        }

        return count == 0 ? ErrorValue.DivByZero : NumberResult(total / count);
    }

    private static ScalarValue Min(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        double? min = null;
        foreach (var arg in args)
        {
            if (arg is ErrorValue err) return err;
            if (arg is ReferencedScalarValue referenced)
            {
                if (TryReferencedNumber(referenced, out double value, out var refError))
                {
                    if (min is null || value < min) min = value;
                }
                else if (refError is not null) return refError;
                continue;
            }
            if (arg is DirectTextLiteralValue direct)
            {
                if (!TryDirectTextNumber(direct, out double value)) return ErrorValue.Value;
                if (!double.IsFinite(value)) return ErrorValue.Num;
                if (min is null || value < min) min = value;
                continue;
            }
            if (arg is BlankValue or TextValue) continue;
            var val = ToNumber(arg);
            if (min is null || val < min) min = val;
        }
        return min.HasValue ? NumberResult(min.Value) : new NumberValue(0);
    }

    private static ScalarValue MinA(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        double? min = null;
        foreach (var arg in args)
        {
            var (values, error) = CollectAValues(arg);
            if (error is not null) return error;
            foreach (var value in values)
                if (min is null || value < min) min = value;
        }

        return min.HasValue ? NumberResult(min.Value) : new NumberValue(0);
    }

    private static ScalarValue Max(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        double? max = null;
        foreach (var arg in args)
        {
            if (arg is ErrorValue err) return err;
            if (arg is ReferencedScalarValue referenced)
            {
                if (TryReferencedNumber(referenced, out double value, out var refError))
                {
                    if (max is null || value > max) max = value;
                }
                else if (refError is not null) return refError;
                continue;
            }
            if (arg is DirectTextLiteralValue direct)
            {
                if (!TryDirectTextNumber(direct, out double value)) return ErrorValue.Value;
                if (!double.IsFinite(value)) return ErrorValue.Num;
                if (max is null || value > max) max = value;
                continue;
            }
            if (arg is BlankValue or TextValue) continue;
            var val = ToNumber(arg);
            if (max is null || val > max) max = val;
        }
        return max.HasValue ? NumberResult(max.Value) : new NumberValue(0);
    }

    private static ScalarValue MaxA(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        double? max = null;
        foreach (var arg in args)
        {
            var (values, error) = CollectAValues(arg);
            if (error is not null) return error;
            foreach (var value in values)
                if (max is null || value > max) max = value;
        }

        return max.HasValue ? NumberResult(max.Value) : new NumberValue(0);
    }

    private static (IReadOnlyList<double> Values, ErrorValue? Error) CollectAValues(ScalarValue value)
    {
        var values = new List<double>();
        var error = AddAValues(value, values, directText: value is DirectTextLiteralValue);
        return (values, error);
    }

    private static ErrorValue? AddAValues(ScalarValue value, List<double> values, bool directText)
    {
        switch (value)
        {
            case ErrorValue e:
                return e;
            case ReferencedScalarValue referenced:
                return AddAValues(referenced.Value, values, directText: false);
            case RangeValue range:
                foreach (var cell in range.Flatten())
                {
                    var error = AddAValues(cell, values, directText: false);
                    if (error is not null) return error;
                }
                return null;
            case BlankValue:
                return null;
            case NumberValue n:
                values.Add(n.Value);
                return null;
            case DateTimeValue d:
                values.Add(d.Value);
                return null;
            case BoolValue b:
                values.Add(b.Value ? 1.0 : 0.0);
                return null;
            case DirectTextLiteralValue t:
                if (ExcelTextNumberParser.TryParse(t.Value, out var directParsed))
                    values.Add(directParsed);
                else if (t.Value.Length == 0 || !directText)
                    values.Add(0.0);
                else
                    return ErrorValue.Value;
                return null;
            case TextValue t:
                if (ExcelTextNumberParser.TryParse(t.Value, out var textParsed))
                    values.Add(textParsed);
                else if (t.Value.Length == 0 || !directText)
                    values.Add(0.0);
                else
                    return ErrorValue.Value;
                return null;
            default:
                return ErrorValue.Value;
        }
    }

    private static ScalarValue Count(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        int count = 0;
        foreach (var arg in args)
        {
            if (arg is ErrorValue err) return err;
            if (arg is ReferencedScalarValue referenced)
            {
                if (TryReferencedNumber(referenced, out _, out var refError)) count++;
                continue;
            }
            if (arg is DirectTextLiteralValue direct)
            {
                if (TryDirectTextNumber(direct, out _)) count++;
                continue;
            }
            if (arg is NumberValue or BoolValue or DateTimeValue)
                count++;
        }
        return new NumberValue(count);
    }

    private static ScalarValue CountA(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        int count = 0;
        foreach (var arg in args)
        {
            if (arg is not BlankValue)
                count++;
        }
        return new NumberValue(count);
    }

    private static ScalarValue Large(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        var range = args[0] is RangeValue rangeArg
            ? rangeArg
            : new RangeValue(new ScalarValue[1, 1] { { args[0] } });
        if (args[1] is ErrorValue e1) return e1;
        if (args[1] is RangeValue kRange) return MapUnaryTextRange(kRange, kValue => LargeScalar(range, kValue));
        return LargeScalar(range, args[1]);
    }

    private static ScalarValue LargeScalar(RangeValue range, ScalarValue kValue)
    {
        var kD = ToNumber(kValue);
        if (!double.IsFinite(kD)) return ErrorValue.Num;
        int k = (int)kD;
        var (values, err) = CollectRangeNumbersForSelection(range);
        if (err is not null) return err;
        var nums = values!;
        if (k < 1 || k > nums.Count) return ErrorValue.Num;
        return new NumberValue(SelectKthSmallest(nums, nums.Count - k));
    }

    private static ScalarValue Small(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        var range = args[0] is RangeValue rangeArg
            ? rangeArg
            : new RangeValue(new ScalarValue[1, 1] { { args[0] } });
        if (args[1] is ErrorValue e1) return e1;
        if (args[1] is RangeValue kRange) return MapUnaryTextRange(kRange, kValue => SmallScalar(range, kValue));
        return SmallScalar(range, args[1]);
    }

    private static ScalarValue SmallScalar(RangeValue range, ScalarValue kValue)
    {
        var kD = ToNumber(kValue);
        if (!double.IsFinite(kD)) return ErrorValue.Num;
        int k = (int)kD;
        var (values, err) = CollectRangeNumbersForSelection(range);
        if (err is not null) return err;
        var nums = values!;
        if (k < 1 || k > nums.Count) return ErrorValue.Num;
        return new NumberValue(SelectKthSmallest(nums, k - 1));
    }

    private static ScalarValue Rank(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var range = args[1] is RangeValue rangeArg
            ? rangeArg
            : new RangeValue(new ScalarValue[1, 1] { { args[1] } });
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        var orderArg = args.Count > 2 ? args[2] : BlankValue.Instance;
        return MapBinaryMathArgs(args[0], orderArg, (numberValue, orderValue) => RankScalar(range, numberValue, orderValue));
    }

    private static ScalarValue RankScalar(RangeValue range, ScalarValue numberValue, ScalarValue orderValue)
    {
        var number = ToNumber(numberValue);
        if (!double.IsFinite(number)) return ErrorValue.Num;
        double rawOrder = orderValue is not BlankValue ? ToNumber(orderValue) : 0;
        if (!double.IsFinite(rawOrder)) return ErrorValue.Num;
        int order  = (int)rawOrder;

        var (nums, err) = CollectRangeNumbers(range);
        if (err is not null) return err;

        if (!nums!.Contains(number)) return ErrorValue.NA;

        int rank;
        if (order == 0)
            rank = nums.Count(x => x > number) + 1;  // descending
        else
            rank = nums.Count(x => x < number) + 1;  // ascending

        return new NumberValue(rank);
    }

    private static ScalarValue Stdev(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (numsOrNull, err) = CollectNumbers(args);
        if (err is not null) return err;
        var nums = numsOrNull!;
        if (nums.Count < 2) return ErrorValue.DivByZero;
        double mean = nums.Average();
        double variance = nums.Sum(x => (x - mean) * (x - mean)) / (nums.Count - 1);
        return NumberResult(Math.Sqrt(variance));
    }

    private static ScalarValue StdevA(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var r = VarA(args, ctx);
        return r is NumberValue nv ? NumberResult(Math.Sqrt(nv.Value)) : r;
    }

    private static ScalarValue StdevPA(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var r = VarPA(args, ctx);
        return r is NumberValue nv ? NumberResult(Math.Sqrt(nv.Value)) : r;
    }

    private static ScalarValue Median(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (numsOrNull, err) = CollectNumbers(args);
        if (err is not null) return err;
        var nums = numsOrNull!;
        if (nums.Count == 0) return ErrorValue.Num;
        nums.Sort();
        int mid = nums.Count / 2;
        if (nums.Count % 2 == 1)
            return NumberResult(nums[mid]);
        return NumberResult((nums[mid - 1] + nums[mid]) / 2.0);
    }

    private static ScalarValue Countblank(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is not RangeValue range) return ErrorValue.Value;
        int count = 0;
        for (int r = 0; r < range.RowCount; r++)
        {
            for (int c = 0; c < range.ColCount; c++)
            {
                var value = range.Cells[r, c];
                if (value is BlankValue || value is TextValue { Value.Length: 0 }) count++;
            }
        }

        return new NumberValue(count);
    }

    private static ScalarValue VarS(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (list, err) = CollectNumbers(args);
        if (err is not null) return err;
        if (list!.Count < 2) return ErrorValue.DivByZero;
        double mean = list.Average();
        return NumberResult(list.Sum(x => (x - mean) * (x - mean)) / (list.Count - 1));
    }

    private static ScalarValue VarA(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (list, err) = CollectAValues(args);
        if (err is not null) return err;
        if (list!.Count < 2) return ErrorValue.DivByZero;
        double mean = list.Average();
        return NumberResult(list.Sum(x => (x - mean) * (x - mean)) / (list.Count - 1));
    }

    private static ScalarValue VarP(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (list, err) = CollectNumbers(args);
        if (err is not null) return err;
        if (list!.Count == 0) return ErrorValue.DivByZero;
        double mean = list.Average();
        return NumberResult(list.Sum(x => (x - mean) * (x - mean)) / list.Count);
    }

    private static ScalarValue VarPA(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (list, err) = CollectAValues(args);
        if (err is not null) return err;
        if (list!.Count == 0) return ErrorValue.DivByZero;
        double mean = list.Average();
        return NumberResult(list.Sum(x => (x - mean) * (x - mean)) / list.Count);
    }

    private static ScalarValue StdevP(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var r = VarP(args, ctx);
        return r is NumberValue nv ? NumberResult(Math.Sqrt(nv.Value)) : r;
    }

    private static (List<double>? Nums, ErrorValue? Error) CollectAValues(IReadOnlyList<ScalarValue> args, int start = 0)
    {
        var list = new List<double>();
        for (var i = start; i < args.Count; i++)
        {
            var (values, error) = CollectAValues(args[i]);
            if (error is not null) return (null, error);
            list.AddRange(values);
        }

        return (list, null);
    }

    private static (List<double>? Nums, ErrorValue? Error) CollectNumbers(IReadOnlyList<ScalarValue> args, int start = 0)
    {
        var list = new List<double>();
        for (int i = start; i < args.Count; i++)
        {
            var a = args[i];
            if (a is ErrorValue e) return (null, e);
            if (a is ReferencedScalarValue referenced)
            {
                if (TryReferencedNumber(referenced, out double value, out var refError)) list.Add(value);
                else if (refError is not null) return (null, refError);
            }
            else if (a is NumberValue nv) list.Add(nv.Value);
            else if (a is BoolValue bv) list.Add(bv.Value ? 1.0 : 0.0);
            else if (a is DateTimeValue dt) list.Add(dt.Value);
            else if (a is DirectTextLiteralValue direct)
            {
                if (!TryDirectTextNumber(direct, out double value)) return (null, ErrorValue.Value);
                list.Add(value);
            }
        }
        return (list, null);
    }

    private static (List<double>? Nums, ErrorValue? Error) CollectRangeNumbers(RangeValue range)
    {
        var (count, err) = CountRangeNumbers(range);
        if (err is not null) return (null, err);

        var list = new List<double>(count);
        for (int r = 0; r < range.RowCount; r++)
        {
            for (int c = 0; c < range.ColCount; c++)
            {
                var value = range.Cells[r, c];
                if (value is NumberValue n) list.Add(n.Value);
                else if (value is DateTimeValue d) list.Add(d.Value);
            }
        }

        return (list, null);
    }

    private static (int Count, ErrorValue? Error) CountRangeNumbers(RangeValue range)
    {
        int count = 0;
        for (int r = 0; r < range.RowCount; r++)
        {
            for (int c = 0; c < range.ColCount; c++)
            {
                var value = range.Cells[r, c];
                if (value is ErrorValue e) return (0, e);
                if (value is NumberValue or DateTimeValue) count++;
            }
        }

        return (count, null);
    }

    private static (List<double>? Nums, ErrorValue? Error) CollectRangeNumbersForSelection(RangeValue range)
    {
        var list = new List<double>(range.RowCount * range.ColCount);
        for (int r = 0; r < range.RowCount; r++)
        {
            for (int c = 0; c < range.ColCount; c++)
            {
                var value = range.Cells[r, c];
                if (value is ErrorValue e) return (null, e);
                if (value is NumberValue n) list.Add(n.Value);
                else if (value is DateTimeValue d) list.Add(d.Value);
            }
        }

        return (list, null);
    }

    private static double SelectKthSmallest(List<double> values, int k)
    {
        int left = 0;
        int right = values.Count - 1;
        var comparer = Comparer<double>.Default;

        while (left < right)
        {
            int pivotIndex = left + ((right - left) / 2);
            var (equalStart, equalEnd) = Partition(values, left, right, pivotIndex, comparer);

            if (k < equalStart)
                right = equalStart - 1;
            else if (k > equalEnd)
                left = equalEnd + 1;
            else
                break;
        }

        return values[k];
    }

    private static (int EqualStart, int EqualEnd) Partition(List<double> values, int left, int right, int pivotIndex, Comparer<double> comparer)
    {
        double pivotValue = values[pivotIndex];
        int less = left;
        int current = left;
        int greater = right;

        while (current <= greater)
        {
            int comparison = comparer.Compare(values[current], pivotValue);
            if (comparison < 0)
            {
                Swap(values, less, current);
                less++;
                current++;
            }
            else if (comparison > 0)
            {
                Swap(values, current, greater);
                greater--;
            }
            else
            {
                current++;
            }
        }

        return (less, greater);
    }

    private static void Swap(List<double> values, int i, int j)
    {
        if (i == j) return;
        (values[i], values[j]) = (values[j], values[i]);
    }

    private static ScalarValue PercentileInc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        var rv = args[0] is RangeValue range
            ? range
            : new RangeValue(new ScalarValue[1, 1] { { args[0] } });
        if (args[1] is ErrorValue e) return e;
        if (args[1] is RangeValue kRange) return MapUnaryTextRange(kRange, kValue => PercentileIncScalar(rv, kValue));
        return PercentileIncScalar(rv, args[1]);
    }

    private static ScalarValue PercentileIncScalar(RangeValue rv, ScalarValue kValue)
    {
        double k = ToNumber(kValue);
        if (!double.IsFinite(k)) return ErrorValue.Num;
        if (k < 0 || k > 1) return ErrorValue.Num;
        var (nums, err) = CollectRangeNumbers(rv);
        if (err is not null) return err;
        var sorted = nums!.OrderBy(x => x).ToList();
        if (sorted.Count == 0) return ErrorValue.Num;
        double rank = k * (sorted.Count - 1);
        int lo = (int)rank;
        if (lo >= sorted.Count - 1) return NumberResult(sorted[^1]);
        return NumberResult(sorted[lo] + (rank - lo) * (sorted[lo + 1] - sorted[lo]));
    }

    private static ScalarValue PercentileExc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        var rv = args[0] is RangeValue range
            ? range
            : SingleCellArray(args[0]);
        if (args[1] is ErrorValue e) return e;
        if (args[1] is RangeValue kRange) return MapUnaryTextRange(kRange, kValue => PercentileExcScalar(rv, kValue));
        return PercentileExcScalar(rv, args[1]);
    }

    private static ScalarValue PercentileExcScalar(RangeValue rv, ScalarValue kValue)
    {
        double k = ToNumber(kValue);
        if (!double.IsFinite(k)) return ErrorValue.Num;
        if (k <= 0 || k >= 1) return ErrorValue.Num;
        var (nums, err) = CollectRangeNumbers(rv);
        if (err is not null) return err;
        var sorted = nums!.OrderBy(x => x).ToList();
        int n = sorted.Count;
        if (n == 0) return ErrorValue.Num;
        double rank = k * (n + 1) - 1;
        if (rank < 0 || rank >= n) return ErrorValue.Num;
        int lo = (int)rank;
        if (lo >= n - 1) return NumberResult(sorted[n - 1]);
        return NumberResult(sorted[lo] + (rank - lo) * (sorted[lo + 1] - sorted[lo]));
    }

    private static ScalarValue QuartileInc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        var rv = args[0] is RangeValue range
            ? range
            : new RangeValue(new ScalarValue[1, 1] { { args[0] } });
        if (args[1] is ErrorValue e) return e;
        if (args[1] is RangeValue quartRange) return MapUnaryTextRange(quartRange, quartValue => QuartileIncScalar(rv, quartValue));
        return QuartileIncScalar(rv, args[1]);
    }

    private static ScalarValue QuartileIncScalar(RangeValue rv, ScalarValue quartValue)
    {
        double rawQuart = ToNumber(quartValue);
        if (!double.IsFinite(rawQuart)) return ErrorValue.Num;
        int quart = (int)rawQuart;
        if (quart < 0 || quart > 4) return ErrorValue.Num;
        var (nums, err) = CollectRangeNumbers(rv);
        if (err is not null) return err;
        var sorted = nums!.OrderBy(x => x).ToList();
        if (sorted.Count == 0) return ErrorValue.Num;
        if (quart == 0) return NumberResult(sorted[0]);
        if (quart == 4) return NumberResult(sorted[^1]);
        double rank = (quart / 4.0) * (sorted.Count - 1);
        int lo = (int)rank;
        if (lo >= sorted.Count - 1) return NumberResult(sorted[^1]);
        return NumberResult(sorted[lo] + (rank - lo) * (sorted[lo + 1] - sorted[lo]));
    }

    private static ScalarValue QuartileExc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        var rv = args[0] is RangeValue range
            ? range
            : SingleCellArray(args[0]);
        if (args[1] is ErrorValue e) return e;
        if (args[1] is RangeValue quartRange) return MapUnaryTextRange(quartRange, quartValue => QuartileExcScalar(rv, quartValue));
        return QuartileExcScalar(rv, args[1]);
    }

    private static ScalarValue QuartileExcScalar(RangeValue rv, ScalarValue quartValue)
    {
        double rawQuart = ToNumber(quartValue);
        if (!double.IsFinite(rawQuart)) return ErrorValue.Num;
        int quart = (int)rawQuart;
        if (quart < 1 || quart > 3) return ErrorValue.Num;
        return PercentileExcScalar(rv, new NumberValue(quart / 4.0));
    }

    private static ScalarValue Geomean(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (nums, err) = CollectNumbers(args);
        if (err is not null) return err;
        if (nums!.Count == 0) return ErrorValue.Num;

        var logSum = 0.0;
        foreach (var value in nums)
        {
            if (value <= 0) return ErrorValue.Num;
            logSum += Math.Log(value);
        }
        return NumberResult(Math.Exp(logSum / nums.Count));
    }

    private static ScalarValue Harmean(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (nums, err) = CollectNumbers(args);
        if (err is not null) return err;
        if (nums!.Count == 0) return ErrorValue.Num;

        double recSum = 0;
        foreach (var value in nums)
        {
            if (value <= 0) return ErrorValue.Num;
            recSum += 1.0 / value;
        }
        return NumberResult(nums.Count / recSum);
    }

    private static ScalarValue Avedev(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (nums, err) = CollectNumbers(args);
        if (err is not null) return err;
        if (nums!.Count == 0) return ErrorValue.DivByZero;
        double mean = nums.Average();
        return NumberResult(nums.Average(x => Math.Abs(x - mean)));
    }

    private static ScalarValue ModeSngl(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (nums, err) = CollectNumbers(args);
        if (err is not null) return err;

        var freq = new Dictionary<double, int>();
        var order = new List<double>();
        foreach (var value in nums!)
        {
            if (!freq.ContainsKey(value)) order.Add(value);
            freq[value] = freq.GetValueOrDefault(value) + 1;
        }

        if (freq.Count == 0) return ErrorValue.NA;
        int maxFreq = freq.Values.Max();
        if (maxFreq < 2) return ErrorValue.NA;
        foreach (var key in order)
            if (freq[key] == maxFreq) return NumberResult(key);
        return ErrorValue.NA;
    }

    private static ScalarValue PercentrankInc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        var rv = args[0] is RangeValue range
            ? range
            : SingleCellArray(args[0]);
        if (args[1] is ErrorValue e) return e;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        var sigArg = args.Count > 2 ? args[2] : BlankValue.Instance;
        return MapBinaryMathArgs(args[1], sigArg, (xValue, sigValue) => PercentrankIncScalar(rv, xValue, sigValue));
    }

    private static ScalarValue PercentrankIncScalar(RangeValue rv, ScalarValue xValue, ScalarValue sigValue)
    {
        double x = ToNumber(xValue);
        if (!double.IsFinite(x)) return ErrorValue.Num;
        double rawSig = sigValue is not BlankValue ? ToNumber(sigValue) : 3;
        if (!double.IsFinite(rawSig) || rawSig > int.MaxValue) return ErrorValue.Num;
        int sig = (int)rawSig;
        if (sig < 1) return ErrorValue.Num;
        var (nums, err) = CollectRangeNumbers(rv);
        if (err is not null) return err;
        var sorted = nums!.OrderBy(v => v).ToList();
        int n = sorted.Count;
        if (n == 0 || x < sorted[0] || x > sorted[^1]) return ErrorValue.NA;
        double factor = Math.Pow(10, sig);
        if (!double.IsFinite(factor)) return ErrorValue.Num;

        int below = sorted.Count(v => v < x);
        int equal = sorted.Count(v => v == x);
        double pctRank;
        if (equal > 0)
        {
            pctRank = n == 1 ? 1.0 : (double)below / (n - 1);
        }
        else
        {
            // Excel interpolates between adjacent values when x is not in the array
            // but lies between sorted[0] and sorted[^1]. Find the largest index where
            // sorted[i] < x, then interpolate the rank between i and i+1.
            int lo = below - 1;
            if (lo < 0 || lo >= n - 1) return ErrorValue.NA;
            double lower = sorted[lo];
            double upper = sorted[lo + 1];
            double frac = upper > lower ? (x - lower) / (upper - lower) : 0.0;
            pctRank = ((double)lo + frac) / (n - 1);
        }

        return NumberResult(Math.Floor(pctRank * factor) / factor);
    }

    private static ScalarValue PercentrankExc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        var rv = args[0] is RangeValue range
            ? range
            : SingleCellArray(args[0]);
        if (args[1] is ErrorValue e) return e;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        var sigArg = args.Count > 2 ? args[2] : BlankValue.Instance;
        return MapBinaryMathArgs(args[1], sigArg, (xValue, sigValue) => PercentrankExcScalar(rv, xValue, sigValue));
    }

    private static ScalarValue PercentrankExcScalar(RangeValue rv, ScalarValue xValue, ScalarValue sigValue)
    {
        double x = ToNumber(xValue);
        if (!double.IsFinite(x)) return ErrorValue.Num;
        double rawSig = sigValue is not BlankValue ? ToNumber(sigValue) : 3;
        if (!double.IsFinite(rawSig) || rawSig > int.MaxValue) return ErrorValue.Num;
        int sig = (int)rawSig;
        if (sig < 1) return ErrorValue.Num;
        var (nums, err) = CollectRangeNumbers(rv);
        if (err is not null) return err;
        var sorted = nums!.OrderBy(v => v).ToList();
        int n = sorted.Count;
        if (n == 0 || x < sorted[0] || x > sorted[^1]) return ErrorValue.NA;
        double factor = Math.Pow(10, sig);
        if (!double.IsFinite(factor)) return ErrorValue.Num;

        int below = sorted.Count(v => v < x);
        int equal = sorted.Count(v => v == x);
        double pctRank;
        if (equal > 0)
        {
            pctRank = (below + 1.0) / (n + 1.0);
        }
        else
        {
            int lo = below - 1;
            if (lo < 0 || lo >= n - 1) return ErrorValue.NA;
            double lower = sorted[lo];
            double upper = sorted[lo + 1];
            double frac = upper > lower ? (x - lower) / (upper - lower) : 0.0;
            pctRank = (lo + 1.0 + frac) / (n + 1.0);
        }

        return NumberResult(Math.Floor(pctRank * factor) / factor);
    }

    private static ScalarValue Correl(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var (xs, ys, pairErr) = CollectPairedNumbers(args[0], args[1]);
        if (pairErr is not null) return pairErr;
        return Correlation(xs, ys);
    }

    private static ScalarValue Pearson(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        Correl(args, ctx);

    private static ScalarValue Rsq(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var correlation = Correl(args, ctx);
        return correlation is NumberValue nv ? NumberResult(nv.Value * nv.Value) : correlation;
    }

    private static ScalarValue CovarianceP(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        Covariance(args, sample: false);

    private static ScalarValue CovarianceS(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        Covariance(args, sample: true);

    private static ScalarValue Covariance(IReadOnlyList<ScalarValue> args, bool sample)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var (xs, ys, pairErr) = CollectPairedNumbers(args[0], args[1]);
        if (pairErr is not null) return pairErr;
        var stats = RegressionStats(xs, ys);
        if (stats.Error is not null) return stats.Error;
        if (sample && stats.Count < 2) return ErrorValue.DivByZero;
        return NumberResult(stats.Sxy / (sample ? stats.Count - 1 : stats.Count));
    }

    private static ScalarValue Forecast(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        double x    = ToNumber(args[0]);
        if (!double.IsFinite(x)) return ErrorValue.Num;
        var (ys, xs, pairErr) = CollectPairedNumbers(args[1], args[2]);
        if (pairErr is not null) return pairErr;
        var fit = LinearFit(ys, xs);
        if (fit.Error is not null) return fit.Error;
        return NumberResult(fit.Intercept + fit.Slope * x);
    }

    private static ScalarValue Slope(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var (ys, xs, pairErr) = CollectPairedNumbers(args[0], args[1]);
        if (pairErr is not null) return pairErr;
        var fit = LinearFit(ys, xs);
        return fit.Error is not null ? fit.Error : NumberResult(fit.Slope);
    }

    private static ScalarValue Intercept(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var (ys, xs, pairErr) = CollectPairedNumbers(args[0], args[1]);
        if (pairErr is not null) return pairErr;
        var fit = LinearFit(ys, xs);
        return fit.Error is not null ? fit.Error : NumberResult(fit.Intercept);
    }

    private static ScalarValue Steyx(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var (ys, xs, pairErr) = CollectPairedNumbers(args[0], args[1]);
        if (pairErr is not null) return pairErr;
        var fit = LinearFit(ys, xs);
        if (fit.Error is not null) return fit.Error;
        if (xs.Count < 3) return ErrorValue.DivByZero;

        double residualSumSquares = 0.0;
        for (var i = 0; i < xs.Count; i++)
        {
            var predicted = fit.Intercept + fit.Slope * xs[i];
            var residual = ys[i] - predicted;
            residualSumSquares += residual * residual;
        }

        return NumberResult(Math.Sqrt(residualSumSquares / (xs.Count - 2)));
    }

    private static (List<double> Left, List<double> Right, ErrorValue? Error) CollectPairedRangeNumbers(RangeValue left, RangeValue right)
    {
        return CollectPairedNumbers(left, right);
    }

    private static (List<double> Left, List<double> Right, ErrorValue? Error) CollectPairedNumbers(ScalarValue left, ScalarValue right)
    {
        var leftSource = BuildPairedSource(left);
        if (leftSource.Error is not null) return ([], [], leftSource.Error);
        var rightSource = BuildPairedSource(right);
        if (rightSource.Error is not null) return ([], [], rightSource.Error);

        if (leftSource.Count != rightSource.Count)
            return ([], [], ErrorValue.NA);

        var leftValues = new List<double>();
        var rightValues = new List<double>();
        for (int i = 0; i < leftSource.Count; i++)
        {
            if (leftSource.Values[i] is double leftNumber &&
                rightSource.Values[i] is double rightNumber)
            {
                leftValues.Add(leftNumber);
                rightValues.Add(rightNumber);
            }
        }

        return (leftValues, rightValues, null);
    }

    private static (int Count, List<double?> Values, ErrorValue? Error) BuildPairedSource(ScalarValue value)
    {
        if (value is ErrorValue error) return (0, [], error);
        if (value is RangeValue range)
        {
            var values = new List<double?>(range.RowCount * range.ColCount);
            for (var r = 0; r < range.RowCount; r++)
            {
                for (var c = 0; c < range.ColCount; c++)
                {
                    var cell = range.Cells[r, c];
                    if (cell is ErrorValue cellError) return (0, [], cellError);
                    values.Add(TryCellNumber(cell, out var number) ? number : null);
                }
            }

            return (values.Count, values, null);
        }

        return (1, [ToNumber(value)], null);
    }

    private static ScalarValue Correlation(List<double> xs, List<double> ys)
    {
        var stats = RegressionStats(xs, ys);
        if (stats.Error is not null) return stats.Error;
        if (stats.Count < 2) return ErrorValue.DivByZero;
        if (stats.Sxx == 0 || stats.Syy == 0) return ErrorValue.DivByZero;
        return NumberResult(stats.Sxy / Math.Sqrt(stats.Sxx * stats.Syy));
    }

    private static (int Count, double Sxx, double Sxy, double Syy, ErrorValue? Error) RegressionStats(List<double> xs, List<double> ys)
    {
        var n = xs.Count;
        if (n == 0) return (0, 0, 0, 0, ErrorValue.DivByZero);
        var xMean = xs.Average();
        var yMean = ys.Average();
        double sxx = 0, sxy = 0, syy = 0;
        for (var i = 0; i < n; i++)
        {
            var dx = xs[i] - xMean;
            var dy = ys[i] - yMean;
            sxx += dx * dx;
            sxy += dx * dy;
            syy += dy * dy;
        }

        return (n, sxx, sxy, syy, null);
    }

    private static (double Slope, double Intercept, ErrorValue? Error) LinearFit(List<double> ys, List<double> xs)
    {
        var stats = RegressionStats(xs, ys);
        if (stats.Error is not null) return (0, 0, stats.Error);
        if (stats.Count < 2 || stats.Sxx == 0) return (0, 0, ErrorValue.DivByZero);
        var slope = stats.Sxy / stats.Sxx;
        var intercept = ys.Average() - slope * xs.Average();
        return (slope, intercept, null);
    }

    private static ScalarValue RankEq(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        Rank(args, ctx);

    private static ScalarValue RankAvg(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[1] is not RangeValue range) return ErrorValue.Value;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        var orderArg = args.Count > 2 ? args[2] : BlankValue.Instance;
        return MapBinaryMathArgs(args[0], orderArg, (numberValue, orderValue) => RankAvgScalar(range, numberValue, orderValue));
    }

    private static ScalarValue RankAvgScalar(RangeValue range, ScalarValue numberValue, ScalarValue orderValue)
    {
        var number = ToNumber(numberValue);
        if (!double.IsFinite(number)) return ErrorValue.Num;
        double rawOrder = orderValue is not BlankValue ? ToNumber(orderValue) : 0;
        if (!double.IsFinite(rawOrder)) return ErrorValue.Num;
        var (nums, err) = CollectRangeNumbers(range);
        if (err is not null) return err;
        if (!nums!.Contains(number)) return ErrorValue.NA;
        int betterCount = rawOrder == 0 ? nums.Count(x => x > number) : nums.Count(x => x < number);
        int tieCount = nums.Count(x => x == number);
        return new NumberValue(betterCount + 1 + (tieCount - 1) / 2.0);
    }

    private static ScalarValue Devsq(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var nums = new List<double>();
        foreach (var arg in args)
        {
            if (arg is ErrorValue e) return e;
            if (arg is RangeValue rv)
            {
                var (rangeNums, rangeError) = CollectRangeNumbers(rv);
                if (rangeError is not null) return rangeError;
                nums.AddRange(rangeNums!);
            }
            else if (arg is NumberValue nv) nums.Add(nv.Value);
            else if (arg is DateTimeValue dt) nums.Add(dt.Value);
            else if (arg is BoolValue bv) nums.Add(bv.Value ? 1 : 0);
            else if (arg is DirectTextLiteralValue direct)
            {
                if (!TryDirectTextNumber(direct, out double value)) return ErrorValue.Value;
                nums.Add(value);
            }
        }
        if (nums.Count == 0) return ErrorValue.DivByZero;
        double mean = nums.Average();
        return NumberResult(nums.Sum(x => (x - mean) * (x - mean)));
    }
}
