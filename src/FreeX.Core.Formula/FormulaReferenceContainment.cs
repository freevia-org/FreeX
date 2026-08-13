using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static class FormulaReferenceContainment
{
    public static bool ContainsUnqualifiedCell(FormulaNode node, CellAddress cell)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node switch
        {
            CellRefNode reference when reference.SheetName is null =>
                reference.Row == cell.Row && reference.ColumnNumber == cell.Col,
            RangeRefNode range when range.SheetName is null =>
                cell.Row >= Math.Min(range.Start.Row, range.End.Row) &&
                cell.Row <= Math.Max(range.Start.Row, range.End.Row) &&
                cell.Col >= Math.Min(range.Start.ColumnNumber, range.End.ColumnNumber) &&
                cell.Col <= Math.Max(range.Start.ColumnNumber, range.End.ColumnNumber),
            FullColumnRangeRefNode range when range.SheetName is null =>
                cell.Col >= Math.Min(range.StartColumnNumber, range.EndColumnNumber) &&
                cell.Col <= Math.Max(range.StartColumnNumber, range.EndColumnNumber),
            FullRowRangeRefNode range when range.SheetName is null =>
                cell.Row >= Math.Min(range.StartRow, range.EndRow) &&
                cell.Row <= Math.Max(range.StartRow, range.EndRow),
            BinaryOpNode binary =>
                ContainsUnqualifiedCell(binary.Left, cell) || ContainsUnqualifiedCell(binary.Right, cell),
            UnaryOpNode unary => ContainsUnqualifiedCell(unary.Operand, cell),
            FunctionCallNode function => ContainsAny(function.Arguments, cell),
            UnionNode union => ContainsAny(union.Areas, cell),
            IntersectionNode intersection =>
                ContainsUnqualifiedCell(intersection.Left, cell) ||
                ContainsUnqualifiedCell(intersection.Right, cell),
            NamedRangeEndpointNode endpoint =>
                ContainsUnqualifiedCell(endpoint.Start, cell) ||
                ContainsUnqualifiedCell(endpoint.End, cell),
            _ => false
        };
    }

    private static bool ContainsAny(IReadOnlyList<FormulaNode> nodes, CellAddress cell)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            if (ContainsUnqualifiedCell(nodes[i], cell))
                return true;
        }

        return false;
    }
}
