using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
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

        var (nums, err) = CollectRangeNumbers(range);
        if (err is not null) return err;

        if (!nums!.Contains(number)) return ErrorValue.NA;

        int rank;
        if (rawOrder == 0)
            rank = nums.Count(x => x > number) + 1;  // descending
        else
            rank = nums.Count(x => x < number) + 1;  // ascending

        return new NumberValue(rank);
    }
    private static ScalarValue RankEq(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        Rank(args, ctx);

    private static ScalarValue RankAvg(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var range = args[1] is RangeValue rangeArg
            ? rangeArg
            : new RangeValue(new ScalarValue[1, 1] { { args[1] } });
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
}
