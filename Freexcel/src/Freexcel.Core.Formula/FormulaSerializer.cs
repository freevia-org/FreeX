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

            case FullColumnRangeRefNode fcr:
                WriteFullColumnRangeRef(fcr, sb);
                break;

            case FullRowRangeRefNode frr:
                WriteFullRowRangeRef(frr, sb);
                break;

            case NamedRangeNode nr:
                sb.Append(nr.Name);
                break;

            case StructuredReferenceNode sr:
                sb.Append(sr.TableName);
                sb.Append('[');
                sb.Append(sr.ColumnName.Contains('[')
                    ? sr.ColumnName
                    : sr.ColumnName.Replace("]", "]]"));
                sb.Append(']');
                break;

            case StructuredCurrentRowReferenceNode current:
                if (!string.IsNullOrWhiteSpace(current.TableName))
                    sb.Append(current.TableName);
                sb.Append("[@");
                sb.Append(current.ColumnName.Replace("]", "]]"));
                sb.Append(']');
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
                // ^ is right-associative: (2^3)^4 needs parens on the LHS to override the natural
                // right-to-left grouping; left-associative -, / never need LHS parens.
                bool lhsNeedsParens = bin.Operator is BinaryOperator.Power;
                WriteSubExpr(bin.Left, GetPrecedence(bin.Operator), lhsNeedsParens, sb);
                sb.Append(OpSymbols[bin.Operator]);
                WriteSubExpr(bin.Right, GetPrecedence(bin.Operator), bin.Operator is BinaryOperator.Subtract or BinaryOperator.Divide, sb);
                break;

            case UnaryOpNode u when u.Operator == UnaryOperator.Negate:
                sb.Append('-');
                if (u.Operand is BinaryOpNode)
                {
                    sb.Append('(');
                    WriteNode(u.Operand, sb);
                    sb.Append(')');
                }
                else
                {
                    WriteNode(u.Operand, sb);
                }
                break;

            case UnaryOpNode u when u.Operator == UnaryOperator.Percent:
                if (u.Operand is BinaryOpNode)
                {
                    sb.Append('(');
                    WriteNode(u.Operand, sb);
                    sb.Append(')');
                }
                else
                {
                    WriteNode(u.Operand, sb);
                }
                sb.Append('%');
                break;
        }
    }

    private static int GetPrecedence(BinaryOperator op) => op switch
    {
        BinaryOperator.Power                                         => 5,
        BinaryOperator.Multiply or BinaryOperator.Divide            => 4,
        BinaryOperator.Add or BinaryOperator.Subtract               => 3,
        BinaryOperator.Concatenate                                   => 2,
        _                                                            => 1,  // comparisons
    };

    private static void WriteSubExpr(FormulaNode node, int parentPrecedence, bool parentIsNonCommutative, StringBuilder sb)
    {
        if (node is BinaryOpNode child)
        {
            var childPrec = GetPrecedence(child.Operator);
            bool needsParens = childPrec < parentPrecedence
                || (parentIsNonCommutative && childPrec == parentPrecedence);
            if (needsParens)
            {
                sb.Append('(');
                WriteNode(node, sb);
                sb.Append(')');
                return;
            }
        }
        WriteNode(node, sb);
    }

    private static void WriteCellRef(CellRefNode cr, StringBuilder sb)
    {
        if (cr.SheetName is not null)
        {
            WriteSheetName(cr.SheetName, sb);
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
            WriteSheetName(sheetName, sb);
            sb.Append('!');
        }
        // Write start without its SheetName prefix (already written above)
        WriteRefPart(rr.Start, sb);
        sb.Append(':');
        WriteRefPart(rr.End, sb);
    }

    private static void WriteFullColumnRangeRef(FullColumnRangeRefNode fcr, StringBuilder sb)
    {
        if (fcr.SheetName is not null)
        {
            WriteSheetName(fcr.SheetName, sb);
            sb.Append('!');
        }
        if (fcr.IsStartAbsolute) sb.Append('$');
        sb.Append(fcr.StartColumnName);
        sb.Append(':');
        if (fcr.IsEndAbsolute) sb.Append('$');
        sb.Append(fcr.EndColumnName);
    }

    private static void WriteFullRowRangeRef(FullRowRangeRefNode frr, StringBuilder sb)
    {
        if (frr.SheetName is not null)
        {
            WriteSheetName(frr.SheetName, sb);
            sb.Append('!');
        }
        if (frr.IsStartAbsolute) sb.Append('$');
        sb.Append(frr.StartRow);
        sb.Append(':');
        if (frr.IsEndAbsolute) sb.Append('$');
        sb.Append(frr.EndRow);
    }

    private static void WriteRefPart(CellRefNode cr, StringBuilder sb)
    {
        if (cr.IsColAbsolute) sb.Append('$');
        sb.Append(cr.ColumnName);
        if (cr.IsRowAbsolute) sb.Append('$');
        sb.Append(cr.Row);
    }

    private static void WriteSheetName(string sheetName, StringBuilder sb)
    {
        if (RequiresQuoting(sheetName))
        {
            sb.Append('\'');
            sb.Append(sheetName.Replace("'", "''"));
            sb.Append('\'');
            return;
        }

        sb.Append(sheetName);
    }

    private static bool RequiresQuoting(string sheetName)
    {
        if (sheetName.Length == 0)
            return true;
        // Sheet names starting with a digit must be quoted: "1Q24!A1" would lex as number "1" + name "Q24"
        if (char.IsDigit(sheetName[0]))
            return true;
        return sheetName.Any(ch => !char.IsLetterOrDigit(ch) && ch != '_' && ch != '.');
    }
}
