using Freexcel.Core.Model;

namespace Freexcel.Core.Calc;

/// <summary>
/// Finds the input value for a changing cell such that a formula cell reaches a target value.
/// Uses the secant method (two-point Newton approximation).
/// </summary>
/// <remarks>
/// Precondition: formula dependencies must be up-to-date (i.e., the workbook must have been
/// recalculated since the last formula edit) so that <paramref name="engine"/>'s dependency
/// graph correctly propagates changes from <c>changingCell</c> to <c>setCell</c>.
/// </remarks>
public static class GoalSeekService
{
    public static GoalSeekResult Seek(
        Workbook workbook,
        RecalcEngine engine,
        CellAddress setCell,
        double targetValue,
        CellAddress changingCell,
        int maxIterations = 1000,
        double tolerance = 1e-6)
    {
        // Save original value
        var originalCell = workbook.GetSheet(changingCell.Sheet)?.GetCell(changingCell)?.Clone();

        try
        {
            // Get starting point x0
            var sheet = workbook.GetSheet(changingCell.Sheet);
            double x0;
            if (sheet?.GetCell(changingCell)?.Value is NumberValue nv0)
                x0 = nv0.Value;
            else
                x0 = 0.0;

            // Evaluate f(x) = formula(x) - target
            double fx0 = EvaluateF(workbook, engine, changingCell, setCell, x0, targetValue);
            if (double.IsNaN(fx0) || double.IsInfinity(fx0))
                return new GoalSeekResult(false, x0, x0 + targetValue, 0);

            // Already at solution?
            if (Math.Abs(fx0) < tolerance)
                return new GoalSeekResult(true, x0, fx0 + targetValue, 0);

            // Second point x1
            double step = x0 != 0.0 ? 0.001 * x0 : 0.001;
            double x1 = x0 + step;

            double fx1 = EvaluateF(workbook, engine, changingCell, setCell, x1, targetValue);
            if (double.IsNaN(fx1) || double.IsInfinity(fx1))
                return new GoalSeekResult(false, x0, fx0 + targetValue, 0);

            for (int i = 0; i < maxIterations; i++)
            {
                double dfx = fx1 - fx0;

                // Guard: flat function — division by zero
                if (Math.Abs(dfx) < 1e-30)
                    return new GoalSeekResult(false, x1, fx1 + targetValue, i + 1);

                // Secant step
                double x2 = x1 - fx1 * (x1 - x0) / dfx;

                if (double.IsNaN(x2) || double.IsInfinity(x2))
                    return new GoalSeekResult(false, x1, fx1 + targetValue, i + 1);

                double fx2 = EvaluateF(workbook, engine, changingCell, setCell, x2, targetValue);
                if (double.IsNaN(fx2) || double.IsInfinity(fx2))
                    return new GoalSeekResult(false, x1, fx1 + targetValue, i + 1);

                x0 = x1; fx0 = fx1;
                x1 = x2; fx1 = fx2;

                if (Math.Abs(fx1) < tolerance)
                    return new GoalSeekResult(true, x1, fx1 + targetValue, i + 1);
            }

            return new GoalSeekResult(false, x1, fx1 + targetValue, maxIterations);
        }
        finally
        {
            // Always restore original value
            var sheet = workbook.GetSheet(changingCell.Sheet);
            if (sheet is not null)
            {
                if (originalCell is not null)
                    sheet.SetCell(changingCell, originalCell);
                else
                    sheet.ClearCell(changingCell);
                engine.Recalculate(workbook, [changingCell]);
            }
        }
    }

    private static double EvaluateF(
        Workbook workbook,
        RecalcEngine engine,
        CellAddress changingCell,
        CellAddress setCell,
        double x,
        double targetValue)
    {
        var sheet = workbook.GetSheet(changingCell.Sheet);
        if (sheet is null) return double.NaN;

        sheet.SetCell(changingCell, new NumberValue(x));
        engine.Recalculate(workbook, [changingCell]);

        var resultSheet = workbook.GetSheet(setCell.Sheet);
        if (resultSheet is null) return double.NaN;

        var value = resultSheet.GetValue(setCell);
        if (value is not NumberValue nv) return double.NaN;

        return nv.Value - targetValue;
    }
}

/// <summary>Result of a Goal Seek operation.</summary>
public record GoalSeekResult(bool Converged, double FoundValue, double ActualResult, int Iterations);
