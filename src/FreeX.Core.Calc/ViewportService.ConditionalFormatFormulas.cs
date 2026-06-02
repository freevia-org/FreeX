using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Calc;

public sealed partial class ViewportService
{
    private static readonly FormulaEvaluator _cfEvaluator = new();

    private static bool MatchesFormula(
        ConditionalFormat cf,
        Sheet sheet,
        CellAddress addr,
        Workbook workbook,
        CfEvaluationContext cfContext)
    {
        if (string.IsNullOrWhiteSpace(cf.FormulaText)) return false;
        if (!cfContext.Formulas.TryGetValue(cf, out var formulaCache)) return false;

        try
        {
            // Shift relative references from the CF range's top-left to the current cell.
            int dr = (int)addr.Row - (int)cf.AppliesTo.Start.Row;
            int dc = (int)addr.Col - (int)cf.AppliesTo.Start.Col;
            if (formulaCache.SimpleComparison is { } simpleComparison)
                return MatchesSimpleComparison(simpleComparison, sheet, workbook, dr, dc);
            if (formulaCache.SimpleAnd is { } simpleAnd)
                return MatchesSimpleAnd(simpleAnd, sheet, workbook, dr, dc);

            var ast = GetShiftedCfFormula(formulaCache, dr, dc);

            var result = _cfEvaluator.Evaluate(ast, sheet, workbook, addr);
            return result switch
            {
                BoolValue bv => bv.Value,
                NumberValue nv => nv.Value != 0,
                _ => false
            };
        }
        catch
        {
            return false;
        }
    }

    private static bool MatchesSimpleComparison(
        CfSimpleFormulaComparison comparison,
        Sheet sheet,
        Workbook workbook,
        int dr,
        int dc)
    {
        if (!TryResolveSimpleOperand(comparison.Left, sheet, workbook, dr, dc, out var left) ||
            !TryResolveSimpleOperand(comparison.Right, sheet, workbook, dr, dc, out var right))
            return false;

        if (left is ErrorValue || right is ErrorValue)
            return false;

        var cmp = CompareSimpleValues(left, right);
        return comparison.Operator switch
        {
            BinaryOperator.Equal => cmp == 0,
            BinaryOperator.NotEqual => cmp != 0,
            BinaryOperator.LessThan => cmp < 0,
            BinaryOperator.GreaterThan => cmp > 0,
            BinaryOperator.LessOrEqual => cmp <= 0,
            BinaryOperator.GreaterOrEqual => cmp >= 0,
            _ => false
        };
    }

    private static bool MatchesSimpleAnd(
        CfSimpleFormulaAnd simpleAnd,
        Sheet sheet,
        Workbook workbook,
        int dr,
        int dc)
    {
        var comparisons = simpleAnd.Comparisons;
        for (var i = 0; i < comparisons.Length; i++)
        {
            if (!MatchesSimpleComparison(comparisons[i], sheet, workbook, dr, dc))
                return false;
        }

        return true;
    }

    private static bool TryResolveSimpleOperand(
        CfFormulaScalarOperand operand,
        Sheet sheet,
        Workbook workbook,
        int dr,
        int dc,
        out ScalarValue value)
    {
        if (operand.Kind == CfFormulaScalarOperandKind.Literal)
        {
            value = operand.Literal ?? BlankValue.Instance;
            return true;
        }

        var row = ShiftRow(operand.Row, operand.IsRowAbsolute, dr);
        var col = ShiftColumn(operand.Col, operand.IsColAbsolute, dc);
        if (!row.HasValue || !col.HasValue)
        {
            value = ErrorValue.Ref;
            return false;
        }

        var targetSheet = operand.SheetName is null ? sheet : workbook.GetSheet(operand.SheetName);
        if (targetSheet is null)
        {
            value = ErrorValue.Ref;
            return false;
        }

        value = targetSheet.GetValue(row.Value, col.Value);
        return true;
    }

    private static int CompareSimpleValues(ScalarValue left, ScalarValue right)
    {
        var leftIsNumber = left is NumberValue or DateTimeValue;
        var rightIsNumber = right is NumberValue or DateTimeValue;
        if (leftIsNumber && rightIsNumber)
            return GetNumber(left).CompareTo(GetNumber(right));

        if (left is TextValue leftText && right is TextValue rightText)
            return string.Compare(leftText.Value, rightText.Value, StringComparison.OrdinalIgnoreCase);

        if (left is BoolValue leftBool && right is BoolValue rightBool)
            return leftBool.Value.CompareTo(rightBool.Value);

        return SimpleValueTypeOrder(left).CompareTo(SimpleValueTypeOrder(right));
    }

    private static double GetNumber(ScalarValue value) =>
        value is DateTimeValue date ? date.Value : ((NumberValue)value).Value;

    private static int SimpleValueTypeOrder(ScalarValue value) => value switch
    {
        BlankValue => 0,
        NumberValue or DateTimeValue => 1,
        TextValue => 2,
        BoolValue => 3,
        _ => 4
    };

    private static FormulaNode GetShiftedCfFormula(CfFormulaCache formulaCache, int dr, int dc)
    {
        if ((dr == 0 && dc == 0) || !formulaCache.HasRelativeReferences)
            return formulaCache.Ast;

        return ShiftAst(formulaCache.Ast, dr, dc);
    }

    private static FormulaNode ShiftAst(FormulaNode node, int dr, int dc)
    {
        return node switch
        {
            CellRefNode cr => ShiftCellRef(cr, dr, dc),
            RangeRefNode rr => ShiftRangeRef(rr, dr, dc),
            FullColumnRangeRefNode fcr => ShiftFullColumnRangeRef(fcr, dc),
            FullRowRangeRefNode frr => ShiftFullRowRangeRef(frr, dr),
            BinaryOpNode bin => ShiftBinaryOp(bin, dr, dc),
            UnaryOpNode un => ShiftUnaryOp(un, dr, dc),
            FunctionCallNode fn => ShiftFunctionCall(fn, dr, dc),
            _ => node
        };
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
        return ReferenceEquals(operand, node.Operand)
            ? node
            : node with { Operand = operand };
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

        return shiftedArgs is null
            ? node
            : node with { Arguments = shiftedArgs };
    }

    private static FormulaNode ShiftRangeRef(RangeRefNode rr, int dr, int dc)
    {
        var start = ShiftCellRefOrError(rr.Start, dr, dc);
        if (start is ErrorNode) return start;

        var end = ShiftCellRefOrError(rr.End, dr, dc);
        if (end is ErrorNode) return end;

        if (ReferenceEquals(start, rr.Start) && ReferenceEquals(end, rr.End))
            return rr;

        return rr with
        {
            Start = (CellRefNode)start,
            End = (CellRefNode)end
        };
    }

    private static FormulaNode ShiftFullColumnRangeRef(FullColumnRangeRefNode range, int dc)
    {
        if (range.IsStartAbsolute && range.IsEndAbsolute)
            return range;

        var start = ShiftColumn(range.StartColumnNumber, range.IsStartAbsolute, dc);
        if (!start.HasValue) return new ErrorNode(ErrorValue.Ref);

        var end = ShiftColumn(range.EndColumnNumber, range.IsEndAbsolute, dc);
        if (!end.HasValue) return new ErrorNode(ErrorValue.Ref);

        var startName = range.IsStartAbsolute ? range.StartColumnName : CellAddress.NumberToColumnName(start.Value);
        var endName = range.IsEndAbsolute ? range.EndColumnName : CellAddress.NumberToColumnName(end.Value);
        if (startName == range.StartColumnName && endName == range.EndColumnName)
            return range;

        return range with
        {
            StartColumnName = startName,
            EndColumnName = endName
        };
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

        return range with
        {
            StartRow = start.Value,
            EndRow = end.Value
        };
    }

    private static FormulaNode ShiftCellRef(CellRefNode cr, int dr, int dc) =>
        ShiftCellRefOrError(cr, dr, dc);

    private static FormulaNode ShiftCellRefOrError(CellRefNode cr, int dr, int dc)
    {
        if (cr.IsRowAbsolute && cr.IsColAbsolute)
            return cr;

        var newRow = ShiftRow(cr.Row, cr.IsRowAbsolute, dr);
        if (!newRow.HasValue) return new ErrorNode(ErrorValue.Ref);

        var newColNum = ShiftColumn(cr.ColumnNumber, cr.IsColAbsolute, dc);
        if (!newColNum.HasValue) return new ErrorNode(ErrorValue.Ref);

        var newColName = cr.IsColAbsolute ? cr.ColumnName : CellAddress.NumberToColumnName(newColNum.Value);
        if (newRow.Value == cr.Row && newColName == cr.ColumnName)
            return cr;

        return cr with { Row = newRow.Value, ColumnName = newColName };
    }

    private static uint? ShiftRow(uint row, bool isAbsolute, int dr)
    {
        if (isAbsolute)
            return row;

        var shifted = (long)row + dr;
        return shifted is < 1 or > CellAddress.MaxRow ? null : (uint)shifted;
    }

    private static uint? ShiftColumn(uint col, bool isAbsolute, int dc)
    {
        if (isAbsolute)
            return col;

        var shifted = (long)col + dc;
        return shifted is < 1 or > CellAddress.MaxCol ? null : (uint)shifted;
    }
}
