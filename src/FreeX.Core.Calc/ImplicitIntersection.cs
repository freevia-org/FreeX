using System.Linq;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Calc;

/// <summary>
/// Excel's legacy implicit intersection (the <c>@</c> operator): resolves a multi-cell range used in a
/// scalar context to the single cell that intersects the formula's own row/column. A 1×1 range always
/// resolves to its single cell; otherwise an off-axis formula position yields <c>#VALUE!</c>.
/// </summary>
public static class ImplicitIntersection
{
    public static ScalarValue Resolve(RangeValue range, uint cellRow, uint cellCol)
    {
        int rows = range.RowCount;
        int cols = range.ColCount;

        if (rows == 1 && cols == 1)
            return range.Cells[0, 0];

        if (rows == 1)
        {
            long c = (long)cellCol - range.StartCol;
            return c >= 0 && c < cols ? range.Cells[0, (int)c] : ErrorValue.Value;
        }

        if (cols == 1)
        {
            long r = (long)cellRow - range.StartRow;
            return r >= 0 && r < rows ? range.Cells[(int)r, 0] : ErrorValue.Value;
        }

        long row = (long)cellRow - range.StartRow;
        long col = (long)cellCol - range.StartCol;
        return row >= 0 && row < rows && col >= 0 && col < cols
            ? range.Cells[(int)row, (int)col]
            : ErrorValue.Value;
    }

    /// <summary>
    /// AST-aware overload used by RecalcEngine for a legacy (non-CSE) <see cref="FormulaArrayMode.Implicit"/>
    /// formula cell whose evaluated result is a multi-cell <see cref="RangeValue"/>. A computed/constant
    /// array with no reference operand anywhere in the formula -- an array constant ("={1,2,3}"), TRANSPOSE
    /// of a constant, arithmetic between two array constants, a SUMPRODUCT-shaped array literal, etc. -- has
    /// no worksheet position to intersect against: its <see cref="RangeValue.StartRow"/>/
    /// <see cref="RangeValue.StartCol"/> default to 1/1 (see <see cref="ScalarValue"/>'s <c>RangeValue</c>
    /// doc), which can coincidentally collide with the formula cell's own row/col. Excel always shows such
    /// a formula's top-left/first element regardless of which cell it lives in.
    ///
    /// Row/col-coordinate positional intersection (the <see cref="Resolve(RangeValue,uint,uint)"/> overload
    /// above) only makes sense once the formula's AST contains at least one genuine reference node (a bare
    /// cell/range reference, named range, structured reference, the space INTERSECTION operator, or a
    /// NAME:endpoint range) -- even then via a *computed* array, e.g. "=A7:J7*B15" broadcasting a scalar
    /// over a range: the array's StartRow/StartCol coordinate frame is inherited from that real reference,
    /// so matching it against the formula cell's own position is meaningful (and is exactly how classic
    /// Excel's automatic/legacy implicit intersection has always behaved -- see
    /// ImplicitIntersectionEvalTests's "=A7:J7*B15" coverage). Gating on the resolved
    /// <see cref="RangeValue.IsSheetReference"/> flag instead of the AST would be too coarse: arithmetic
    /// broadcast over a genuine range reference already clears that flag (it's a synthesized/computed
    /// result, per the flag's own doc), so that gate would wrongly collapse "=A7:J7*B15" to its top-left
    /// element too. See R80-formula-array-cse-5-1.
    /// </summary>
    public static ScalarValue Resolve(FormulaNode formula, RangeValue range, uint cellRow, uint cellCol)
    {
        if (!ContainsReference(formula))
            return range.RowCount > 0 && range.ColCount > 0 ? range.Cells[0, 0] : ErrorValue.Value;

        return Resolve(range, cellRow, cellCol);
    }

    /// <summary>True when <paramref name="node"/> contains, anywhere in its tree, a node kind that
    /// resolves to a genuine worksheet position (as opposed to being purely computed from literals).</summary>
    private static bool ContainsReference(FormulaNode node) => node switch
    {
        CellRefNode or RangeRefNode or FullColumnRangeRefNode or FullRowRangeRefNode
            or NamedRangeNode or StructuredReferenceNode or StructuredCurrentRowReferenceNode
            or IntersectionNode or NamedRangeEndpointNode => true,
        BinaryOpNode b => ContainsReference(b.Left) || ContainsReference(b.Right),
        UnaryOpNode u => ContainsReference(u.Operand),
        FunctionCallNode f => f.Arguments.Any(ContainsReference),
        ArrayConstantNode a => a.Rows.Any(row => row.Any(ContainsReference)),
        _ => false, // NumberNode, StringNode, BooleanNode, OmittedArgumentNode, ErrorNode
    };
}
