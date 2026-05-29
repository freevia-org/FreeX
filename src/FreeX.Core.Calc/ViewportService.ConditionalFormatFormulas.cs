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
            var ast = GetShiftedCfFormula(formulaCache, dr, dc);

            var result = _cfEvaluator.Evaluate(ast, sheet, workbook);
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

    private static FormulaNode GetShiftedCfFormula(CfFormulaCache formulaCache, int dr, int dc)
    {
        if (dr == 0 && dc == 0)
            return formulaCache.Ast;

        var key = (dr, dc);
        if (!formulaCache.ShiftedAsts.TryGetValue(key, out var shifted))
        {
            shifted = ShiftAst(formulaCache.Ast, dr, dc);
            formulaCache.ShiftedAsts[key] = shifted;
        }

        return shifted;
    }

    private static FormulaNode ShiftAst(FormulaNode node, int dr, int dc)
    {
        return node switch
        {
            CellRefNode cr => ShiftCellRef(cr, dr, dc),
            RangeRefNode rr => ShiftRangeRef(rr, dr, dc),
            FullColumnRangeRefNode fcr => ShiftFullColumnRangeRef(fcr, dc),
            FullRowRangeRefNode frr => ShiftFullRowRangeRef(frr, dr),
            BinaryOpNode bin => bin with
            {
                Left = ShiftAst(bin.Left, dr, dc),
                Right = ShiftAst(bin.Right, dr, dc)
            },
            UnaryOpNode un => un with { Operand = ShiftAst(un.Operand, dr, dc) },
            FunctionCallNode fn => fn with
            {
                Arguments = fn.Arguments.Select(a => ShiftAst(a, dr, dc)).ToList()
            },
            _ => node
        };
    }

    private static FormulaNode ShiftRangeRef(RangeRefNode rr, int dr, int dc)
    {
        var start = ShiftCellRefOrError(rr.Start, dr, dc);
        if (start is ErrorNode) return start;

        var end = ShiftCellRefOrError(rr.End, dr, dc);
        if (end is ErrorNode) return end;

        return rr with
        {
            Start = (CellRefNode)start,
            End = (CellRefNode)end
        };
    }

    private static FormulaNode ShiftFullColumnRangeRef(FullColumnRangeRefNode range, int dc)
    {
        var start = ShiftColumn(range.StartColumnNumber, range.IsStartAbsolute, dc);
        if (!start.HasValue) return new ErrorNode(ErrorValue.Ref);

        var end = ShiftColumn(range.EndColumnNumber, range.IsEndAbsolute, dc);
        if (!end.HasValue) return new ErrorNode(ErrorValue.Ref);

        return range with
        {
            StartColumnName = range.IsStartAbsolute ? range.StartColumnName : CellAddress.NumberToColumnName(start.Value),
            EndColumnName = range.IsEndAbsolute ? range.EndColumnName : CellAddress.NumberToColumnName(end.Value)
        };
    }

    private static FormulaNode ShiftFullRowRangeRef(FullRowRangeRefNode range, int dr)
    {
        var start = ShiftRow(range.StartRow, range.IsStartAbsolute, dr);
        if (!start.HasValue) return new ErrorNode(ErrorValue.Ref);

        var end = ShiftRow(range.EndRow, range.IsEndAbsolute, dr);
        if (!end.HasValue) return new ErrorNode(ErrorValue.Ref);

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
        var newRow = ShiftRow(cr.Row, cr.IsRowAbsolute, dr);
        if (!newRow.HasValue) return new ErrorNode(ErrorValue.Ref);

        var newColNum = ShiftColumn(cr.ColumnNumber, cr.IsColAbsolute, dc);
        if (!newColNum.HasValue) return new ErrorNode(ErrorValue.Ref);

        var newColName = cr.IsColAbsolute ? cr.ColumnName : CellAddress.NumberToColumnName(newColNum.Value);
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
