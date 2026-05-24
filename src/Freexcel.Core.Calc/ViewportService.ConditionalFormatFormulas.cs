using Freexcel.Core.Formula;
using Freexcel.Core.Model;

namespace Freexcel.Core.Calc;

public sealed partial class ViewportService
{
    private static bool MatchesFormula(ConditionalFormat cf, Sheet sheet, CellAddress addr, Workbook workbook)
    {
        if (string.IsNullOrWhiteSpace(cf.FormulaText)) return false;
        try
        {
            // Shift relative references from the CF range's top-left to the current cell.
            int dr = (int)addr.Row - (int)cf.AppliesTo.Start.Row;
            int dc = (int)addr.Col - (int)cf.AppliesTo.Start.Col;
            var formulaText = dr == 0 && dc == 0
                ? cf.FormulaText
                : ShiftCfFormula(cf.FormulaText, dr, dc);

            var result = _cfEvaluator.Evaluate("=" + formulaText, sheet, workbook);
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

    private static string ShiftCfFormula(string formulaText, int dr, int dc)
    {
        try
        {
            var ast = new Parser(new Lexer("=" + formulaText).Tokenize()).Parse();
            var shifted = ShiftAst(ast, dr, dc);
            return FormulaSerializer.Serialize(shifted);
        }
        catch
        {
            return formulaText;
        }
    }

    private static FormulaNode ShiftAst(FormulaNode node, int dr, int dc)
    {
        return node switch
        {
            CellRefNode cr => ShiftCellRef(cr, dr, dc),
            RangeRefNode rr => rr with
            {
                Start = ShiftCellRef(rr.Start, dr, dc),
                End = ShiftCellRef(rr.End, dr, dc)
            },
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

    private static CellRefNode ShiftCellRef(CellRefNode cr, int dr, int dc)
    {
        uint newRow = cr.IsRowAbsolute ? cr.Row
            : (uint)Math.Max(1, (int)cr.Row + dr);
        uint newColNum = cr.IsColAbsolute ? cr.ColumnNumber
            : (uint)Math.Max(1, (int)cr.ColumnNumber + dc);
        var newColName = cr.IsColAbsolute ? cr.ColumnName
            : CellAddress.NumberToColumnName(newColNum);
        return cr with { Row = newRow, ColumnName = newColName };
    }
}
