using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

public static partial class BuiltInFunctions
{
    // Matrix functions: MMULT, MINVERSE, MDETERM.

    private static bool TryRangeToMatrix(ScalarValue value, out double[,] matrix, out ScalarValue? error)
    {
        matrix = null!;
        error = null;
        if (value is ErrorValue err) { error = err; return false; }
        RangeValue rv;
        if (value is RangeValue range)
        {
            rv = range;
        }
        else
        {
            rv = SingleCellArray(value);
        }
        int rows = rv.RowCount;
        int cols = rv.ColCount;
        var m = new double[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                var cell = rv.Cells[r, c];
                if (cell is ErrorValue ecell) { error = ecell; return false; }
                if (!TryCellNumber(cell, out double d))
                {
                    error = ErrorValue.Value;
                    return false;
                }
                m[r, c] = d;
            }
        }
        matrix = m;
        return true;
    }

    private static ScalarValue Mmult(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryRangeToMatrix(args[0], out var a, out var ea)) return ea!;
        if (!TryRangeToMatrix(args[1], out var b, out var eb)) return eb!;

        int m = a.GetLength(0);
        int k = a.GetLength(1);
        int k2 = b.GetLength(0);
        int n = b.GetLength(1);
        if (k != k2) return ErrorValue.Value;

        var result = new ScalarValue[m, n];
        for (int i = 0; i < m; i++)
        {
            for (int j = 0; j < n; j++)
            {
                double sum = 0;
                for (int p = 0; p < k; p++)
                    sum += a[i, p] * b[p, j];
                if (!double.IsFinite(sum)) return ErrorValue.Num;
                result[i, j] = new NumberValue(sum);
            }
        }
        return new RangeValue(result);
    }

    private static ScalarValue Minverse(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryRangeToMatrix(args[0], out var a, out var ea)) return ea!;
        int n = a.GetLength(0);
        if (a.GetLength(1) != n) return ErrorValue.Value;

        // Build augmented matrix [A | I]
        var aug = new double[n, 2 * n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++) aug[i, j] = a[i, j];
            aug[i, n + i] = 1.0;
        }

        // Gauss-Jordan elimination with partial pivoting
        for (int col = 0; col < n; col++)
        {
            int pivotRow = col;
            double pivotMax = Math.Abs(aug[col, col]);
            for (int r = col + 1; r < n; r++)
            {
                double v = Math.Abs(aug[r, col]);
                if (v > pivotMax) { pivotMax = v; pivotRow = r; }
            }
            if (pivotMax < 1e-14) return ErrorValue.Num; // singular

            if (pivotRow != col)
            {
                for (int j = 0; j < 2 * n; j++)
                    (aug[col, j], aug[pivotRow, j]) = (aug[pivotRow, j], aug[col, j]);
            }

            double pivot = aug[col, col];
            for (int j = 0; j < 2 * n; j++) aug[col, j] /= pivot;

            for (int r = 0; r < n; r++)
            {
                if (r == col) continue;
                double factor = aug[r, col];
                if (factor == 0) continue;
                for (int j = 0; j < 2 * n; j++)
                    aug[r, j] -= factor * aug[col, j];
            }
        }

        var result = new ScalarValue[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                double v = aug[i, n + j];
                if (!double.IsFinite(v)) return ErrorValue.Num;
                result[i, j] = new NumberValue(v);
            }
        return new RangeValue(result);
    }

    private static ScalarValue Mdeterm(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryRangeToMatrix(args[0], out var a, out var ea)) return ea!;
        int n = a.GetLength(0);
        if (a.GetLength(1) != n) return ErrorValue.Value;

        // LU decomposition with partial pivoting; det = product of U diagonals * (-1)^swaps
        var lu = (double[,])a.Clone();
        int swaps = 0;
        for (int col = 0; col < n; col++)
        {
            int pivotRow = col;
            double pivotMax = Math.Abs(lu[col, col]);
            for (int r = col + 1; r < n; r++)
            {
                double v = Math.Abs(lu[r, col]);
                if (v > pivotMax) { pivotMax = v; pivotRow = r; }
            }
            if (pivotMax < 1e-300) return new NumberValue(0);
            if (pivotRow != col)
            {
                for (int j = 0; j < n; j++)
                    (lu[col, j], lu[pivotRow, j]) = (lu[pivotRow, j], lu[col, j]);
                swaps++;
            }

            double pivot = lu[col, col];
            for (int r = col + 1; r < n; r++)
            {
                double factor = lu[r, col] / pivot;
                lu[r, col] = factor;
                for (int j = col + 1; j < n; j++)
                    lu[r, j] -= factor * lu[col, j];
            }
        }

        double det = (swaps % 2 == 0) ? 1.0 : -1.0;
        for (int i = 0; i < n; i++) det *= lu[i, i];
        return NumberResult(det);
    }
}
