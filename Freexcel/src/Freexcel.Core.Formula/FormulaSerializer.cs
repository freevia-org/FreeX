using System.Globalization;
using System.Text;
using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

/// <summary>
/// Converts a formula AST back to a formula string (without leading '=').
/// This is the inverse of Parser and is used by FormulaRewriter.
/// </summary>
public static class FormulaSerializer
{
    private static readonly Dictionary<BinaryOperator, string> OpSymbols = new()
    {
        [BinaryOperator.Add]            = "+",
        [BinaryOperator.Subtract]       = "-",
        [BinaryOperator.Multiply]       = "*",
        [BinaryOperator.Divide]         = "/",
        [BinaryOperator.Power]          = "^",
        [BinaryOperator.Concatenate]    = "&",
        [BinaryOperator.Equal]          = "=",
        [BinaryOperator.NotEqual]       = "<>",
        [BinaryOperator.LessThan]       = "<",
        [BinaryOperator.GreaterThan]    = ">",
        [BinaryOperator.LessOrEqual]    = "<=",
        [BinaryOperator.GreaterOrEqual] = ">=",
    };

    public static string Serialize(FormulaNode node)
    {
        var sb = new StringBuilder();
        WriteNode(node, sb);
        return sb.ToString();
    }

    private static void WriteNode(FormulaNode node, StringBuilder sb)
    {
        switch (node)
        {
            case NumberNode n:
                sb.Append(n.Value.ToString(CultureInfo.InvariantCulture));
                break;

            case StringNode s:
                sb.Append('"');
                sb.Append(s.Value.Replace("\"", "\"\""));
                sb.Append('"');
                break;

            case BooleanNode b:
                sb.Append(b.Value ? "TRUE" : "FALSE");
                break;

            case ErrorNode e:
                sb.Append(e.Error.Code);
                break;

            case CellRefNode cr:
                WriteCellRef(cr, sb);
                break;

            case RangeRefNode rr:
                WriteRangeRef(rr, sb);
                break;

            case NamedRangeNode nr:
                sb.Append(nr.Name);
                break;

            case FunctionCallNode f:
                sb.Append(f.FunctionName);
                sb.Append('(');
                for (int i = 0; i < f.Arguments.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    WriteNode(f.Arguments[i], sb);
                }
                sb.Append(')');
                break;

            case BinaryOpNode bin:
                WriteNode(bin.Left, sb);
                sb.Append(OpSymbols[bin.Operator]);
                WriteNode(bin.Right, sb);
                break;

            case UnaryOpNode u when u.Operator == UnaryOperator.Negate:
                sb.Append('-');
                WriteNode(u.Operand, sb);
                break;

            case UnaryOpNode u when u.Operator == UnaryOperator.Percent:
                WriteNode(u.Operand, sb);
                sb.Append('%');
                break;
        }
    }

    private static void WriteCellRef(CellRefNode cr, StringBuilder sb)
    {
        if (cr.SheetName is not null)
        {
            sb.Append(cr.SheetName);
            sb.Append('!');
        }
        if (cr.IsColAbsolute) sb.Append('$');
        sb.Append(cr.ColumnName);
        if (cr.IsRowAbsolute) sb.Append('$');
        sb.Append(cr.Row);
    }

    private static void WriteRangeRef(RangeRefNode rr, StringBuilder sb)
    {
        var sheetName = rr.SheetName ?? rr.Start.SheetName;
        if (sheetName is not null)
        {
            sb.Append(sheetName);
            sb.Append('!');
        }
        // Write start without its SheetName prefix (already written above)
        WriteRefPart(rr.Start, sb);
        sb.Append(':');
        WriteRefPart(rr.End, sb);
    }

    private static void WriteRefPart(CellRefNode cr, StringBuilder sb)
    {
        if (cr.IsColAbsolute) sb.Append('$');
        sb.Append(cr.ColumnName);
        if (cr.IsRowAbsolute) sb.Append('$');
        sb.Append(cr.Row);
    }
}
