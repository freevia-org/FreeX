using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    private static ScalarValue MapBinaryMathArgs(
        ScalarValue left,
        ScalarValue right,
        Func<ScalarValue, ScalarValue, ScalarValue> map)
    {
        if (left is RangeValue leftRange && right is RangeValue rightRange)
        {
            // Grows to the bounding Max(rows)/Max(cols) 2-D shape (e.g. a 2x1 column vector crossed
            // with a 1x2 row vector spills a 2x2 result), matching Excel's dynamic-array broadcast
            // rule already used by IF/CHOOSE/binary operators -- see TryGrowBroadcastShape.
            if (!TryGrowBroadcastShape([leftRange, rightRange], out int rows, out int cols))
                return ErrorValue.Value;

            var cells = new ScalarValue[rows, cols];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    cells[r, c] = map(ValueAtBroadcastCell(leftRange, r, c), ValueAtBroadcastCell(rightRange, r, c));
            return new RangeValue(cells);
        }

        if (left is RangeValue lRange)
            return MapUnaryTextRange(lRange, value => map(value, right));
        if (right is RangeValue rRange)
            return MapUnaryTextRange(rRange, value => map(left, value));
        return map(left, right);
    }

}
