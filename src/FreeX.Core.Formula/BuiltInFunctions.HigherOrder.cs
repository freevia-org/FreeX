using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    // ── Phase D: Higher-order function implementations ───────────────────────

    // MAP(array1, [array2, ...], lambda(v1, [v2, ...])) → same-shape array
    private static RangeValue SingleCellArray(ScalarValue value) =>
        new(new[,] { { value } });

    private static ScalarValue MapFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args.Count < 2) return ErrorValue.Value;
        if (args[^1] is ErrorValue lambdaErr) return lambdaErr;
        if (args[^1] is not LambdaValue lambda) return ErrorValue.Value;

        var arrays = new List<RangeValue>(args.Count - 1);
        for (int i = 0; i < args.Count - 1; i++)
        {
            if (args[i] is ErrorValue e) return e;
            arrays.Add(args[i] is RangeValue rv ? rv : SingleCellArray(args[i]));
        }

        // A 1x1 array — whether from a genuine single-cell range (e.g. A2:A2) or from
        // wrapping a plain scalar argument via SingleCellArray (e.g. a bare cell ref like
        // B1) — broadcasts against every other array's shape, matching real Excel's MAP
        // scalar-broadcast rule. Arrays that are NOT 1x1 must still all share one shape.
        int rows = 1, cols = 1;
        foreach (var a in arrays)
        {
            if (a.RowCount == 1 && a.ColCount == 1) continue;
            rows = a.RowCount;
            cols = a.ColCount;
            break;
        }
        if (arrays.Any(a => !(a.RowCount == 1 && a.ColCount == 1) && (a.RowCount != rows || a.ColCount != cols)))
            return ErrorValue.Value;
        if (lambda.Parameters.Count != arrays.Count) return ErrorValue.Value;

        var result = new ScalarValue[rows, cols];
        var invokeArgs = new ScalarValue[arrays.Count];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                for (int k = 0; k < arrays.Count; k++)
                {
                    var a = arrays[k];
                    invokeArgs[k] = a.RowCount == 1 && a.ColCount == 1 ? a.At(1, 1) : a.At(r + 1, c + 1);
                }
                var value = ctx.InvokeLambda(lambda, invokeArgs);
                if (value is RangeValue) return ErrorValue.Calc;
                result[r, c] = value;
            }
        return new RangeValue(result);
    }

    // REDUCE([initial_value], array, lambda(accumulator, value)) → scalar
    // initial_value is optional (Excel: "If no value is supplied for the initial_value, the
    // first value in the array will be used as the starting value" and reduction then proceeds
    // from the array's second element).
    private static ScalarValue ReduceFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args.Count is < 2 or > 3) return ErrorValue.Value;
        bool hasInitialValue = args.Count == 3;
        var arrayArg = args[hasInitialValue ? 1 : 0];
        var lambdaArg = args[hasInitialValue ? 2 : 1];
        if (arrayArg is ErrorValue e) return e;
        var rv = arrayArg is RangeValue range ? range : SingleCellArray(arrayArg);
        if (lambdaArg is ErrorValue lambdaErr) return lambdaErr;
        if (lambdaArg is not LambdaValue lambda) return ErrorValue.Value;
        if (lambda.Parameters.Count != 2) return ErrorValue.Value;

        var flat = rv.Flatten();
        int startIndex;
        ScalarValue acc;
        if (hasInitialValue)
        {
            acc = args[0];
            startIndex = 0;
        }
        else
        {
            if (flat.Count == 0) return ErrorValue.Value;
            acc = flat[0];
            startIndex = 1;
        }

        for (int i = startIndex; i < flat.Count; i++)
        {
            acc = ctx.InvokeLambda(lambda, [acc, flat[i]]);
        }
        return acc;
    }

    // SCAN([initial_value], array, lambda(accumulator, value)) → same-shape array of intermediates
    // initial_value is optional, matching REDUCE: when omitted, the array's first element seeds
    // the accumulator (and becomes the first output element) and scanning proceeds from the
    // second element.
    private static ScalarValue ScanFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args.Count is < 2 or > 3) return ErrorValue.Value;
        bool hasInitialValue = args.Count == 3;
        var arrayArg = args[hasInitialValue ? 1 : 0];
        var lambdaArg = args[hasInitialValue ? 2 : 1];
        if (arrayArg is ErrorValue e) return e;
        var rv = arrayArg is RangeValue range ? range : SingleCellArray(arrayArg);
        if (lambdaArg is ErrorValue lambdaErr) return lambdaErr;
        if (lambdaArg is not LambdaValue lambda) return ErrorValue.Value;
        if (lambda.Parameters.Count != 2) return ErrorValue.Value;

        int rows = rv.RowCount, cols = rv.ColCount;
        if (!hasInitialValue && rows * cols == 0) return ErrorValue.Value;
        var flat = rv.Flatten();
        var result = new ScalarValue[rows, cols];
        ScalarValue acc = hasInitialValue ? args[0] : flat[0];
        int flatIndex = 0;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                if (!hasInitialValue && flatIndex == 0)
                {
                    result[r, c] = acc;
                }
                else
                {
                    acc = ctx.InvokeLambda(lambda, [acc, flat[flatIndex]]);
                    if (acc is RangeValue) return ErrorValue.Calc;
                    result[r, c] = acc;
                }
                flatIndex++;
            }
        return new RangeValue(result);
    }

    // BYROW(array, lambda(row)) → N×1 array
    private static ScalarValue ByRowFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args.Count != 2) return ErrorValue.Value;
        if (args[0] is ErrorValue e) return e;
        var rv = args[0] is RangeValue range ? range : SingleCellArray(args[0]);
        if (args[1] is ErrorValue lambdaErr) return lambdaErr;
        if (args[1] is not LambdaValue lambda) return ErrorValue.Value;
        if (lambda.Parameters.Count != 1) return ErrorValue.Value;

        int rows = rv.RowCount, cols = rv.ColCount;
        var result = new ScalarValue[rows, 1];
        for (int r = 0; r < rows; r++)
        {
            var rowCells = new ScalarValue[1, cols];
            for (int c = 0; c < cols; c++) rowCells[0, c] = rv.At(r + 1, c + 1);
            var value = ctx.InvokeLambda(lambda, [new RangeValue(rowCells)]);
            if (value is RangeValue) return ErrorValue.Calc;
            result[r, 0] = value;
        }
        return new RangeValue(result);
    }

    // BYCOL(array, lambda(col)) → 1×M array
    private static ScalarValue ByColFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args.Count != 2) return ErrorValue.Value;
        if (args[0] is ErrorValue e) return e;
        var rv = args[0] is RangeValue range ? range : SingleCellArray(args[0]);
        if (args[1] is ErrorValue lambdaErr) return lambdaErr;
        if (args[1] is not LambdaValue lambda) return ErrorValue.Value;
        if (lambda.Parameters.Count != 1) return ErrorValue.Value;

        int rows = rv.RowCount, cols = rv.ColCount;
        var result = new ScalarValue[1, cols];
        for (int c = 0; c < cols; c++)
        {
            var colCells = new ScalarValue[rows, 1];
            for (int r = 0; r < rows; r++) colCells[r, 0] = rv.At(r + 1, c + 1);
            var value = ctx.InvokeLambda(lambda, [new RangeValue(colCells)]);
            if (value is RangeValue) return ErrorValue.Calc;
            result[0, c] = value;
        }
        return new RangeValue(result);
    }

    // MAKEARRAY(rows, cols, lambda(row_num, col_num)) → rows×cols array
    private static ScalarValue MakeArrayFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args.Count != 3) return ErrorValue.Value;
        if (!TryGetScalarControlArgument(args[0], out var rowsArg, out var rowsError)) return rowsError;
        if (!TryGetScalarControlArgument(args[1], out var colsArg, out var colsError)) return colsError;
        if (args[2] is ErrorValue lambdaErr) return lambdaErr;
        if (args[2] is not LambdaValue lambda) return ErrorValue.Value;
        if (lambda.Parameters.Count != 2) return ErrorValue.Value;
        double rawRows;
        double rawCols;
        try
        {
            rawRows = ToNumber(rowsArg);
            rawCols = ToNumber(colsArg);
        }
        catch (FormulaEvalException)
        {
            return ErrorValue.Value;
        }

        if (!double.IsFinite(rawRows) || !double.IsFinite(rawCols)) return ErrorValue.Value;
        int rows = (int)rawRows, cols = (int)rawCols;
        if (rows < 1 || cols < 1 || (long)rows * cols > FormulaSafetyLimits.MaxMaterializedRangeCells) return ErrorValue.Value;

        var result = new ScalarValue[rows, cols];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var value = ctx.InvokeLambda(lambda, [new NumberValue(r + 1), new NumberValue(c + 1)]);
                if (value is RangeValue) return ErrorValue.Calc;
                result[r, c] = value;
            }
        return new RangeValue(result);
    }
}
