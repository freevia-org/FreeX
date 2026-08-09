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
        CellAddress current)
    {
        int dr = (int)current.Row - (int)anchor.Row;
        int dc = (int)current.Col - (int)anchor.Col;
        if (dr == 0 && dc == 0)
            return ast;

        if (!HasRelativeReferences(ast))
            return ast;

        return ShiftAst(ast, dr, dc);
    }

    // ── Relative-reference detection ─────────────────────────────────────────

    private static bool HasRelativeReferences(FormulaNode node) => node switch
    {
        CellRefNode cr => !cr.IsColAbsolute || !cr.IsRowAbsolute,
        RangeRefNode rr => HasRelativeReferences(rr.Start) || HasRelativeReferences(rr.End),
        FullColumnRangeRefNode fcr => !fcr.IsStartAbsolute || !fcr.IsEndAbsolute,
        FullRowRangeRefNode frr => !frr.IsStartAbsolute || !frr.IsEndAbsolute,
        BinaryOpNode bin => HasRelativeReferences(bin.Left) || HasRelativeReferences(bin.Right),
        UnaryOpNode un => HasRelativeReferences(un.Operand),
        FunctionCallNode fn => HasRelativeReferences(fn.Arguments),
        UnionNode union => HasRelativeReferences(union.Areas),
        IntersectionNode ix => HasRelativeReferences(ix.Left) || HasRelativeReferences(ix.Right),
        NamedRangeEndpointNode nre => HasRelativeReferences(nre.Start) || HasRelativeReferences(nre.End),
        _ => false
    };

    private static bool HasRelativeReferences(IReadOnlyList<FormulaNode> nodes)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            if (HasRelativeReferences(nodes[i]))
                return true;
        }
        return false;
    }

    // ── AST shift ────────────────────────────────────────────────────────────

    private static FormulaNode ShiftAst(FormulaNode node, int dr, int dc) => node switch
    {
        CellRefNode cr => ShiftCellRefOrError(cr, dr, dc),
        RangeRefNode rr => ShiftRangeRef(rr, dr, dc),
        FullColumnRangeRefNode fcr => ShiftFullColumnRangeRef(fcr, dc),
        FullRowRangeRefNode frr => ShiftFullRowRangeRef(frr, dr),
        BinaryOpNode bin => ShiftBinaryOp(bin, dr, dc),
        UnaryOpNode un => ShiftUnaryOp(un, dr, dc),
        FunctionCallNode fn => ShiftFunctionCall(fn, dr, dc),
        UnionNode union => ShiftUnion(union, dr, dc),
        IntersectionNode ix => ShiftIntersection(ix, dr, dc),
        NamedRangeEndpointNode nre => ShiftNamedRangeEndpoint(nre, dr, dc),
        _ => node
    };

    private static FormulaNode ShiftUnion(UnionNode node, int dr, int dc)
    {
        List<FormulaNode>? shiftedAreas = null;
        for (var i = 0; i < node.Areas.Count; i++)
        {
            var original = node.Areas[i];
            var shifted = ShiftAst(original, dr, dc);
            if (shiftedAreas is not null)
            {
                shiftedAreas.Add(shifted);
                continue;
            }
            if (ReferenceEquals(shifted, original))
                continue;

            shiftedAreas = new List<FormulaNode>(node.Areas.Count);
            for (var j = 0; j < i; j++)
                shiftedAreas.Add(node.Areas[j]);
            shiftedAreas.Add(shifted);
        }
        return shiftedAreas is null ? node : node with { Areas = shiftedAreas };
    }

    private static FormulaNode ShiftIntersection(IntersectionNode node, int dr, int dc)
    {
        var left = ShiftAst(node.Left, dr, dc);
        var right = ShiftAst(node.Right, dr, dc);
        return ReferenceEquals(left, node.Left) && ReferenceEquals(right, node.Right)
            ? node
            : node with { Left = left, Right = right };
    }

    private static FormulaNode ShiftNamedRangeEndpoint(NamedRangeEndpointNode node, int dr, int dc)
    {
        var start = ShiftAst(node.Start, dr, dc);
        var end = ShiftAst(node.End, dr, dc);
        return ReferenceEquals(start, node.Start) && ReferenceEquals(end, node.End)
            ? node
            : node with { Start = start, End = end };
    }

    private static FormulaNode ShiftBinaryOp(BinaryOpNode node, int dr, int dc)
    {
        var left = ShiftAst(node.Left, dr, dc);
        var right = ShiftAst(node.Right, dr, dc);
        return ReferenceEquals(left, node.Left) && ReferenceEquals(right, node.Right)
            ? node
            : node with { Left = left, Right = right };
    }

    private static FormulaNode ShiftUnaryOp(UnaryOpNode node, int dr, int dc)
    {
        var operand = ShiftAst(node.Operand, dr, dc);
        return ReferenceEquals(operand, node.Operand) ? node : node with { Operand = operand };
    }

    private static FormulaNode ShiftFunctionCall(FunctionCallNode node, int dr, int dc)
    {
        List<FormulaNode>? shiftedArgs = null;
        for (var i = 0; i < node.Arguments.Count; i++)
        {
            var original = node.Arguments[i];
            var shifted = ShiftAst(original, dr, dc);
            if (shiftedArgs is not null)
            {
                shiftedArgs.Add(shifted);
                continue;
            }
            if (ReferenceEquals(shifted, original))
                continue;

            shiftedArgs = new List<FormulaNode>(node.Arguments.Count);
            for (var j = 0; j < i; j++)
                shiftedArgs.Add(node.Arguments[j]);
            shiftedArgs.Add(shifted);
        }
        return shiftedArgs is null ? node : node with { Arguments = shiftedArgs };
    }

    private static FormulaNode ShiftRangeRef(RangeRefNode rr, int dr, int dc)
    {
        var start = ShiftCellRefOrError(rr.Start, dr, dc);
        if (start is ErrorNode) return start;

        var end = ShiftCellRefOrError(rr.End, dr, dc);
        if (end is ErrorNode) return end;

        if (ReferenceEquals(start, rr.Start) && ReferenceEquals(end, rr.End))
            return rr;

        return rr with { Start = (CellRefNode)start, End = (CellRefNode)end };
    }

    private static FormulaNode ShiftFullColumnRangeRef(FullColumnRangeRefNode range, int dc)
    {
        if (range.IsStartAbsolute && range.IsEndAbsolute)
            return range;

        var start = ShiftCol(range.StartColumnNumber, range.IsStartAbsolute, dc);
        if (!start.HasValue) return new ErrorNode(ErrorValue.Ref);

        var end = ShiftCol(range.EndColumnNumber, range.IsEndAbsolute, dc);
        if (!end.HasValue) return new ErrorNode(ErrorValue.Ref);

        var startName = range.IsStartAbsolute ? range.StartColumnName : CellAddress.NumberToColumnName(start.Value);
        var endName   = range.IsEndAbsolute   ? range.EndColumnName   : CellAddress.NumberToColumnName(end.Value);
        if (startName == range.StartColumnName && endName == range.EndColumnName)
            return range;

        // Must construct a fresh FullColumnRangeRefNode via its constructor rather than
        // `range with { StartColumnName = ..., EndColumnName = ... }`. StartColumnNumber/
        // EndColumnNumber are properties with field initializers computed from
        // StartColumnName/EndColumnName — under a `with` expression the compiler-generated
        // copy constructor copies the already-computed backing fields verbatim and does NOT
        // re-run the initializers, so a `with` that changes the column names would silently
        // leave StartColumnNumber/EndColumnNumber stale at the old, unshifted values.
        return new FullColumnRangeRefNode(
            startName,
            endName,
            range.IsStartAbsolute,
            range.IsEndAbsolute,
            range.SheetName);
    }

    private static FormulaNode ShiftFullRowRangeRef(FullRowRangeRefNode range, int dr)
    {
        if (range.IsStartAbsolute && range.IsEndAbsolute)
            return range;

        var start = ShiftRow(range.StartRow, range.IsStartAbsolute, dr);
        if (!start.HasValue) return new ErrorNode(ErrorValue.Ref);

        var end = ShiftRow(range.EndRow, range.IsEndAbsolute, dr);
        if (!end.HasValue) return new ErrorNode(ErrorValue.Ref);

        if (start.Value == range.StartRow && end.Value == range.EndRow)
            return range;

        return range with { StartRow = start.Value, EndRow = end.Value };
    }

    private static FormulaNode ShiftCellRefOrError(CellRefNode cr, int dr, int dc)
    {
        if (cr.IsRowAbsolute && cr.IsColAbsolute)
            return cr;

        var newRow = ShiftRow(cr.Row, cr.IsRowAbsolute, dr);
        if (!newRow.HasValue) return new ErrorNode(ErrorValue.Ref);

        var newCol = ShiftCol(cr.ColumnNumber, cr.IsColAbsolute, dc);
        if (!newCol.HasValue) return new ErrorNode(ErrorValue.Ref);

        var newColName = cr.IsColAbsolute ? cr.ColumnName : CellAddress.NumberToColumnName(newCol.Value);
        if (newRow.Value == cr.Row && newColName == cr.ColumnName)
            return cr;

        // Must construct a fresh CellRefNode via its constructor rather than `cr with { ... }`.
        // CellRefNode.ColumnNumber is a property with a field initializer computed from
        // ColumnName — under a `with` expression the compiler-generated copy constructor
        // copies the already-computed backing field verbatim and does NOT re-run the
        // initializer, so a `with` that changes ColumnName (as a relative-column shift does)
        // would silently leave ColumnNumber stale at the old, unshifted value.
        return new CellRefNode(
            newColName,
            newRow.Value,
            cr.IsColAbsolute,
            cr.IsRowAbsolute,
            cr.SheetName);
    }

    private static uint? ShiftRow(uint row, bool isAbsolute, int dr)
    {
        if (isAbsolute) return row;
        var shifted = (long)row + dr;
        return shifted is < 1 or > CellAddress.MaxRow ? null : (uint)shifted;
    }

    private static uint? ShiftCol(uint col, bool isAbsolute, int dc)
    {
        if (isAbsolute) return col;
        var shifted = (long)col + dc;
        return shifted is < 1 or > CellAddress.MaxCol ? null : (uint)shifted;
    }
}
