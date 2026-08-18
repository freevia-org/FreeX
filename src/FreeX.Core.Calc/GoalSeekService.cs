using FreeX.Core.Model;

namespace FreeX.Core.Calc;

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
            double x0 = ReadInitialChangingValue(workbook, changingCell);

            // Evaluate f(x) = formula(x) - target
            double fx0 = EvaluateF(workbook, engine, changingCell, setCell, x0, targetValue);
            if (IsInvalidNumber(fx0))
            {
                // No valid prior point exists yet to fall back to (unlike the branches below,
                // which reuse fx0/fx1 once those are known finite). Report what the set cell
                // actually holds right now — including its error code when it holds one —
                // instead of synthesizing x0 + targetValue, which has no relationship to the
                // real (invalid) result and would misrepresent how close the search got.
                var (actualValue, actualError) = ReadActualSetCellValue(workbook, setCell);
                return new GoalSeekResult(false, x0, actualValue, 0, actualError);
            }

            // Already at solution?
            if (Math.Abs(fx0) < tolerance)
                return new GoalSeekResult(true, x0, fx0 + targetValue, 0);

            // Second point x1
            double step = x0 != 0.0 ? 0.001 * x0 : 0.001;
            double x1 = x0 + step;

            double fx1 = EvaluateF(workbook, engine, changingCell, setCell, x1, targetValue);
            if (IsInvalidNumber(fx1))
                return new GoalSeekResult(false, x0, fx0 + targetValue, 0);

            for (int i = 0; i < maxIterations; i++)
            {
                double dfx = fx1 - fx0;

                // Guard: flat function — division by zero
                if (Math.Abs(dfx) < 1e-30)
                    return new GoalSeekResult(false, x1, fx1 + targetValue, i + 1);

                // Secant step
                double x2 = x1 - fx1 * (x1 - x0) / dfx;

                if (IsInvalidNumber(x2))
                    return new GoalSeekResult(false, x1, fx1 + targetValue, i + 1);

                double fx2 = EvaluateF(workbook, engine, changingCell, setCell, x2, targetValue);
                if (IsInvalidNumber(fx2))
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
            RestoreChangingCell(workbook, engine, changingCell, originalCell);
        }
    }

    private static double ReadInitialChangingValue(Workbook workbook, CellAddress changingCell)
    {
        var sheet = workbook.GetSheet(changingCell.Sheet);
        return sheet?.GetCell(changingCell)?.Value is NumberValue value ? value.Value : 0.0;
    }

    private static void RestoreChangingCell(
        Workbook workbook,
        RecalcEngine engine,
        CellAddress changingCell,
        Cell? originalCell)
    {
        var sheet = workbook.GetSheet(changingCell.Sheet);
        if (sheet is null)
            return;

        if (originalCell is not null)
            sheet.SetCell(changingCell, originalCell);
        else
            sheet.ClearCell(changingCell);

        engine.Recalculate(workbook, [changingCell]);
    }

    private static bool IsInvalidNumber(double value) =>
        double.IsNaN(value) || double.IsInfinity(value);

    /// <summary>
    /// Reads the set cell's real current value (as of the caller's most recent
    /// <c>EvaluateF</c> call) for reporting in a failed <see cref="GoalSeekResult"/>. Returns the
    /// numeric value when the cell holds a number, or the cell's error code (e.g. "#DIV/0!") when
    /// it holds an <see cref="ErrorValue"/>, so the failure report reflects reality instead of a
    /// fabricated number.
    /// </summary>
    private static (double Value, string? ErrorCode) ReadActualSetCellValue(Workbook workbook, CellAddress setCell)
    {
        var value = workbook.GetSheet(setCell.Sheet)?.GetValue(setCell);
        return value switch
        {
            NumberValue nv => (nv.Value, null),
            ErrorValue ev => (double.NaN, ev.Code),
            _ => (double.NaN, null)
        };
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
/// <param name="ActualResultError">
/// When the set cell holds an error at the point <see cref="ActualResult"/> was captured (e.g. the
/// starting point was already invalid, such as "#DIV/0!"), this carries that error code and
/// <see cref="ActualResult"/> is <see cref="double.NaN"/>. Null whenever <see cref="ActualResult"/>
/// is a genuine number. Presenters must check this before formatting <see cref="ActualResult"/> as
/// a number.
/// </param>
public record GoalSeekResult(
    bool Converged,
    double FoundValue,
    double ActualResult,
    int Iterations,
    string? ActualResultError = null);
