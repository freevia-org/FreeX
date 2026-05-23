using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

public static partial class BuiltInFunctions
{
    // ── Phase D: Higher-order function implementations ───────────────────────

    // MAP(array1, [array2, ...], lambda(v1, [v2, ...])) → same-shape array
    private static RangeValue SingleCellArray(ScalarValue value) =>
        new(new[,] { { value } });

    private static ScalarValue MapFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args.Count < 2) return ErrorValue.Value;
        if (args[^1] is not LambdaValue lambda) return ErrorValue.Value;

        var arrays = new List<RangeValue>(args.Count - 1);
        for (int i = 0; i < args.Count - 1; i++)
        {
            if (args[i] is ErrorValue e) return e;
            arrays.Add(args[i] is RangeValue rv ? rv : SingleCellArray(args[i]));
        }

        int rows = arrays[0].RowCount, cols = arrays[0].ColCount;
        if (arrays.Any(a => a.RowCount != rows || a.ColCount != cols)) return ErrorValue.Value;
        if (lambda.Parameters.Count != arrays.Count) return ErrorValue.Value;

        var result = new ScalarValue[rows, cols];
        var invokeArgs = new ScalarValue[arrays.Count];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                for (int k = 0; k < arrays.Count; k++)
                    invokeArgs[k] = arrays[k].At(r + 1, c + 1);
                var value = ctx.InvokeLambda(lambda, invokeArgs);
                if (value is RangeValue) return ErrorValue.Calc;
                result[r, c] = value;
            }
        return new RangeValue(result);
    }

    // REDUCE(initial, array, lambda(accumulator, value)) → scalar
    private static ScalarValue ReduceFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args.Count != 3) return ErrorValue.Value;
        if (args[1] is ErrorValue e) return e;
        var rv = args[1] is RangeValue range ? range : SingleCellArray(args[1]);
        if (args[2] is not LambdaValue lambda) return ErrorValue.Value;
        if (lambda.Parameters.Count != 2) return ErrorValue.Value;

        ScalarValue acc = args[0];
        var flat = rv.Flatten();
        foreach (var val in flat)
            acc = ctx.InvokeLambda(lambda, [acc, val]);
        return acc;
    }

    // SCAN(initial, array, lambda(accumulator, value)) → same-shape array of intermediates
    private static ScalarValue ScanFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args.Count != 3) return ErrorValue.Value;
        if (args[1] is ErrorValue e) return e;
        var rv = args[1] is RangeValue range ? range : SingleCellArray(args[1]);
        if (args[2] is not LambdaValue lambda) return ErrorValue.Value;
        if (lambda.Parameters.Count != 2) return ErrorValue.Value;

        int rows = rv.RowCount, cols = rv.ColCount;
        var result = new ScalarValue[rows, cols];
        ScalarValue acc = args[0];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                acc = ctx.InvokeLambda(lambda, [acc, rv.At(r + 1, c + 1)]);
                if (acc is RangeValue) return ErrorValue.Calc;
                result[r, c] = acc;
            }
        return new RangeValue(result);
    }

    // BYROW(array, lambda(row)) → N×1 array
    private static ScalarValue ByRowFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args.Count != 2) return ErrorValue.Value;
        if (args[0] is ErrorValue e) return e;
        var rv = args[0] is RangeValue range ? range : SingleCellArray(args[0]);
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
        if (args[0] is ErrorValue rowError) return rowError;
        if (args[1] is ErrorValue colError) return colError;
        if (args[2] is not LambdaValue lambda) return ErrorValue.Value;
        if (lambda.Parameters.Count != 2) return ErrorValue.Value;
        double rawRows;
        double rawCols;
        try
        {
            rawRows = ToNumber(args[0]);
            rawCols = ToNumber(args[1]);
        }
        catch (FormulaEvalException)
        {
            return ErrorValue.Value;
        }

        if (!double.IsFinite(rawRows) || !double.IsFinite(rawCols)) return ErrorValue.Value;
        int rows = (int)rawRows, cols = (int)rawCols;
        if (rows < 1 || cols < 1 || (long)rows * cols > 1_000_000L) return ErrorValue.Value;

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
