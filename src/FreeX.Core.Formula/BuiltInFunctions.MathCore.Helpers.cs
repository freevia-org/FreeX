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
            var shape = leftRange.RowCount == 1 && leftRange.ColCount == 1 ? rightRange : leftRange;
            if (!CanBroadcastToShape(leftRange, shape.RowCount, shape.ColCount) ||
                !CanBroadcastToShape(rightRange, shape.RowCount, shape.ColCount))
                return ErrorValue.Value;

            var cells = new ScalarValue[shape.RowCount, shape.ColCount];
            for (int r = 0; r < shape.RowCount; r++)
                for (int c = 0; c < shape.ColCount; c++)
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
