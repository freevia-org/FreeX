using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

/// <summary>
/// Evaluates a formula AST against a worksheet to produce a ScalarValue.
/// This is the heart of the formula engine.
/// </summary>
public sealed class FormulaEvaluator
{
    /// <summary>
    /// Parse and evaluate a formula string against a sheet.
    /// </summary>
    public ScalarValue Evaluate(string formulaText, Sheet sheet, Freexcel.Core.Model.Workbook? workbook = null)
    {
        var lexer = new Lexer(formulaText);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();
        var context = new SheetEvalContext(sheet, workbook);
        return EvaluateNode(ast, context);
    }

    /// <summary>
    /// Evaluate a pre-parsed AST against a sheet.
    /// </summary>
    public ScalarValue Evaluate(FormulaNode ast, Sheet sheet, Freexcel.Core.Model.Workbook? workbook = null)
    {
        var context = new SheetEvalContext(sheet, workbook);
        return EvaluateNode(ast, context);
    }

    /// <summary>
    /// Evaluate an AST node recursively.
    /// </summary>
    internal ScalarValue EvaluateNode(FormulaNode node, IEvalContext context)
    {
        return node switch
        {
            NumberNode n => new NumberValue(n.Value),
            StringNode s => new TextValue(s.Value),
            BooleanNode b => new BoolValue(b.Value),
            ErrorNode err => err.Error,
            CellRefNode cell when cell.SheetName is not null
                => context.GetCellValue(cell.SheetName, cell.Row, cell.ColumnNumber),
            CellRefNode cell => context.GetCellValue(cell.Row, cell.ColumnNumber),
            RangeRefNode range => EvaluateRange(range, context),
            NamedRangeNode named => EvaluateNamedRange(named, context),
            BinaryOpNode binary => EvaluateBinaryOp(binary, context),
            UnaryOpNode unary => EvaluateUnaryOp(unary, context),
            FunctionCallNode func => EvaluateFunction(func, context),
            _ => throw new FormulaEvalException("#VALUE!", $"Unknown node type: {node.GetType().Name}")
        };
    }

    private static ScalarValue EvaluateNamedRange(NamedRangeNode node, IEvalContext context)
    {
        var range = context.TryResolveNamedRange(node.Name);
        if (range is null)
            return ErrorValue.Name;

        // Bare named range reference outside a function: return top-left cell value.
        // For 2D named ranges this is intentionally lossy — full implicit-intersection
        // semantics (Excel 365 spill behaviour) are a Phase 5 enhancement.
        var r = range.Value;
        return context.GetCellValue(r.Start.Row, r.Start.Col);
    }

    private static ScalarValue EvaluateRange(RangeRefNode range, IEvalContext context)
    {
        // A bare range reference outside a function context returns the first value
        // (This matches Excel's implicit intersection behavior for simple cases)
        return range.SheetName is not null
            ? context.GetCellValue(range.SheetName, range.Start.Row, range.Start.ColumnNumber)
            : context.GetCellValue(range.Start.Row, range.Start.ColumnNumber);
    }

    private ScalarValue EvaluateBinaryOp(BinaryOpNode node, IEvalContext context)
    {
        var left = EvaluateNode(node.Left, context);
        var right = EvaluateNode(node.Right, context);

        // Propagate errors
        if (left is ErrorValue errL) return errL;
        if (right is ErrorValue errR) return errR;

        return node.Operator switch
        {
            BinaryOperator.Add => ArithOp(left, right, (a, b) => a + b),
            BinaryOperator.Subtract => ArithOp(left, right, (a, b) => a - b),
            BinaryOperator.Multiply => ArithOp(left, right, (a, b) => a * b),
            BinaryOperator.Divide => DivideOp(left, right),
            BinaryOperator.Power => ArithOp(left, right, Math.Pow),
            BinaryOperator.Concatenate => ConcatOp(left, right),
            BinaryOperator.Equal => CompareOp(left, right, 0),
            BinaryOperator.NotEqual => CompareOpNot(left, right, 0),
            BinaryOperator.LessThan => CompareOp(left, right, -1),
            BinaryOperator.GreaterThan => CompareOp(left, right, 1),
            BinaryOperator.LessOrEqual => CompareOpLessOrEqual(left, right),
            BinaryOperator.GreaterOrEqual => CompareOpGreaterOrEqual(left, right),
            _ => throw new FormulaEvalException("#VALUE!", $"Unknown operator: {node.Operator}")
        };
    }

    private static ScalarValue ArithOp(ScalarValue left, ScalarValue right, Func<double, double, double> op)
    {
        var a = CoerceToNumber(left);
        var b = CoerceToNumber(right);
        if (a is ErrorValue errA) return errA;
        if (b is ErrorValue errB) return errB;
        return new NumberValue(op(((NumberValue)a).Value, ((NumberValue)b).Value));
    }

    private static ScalarValue DivideOp(ScalarValue left, ScalarValue right)
    {
        var a = CoerceToNumber(left);
        var b = CoerceToNumber(right);
        if (a is ErrorValue errA) return errA;
        if (b is ErrorValue errB) return errB;
        var divisor = ((NumberValue)b).Value;
        if (divisor == 0) return ErrorValue.DivByZero;
        return new NumberValue(((NumberValue)a).Value / divisor);
    }

    private static ScalarValue ConcatOp(ScalarValue left, ScalarValue right)
    {
        return new TextValue(ValueToString(left) + ValueToString(right));
    }

    private static ScalarValue CompareOp(ScalarValue left, ScalarValue right, int expected)
    {
        var cmp = CompareValues(left, right);
        return new BoolValue(cmp == expected);
    }

    private static ScalarValue CompareOpNot(ScalarValue left, ScalarValue right, int expected)
    {
        var cmp = CompareValues(left, right);
        return new BoolValue(cmp != expected);
    }

    private static ScalarValue CompareOpLessOrEqual(ScalarValue left, ScalarValue right)
    {
        var cmp = CompareValues(left, right);
        return new BoolValue(cmp <= 0);
    }

    private static ScalarValue CompareOpGreaterOrEqual(ScalarValue left, ScalarValue right)
    {
        var cmp = CompareValues(left, right);
        return new BoolValue(cmp >= 0);
    }

    private static int CompareValues(ScalarValue left, ScalarValue right)
    {
        // Numbers compare as numbers, text as case-insensitive text
        if (left is NumberValue ln && right is NumberValue rn)
            return ln.Value.CompareTo(rn.Value);
        if (left is TextValue lt && right is TextValue rt)
            return string.Compare(lt.Value, rt.Value, StringComparison.OrdinalIgnoreCase);
        if (left is BoolValue lb && right is BoolValue rb)
            return lb.Value.CompareTo(rb.Value);

        // Mixed types: numbers < text < booleans (Excel convention)
        return TypeOrder(left).CompareTo(TypeOrder(right));
    }

    private static int TypeOrder(ScalarValue v) => v switch
    {
        BlankValue => 0,
        NumberValue => 1,
        TextValue => 2,
        BoolValue => 3,
        _ => 4
    };

    private ScalarValue EvaluateUnaryOp(UnaryOpNode node, IEvalContext context)
    {
        var operand = EvaluateNode(node.Operand, context);
        if (operand is ErrorValue err) return err;

        return node.Operator switch
        {
            UnaryOperator.Negate => NegateOp(operand),
            UnaryOperator.Percent => PercentOp(operand),
            _ => throw new FormulaEvalException("#VALUE!", $"Unknown unary operator: {node.Operator}")
        };
    }

    private static ScalarValue NegateOp(ScalarValue v)
    {
        var n = CoerceToNumber(v);
        if (n is ErrorValue err) return err;
        return new NumberValue(-((NumberValue)n).Value);
    }

    private static ScalarValue PercentOp(ScalarValue v)
    {
        var n = CoerceToNumber(v);
        if (n is ErrorValue err) return err;
        return new NumberValue(((NumberValue)n).Value / 100.0);
    }

    private ScalarValue EvaluateFunction(FunctionCallNode node, IEvalContext context)
    {
        if (!BuiltInFunctions.Exists(node.FunctionName))
            return ErrorValue.Name;

        var (func, minArgs, maxArgs) = BuiltInFunctions.Get(node.FunctionName);

        bool isStructured = IsStructuredRangeFunction(node.FunctionName);

        // Expand range arguments into individual values for aggregate functions,
        // or wrap as RangeValue for structured functions that need 2-D access.
        var expandedArgs = new List<ScalarValue>();
        foreach (var arg in node.Arguments)
        {
            if (arg is RangeRefNode range)
            {
                if (isStructured)
                {
                    // Build a 2-D RangeValue for structured functions
                    var rv = BuildRangeValue(range, context);
                    expandedArgs.Add(rv);
                }
                else
                {
                    IReadOnlyList<ScalarValue> values = range.SheetName is not null
                        ? context.GetRangeValues(range.SheetName,
                            range.Start.Row, range.Start.ColumnNumber,
                            range.End.Row, range.End.ColumnNumber)
                        : context.GetRangeValues(
                            range.Start.Row, range.Start.ColumnNumber,
                            range.End.Row, range.End.ColumnNumber);
                    expandedArgs.AddRange(values);
                }
            }
            else if (arg is NamedRangeNode named)
            {
                var resolvedRange = context.TryResolveNamedRange(named.Name);
                if (resolvedRange is null)
                {
                    expandedArgs.Add(ErrorValue.Name);
                }
                else
                {
                    var r = resolvedRange.Value;
                    if (isStructured)
                    {
                        // Build a RangeRefNode-equivalent structure for structured functions
                        var start = new CellRefNode(
                            Freexcel.Core.Model.CellAddress.NumberToColumnName(r.Start.Col),
                            r.Start.Row);
                        var end = new CellRefNode(
                            Freexcel.Core.Model.CellAddress.NumberToColumnName(r.End.Col),
                            r.End.Row);
                        var syntheticRange = new RangeRefNode(start, end);
                        expandedArgs.Add(BuildRangeValue(syntheticRange, context));
                    }
                    else
                    {
                        // Resolve the sheet name when the named range lives on a different sheet
                        var sheetName = context.TryGetSheetName(r.Start.Sheet);
                        IReadOnlyList<ScalarValue> values = sheetName is not null
                            ? context.GetRangeValues(sheetName,
                                r.Start.Row, r.Start.Col,
                                r.End.Row, r.End.Col)
                            : context.GetRangeValues(
                                r.Start.Row, r.Start.Col,
                                r.End.Row, r.End.Col);
                        expandedArgs.AddRange(values);
                    }
                }
            }
            else
            {
                expandedArgs.Add(EvaluateNode(arg, context));
            }
        }

        // Validate arg count AFTER range expansion for most functions,
        // but use original count for functions like IF where ranges aren't expanded
        if (node.Arguments.Count < minArgs || node.Arguments.Count > maxArgs)
        {
            // Check original arg count for non-aggregate functions
            if (!IsAggregateFunction(node.FunctionName))
                return ErrorValue.Value;
        }

        return func(expandedArgs, context);
    }

    private static RangeValue BuildRangeValue(RangeRefNode range, IEvalContext context)
    {
        uint r0 = range.Start.Row, c0 = range.Start.ColumnNumber;
        uint r1 = range.End.Row,   c1 = range.End.ColumnNumber;
        int rows = (int)(r1 - r0 + 1);
        int cols = (int)(c1 - c0 + 1);
        var cells = new ScalarValue[rows, cols];
        for (int ri = 0; ri < rows; ri++)
            for (int ci = 0; ci < cols; ci++)
            {
                cells[ri, ci] = range.SheetName is not null
                    ? context.GetCellValue(range.SheetName, r0 + (uint)ri, c0 + (uint)ci)
                    : context.GetCellValue(r0 + (uint)ri, c0 + (uint)ci);
            }
        return new RangeValue(cells, r0, c0);
    }

    private static bool IsAggregateFunction(string name) =>
        name is "SUM" or "AVERAGE" or "MIN" or "MAX" or "COUNT" or "COUNTA" or "AND" or "OR" or "CONCAT"
             or "STDEV" or "MEDIAN"
             or "PRODUCT" or "XOR"
             or "VAR" or "VAR.S" or "VAR.P" or "STDEV.P"
             or "GEOMEAN" or "HARMEAN" or "AVEDEV"
             or "MODE" or "MODE.SNGL"
             or "CONCATENATE"
             or "NPV";

    private static bool IsStructuredRangeFunction(string name) =>
        name is "VLOOKUP" or "HLOOKUP" or "INDEX" or "MATCH"
             or "SUMIF" or "COUNTIF" or "AVERAGEIF"
             or "LARGE" or "SMALL" or "RANK"
             or "SUMIFS" or "COUNTIFS" or "AVERAGEIFS"
             or "XLOOKUP"
             or "WORKDAY" or "NETWORKDAYS"
             or "CORREL" or "FORECAST" or "FORECAST.LINEAR"
             or "PERCENTILE" or "PERCENTILE.INC" or "PERCENTILE.EXC"
             or "QUARTILE" or "QUARTILE.INC"
             or "PERCENTRANK" or "PERCENTRANK.INC"
             or "LOOKUP"
             or "IRR";

    private static ScalarValue CoerceToNumber(ScalarValue v) => v switch
    {
        NumberValue => v,
        BlankValue => new NumberValue(0),
        BoolValue b => new NumberValue(b.Value ? 1 : 0),
        TextValue t when double.TryParse(t.Value, System.Globalization.CultureInfo.InvariantCulture, out var d) =>
            new NumberValue(d),
        TextValue => ErrorValue.Value,
        DateTimeValue dt => new NumberValue(dt.Value),
        _ => ErrorValue.Value
    };

    private static string ValueToString(ScalarValue v) => v switch
    {
        TextValue t => t.Value,
        NumberValue n => n.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        BoolValue b => b.Value ? "TRUE" : "FALSE",
        BlankValue => "",
        ErrorValue e => e.Code,
        _ => v.ToString() ?? ""
    };

    private sealed class SheetEvalContext : IEvalContext
    {
        private readonly Sheet _sheet;
        private readonly Freexcel.Core.Model.Workbook? _workbook;

        public SheetEvalContext(Sheet sheet, Freexcel.Core.Model.Workbook? workbook = null)
        {
            _sheet = sheet;
            _workbook = workbook;
        }

        public ScalarValue GetCellValue(uint row, uint col) => _sheet.GetValue(row, col);

        public ScalarValue GetCellValue(string sheetName, uint row, uint col)
        {
            var target = _workbook?.GetSheet(sheetName);
            if (target is null) return ErrorValue.Ref;
            return target.GetValue(row, col);
        }

        public IReadOnlyList<ScalarValue> GetRangeValues(uint startRow, uint startCol, uint endRow, uint endCol)
        {
            var values = new List<ScalarValue>();
            for (var r = startRow; r <= endRow; r++)
                for (var c = startCol; c <= endCol; c++)
                    values.Add(_sheet.GetValue(r, c));
            return values;
        }

        public IReadOnlyList<ScalarValue> GetRangeValues(string sheetName, uint startRow, uint startCol, uint endRow, uint endCol)
        {
            var target = _workbook?.GetSheet(sheetName);
            if (target is null) return [];
            var values = new List<ScalarValue>();
            for (var r = startRow; r <= endRow; r++)
                for (var c = startCol; c <= endCol; c++)
                    values.Add(target.GetValue(r, c));
            return values;
        }

        public Freexcel.Core.Model.GridRange? TryResolveNamedRange(string name)
        {
            if (_workbook is null) return null;
            if (_workbook.TryGetNamedRange(name, out var range))
                return range;
            return null;
        }

        public string? TryGetSheetName(Freexcel.Core.Model.SheetId sheetId)
            => _workbook?.GetSheet(sheetId)?.Name;
    }
}
