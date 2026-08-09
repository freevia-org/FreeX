using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
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
        if (rows == 0 || cols == 0) return ErrorValue.Calc;
        if (rows < 0 || cols < 0) return ErrorValue.Value;
        if ((long)rows * cols > FormulaSafetyLimits.MaxMaterializedRangeCells) return ErrorValue.Value;
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
        if (rows == 0 || cols == 0) return ErrorValue.Calc;
        if (rows < 0 || cols < 0) return ErrorValue.Value;
        if ((long)rows * cols > FormulaSafetyLimits.MaxMaterializedRangeCells) return ErrorValue.Value;
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
}

