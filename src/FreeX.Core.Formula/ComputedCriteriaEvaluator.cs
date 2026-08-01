using FreeX.Core.Model;

namespace FreeX.Core.Formula;

/// <summary>
/// Shared implementation of Excel's "computed criteria" evaluation rule. Both the D-functions
/// (DSUM/DGET/etc. -- see <see cref="BuiltInFunctions"/>'s database-criteria handling) and
/// Advanced Filter (<c>AdvancedFilterPlanBuilder.ComputedCriteriaCheck</c> in
/// FreeX.Core.Commands) evaluate a computed criterion the same way: the formula authored in a
/// criteria cell whose column header is blank/unmapped is evaluated as if it were anchored at the
/// database/list range's own first data row, then its relative references are shifted (row only
/// -- the column never changes across candidate rows) to each candidate row in turn. The anchor
/// is deliberately the range's first data row, NOT the criteria cell's own (usually disjoint)
/// row -- using the criteria cell's row offsets every comparison by the arbitrary distance
/// between the criteria region and the database/list, silently breaking the computed condition.
/// Kept as a single shared helper (Core.Commands already references Core.Formula) so the anchor
/// rule cannot drift between the two call sites again.
/// </summary>
public static class ComputedCriteriaEvaluator
{
    /// <summary>
    /// Evaluates <paramref name="formulaText"/> (as authored at <paramref name="formulaCol"/> on
    /// <paramref name="sheet"/>) against <paramref name="targetRow"/>, anchoring the
    /// relative-reference shift at (<paramref name="firstDataRow"/>, <paramref name="formulaCol"/>).
    /// A formula that errors during parse/shift/evaluate is treated as a real, non-matching
    /// evaluation (Excel does not skip an erroring computed criterion -- it just doesn't match).
    /// </summary>
    public static bool Evaluate(
        Sheet sheet,
        string formulaText,
        uint firstDataRow,
        uint formulaCol,
        uint targetRow,
        Workbook? workbook)
    {
        try
        {
            var ast = FormulaEvaluator.ParseFormula(formulaText);
            var anchor = new CellAddress(sheet.Id, firstDataRow, formulaCol);
            var current = new CellAddress(sheet.Id, targetRow, formulaCol);
            var shifted = FormulaEvaluator.ShiftFormulaForCell(ast, anchor, current);
            var evaluator = new FormulaEvaluator();
            var value = evaluator.Evaluate(shifted, sheet, workbook, currentCell: current);
            return value switch
            {
                BoolValue b => b.Value,
                NumberValue n => n.Value != 0,
                _ => false
            };
        }
        catch
        {
            return false;
        }
    }
}
