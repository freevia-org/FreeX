using FreeX.Core.Model;

namespace FreeX.Core.Formula;

internal static class FormulaAstReferenceShifter
{
    internal static FormulaNode ShiftForCell(
        FormulaNode ast,
        CellAddress anchor,
        CellAddress current,
        bool? hasRelativeReferences = null)
    {
        var rowDelta = (int)current.Row - (int)anchor.Row;
        var columnDelta = (int)current.Col - (int)anchor.Col;
        if ((rowDelta == 0 && columnDelta == 0) ||
            !(hasRelativeReferences ?? HasRelativeReferences(ast)))
        {
            return ast;
        }

        return Shift(ast, rowDelta, columnDelta);
    }

    internal static bool HasRelativeReferences(FormulaNode node) => node switch
    {
        CellRefNode cell => !cell.IsColAbsolute || !cell.IsRowAbsolute,
        RangeRefNode range => HasRelativeReferences(range.Start) || HasRelativeReferences(range.End),
        FullColumnRangeRefNode range => !range.IsStartAbsolute || !range.IsEndAbsolute,
        FullRowRangeRefNode range => !range.IsStartAbsolute || !range.IsEndAbsolute,
        BinaryOpNode binary => HasRelativeReferences(binary.Left) || HasRelativeReferences(binary.Right),
        UnaryOpNode unary => HasRelativeReferences(unary.Operand),
        FunctionCallNode function => HasRelativeReferences(function.Arguments),
        UnionNode union => HasRelativeReferences(union.Areas),
        IntersectionNode intersection =>
            HasRelativeReferences(intersection.Left) || HasRelativeReferences(intersection.Right),
        NamedRangeEndpointNode endpoint =>
            HasRelativeReferences(endpoint.Start) || HasRelativeReferences(endpoint.End),
        _ => false
    };

    internal static uint? ShiftRow(uint row, bool isAbsolute, int delta) =>
        ShiftCoordinate(row, isAbsolute, delta, CellAddress.MaxRow);

    internal static uint? ShiftColumn(uint column, bool isAbsolute, int delta) =>
        ShiftCoordinate(column, isAbsolute, delta, CellAddress.MaxCol);

    private static bool HasRelativeReferences(IReadOnlyList<FormulaNode> nodes)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            if (HasRelativeReferences(nodes[i]))
                return true;
        }

        return false;
    }

    private static FormulaNode Shift(FormulaNode node, int rowDelta, int columnDelta) => node switch
    {
        CellRefNode cell => ShiftCellReference(cell, rowDelta, columnDelta),
        RangeRefNode range => ShiftRangeReference(range, rowDelta, columnDelta),
        FullColumnRangeRefNode range => ShiftFullColumnRangeReference(range, columnDelta),
        FullRowRangeRefNode range => ShiftFullRowRangeReference(range, rowDelta),
        BinaryOpNode binary => ShiftBinary(binary, rowDelta, columnDelta),
        UnaryOpNode unary => ShiftUnary(unary, rowDelta, columnDelta),
        FunctionCallNode function => ShiftFunction(function, rowDelta, columnDelta),
        UnionNode union => ShiftUnion(union, rowDelta, columnDelta),
        IntersectionNode intersection => ShiftIntersection(intersection, rowDelta, columnDelta),
        NamedRangeEndpointNode endpoint => ShiftNamedRangeEndpoint(endpoint, rowDelta, columnDelta),
        _ => node
    };

    private static FormulaNode ShiftUnion(UnionNode node, int rowDelta, int columnDelta)
    {
        var areas = ShiftList(node.Areas, rowDelta, columnDelta);
        return ReferenceEquals(areas, node.Areas) ? node : node with { Areas = areas };
    }

    private static FormulaNode ShiftIntersection(IntersectionNode node, int rowDelta, int columnDelta)
    {
        var left = Shift(node.Left, rowDelta, columnDelta);
        var right = Shift(node.Right, rowDelta, columnDelta);
        return ReferenceEquals(left, node.Left) && ReferenceEquals(right, node.Right)
            ? node
            : node with { Left = left, Right = right };
    }

    private static FormulaNode ShiftNamedRangeEndpoint(
        NamedRangeEndpointNode node,
        int rowDelta,
        int columnDelta)
    {
        var start = Shift(node.Start, rowDelta, columnDelta);
        var end = Shift(node.End, rowDelta, columnDelta);
        return ReferenceEquals(start, node.Start) && ReferenceEquals(end, node.End)
            ? node
            : node with { Start = start, End = end };
    }

    private static FormulaNode ShiftBinary(BinaryOpNode node, int rowDelta, int columnDelta)
    {
        var left = Shift(node.Left, rowDelta, columnDelta);
        var right = Shift(node.Right, rowDelta, columnDelta);
        return ReferenceEquals(left, node.Left) && ReferenceEquals(right, node.Right)
            ? node
            : node with { Left = left, Right = right };
    }

    private static FormulaNode ShiftUnary(UnaryOpNode node, int rowDelta, int columnDelta)
    {
        var operand = Shift(node.Operand, rowDelta, columnDelta);
        return ReferenceEquals(operand, node.Operand) ? node : node with { Operand = operand };
    }

    private static FormulaNode ShiftFunction(FunctionCallNode node, int rowDelta, int columnDelta)
    {
        var arguments = ShiftList(node.Arguments, rowDelta, columnDelta);
        return ReferenceEquals(arguments, node.Arguments) ? node : node with { Arguments = arguments };
    }

    private static IReadOnlyList<FormulaNode> ShiftList(
        IReadOnlyList<FormulaNode> nodes,
        int rowDelta,
        int columnDelta)
    {
        List<FormulaNode>? shiftedNodes = null;
        for (var i = 0; i < nodes.Count; i++)
        {
            var original = nodes[i];
            var shifted = Shift(original, rowDelta, columnDelta);
            if (shiftedNodes is not null)
            {
                shiftedNodes.Add(shifted);
                continue;
            }

            if (ReferenceEquals(shifted, original))
                continue;

            shiftedNodes = new List<FormulaNode>(nodes.Count);
            for (var j = 0; j < i; j++)
                shiftedNodes.Add(nodes[j]);
            shiftedNodes.Add(shifted);
        }

        return shiftedNodes ?? nodes;
    }

    private static FormulaNode ShiftRangeReference(RangeRefNode range, int rowDelta, int columnDelta)
    {
        var start = ShiftCellReference(range.Start, rowDelta, columnDelta);
        if (start is ErrorNode)
            return start;

        var end = ShiftCellReference(range.End, rowDelta, columnDelta);
        if (end is ErrorNode)
            return end;

        return ReferenceEquals(start, range.Start) && ReferenceEquals(end, range.End)
            ? range
            : range with { Start = (CellRefNode)start, End = (CellRefNode)end };
    }

    private static FormulaNode ShiftFullColumnRangeReference(FullColumnRangeRefNode range, int delta)
    {
        if (range.IsStartAbsolute && range.IsEndAbsolute)
            return range;

        var start = ShiftColumn(range.StartColumnNumber, range.IsStartAbsolute, delta);
        var end = ShiftColumn(range.EndColumnNumber, range.IsEndAbsolute, delta);
        if (!start.HasValue || !end.HasValue)
            return new ErrorNode(ErrorValue.Ref);

        var startName = range.IsStartAbsolute
            ? range.StartColumnName
            : CellAddress.NumberToColumnName(start.Value);
        var endName = range.IsEndAbsolute
            ? range.EndColumnName
            : CellAddress.NumberToColumnName(end.Value);
        if (startName == range.StartColumnName && endName == range.EndColumnName)
            return range;

        return new FullColumnRangeRefNode(
            startName,
            endName,
            range.IsStartAbsolute,
            range.IsEndAbsolute,
            range.SheetName);
    }

    private static FormulaNode ShiftFullRowRangeReference(FullRowRangeRefNode range, int delta)
    {
        if (range.IsStartAbsolute && range.IsEndAbsolute)
            return range;

        var start = ShiftRow(range.StartRow, range.IsStartAbsolute, delta);
        var end = ShiftRow(range.EndRow, range.IsEndAbsolute, delta);
        if (!start.HasValue || !end.HasValue)
            return new ErrorNode(ErrorValue.Ref);

        return start.Value == range.StartRow && end.Value == range.EndRow
            ? range
            : range with { StartRow = start.Value, EndRow = end.Value };
    }

    private static FormulaNode ShiftCellReference(CellRefNode cell, int rowDelta, int columnDelta)
    {
        if (cell.IsRowAbsolute && cell.IsColAbsolute)
            return cell;

        var row = ShiftRow(cell.Row, cell.IsRowAbsolute, rowDelta);
        var column = ShiftColumn(cell.ColumnNumber, cell.IsColAbsolute, columnDelta);
        if (!row.HasValue || !column.HasValue)
            return new ErrorNode(ErrorValue.Ref);

        var columnName = cell.IsColAbsolute
            ? cell.ColumnName
            : CellAddress.NumberToColumnName(column.Value);
        if (row.Value == cell.Row && columnName == cell.ColumnName)
            return cell;

        return new CellRefNode(
            columnName,
            row.Value,
            cell.IsColAbsolute,
            cell.IsRowAbsolute,
            cell.SheetName);
    }

    private static uint? ShiftCoordinate(uint coordinate, bool isAbsolute, int delta, uint maximum)
    {
        if (isAbsolute)
            return coordinate;

        var shifted = (long)coordinate + delta;
        return shifted is < 1 || shifted > maximum ? null : (uint)shifted;
    }
}
