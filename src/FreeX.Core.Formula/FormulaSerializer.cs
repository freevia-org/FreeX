using System.Globalization;
using System.Text;
using FreeX.Core.Model;

namespace FreeX.Core.Formula;

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

            case ArrayConstantNode arr:
                sb.Append('{');
                for (int r = 0; r < arr.Rows.Count; r++)
                {
                    if (r > 0) sb.Append(';');
                    var row = arr.Rows[r];
                    for (int c = 0; c < row.Count; c++)
                    {
                        if (c > 0) sb.Append(',');
                        WriteNode(row[c], sb);
                    }
                }
                sb.Append('}');
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
                AppendStructuredReferenceSelector(sr.ColumnName, sb);
                sb.Append(']');
                break;

            case StructuredCurrentRowReferenceNode current:
                if (!string.IsNullOrWhiteSpace(current.TableName))
                    sb.Append(current.TableName);
                sb.Append("[@");
                sb.Append(current.ColumnName.Replace("]", "]]"));
                sb.Append(']');
                break;

            // ANCHORARRAY(ref) is the internal representation of the A1# spill-anchor operator
            // (see Parser.WrapSpillAnchor, which wraps a CellRefNode or a NamedRangeNode) —
            // serialize it back to that literal syntax rather than the function-call form so a
            // formula containing '#' round-trips unchanged through structural rewrites
            // (insert/delete rows or columns). Guard on the argument actually being one of those
            // two shapes so this only rewrites what the parser produces; anything else falls
            // through to the ordinary function-call rendering.
            case FunctionCallNode f when f.FunctionName == "ANCHORARRAY" &&
                                         f.Arguments is [CellRefNode anchorRef]:
                WriteCellRef(anchorRef, sb);
                sb.Append('#');
                break;

            case FunctionCallNode f when f.FunctionName == "ANCHORARRAY" &&
                                         f.Arguments is [NamedRangeNode anchorName]:
                sb.Append(anchorName.Name);
                sb.Append('#');
                break;

            // ANCHORARRAY(ref, end) is the internal representation of the A1#:B5 shape — a spill
            // range used as the start endpoint of a larger range (see Parser.ParsePostfix). Print
            // it back as "<ref>#:<end>" so it round-trips through rewrites the same way the plain
            // spill-anchor shape above does.
            case FunctionCallNode f when f.FunctionName == "ANCHORARRAY" &&
                                         f.Arguments is [CellRefNode rangeAnchorRef, CellRefNode rangeEnd]:
                WriteCellRef(rangeAnchorRef, sb);
                sb.Append("#:");
                WriteCellRef(rangeEnd, sb);
                break;

            case FunctionCallNode f when f.FunctionName == "ANCHORARRAY" &&
                                         f.Arguments is [NamedRangeNode rangeAnchorName, CellRefNode namedRangeEnd]:
                sb.Append(rangeAnchorName.Name);
                sb.Append("#:");
                WriteCellRef(namedRangeEnd, sb);
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

            case UnaryOpNode u when u.Operator == UnaryOperator.ImplicitIntersection:
                sb.Append('@');
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
        if (rr.EndSheetName is not null)
        {
            // 3-D sheet-span reference (e.g. Sheet1:Sheet3!A1 or Sheet1:Sheet3!A1:B5). Excel quotes
            // the whole "Start:End" span as a single token when either name needs quoting (never
            // each name individually) — mirror that here so the Lexer (which reads a quoted
            // SheetQualifier token's embedded ':' as the span separator; see
            // Lexer.ReadQuotedSheetQualifier / Parser's SheetQualifier primary case) round-trips it.
            // Note: this normalizes an unusual "mixed quoting" input (e.g. Sheet1:'Last Sheet'!A1,
            // where only the end name was quoted) to the canonical whole-span-quoted form
            // ('Sheet1:Last Sheet'!A1) rather than preserving the original mixed style — the parsed
            // AST only records each sheet name, not which quoting style produced it, and Excel's own
            // canonical form is exactly this whole-span quoting, so this is the more correct output.
            WriteSheetSpanName(rr.SheetName!, rr.EndSheetName, sb);
            sb.Append('!');
            WriteRefPart(rr.Start, sb);
            if (!rr.IsSingleCellSpan)
            {
                sb.Append(':');
                WriteRefPart(rr.End, sb);
            }
            return;
        }

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

    private static void WriteSheetSpanName(string startSheetName, string endSheetName, StringBuilder sb)
    {
        if (!SheetNameFormatter.NeedsQuoting(startSheetName) && !SheetNameFormatter.NeedsQuoting(endSheetName))
        {
            sb.Append(startSheetName);
            sb.Append(':');
            sb.Append(endSheetName);
            return;
        }

        sb.Append('\'');
        sb.Append(startSheetName.Replace("'", "''", StringComparison.Ordinal));
        sb.Append(':');
        sb.Append(endSheetName.Replace("'", "''", StringComparison.Ordinal));
        sb.Append('\'');
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

    private static void WriteSheetName(string sheetName, StringBuilder sb) =>
        sb.Append(SheetNameFormatter.QuoteIfNeeded(sheetName));

    // R26-table-structured-ref-deep-1: sr.ColumnName is whatever selector text the parser produced
    // (Lexer.ReadStructuredReferenceSelectorSlow already stripped any apostrophe-escapes), so a bare
    // (non-combined) selector such as a literal column name must have its escapable characters
    // ('[', ']', '#', and an apostrophe itself — Lexer.IsEscapableStructuredReferenceChar's exact
    // set) re-escaped with a leading apostrophe before being written back out. Skipping this (the
    // previous behavior) turned a literal column named e.g. "#Data" back into the bare text
    // "#Data", which re-parses as the #Data section keyword instead of the literal column, and left
    // a literal name containing an unescaped '[' unparseable on the next read. A combined/bracketed
    // selector (e.g. "[#Data],[Amount]" or "[Amount]:[Tax]") is written through unescaped instead —
    // it is always wrapped in its own '[...]' pairs by the parser, so it is recognized by (and only
    // by) a leading '[', which no bare selector can start with under this convention. Mirrors
    // StructuredTableTotalsCommand.EscapeStructuredReferenceColumnName, the codebase's other writer
    // of structured-reference selector text.
    private static readonly char[] StructuredReferenceEscapableChars = ['[', ']', '#', '\''];

    // R27-meta-3: the lexer strips apostrophe-escapes on read (ReadStructuredReferenceSelectorSlow),
    // so a genuine "#Data" section keyword and a literal column escaped as "'#Data" both produce the
    // IDENTICAL ColumnName ("#Data") — this layer cannot tell them apart. Blindly re-escaping every
    // bare '#' (the round-26 behavior above) therefore corrupts the overwhelmingly common case — a
    // real #Data/#Headers/#Totals/#All/#This Row keyword reference — into a literal-column escape on
    // every reserialize, changing its meaning in Excel's structured-reference syntax. Recognize the
    // fixed set of section keywords (mirrors StructuredReferenceResolver's own #ALL/#HEADERS/#DATA/
    // #TOTALS/#THIS ROW handling) and pass them through unescaped, keeping the round-26 escaping only
    // for selectors that are NOT a recognized keyword spelling (e.g. a literal name containing '[',
    // ']', or an apostrophe). A literal column named exactly "#Data" is not distinguishable from the
    // keyword at this layer and is treated as the keyword, favoring the common case.
    private static readonly HashSet<string> StructuredReferenceSectionKeywords =
        new(StringComparer.OrdinalIgnoreCase) { "#All", "#Data", "#Headers", "#Totals", "#This Row" };

    private static void AppendStructuredReferenceSelector(string selector, StringBuilder sb)
    {
        if (selector.StartsWith('[') ||
            selector.AsSpan().IndexOfAny(StructuredReferenceEscapableChars) < 0 ||
            StructuredReferenceSectionKeywords.Contains(selector.Trim()))
        {
            sb.Append(selector);
            return;
        }

        foreach (var ch in selector)
        {
            if (ch is '[' or ']' or '#' or '\'')
                sb.Append('\'');
            sb.Append(ch);
        }
    }
}
