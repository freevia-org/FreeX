using System.Globalization;
using System.Text;

namespace FreeW.Core.Model;

/// <summary>
/// A table-cell formula field (Word's Table &gt; Data &gt; Formula): a formula expression such as
/// <c>=SUM(ABOVE)</c> plus an optional number-format picture (e.g. <c>#,##0.00</c>). Carried by a
/// <see cref="Run"/> via <see cref="Run.TableFormula"/> and serialised as a <c>w:fldSimple</c> whose
/// <c>w:instr</c> is <c> =SUM(ABOVE) \# "#,##0.00" </c>. The run's <see cref="Run.Text"/> doubles as the
/// cached/last-computed result so field-unaware consumers still render a value.
/// </summary>
public sealed record TableFormulaField(string Expression, string? NumberFormat = null)
{
    /// <summary>
    /// The formula text without the leading <c>=</c> (Word stores the field instruction with the <c>=</c>,
    /// but the bare expression is convenient for evaluation). Empty when the expression is empty.
    /// </summary>
    public string BareExpression =>
        Expression.TrimStart().StartsWith('=') ? Expression.TrimStart()[1..].Trim() : Expression.Trim();
}

/// <summary>
/// Pure evaluator for Word table-cell formulas. Supports the common Word aggregate functions over a
/// directional cell range — <c>SUM</c>, <c>AVERAGE</c>, <c>COUNT</c>, <c>PRODUCT</c>, <c>MIN</c>,
/// <c>MAX</c> with the <c>ABOVE</c>/<c>LEFT</c>/<c>RIGHT</c>/<c>BELOW</c> directions — plus a plain numeric
/// arithmetic expression (<c>+ - * / ( )</c>). The result is formatted with an optional number-format
/// picture. Numbers are parsed leniently out of cell text (stripping currency/thousands separators), so a
/// cell containing <c>$1,200.50</c> contributes <c>1200.50</c>, matching Word.
/// </summary>
public static class TableFormulaEvaluator
{
    /// <summary>
    /// Evaluates a literal arithmetic expression outside table context. This is the shared calculation
    /// path for Word's ordinary <c>{ = ... }</c> formula field; directional table references remain owned
    /// by <see cref="Evaluate(Table,int,int,TableFormulaField)"/>.
    /// </summary>
    public static string EvaluateLiteralExpression(string expression, string? numberFormat = null)
    {
        try
        {
            return Format(ArithmeticParser.Evaluate(expression.Trim()), numberFormat);
        }
        catch (FormatException)
        {
            return "!Syntax Error";
        }
    }

    /// <summary>
    /// Evaluate <paramref name="formula"/> for the cell at (<paramref name="rowIndex"/>,
    /// <paramref name="columnIndex"/>) of <paramref name="table"/> and format the result. Returns the
    /// formatted result string; on a parse/evaluation error returns Word's literal error marker
    /// <c>!Syntax Error</c> so the field still renders something rather than throwing.
    /// </summary>
    public static string Evaluate(Table table, int rowIndex, int columnIndex, TableFormulaField formula)
    {
        double value;
        try
        {
            value = EvaluateValue(table, rowIndex, columnIndex, formula.BareExpression);
        }
        catch (FormatException)
        {
            return "!Syntax Error";
        }
        return Format(value, formula.NumberFormat);
    }

    /// <summary>
    /// Evaluate the bare formula expression (no leading <c>=</c>) to a numeric value. Recognises a single
    /// directional aggregate function call (e.g. <c>SUM(ABOVE)</c>) or a plain arithmetic expression.
    /// Throws <see cref="FormatException"/> on a syntax error.
    /// </summary>
    public static double EvaluateValue(Table table, int rowIndex, int columnIndex, string expression)
    {
        var expr = expression.Trim();
        if (expr.Length == 0)
            throw new FormatException("Empty formula.");

        // A directional aggregate function: NAME( DIRECTION ). The argument is one of the four directions.
        var open = expr.IndexOf('(');
        if (open > 0 && expr.EndsWith(')'))
        {
            var name = expr[..open].Trim().ToUpperInvariant();
            var arg = expr[(open + 1)..^1].Trim().ToUpperInvariant();
            if (TryDirection(arg, out var direction))
            {
                var cells = Collect(table, rowIndex, columnIndex, direction);
                return Aggregate(name, cells);
            }
        }

        // Otherwise evaluate as a plain arithmetic expression over literal numbers.
        return ArithmeticParser.Evaluate(expr);
    }

    private static bool TryDirection(string arg, out Direction direction)
    {
        switch (arg)
        {
            case "ABOVE": direction = Direction.Above; return true;
            case "BELOW": direction = Direction.Below; return true;
            case "LEFT": direction = Direction.Left; return true;
            case "RIGHT": direction = Direction.Right; return true;
            default: direction = Direction.Above; return false;
        }
    }

    private enum Direction { Above, Below, Left, Right }

    // Collect the numeric cell values in the requested direction from the formula cell (exclusive of it).
    // Matching Word, the run stops at the first non-numeric / empty cell so a header label terminates the
    // range. Numbers that fail to parse contribute nothing (and stop an ABOVE/BELOW/LEFT/RIGHT walk).
    private static List<double> Collect(Table table, int rowIndex, int columnIndex, Direction direction)
    {
        var values = new List<double>();

        void Walk(IEnumerable<(int r, int c)> coords)
        {
            foreach (var (r, c) in coords)
            {
                if (r < 0 || r >= table.Rows.Count)
                    break;
                var cells = table.Rows[r].Cells;
                if (c < 0 || c >= cells.Count)
                    break;
                if (!TryParseCellNumber(cells[c].PlainText, out var n))
                    break; // a non-numeric cell terminates the range (e.g. a header label)
                values.Add(n);
            }
        }

        switch (direction)
        {
            case Direction.Above:
                Walk(Descending(rowIndex - 1).Select(r => (r, columnIndex)));
                break;
            case Direction.Below:
                Walk(Ascending(rowIndex + 1, table.Rows.Count).Select(r => (r, columnIndex)));
                break;
            case Direction.Left:
                Walk(Descending(columnIndex - 1).Select(c => (rowIndex, c)));
                break;
            case Direction.Right:
                var width = rowIndex >= 0 && rowIndex < table.Rows.Count ? table.Rows[rowIndex].Cells.Count : 0;
                Walk(Ascending(columnIndex + 1, width).Select(c => (rowIndex, c)));
                break;
        }
        return values;
    }

    private static IEnumerable<int> Descending(int from)
    {
        for (var i = from; i >= 0; i--)
            yield return i;
    }

    private static IEnumerable<int> Ascending(int from, int exclusiveEnd)
    {
        for (var i = from; i < exclusiveEnd; i++)
            yield return i;
    }

    private static double Aggregate(string name, IReadOnlyList<double> values) => name switch
    {
        "SUM" => values.Sum(),
        "AVERAGE" => values.Count == 0 ? 0 : values.Average(),
        "COUNT" => values.Count,
        "PRODUCT" => values.Count == 0 ? 0 : values.Aggregate(1.0, (a, b) => a * b),
        "MIN" => values.Count == 0 ? 0 : values.Min(),
        "MAX" => values.Count == 0 ? 0 : values.Max(),
        _ => throw new FormatException($"Unknown function '{name}'.")
    };

    /// <summary>
    /// Parse a number out of a cell's text, tolerating currency symbols, thousands separators and
    /// surrounding whitespace (e.g. <c>"$1,200.50"</c> → <c>1200.50</c>). Returns false for empty or
    /// non-numeric cells. Parsing is invariant-culture so round-trips are deterministic.
    /// </summary>
    public static bool TryParseCellNumber(string text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsDigit(ch) || ch is '.' or '-' or '+')
                sb.Append(ch);
            // Skip currency symbols, thousands separators, spaces, percent signs etc.
        }
        var cleaned = sb.ToString();
        return cleaned.Length > 0
            && double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// Format <paramref name="value"/> with the Word number-format picture <paramref name="format"/>
    /// (e.g. <c>#,##0.00</c>, <c>0</c>, <c>0%</c>). A null/empty format renders a general number (no
    /// trailing zeros). Formatting is invariant-culture so results round-trip deterministically.
    /// </summary>
    public static string Format(double value, string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            // General: integers print without a decimal point; otherwise up to ~10 significant digits.
            return value == Math.Truncate(value) && Math.Abs(value) < 1e15
                ? value.ToString("0", CultureInfo.InvariantCulture)
                : value.ToString("0.##########", CultureInfo.InvariantCulture);
        }
        try
        {
            return value.ToString(format, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return value.ToString("0.##########", CultureInfo.InvariantCulture);
        }
    }

    // A tiny recursive-descent parser for +,-,*,/ and parentheses over literal numbers. Used for the
    // "plain numeric expression" formula case (e.g. "=2*(3+4)"). Throws FormatException on a syntax error.
    private sealed class ArithmeticParser
    {
        /// <summary>
        /// Maximum parenthesis/unary-sign nesting accepted. Each level costs a C# stack frame, and
        /// the expression text comes straight out of a w:instrText field instruction in an opened
        /// .docx — so without a cap, a document containing "=((((…1…))))" nested deep enough
        /// overflows the stack. StackOverflowException is uncatchable: it would kill the process
        /// rather than surfacing as the FormatException the caller already handles. The sibling
        /// FreeX formula parser caps its recursion for the same reason.
        /// </summary>
        private const int MaxParseDepth = 128;

        private readonly string _text;
        private int _pos;
        private int _depth;

        private ArithmeticParser(string text) => _text = text;

        public static double Evaluate(string text)
        {
            var parser = new ArithmeticParser(text);
            var value = parser.ParseExpression();
            parser.SkipWhitespace();
            if (parser._pos != parser._text.Length)
                throw new FormatException("Unexpected trailing characters.");
            return value;
        }

        private double ParseExpression()
        {
            var value = ParseTerm();
            while (true)
            {
                SkipWhitespace();
                if (Match('+'))
                    value += ParseTerm();
                else if (Match('-'))
                    value -= ParseTerm();
                else
                    return value;
            }
        }

        private double ParseTerm()
        {
            var value = ParseFactor();
            while (true)
            {
                SkipWhitespace();
                if (Match('*'))
                    value *= ParseFactor();
                else if (Match('/'))
                    value /= ParseFactor();
                else
                    return value;
            }
        }

        private double ParseFactor()
        {
            if (++_depth > MaxParseDepth)
                throw new FormatException("Expression nesting is too deep.");
            try
            {
                SkipWhitespace();
                if (Match('('))
                {
                    var value = ParseExpression();
                    SkipWhitespace();
                    if (!Match(')'))
                        throw new FormatException("Expected ')'.");
                    return value;
                }
                if (Match('-'))
                    return -ParseFactor();
                if (Match('+'))
                    return ParseFactor();
                return ParseNumber();
            }
            finally
            {
                _depth--;
            }
        }

        private double ParseNumber()
        {
            SkipWhitespace();
            var start = _pos;
            while (_pos < _text.Length && (char.IsDigit(_text[_pos]) || _text[_pos] == '.'))
                _pos++;
            if (_pos == start)
                throw new FormatException("Expected a number.");
            return double.Parse(_text[start.._pos], CultureInfo.InvariantCulture);
        }

        private bool Match(char c)
        {
            SkipWhitespace();
            if (_pos < _text.Length && _text[_pos] == c)
            {
                _pos++;
                return true;
            }
            return false;
        }

        private void SkipWhitespace()
        {
            while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos]))
                _pos++;
        }
    }
}
