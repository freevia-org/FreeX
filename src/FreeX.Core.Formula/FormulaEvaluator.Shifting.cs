using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public sealed partial class FormulaEvaluator
{
    /// <summary>
    /// Shifts relative cell references in a formula AST from <paramref name="anchor"/> to
    /// <paramref name="current"/>, matching the semantics used for conditional-format and
    /// data-validation formulas in Excel: the rule is authored as if the anchor cell is active;
    /// relative references shift by the row/column delta when evaluated for any other cell.
    /// </summary>
    /// <remarks>
    /// Returns the original <paramref name="ast"/> unchanged when no shift is needed (cells are
    /// the same, or the formula has no relative references). Returns an <see cref="ErrorNode"/>
    /// with #REF! if a shifted reference would fall outside the valid sheet bounds.
    /// </remarks>
    public static FormulaNode ShiftFormulaForCell(
        FormulaNode ast,
        CellAddress anchor,
        CellAddress current) =>
        FormulaAstReferenceShifter.ShiftForCell(ast, anchor, current);
}
