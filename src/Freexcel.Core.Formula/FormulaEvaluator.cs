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
            OmittedArgumentNode => BlankValue.Instance,
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
        var sheetName = context.TryGetSheetName(r.Start.Sheet);
        return sheetName is not null
            ? context.GetCellValue(sheetName, r.Start.Row, r.Start.Col)
            : context.GetCellValue(r.Start.Row, r.Start.Col);
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
        double result = op(((NumberValue)a).Value, ((NumberValue)b).Value);
        return double.IsFinite(result) ? new NumberValue(result) : ErrorValue.Num;
    }

    private static ScalarValue DivideOp(ScalarValue left, ScalarValue right)
    {
        var a = CoerceToNumber(left);
        var b = CoerceToNumber(right);
        if (a is ErrorValue errA) return errA;
        if (b is ErrorValue errB) return errB;
        var divisor = ((NumberValue)b).Value;
        if (divisor == 0) return ErrorValue.DivByZero;
        double result = ((NumberValue)a).Value / divisor;
        return double.IsFinite(result) ? new NumberValue(result) : ErrorValue.Num;
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
        // Numbers and dates compare as numbers (dates are OADate serial numbers)
        bool lNum = left is NumberValue or DateTimeValue;
        bool rNum = right is NumberValue or DateTimeValue;
        if (lNum && rNum)
        {
            double lv = left is DateTimeValue ld ? ld.Value : ((NumberValue)left).Value;
            double rv = right is DateTimeValue rd ? rd.Value : ((NumberValue)right).Value;
            return lv.CompareTo(rv);
        }
        if (left is TextValue lt && right is TextValue rt)
            return string.Compare(lt.Value, rt.Value, StringComparison.OrdinalIgnoreCase);
        if (left is BoolValue lb && right is BoolValue rb)
            return lb.Value.CompareTo(rb.Value);

        // Mixed types: numbers/dates < text < booleans (Excel convention)
        return TypeOrder(left).CompareTo(TypeOrder(right));
    }

    private static int TypeOrder(ScalarValue v) => v switch
    {
        BlankValue => 0,
        NumberValue or DateTimeValue => 1,
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

        // Short-circuit functions evaluate arguments lazily to avoid propagating errors from untaken branches.
        if (node.FunctionName is "IF" or "IFERROR" or "IFNA" or "CHOOSE" or "IFS" or "SWITCH")
            return EvaluateShortCircuit(node, context);

        var (func, minArgs, maxArgs) = BuiltInFunctions.Get(node.FunctionName);

        bool isStructured = IsStructuredRangeFunction(node.FunctionName);

        // Expand range arguments into individual values for aggregate functions,
        // or wrap as RangeValue for structured functions that need 2-D access.
        var expandedArgs = new List<ScalarValue>();
        foreach (var arg in node.Arguments)
        {
            if (arg is RangeRefNode range)
            {
                if (range.SheetName is not null && !context.SheetExists(range.SheetName))
                {
                    expandedArgs.Add(ErrorValue.Ref);
                    continue;
                }

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
                    AddRangeValues(expandedArgs, values, node.FunctionName);
                }
            }
            else if (arg is StringNode directText && IsDirectTextCoercingAggregate(node.FunctionName))
            {
                expandedArgs.Add(new DirectTextLiteralValue(directText.Value));
            }
            else if (arg is CellRefNode cell && IsSingleCellReferenceRangeFunction(node.FunctionName))
            {
                if (cell.SheetName is not null && !context.SheetExists(cell.SheetName))
                {
                    expandedArgs.Add(ErrorValue.Ref);
                    continue;
                }

                expandedArgs.Add(BuildRangeValue(new RangeRefNode(cell, cell, cell.SheetName), context));
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
                        AddRangeValues(expandedArgs, values, node.FunctionName);
                    }
                }
            }
            else
            {
                expandedArgs.Add(EvaluateNode(arg, context));
            }
        }

        // Always enforce minimum arg count for every function, including aggregates.
        if (node.Arguments.Count < minArgs)
            return ErrorValue.Value;
        // Enforce maximum only for non-aggregate functions (aggregates accept unbounded ranges).
        if (!IsAggregateFunction(node.FunctionName) && node.Arguments.Count > maxArgs)
            return ErrorValue.Value;

        try
        {
            return func(expandedArgs, context);
        }
        catch (FormulaEvalException ex)
        {
            return ErrorFromCode(ex.ErrorCode);
        }
        catch (OverflowException)
        {
            return ErrorValue.Num;
        }
        catch (ArgumentOutOfRangeException)
        {
            return ErrorValue.Num;
        }
    }

    private static ErrorValue ErrorFromCode(string code) => code.ToUpperInvariant() switch
    {
        "#DIV/0!" => ErrorValue.DivByZero,
        "#VALUE!" => ErrorValue.Value,
        "#REF!" => ErrorValue.Ref,
        "#NAME?" => ErrorValue.Name,
        "#NULL!" => ErrorValue.Null,
        "#N/A" => ErrorValue.NA,
        "#NUM!" => ErrorValue.Num,
        _ => ErrorValue.Value
    };

    private static void AddRangeValues(List<ScalarValue> expandedArgs, IReadOnlyList<ScalarValue> values, string functionName)
    {
        if (IsReferenceProvenanceAggregate(functionName))
            expandedArgs.AddRange(values.Select(v => new ReferencedScalarValue(v)));
        else
            expandedArgs.AddRange(values);
    }

    private static RangeValue BuildRangeValue(RangeRefNode range, IEvalContext context)
    {
        // Normalize so r0 ≤ r1 and c0 ≤ c1 — Excel accepts B5:A1 and treats it as A1:B5.
        // Without this, uint subtraction wraps and produces a negative dimension.
        uint r0 = Math.Min(range.Start.Row, range.End.Row);
        uint r1 = Math.Max(range.Start.Row, range.End.Row);
        uint c0 = Math.Min(range.Start.ColumnNumber, range.End.ColumnNumber);
        uint c1 = Math.Max(range.Start.ColumnNumber, range.End.ColumnNumber);
        long rows = r1 - r0 + 1;
        long cols = c1 - c0 + 1;
        if (rows * cols > 1_000_000L)
            throw new FormulaEvalException("#REF!", "Range contains more than 1,000,000 cells");
        var cells = new ScalarValue[(int)rows, (int)cols];
        for (int ri = 0; ri < rows; ri++)
            for (int ci = 0; ci < cols; ci++)
            {
                cells[ri, ci] = range.SheetName is not null
                    ? context.GetCellValue(range.SheetName, r0 + (uint)ri, c0 + (uint)ci)
                    : context.GetCellValue(r0 + (uint)ri, c0 + (uint)ci);
            }
        return new RangeValue(cells, r0, c0);
    }

    private ScalarValue EvaluateShortCircuit(FunctionCallNode node, IEvalContext context)
    {
        return node.FunctionName switch
        {
            "IF"      => EvaluateIf(node, context),
            "IFERROR" => EvaluateIfError(node, context),
            "IFNA"    => EvaluateIfNa(node, context),
            "CHOOSE"  => EvaluateChoose(node, context),
            "IFS"     => EvaluateIfs(node, context),
            "SWITCH"  => EvaluateSwitch(node, context),
            _         => ErrorValue.Value
        };
    }

    private ScalarValue EvaluateIf(FunctionCallNode node, IEvalContext context)
    {
        if (node.Arguments.Count is < 2 or > 3) return ErrorValue.Value;
        var cond = EvaluateNode(node.Arguments[0], context);
        if (cond is ErrorValue e) return e;
        bool? taken = cond switch
        {
            BoolValue b     => b.Value,
            NumberValue n   => n.Value != 0,
            DateTimeValue d => d.Value != 0,
            BlankValue      => false,
            _               => null   // text condition is #VALUE! in Excel
        };
        if (taken is null) return ErrorValue.Value;
        if (taken.Value)  return EvaluateNode(node.Arguments[1], context);
        if (node.Arguments.Count == 3) return EvaluateNode(node.Arguments[2], context);
        return new BoolValue(false);
    }

    private ScalarValue EvaluateIfError(FunctionCallNode node, IEvalContext context)
    {
        if (node.Arguments.Count != 2) return ErrorValue.Value;
        var value = EvaluateNode(node.Arguments[0], context);
        return value is ErrorValue ? EvaluateNode(node.Arguments[1], context) : value;
    }

    private ScalarValue EvaluateIfNa(FunctionCallNode node, IEvalContext context)
    {
        if (node.Arguments.Count != 2) return ErrorValue.Value;
        var value = EvaluateNode(node.Arguments[0], context);
        return value == ErrorValue.NA ? EvaluateNode(node.Arguments[1], context) : value;
    }

    private ScalarValue EvaluateChoose(FunctionCallNode node, IEvalContext context)
    {
        if (node.Arguments.Count < 2) return ErrorValue.Value;
        var indexVal = EvaluateNode(node.Arguments[0], context);
        if (indexVal is ErrorValue e) return e;
        var coerced = CoerceToNumber(indexVal);
        if (coerced is ErrorValue ec) return ec;
        double rawIdx = ((NumberValue)coerced).Value;
        if (!double.IsFinite(rawIdx)) return ErrorValue.Value;
        int idx = (int)rawIdx;
        if (idx < 1 || idx >= node.Arguments.Count) return ErrorValue.Value;
        return EvaluateNode(node.Arguments[idx], context);
    }

    private ScalarValue EvaluateIfs(FunctionCallNode node, IEvalContext context)
    {
        if (node.Arguments.Count < 2 || node.Arguments.Count % 2 != 0) return ErrorValue.Value;
        for (int i = 0; i < node.Arguments.Count - 1; i += 2)
        {
            var cond = EvaluateNode(node.Arguments[i], context);
            if (cond is ErrorValue e) return e;
            bool? taken = cond switch
            {
                BoolValue b     => b.Value,
                NumberValue n   => n.Value != 0,
                DateTimeValue d => d.Value != 0,
                BlankValue      => false,
                _               => null
            };
            if (taken is null) return ErrorValue.Value;
            if (taken.Value) return EvaluateNode(node.Arguments[i + 1], context);
        }
        return ErrorValue.NA;
    }

    private ScalarValue EvaluateSwitch(FunctionCallNode node, IEvalContext context)
    {
        if (node.Arguments.Count < 3) return ErrorValue.Value;
        var expr = EvaluateNode(node.Arguments[0], context);
        if (expr is ErrorValue e) return e;
        bool hasDefault = (node.Arguments.Count - 1) % 2 == 1;
        int pairCount = (node.Arguments.Count - 1) / 2;
        for (int i = 0; i < pairCount; i++)
        {
            var val = EvaluateNode(node.Arguments[1 + i * 2], context);
            if (val is ErrorValue ve) return ve;
            if (BuiltInFunctions.ScalarEquals(expr, val))
                return EvaluateNode(node.Arguments[1 + i * 2 + 1], context);
        }
        return hasDefault ? EvaluateNode(node.Arguments[^1], context) : ErrorValue.NA;
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

    private static bool IsDirectTextCoercingAggregate(string name) =>
        name is "SUM" or "AVERAGE" or "MIN" or "MAX" or "COUNT" or "PRODUCT"
             or "STDEV" or "STDEV.S" or "STDEV.P"
             or "VAR" or "VAR.S" or "VAR.P"
             or "MEDIAN"
             or "GEOMEAN" or "HARMEAN" or "AVEDEV"
             or "MODE" or "MODE.SNGL"
             or "NPV"
             or "GCD" or "LCM";

    private static bool IsReferenceProvenanceAggregate(string name) =>
        name is "SUM" or "AVERAGE" or "MIN" or "MAX" or "COUNT" or "PRODUCT" or "AND" or "OR" or "XOR"
             or "STDEV" or "STDEV.S" or "STDEV.P"
             or "VAR" or "VAR.S" or "VAR.P"
             or "MEDIAN"
             or "GEOMEAN" or "HARMEAN" or "AVEDEV"
             or "MODE" or "MODE.SNGL"
             or "NPV"
             or "GCD" or "LCM";

    private static bool IsStructuredRangeFunction(string name) =>
        name is "VLOOKUP" or "HLOOKUP" or "INDEX" or "MATCH" or "XMATCH"
             or "SUMIF" or "COUNTIF" or "AVERAGEIF"
             or "SUMPRODUCT"
             or "LARGE" or "SMALL" or "RANK"
             or "SUMIFS" or "COUNTIFS" or "AVERAGEIFS"
             or "XLOOKUP"
             or "WORKDAY" or "NETWORKDAYS"
             or "CORREL" or "FORECAST" or "FORECAST.LINEAR"
             or "PERCENTILE" or "PERCENTILE.INC" or "PERCENTILE.EXC"
             or "QUARTILE" or "QUARTILE.INC"
             or "PERCENTRANK" or "PERCENTRANK.INC"
             or "LOOKUP"
             or "IRR"
             or "RANDARRAY"
             or "FILTER" or "SORT" or "SORTBY" or "TAKE" or "DROP"
             or "CHOOSEROWS" or "CHOOSECOLS" or "VSTACK" or "HSTACK"
             or "TOROW" or "TOCOL" or "WRAPROWS" or "WRAPCOLS" or "EXPAND" or "UNIQUE"
             or "SUBTOTAL"
             or "ROW" or "COLUMN" or "ROWS" or "COLUMNS" or "COUNTBLANK";

    private static bool IsSingleCellReferenceRangeFunction(string name) =>
        name is "ROW" or "COLUMN" or "ROWS" or "COLUMNS" or "COUNTBLANK";

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
            var r0 = Math.Min(startRow, endRow); var r1 = Math.Max(startRow, endRow);
            var c0 = Math.Min(startCol, endCol); var c1 = Math.Max(startCol, endCol);
            for (var r = r0; r <= r1; r++)
                for (var c = c0; c <= c1; c++)
                    values.Add(_sheet.GetValue(r, c));
            return values;
        }

        public IReadOnlyList<ScalarValue> GetRangeValues(string sheetName, uint startRow, uint startCol, uint endRow, uint endCol)
        {
            var target = _workbook?.GetSheet(sheetName);
            if (target is null) return [ErrorValue.Ref];
            var values = new List<ScalarValue>();
            var r0 = Math.Min(startRow, endRow); var r1 = Math.Max(startRow, endRow);
            var c0 = Math.Min(startCol, endCol); var c1 = Math.Max(startCol, endCol);
            for (var r = r0; r <= r1; r++)
                for (var c = c0; c <= c1; c++)
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

        public bool SheetExists(string sheetName) => _workbook?.GetSheet(sheetName) is not null;

        public bool IsRowHidden(uint row) => _sheet.IsRowEffectivelyHidden(row);
    }
}

internal sealed record DirectTextLiteralValue(string Value) : ScalarValue;
internal sealed record ReferencedScalarValue(ScalarValue Value) : ScalarValue;
