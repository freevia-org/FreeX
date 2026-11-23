using FreeX.Core.Model;

namespace FreeX.Core.Formula;

// LINEST / LOGEST / TREND / GROWTH: ordinary least squares over one or more explanatory variables.
// Behaviour measured against Excel 16 through COM (en-US):
//  * known_y's is one row or one column; for a column the explanatory variables are the columns of
//    known_x's (same row count), for a row they are its rows (same column count) - anything else is #REF!.
//  * Text, logicals and empty cells anywhere in the data are #VALUE!; an error value propagates.
//  * LINEST returns the coefficients in REVERSE variable order followed by the intercept; with stats it adds
//    standard errors, r2/se_y, F/df and ss_reg/ss_resid (unused cells are #N/A).
//  * const = FALSE forces the intercept to 0 (its standard error becomes #N/A) and r2 is the uncentred one.
public static partial class BuiltInFunctions
{
    private sealed class RegressionData
    {
        public required double[] Y;
        public required double[][] X; // [variable][observation]
        public bool YIsRow;
        public int N => Y.Length;
        public int Vars => X.Length;
        // Shape of the explanatory block when known_x's was supplied (for TREND/GROWTH output shape).
        public int XRows, XCols;
        public bool XOmitted;
    }

    private static bool TryReadNumericMatrix(ScalarValue value, out double[,] matrix, out ScalarValue error)
    {
        error = ErrorValue.Value;
        matrix = new double[0, 0];
        if (value is ErrorValue e) { error = e; return false; }
        if (value is not RangeValue range)
        {
            if (value is NumberValue n) { matrix = new[,] { { n.Value } }; return true; }
            if (value is DateTimeValue d) { matrix = new[,] { { d.Value } }; return true; }
            return false;
        }

        matrix = new double[range.RowCount, range.ColCount];
        for (int r = 0; r < range.RowCount; r++)
            for (int c = 0; c < range.ColCount; c++)
            {
                switch (range.Cells[r, c])
                {
                    case NumberValue n: matrix[r, c] = n.Value; break;
                    case DateTimeValue d: matrix[r, c] = d.Value; break;
                    case ErrorValue err: error = err; return false;
                    default: return false;
                }
            }

        return true;
    }

    private static bool TryReadRegressionData(
        ScalarValue yArg,
        ScalarValue xArg,
        out RegressionData data,
        out ScalarValue error)
    {
        data = null!;
        if (!TryReadNumericMatrix(yArg, out var y, out error)) return false;
        bool yIsRow = y.GetLength(0) == 1 && y.GetLength(1) > 1;
        if (y.GetLength(0) != 1 && y.GetLength(1) != 1) { error = ErrorValue.Ref; return false; }
        int n = y.Length;
        var yv = new double[n];
        for (int i = 0; i < n; i++) yv[i] = yIsRow ? y[0, i] : y[i, 0];

        double[][] xs;
        int xRows, xCols;
        bool omitted = xArg is BlankValue or OmittedLambdaArgumentValue;
        if (omitted)
        {
            xs = new[] { Enumerable.Range(1, n).Select(i => (double)i).ToArray() };
            xRows = yIsRow ? 1 : n;
            xCols = yIsRow ? n : 1;
        }
        else
        {
            if (!TryReadNumericMatrix(xArg, out var x, out error)) return false;
            xRows = x.GetLength(0);
            xCols = x.GetLength(1);
            if (yIsRow)
            {
                if (xCols != n) { error = ErrorValue.Ref; return false; }
                xs = new double[xRows][];
                for (int v = 0; v < xRows; v++)
                {
                    xs[v] = new double[n];
                    for (int i = 0; i < n; i++) xs[v][i] = x[v, i];
                }
            }
            else
            {
                if (xRows != n) { error = ErrorValue.Ref; return false; }
                xs = new double[xCols][];
                for (int v = 0; v < xCols; v++)
                {
                    xs[v] = new double[n];
                    for (int i = 0; i < n; i++) xs[v][i] = x[i, v];
                }
            }
        }

        data = new RegressionData { Y = yv, X = xs, YIsRow = yIsRow, XRows = xRows, XCols = xCols, XOmitted = omitted };
        return true;
    }

    private static bool TryReadRegressionFlag(ScalarValue arg, bool defaultValue, out bool flag, out ScalarValue error)
    {
        error = ErrorValue.Value;
        flag = defaultValue;
        switch (arg)
        {
            case BlankValue or OmittedLambdaArgumentValue: return true;
            case ErrorValue e: error = e; return false;
            case BoolValue b: flag = b.Value; return true;
            case NumberValue n: flag = n.Value != 0; return true;
            case RangeValue { RowCount: 1, ColCount: 1 } single:
                return TryReadRegressionFlag(single.Cells[0, 0], defaultValue, out flag, out error);
            default: return false;
        }
    }

    private sealed class OlsFit
    {
        public double[] Coefficients = []; // per variable (0 for dropped) then intercept at [Vars]
        public bool[] Dropped = [];
        public double[] StdErrors = [];
        public int Rank;                   // independent columns including the intercept
        public double SsResid, SsTotal;
        public int Df;
    }

    // Householder QR; the intercept column (if any) is processed first and a column whose residual norm
    // collapses against its own norm is treated as collinear (coefficient 0, standard error 0).
    private static OlsFit FitOls(double[] y, double[][] xs, bool constant)
    {
        int n = y.Length, m = xs.Length;
        int p = m + (constant ? 1 : 0);
        var cols = new double[p][];
        int offset = 0;
        if (constant) { cols[0] = Enumerable.Repeat(1d, n).ToArray(); offset = 1; }
        for (int j = 0; j < m; j++) cols[offset + j] = (double[])xs[j].Clone();

        // Modified Gram-Schmidt with re-orthogonalisation: robust enough for the sizes of worksheet data
        // and exposes the dependency test directly.
        var q = new List<double[]>();
        var qIndex = new List<int>();
        var rMat = new double[p, p];
        var dropped = new bool[p];
        for (int j = 0; j < p; j++)
        {
            var v = (double[])cols[j].Clone();
            double norm0 = Math.Sqrt(v.Sum(t => t * t));
            for (int pass = 0; pass < 2; pass++)
                for (int k = 0; k < q.Count; k++)
                {
                    double dot = 0;
                    for (int i = 0; i < n; i++) dot += q[k][i] * v[i];
                    rMat[qIndex[k], j] += dot;
                    for (int i = 0; i < n; i++) v[i] -= dot * q[k][i];
                }

            double norm = Math.Sqrt(v.Sum(t => t * t));
            if (norm0 == 0 || norm <= norm0 * 1e-10 || q.Count >= n)
            {
                dropped[j] = true;
                continue;
            }

            rMat[j, j] = norm;
            for (int i = 0; i < n; i++) v[i] /= norm;
            q.Add(v);
            qIndex.Add(j);
        }

        // Solve R b = Q' y over the kept columns.
        int rank = q.Count;
        var qty = new double[rank];
        for (int k = 0; k < rank; k++)
        {
            double dot = 0;
            for (int i = 0; i < n; i++) dot += q[k][i] * y[i];
            qty[k] = dot;
        }

        var kept = qIndex.ToArray();
        var beta = new double[rank];
        for (int k = rank - 1; k >= 0; k--)
        {
            double sum = qty[k];
            for (int l = k + 1; l < rank; l++) sum -= rMat[kept[k], kept[l]] * beta[l];
            beta[k] = sum / rMat[kept[k], kept[k]];
        }

        var coefficients = new double[p];
        for (int k = 0; k < rank; k++) coefficients[kept[k]] = beta[k];

        // (X'X)^-1 diagonal = row norms of R^-1.
        var rInv = new double[rank, rank];
        for (int c = 0; c < rank; c++)
        {
            for (int r = c; r >= 0; r--)
            {
                double sum = r == c ? 1d : 0d;
                for (int l = r + 1; l <= c; l++) sum -= rMat[kept[r], kept[l]] * rInv[l, c];
                rInv[r, c] = sum / rMat[kept[r], kept[r]];
            }
        }

        var diag = new double[p];
        for (int k = 0; k < rank; k++)
        {
            double sum = 0;
            for (int c = k; c < rank; c++) sum += rInv[k, c] * rInv[k, c];
            diag[kept[k]] = sum;
        }

        double ssResid = 0;
        for (int i = 0; i < n; i++)
        {
            double fit = 0;
            for (int j = 0; j < p; j++) fit += coefficients[j] * cols[j][i];
            double res = y[i] - fit;
            ssResid += res * res;
        }

        double ssTotal;
        if (constant)
        {
            double mean = y.Average();
            ssTotal = y.Sum(t => (t - mean) * (t - mean));
        }
        else
        {
            ssTotal = y.Sum(t => t * t);
        }

        // Re-map to [variables..., intercept].
        var outCoef = new double[m + 1];
        var outDropped = new bool[m + 1];
        var outDiag = new double[m + 1];
        for (int j = 0; j < m; j++)
        {
            outCoef[j] = coefficients[offset + j];
            outDropped[j] = dropped[offset + j];
            outDiag[j] = diag[offset + j];
        }

        outCoef[m] = constant ? coefficients[0] : 0d;
        outDropped[m] = !constant;
        outDiag[m] = constant ? diag[0] : 0d;

        return new OlsFit
        {
            Coefficients = outCoef,
            Dropped = outDropped,
            StdErrors = outDiag,
            Rank = rank,
            SsResid = ssResid,
            SsTotal = ssTotal,
            Df = n - rank
        };
    }

    private static ScalarValue RegressionStatsTable(OlsFit fit, int vars, bool constant, bool exponentiate)
    {
        int cols = vars + 1;
        var table = new ScalarValue[5, cols];
        for (int r = 0; r < 5; r++)
            for (int c = 0; c < cols; c++)
                table[r, c] = ErrorValue.NA;

        double ssResid = fit.SsResid;
        double ssReg = Math.Max(0d, fit.SsTotal - ssResid);
        int df = fit.Df;
        double sigma2 = df > 0 ? ssResid / df : 0d;

        for (int j = 0; j <= vars; j++)
        {
            // Output column order is reversed: variable n .. variable 1, intercept.
            int col = j < vars ? vars - 1 - j : vars;
            double coef = fit.Coefficients[j];
            table[0, col] = new NumberValue(exponentiate ? Math.Exp(coef) : coef);
            if (j == vars && !constant) continue; // se of a forced-zero intercept stays #N/A
            double se = fit.Dropped[j] ? 0d : Math.Sqrt(Math.Max(0d, fit.StdErrors[j] * sigma2));
            table[1, col] = new NumberValue(se);
        }

        table[2, 0] = new NumberValue(fit.SsTotal > 0 ? ssReg / fit.SsTotal : 0d);
        table[2, 1] = new NumberValue(df > 0 ? Math.Sqrt(sigma2) : 0d);
        int regDf = fit.Rank - (constant ? 1 : 0);
        table[3, 0] = ssResid > 0 && df > 0 ? new NumberValue(ssReg / Math.Max(1, regDf) / sigma2) : ErrorValue.Num;
        table[3, 1] = new NumberValue(df);
        table[4, 0] = new NumberValue(ssReg);
        table[4, 1] = new NumberValue(ssResid);
        return new RangeValue(table);
    }

    private static ScalarValue LinestCore(IReadOnlyList<ScalarValue> args, bool logarithmic)
    {
        if (args.Count < 1) return ErrorValue.Value;
        if (!TryReadRegressionData(args[0], args.Count > 1 ? args[1] : BlankValue.Instance, out var data, out var error)) return error;
        if (!TryReadRegressionFlag(args.Count > 2 ? args[2] : BlankValue.Instance, true, out var constant, out error)) return error;
        if (!TryReadRegressionFlag(args.Count > 3 ? args[3] : BlankValue.Instance, false, out var stats, out error)) return error;

        var y = data.Y;
        if (logarithmic)
        {
            if (y.Any(v => v <= 0)) return ErrorValue.Num;
            y = y.Select(v => Math.Log(v)).ToArray();
        }

        var fit = FitOls(y, data.X, constant);
        int vars = data.Vars;
        if (stats) return RegressionStatsTable(fit, vars, constant, logarithmic);

        var row = new ScalarValue[1, vars + 1];
        for (int j = 0; j <= vars; j++)
        {
            int col = j < vars ? vars - 1 - j : vars;
            double coef = fit.Coefficients[j];
            row[0, col] = new NumberValue(logarithmic ? Math.Exp(coef) : coef);
        }

        return new RangeValue(row);
    }

    private static ScalarValue Linest(IReadOnlyList<ScalarValue> args, IEvalContext ctx) => LinestCore(args, logarithmic: false);

    private static ScalarValue Logest(IReadOnlyList<ScalarValue> args, IEvalContext ctx) => LinestCore(args, logarithmic: true);

    private static ScalarValue TrendCore(IReadOnlyList<ScalarValue> args, bool logarithmic)
    {
        if (args.Count < 1) return ErrorValue.Value;
        if (!TryReadRegressionData(args[0], args.Count > 1 ? args[1] : BlankValue.Instance, out var data, out var error)) return error;
        if (!TryReadRegressionFlag(args.Count > 3 ? args[3] : BlankValue.Instance, true, out var constant, out error)) return error;

        var y = data.Y;
        if (logarithmic)
        {
            if (y.Any(v => v <= 0)) return ErrorValue.Num;
            y = y.Select(v => Math.Log(v)).ToArray();
        }

        var fit = FitOls(y, data.X, constant);
        int vars = data.Vars;

        // Resolve the prediction points: [variable][point] plus the output shape.
        double[][] points;
        int outRows, outCols;
        var newXArg = args.Count > 2 ? args[2] : BlankValue.Instance;
        if (newXArg is BlankValue or OmittedLambdaArgumentValue)
        {
            points = data.X;
            if (vars == 1 && !data.XOmitted) { outRows = data.XRows; outCols = data.XCols; }
            else { outRows = data.YIsRow ? 1 : data.N; outCols = data.YIsRow ? data.N : 1; }
        }
        else
        {
            if (!TryReadNumericMatrix(newXArg, out var nx, out error)) return error;
            int nr = nx.GetLength(0), nc = nx.GetLength(1);
            if (vars == 1)
            {
                // A single variable: every element of new_x is a point and the result keeps new_x's shape.
                points = new[] { new double[nr * nc] };
                for (int r = 0; r < nr; r++)
                    for (int c = 0; c < nc; c++)
                        points[0][r * nc + c] = nx[r, c];
                outRows = nr;
                outCols = nc;
            }
            else if (data.YIsRow)
            {
                if (nr != vars) return ErrorValue.Ref;
                points = new double[vars][];
                for (int v = 0; v < vars; v++)
                {
                    points[v] = new double[nc];
                    for (int c = 0; c < nc; c++) points[v][c] = nx[v, c];
                }

                outRows = 1;
                outCols = nc;
            }
            else
            {
                if (nc != vars) return ErrorValue.Ref;
                points = new double[vars][];
                for (int v = 0; v < vars; v++)
                {
                    points[v] = new double[nr];
                    for (int r = 0; r < nr; r++) points[v][r] = nx[r, v];
                }

                outRows = nr;
                outCols = 1;
            }
        }

        int count = points[0].Length;
        var result = new ScalarValue[outRows, outCols];
        for (int k = 0; k < count; k++)
        {
            double value = constant ? fit.Coefficients[vars] : 0d;
            for (int v = 0; v < vars; v++) value += fit.Coefficients[v] * points[v][k];
            if (logarithmic) value = Math.Exp(value);
            result[k / outCols, k % outCols] = new NumberValue(value);
        }

        return outRows == 1 && outCols == 1 ? result[0, 0] : new RangeValue(result);
    }

    private static ScalarValue Trend(IReadOnlyList<ScalarValue> args, IEvalContext ctx) => TrendCore(args, logarithmic: false);

    private static ScalarValue Growth(IReadOnlyList<ScalarValue> args, IEvalContext ctx) => TrendCore(args, logarithmic: true);
}
