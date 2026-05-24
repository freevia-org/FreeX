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
    public ScalarValue Evaluate(
        string formulaText,
        Sheet sheet,
        Freexcel.Core.Model.Workbook? workbook = null,
        Freexcel.Core.Model.CellAddress? currentCell = null)
    {
        var lexer = new Lexer(formulaText);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();
        var context = new SheetEvalContext(sheet, workbook, this, currentCell);
        return EvaluateNode(ast, context);
    }

    /// <summary>
    /// Evaluate a pre-parsed AST against a sheet.
    /// </summary>
    public ScalarValue Evaluate(
        FormulaNode ast,
        Sheet sheet,
        Freexcel.Core.Model.Workbook? workbook = null,
        Freexcel.Core.Model.CellAddress? currentCell = null)
    {
        var context = new SheetEvalContext(sheet, workbook, this, currentCell);
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
            FullColumnRangeRefNode range => EvaluateRange(ToRangeRef(range), context),
            FullRowRangeRefNode range => EvaluateRange(ToRangeRef(range), context),
            NamedRangeNode named => EvaluateNamedRange(named, context),
            StructuredReferenceNode structured => EvaluateStructuredReference(structured, context),
            StructuredCurrentRowReferenceNode currentRow => EvaluateCurrentRowReference(currentRow, context),
            BinaryOpNode binary => EvaluateBinaryOp(binary, context),
            UnaryOpNode unary => EvaluateUnaryOp(unary, context),
            FunctionCallNode func => EvaluateFunction(func, context),
            _ => throw new FormulaEvalException("#VALUE!", $"Unknown node type: {node.GetType().Name}")
        };
    }

    private static ScalarValue EvaluateNamedRange(NamedRangeNode node, IEvalContext context)
    {
        // Local LET/LAMBDA bindings shadow workbook named ranges.
        var binding = context.TryResolveLambdaBinding(node.Name);
        if (binding is not null) return binding;

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
        var left = EvaluateArrayOperand(node.Left, context);
        var right = EvaluateArrayOperand(node.Right, context);

        // Propagate errors
        if (left is ErrorValue errL) return errL;
        if (right is ErrorValue errR) return errR;

        return node.Operator switch
        {
            BinaryOperator.Add => ArithOp(left, right, (a, b) => a + b),
            BinaryOperator.Subtract => ArithOp(left, right, (a, b) => a - b),
            BinaryOperator.Multiply => ArithOp(left, right, (a, b) => a * b),
            BinaryOperator.Divide => DivideOp(left, right),
            BinaryOperator.Power => PowerOp(left, right),
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

    private ScalarValue EvaluateArrayOperand(FormulaNode node, IEvalContext context)
    {
        if (node is RangeRefNode range)
            return BuildRangeValue(range, context);

        if (node is NamedRangeNode named)
        {
            var binding = context.TryResolveLambdaBinding(named.Name);
            if (binding is not null)
                return binding;

            var resolvedRange = context.TryResolveNamedRange(named.Name);
            return resolvedRange is null
                ? ErrorValue.Name
                : BuildRangeValue(resolvedRange.Value, context);
        }

        if (node is StructuredReferenceNode structured)
        {
            var resolvedRange = TryResolveStructuredReferenceRange(structured, context);
            return resolvedRange is null
                ? ErrorValue.Name
                : BuildRangeValue(resolvedRange.Value, context);
        }

        if (node is StructuredCurrentRowReferenceNode currentRow)
            return EvaluateCurrentRowReference(currentRow, context);

        var value = EvaluateNode(node, context);
        return value;
    }

    private static ScalarValue EvaluateStructuredReference(StructuredReferenceNode node, IEvalContext context)
    {
        var range = TryResolveStructuredReferenceRange(node, context);
        return range is null
            ? ErrorValue.Name
            : BuildRangeValue(range.Value, context);
    }

    private static ScalarValue EvaluateCurrentRowReference(StructuredCurrentRowReferenceNode node, IEvalContext context)
    {
        var address = StructuredReferenceResolver.ResolveCurrentRowColumn(
            context.CurrentWorkbook,
            context.CurrentSheet,
            context.CurrentCellAddress,
            node.TableName,
            node.ColumnName);
        return address is null
            ? ErrorValue.Name
            : context.GetCellValue(address.Value.Row, address.Value.Col);
    }

    private static ScalarValue PowerOp(ScalarValue left, ScalarValue right)
        => ElementwiseOp(left, right, PowerScalarOp);

    private static ScalarValue PowerScalarOp(ScalarValue left, ScalarValue right)
    {
        var a = CoerceToNumber(left);
        var b = CoerceToNumber(right);
        if (a is ErrorValue errA) return errA;
        if (b is ErrorValue errB) return errB;
        double baseVal = ((NumberValue)a).Value;
        double exp = ((NumberValue)b).Value;
        if (baseVal == 0 && exp <= 0) return exp == 0 ? ErrorValue.Num : ErrorValue.DivByZero;
        double result = Math.Pow(baseVal, exp);
        return double.IsFinite(result) ? new NumberValue(result) : ErrorValue.Num;
    }

    private static ScalarValue ArithOp(ScalarValue left, ScalarValue right, Func<double, double, double> op)
        => ElementwiseOp(left, right, (l, r) => ArithScalarOp(l, r, op));

    private static ScalarValue ArithScalarOp(ScalarValue left, ScalarValue right, Func<double, double, double> op)
    {
        var a = CoerceToNumber(left);
        var b = CoerceToNumber(right);
        if (a is ErrorValue errA) return errA;
        if (b is ErrorValue errB) return errB;
        double result = op(((NumberValue)a).Value, ((NumberValue)b).Value);
        return double.IsFinite(result) ? new NumberValue(result) : ErrorValue.Num;
    }

    private static ScalarValue DivideOp(ScalarValue left, ScalarValue right)
        => ElementwiseOp(left, right, DivideScalarOp);

    private static ScalarValue DivideScalarOp(ScalarValue left, ScalarValue right)
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
        => ElementwiseOp(left, right, (l, r) => new TextValue(ValueToString(l) + ValueToString(r)));

    private static ScalarValue ElementwiseOp(
        ScalarValue left,
        ScalarValue right,
        Func<ScalarValue, ScalarValue, ScalarValue> scalarOp)
    {
        var leftRange = left as RangeValue;
        var rightRange = right as RangeValue;
        if (leftRange is null && rightRange is null)
            return scalarOp(left, right);

        if (leftRange is RangeValue lr && rightRange is RangeValue rr)
        {
            if (!CanBroadcast(lr.RowCount, rr.RowCount) || !CanBroadcast(lr.ColCount, rr.ColCount))
                return ErrorValue.Value;

            var rowCount = Math.Max(lr.RowCount, rr.RowCount);
            var colCount = Math.Max(lr.ColCount, rr.ColCount);
            var cells = new ScalarValue[rowCount, colCount];
            for (var row = 0; row < rowCount; row++)
                for (var col = 0; col < colCount; col++)
                    cells[row, col] = scalarOp(
                        lr.Cells[lr.RowCount == 1 ? 0 : row, lr.ColCount == 1 ? 0 : col],
                        rr.Cells[rr.RowCount == 1 ? 0 : row, rr.ColCount == 1 ? 0 : col]);
            return new RangeValue(cells, lr.StartRow, lr.StartCol) { SheetName = lr.SheetName };
        }

        var range = leftRange ?? rightRange!;
        var scalar = leftRange is null ? left : right;
        var scalarOnLeft = leftRange is null;
        var result = new ScalarValue[range.RowCount, range.ColCount];
        for (var row = 0; row < range.RowCount; row++)
        {
            for (var col = 0; col < range.ColCount; col++)
            {
                var rangeValue = range.Cells[row, col];
                result[row, col] = scalarOnLeft
                    ? scalarOp(scalar, rangeValue)
                    : scalarOp(rangeValue, scalar);
            }
        }

        return new RangeValue(result, range.StartRow, range.StartCol) { SheetName = range.SheetName };
    }

    private static bool CanBroadcast(int left, int right) => left == right || left == 1 || right == 1;

    private static ScalarValue CompareOp(ScalarValue left, ScalarValue right, int expected)
        => ElementwiseOp(left, right, (l, r) => CompareScalarOp(l, r, expected));

    private static ScalarValue CompareScalarOp(ScalarValue left, ScalarValue right, int expected)
    {
        if (left is ErrorValue errL) return errL;
        if (right is ErrorValue errR) return errR;
        var cmp = CompareValues(left, right);
        return new BoolValue(cmp == expected);
    }

    private static ScalarValue CompareOpNot(ScalarValue left, ScalarValue right, int expected)
        => ElementwiseOp(left, right, (l, r) => CompareScalarOpNot(l, r, expected));

    private static ScalarValue CompareScalarOpNot(ScalarValue left, ScalarValue right, int expected)
    {
        if (left is ErrorValue errL) return errL;
        if (right is ErrorValue errR) return errR;
        var cmp = CompareValues(left, right);
        return new BoolValue(cmp != expected);
    }

    private static ScalarValue CompareOpLessOrEqual(ScalarValue left, ScalarValue right)
        => ElementwiseOp(left, right, CompareScalarOpLessOrEqual);

    private static ScalarValue CompareScalarOpLessOrEqual(ScalarValue left, ScalarValue right)
    {
        if (left is ErrorValue errL) return errL;
        if (right is ErrorValue errR) return errR;
        var cmp = CompareValues(left, right);
        return new BoolValue(cmp <= 0);
    }

    private static ScalarValue CompareOpGreaterOrEqual(ScalarValue left, ScalarValue right)
        => ElementwiseOp(left, right, CompareScalarOpGreaterOrEqual);

    private static ScalarValue CompareScalarOpGreaterOrEqual(ScalarValue left, ScalarValue right)
    {
        if (left is ErrorValue errL) return errL;
        if (right is ErrorValue errR) return errR;
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
        var operand = EvaluateArrayOperand(node.Operand, context);
        if (operand is ErrorValue err) return err;

        return node.Operator switch
        {
            UnaryOperator.Negate => NegateOp(operand),
            UnaryOperator.Percent => PercentOp(operand),
            _ => throw new FormulaEvalException("#VALUE!", $"Unknown unary operator: {node.Operator}")
        };
    }

    private static ScalarValue NegateOp(ScalarValue v)
        => ElementwiseUnaryOp(v, NegateScalarOp);

    private static ScalarValue NegateScalarOp(ScalarValue v)
    {
        var n = CoerceToNumber(v);
        if (n is ErrorValue err) return err;
        return new NumberValue(-((NumberValue)n).Value);
    }

    private static ScalarValue PercentOp(ScalarValue v)
        => ElementwiseUnaryOp(v, PercentScalarOp);

    private static ScalarValue PercentScalarOp(ScalarValue v)
    {
        var n = CoerceToNumber(v);
        if (n is ErrorValue err) return err;
        return new NumberValue(((NumberValue)n).Value / 100.0);
    }

    private static ScalarValue ElementwiseUnaryOp(ScalarValue value, Func<ScalarValue, ScalarValue> scalarOp)
    {
        if (value is not RangeValue range)
            return scalarOp(value);

        var cells = new ScalarValue[range.RowCount, range.ColCount];
        for (var row = 0; row < range.RowCount; row++)
            for (var col = 0; col < range.ColCount; col++)
                cells[row, col] = scalarOp(range.Cells[row, col]);

        return new RangeValue(cells, range.StartRow, range.StartCol) { SheetName = range.SheetName };
    }

    private ScalarValue EvaluateFunction(FunctionCallNode node, IEvalContext context)
    {
        // LET-scoped lambda bindings: a name like "double" resolves to a LambdaValue
        // before any built-in lookup, allowing user-defined functions to shadow nothing.
        var lambdaBinding = context.TryResolveLambdaBinding(node.FunctionName);
        if (lambdaBinding is LambdaValue lv)
            return InvokeLambdaWithArgs(lv, node.Arguments, context);

        // LET and LAMBDA are AST-aware special forms not in the built-in registry.
        if (node.FunctionName is "LET" or "LAMBDA")
            return EvaluateAstAware(node, context);

        if (!BuiltInFunctions.Exists(node.FunctionName))
            return ErrorValue.Name;

        // Short-circuit functions evaluate arguments lazily to avoid propagating errors from untaken branches.
        if (node.FunctionName is "IF" or "IFERROR" or "IFNA" or "CHOOSE" or "IFS" or "SWITCH")
            return EvaluateShortCircuit(node, context);

        // AST-aware functions: must inspect the raw argument nodes before evaluation.
        if (node.FunctionName is "ISREF" or "ISFORMULA" or "FORMULATEXT" or "OFFSET")
            return EvaluateAstAware(node, context);

        var (func, minArgs, maxArgs) = BuiltInFunctions.Get(node.FunctionName);

        bool isStructured = IsStructuredRangeFunction(node.FunctionName);

        // Expand range arguments into individual values for aggregate functions,
        // or wrap as RangeValue for structured functions that need 2-D access.
        var expandedArgs = new List<ScalarValue>();
        for (var argIndex = 0; argIndex < node.Arguments.Count; argIndex++)
        {
            var arg = node.Arguments[argIndex];
            if (TryAsRangeRef(arg, out var range))
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
            else if (arg is CellRefNode aggregateCell && IsSingleCellReferenceProvenanceArgument(node.FunctionName, argIndex))
            {
                if (aggregateCell.SheetName is not null && !context.SheetExists(aggregateCell.SheetName))
                {
                    expandedArgs.Add(ErrorValue.Ref);
                    continue;
                }

                var value = aggregateCell.SheetName is not null
                    ? context.GetCellValue(aggregateCell.SheetName, aggregateCell.Row, aggregateCell.ColumnNumber)
                    : context.GetCellValue(aggregateCell.Row, aggregateCell.ColumnNumber);
                expandedArgs.Add(new ReferencedScalarValue(value));
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
                // Check LET/LAMBDA bindings first — these shadow workbook named ranges.
                var lambdaBound = context.TryResolveLambdaBinding(named.Name);
                if (lambdaBound is not null)
                {
                    if (isStructured && lambdaBound is RangeValue)
                        expandedArgs.Add(lambdaBound);
                    else if (!isStructured && lambdaBound is RangeValue flatRv)
                        AddRangeValues(expandedArgs, flatRv.Flatten(), node.FunctionName);
                    else
                        expandedArgs.Add(lambdaBound);
                }
                else
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
                            expandedArgs.Add(BuildRangeValue(r, context));
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
            }
            else
            {
                var value = EvaluateNode(arg, context);
                if (!isStructured && IsAggregateFunction(node.FunctionName) && value is RangeValue rangeValue)
                    AddRangeValues(expandedArgs, rangeValue.Flatten(), node.FunctionName);
                else
                    expandedArgs.Add(value);
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
        catch (IndexOutOfRangeException)
        {
            return ErrorValue.Ref;
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
        "#SPILL!" => ErrorValue.Spill,
        "#CALC!" => ErrorValue.Calc,
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
        return new RangeValue(cells, r0, c0) { SheetName = range.SheetName };
    }

    private static RangeValue BuildRangeValue(Freexcel.Core.Model.GridRange range, IEvalContext context)
    {
        var sheetName = context.TryGetSheetName(range.Start.Sheet);
        var start = new CellRefNode(
            Freexcel.Core.Model.CellAddress.NumberToColumnName(range.Start.Col),
            range.Start.Row,
            SheetName: sheetName);
        var end = new CellRefNode(
            Freexcel.Core.Model.CellAddress.NumberToColumnName(range.End.Col),
            range.End.Row,
            SheetName: sheetName);
        return BuildRangeValue(new RangeRefNode(start, end, sheetName), context);
    }

    private static Freexcel.Core.Model.GridRange? TryResolveStructuredReferenceRange(
        StructuredReferenceNode node,
        IEvalContext context)
        => StructuredReferenceResolver.ResolveDataBodyColumn(
            context.CurrentWorkbook,
            context.CurrentSheet,
            node.TableName,
            node.ColumnName,
            context.CurrentCellAddress);

    private static bool TryAsRangeRef(FormulaNode node, out RangeRefNode range)
    {
        range = node switch
        {
            RangeRefNode rr => rr,
            FullColumnRangeRefNode fcr => ToRangeRef(fcr),
            FullRowRangeRefNode frr => ToRangeRef(frr),
            _ => null!
        };
        return range is not null;
    }

    private static RangeRefNode ToRangeRef(FullColumnRangeRefNode range)
    {
        var start = new CellRefNode(range.StartColumnName, 1, range.IsStartAbsolute, false, range.SheetName);
        var end = new CellRefNode(range.EndColumnName, CellAddress.MaxRow, range.IsEndAbsolute);
        return new RangeRefNode(start, end, range.SheetName);
    }

    private static RangeRefNode ToRangeRef(FullRowRangeRefNode range)
    {
        var start = new CellRefNode("A", range.StartRow, false, range.IsStartAbsolute, range.SheetName);
        var end = new CellRefNode(CellAddress.NumberToColumnName(CellAddress.MaxCol), range.EndRow, false, range.IsEndAbsolute);
        return new RangeRefNode(start, end, range.SheetName);
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

    private ScalarValue EvaluateAstAware(FunctionCallNode node, IEvalContext context)
    {
        return node.FunctionName switch
        {
            "ISREF"        => EvaluateIsRef(node, context),
            "ISFORMULA"    => EvaluateIsFormula(node, context),
            "FORMULATEXT"  => EvaluateFormulaText(node, context),
            "OFFSET"       => EvaluateOffset(node, context),
            "LET"          => EvaluateLet(node, context),
            "LAMBDA"       => EvaluateLambda(node, context),
            _              => ErrorValue.Value
        };
    }

    private static ScalarValue EvaluateIsRef(FunctionCallNode node, IEvalContext context)
    {
        if (node.Arguments.Count != 1) return ErrorValue.Value;
        var arg = node.Arguments[0];
        return arg switch
        {
            CellRefNode cell  => new BoolValue(cell.SheetName is null || context.SheetExists(cell.SheetName)),
            RangeRefNode rng  => new BoolValue(rng.SheetName is null || context.SheetExists(rng.SheetName)),
            NamedRangeNode nm => new BoolValue(context.TryResolveNamedRange(nm.Name) is not null),
            _                 => new BoolValue(false)
        };
    }

    private ScalarValue EvaluateIsFormula(FunctionCallNode node, IEvalContext context)
    {
        if (node.Arguments.Count != 1) return ErrorValue.Value;
        var arg = node.Arguments[0];
        if (arg is NamedRangeNode nm)
        {
            var range = context.TryResolveNamedRange(nm.Name);
            if (range is null) return ErrorValue.Name;
            var r = range.Value;
            var sheetName = context.TryGetSheetName(r.Start.Sheet);
            var cell = sheetName is not null
                ? context.TryGetCell(sheetName, r.Start.Row, r.Start.Col)
                : context.TryGetCell(r.Start.Row, r.Start.Col);
            return new BoolValue(cell?.HasFormula == true);
        }
        if (arg is CellRefNode cellRef)
        {
            if (cellRef.SheetName is not null && !context.SheetExists(cellRef.SheetName))
                return ErrorValue.Ref;
            var cell = cellRef.SheetName is not null
                ? context.TryGetCell(cellRef.SheetName, cellRef.Row, cellRef.ColumnNumber)
                : context.TryGetCell(cellRef.Row, cellRef.ColumnNumber);
            return new BoolValue(cell?.HasFormula == true);
        }
        if (arg is RangeRefNode rangeRef)
        {
            if (rangeRef.SheetName is not null && !context.SheetExists(rangeRef.SheetName))
                return ErrorValue.Ref;
            var cell = rangeRef.SheetName is not null
                ? context.TryGetCell(rangeRef.SheetName, rangeRef.Start.Row, rangeRef.Start.ColumnNumber)
                : context.TryGetCell(rangeRef.Start.Row, rangeRef.Start.ColumnNumber);
            return new BoolValue(cell?.HasFormula == true);
        }
        return ErrorValue.Value;
    }

    private ScalarValue EvaluateFormulaText(FunctionCallNode node, IEvalContext context)
    {
        if (node.Arguments.Count != 1) return ErrorValue.NA;
        var arg = node.Arguments[0];
        Freexcel.Core.Model.Cell? cell = null;
        if (arg is CellRefNode cellRef)
        {
            if (cellRef.SheetName is not null && !context.SheetExists(cellRef.SheetName))
                return ErrorValue.Ref;
            cell = cellRef.SheetName is not null
                ? context.TryGetCell(cellRef.SheetName, cellRef.Row, cellRef.ColumnNumber)
                : context.TryGetCell(cellRef.Row, cellRef.ColumnNumber);
        }
        else if (arg is RangeRefNode rangeRef)
        {
            if (rangeRef.SheetName is not null && !context.SheetExists(rangeRef.SheetName))
                return ErrorValue.Ref;
            cell = rangeRef.SheetName is not null
                ? context.TryGetCell(rangeRef.SheetName, rangeRef.Start.Row, rangeRef.Start.ColumnNumber)
                : context.TryGetCell(rangeRef.Start.Row, rangeRef.Start.ColumnNumber);
        }
        else if (arg is NamedRangeNode nm)
        {
            var range = context.TryResolveNamedRange(nm.Name);
            if (range is null) return ErrorValue.Name;
            var r = range.Value;
            var sheetName = context.TryGetSheetName(r.Start.Sheet);
            cell = sheetName is not null
                ? context.TryGetCell(sheetName, r.Start.Row, r.Start.Col)
                : context.TryGetCell(r.Start.Row, r.Start.Col);
        }
        else
        {
            return ErrorValue.NA;
        }
        if (cell is null || !cell.HasFormula) return ErrorValue.NA;
        return new TextValue(cell.FormulaText!);
    }

    private ScalarValue EvaluateOffset(FunctionCallNode node, IEvalContext context)
    {
        if (node.Arguments.Count is < 3 or > 5) return ErrorValue.Value;
        var baseArg = node.Arguments[0];

        uint baseRow, baseCol; int baseHeight, baseWidth; string? baseSheet = null;
        switch (baseArg)
        {
            case CellRefNode cellRef:
                if (cellRef.SheetName is not null && !context.SheetExists(cellRef.SheetName))
                    return ErrorValue.Ref;
                baseRow = cellRef.Row; baseCol = cellRef.ColumnNumber;
                baseHeight = 1; baseWidth = 1;
                baseSheet = cellRef.SheetName;
                break;
            case RangeRefNode rangeRef:
                if (rangeRef.SheetName is not null && !context.SheetExists(rangeRef.SheetName))
                    return ErrorValue.Ref;
                uint r0 = Math.Min(rangeRef.Start.Row, rangeRef.End.Row);
                uint r1 = Math.Max(rangeRef.Start.Row, rangeRef.End.Row);
                uint c0 = Math.Min(rangeRef.Start.ColumnNumber, rangeRef.End.ColumnNumber);
                uint c1 = Math.Max(rangeRef.Start.ColumnNumber, rangeRef.End.ColumnNumber);
                baseRow = r0; baseCol = c0;
                baseHeight = (int)(r1 - r0 + 1);
                baseWidth = (int)(c1 - c0 + 1);
                baseSheet = rangeRef.SheetName;
                break;
            case NamedRangeNode nm:
                var nr = context.TryResolveNamedRange(nm.Name);
                if (nr is null) return ErrorValue.Name;
                var g = nr.Value;
                uint nr0 = Math.Min(g.Start.Row, g.End.Row);
                uint nr1 = Math.Max(g.Start.Row, g.End.Row);
                uint nc0 = Math.Min(g.Start.Col, g.End.Col);
                uint nc1 = Math.Max(g.Start.Col, g.End.Col);
                baseRow = nr0; baseCol = nc0;
                baseHeight = (int)(nr1 - nr0 + 1);
                baseWidth = (int)(nc1 - nc0 + 1);
                baseSheet = context.TryGetSheetName(g.Start.Sheet);
                break;
            default:
                return ErrorValue.Value;
        }

        var rowsArg = EvaluateNode(node.Arguments[1], context);
        if (rowsArg is ErrorValue er) return er;
        var colsArg = EvaluateNode(node.Arguments[2], context);
        if (colsArg is ErrorValue ec) return ec;
        var rowsCoerced = CoerceToNumber(rowsArg);
        if (rowsCoerced is ErrorValue erc) return erc;
        var colsCoerced = CoerceToNumber(colsArg);
        if (colsCoerced is ErrorValue ecc) return ecc;
        double dRows = ((NumberValue)rowsCoerced).Value;
        double dCols = ((NumberValue)colsCoerced).Value;
        if (!double.IsFinite(dRows) || !double.IsFinite(dCols)) return ErrorValue.Value;
        long rowsOff = (long)Math.Truncate(dRows);
        long colsOff = (long)Math.Truncate(dCols);

        int height = baseHeight;
        int width = baseWidth;
        if (node.Arguments.Count >= 4 && node.Arguments[3] is not OmittedArgumentNode)
        {
            var hArg = EvaluateNode(node.Arguments[3], context);
            if (hArg is ErrorValue eh) return eh;
            if (hArg is not BlankValue)
            {
                var hc = CoerceToNumber(hArg);
                if (hc is ErrorValue ehc) return ehc;
                double dh = ((NumberValue)hc).Value;
                if (!double.IsFinite(dh)) return ErrorValue.Value;
                height = (int)Math.Truncate(dh);
            }
        }
        if (node.Arguments.Count == 5 && node.Arguments[4] is not OmittedArgumentNode)
        {
            var wArg = EvaluateNode(node.Arguments[4], context);
            if (wArg is ErrorValue ew) return ew;
            if (wArg is not BlankValue)
            {
                var wc = CoerceToNumber(wArg);
                if (wc is ErrorValue ewc) return ewc;
                double dw = ((NumberValue)wc).Value;
                if (!double.IsFinite(dw)) return ErrorValue.Value;
                width = (int)Math.Truncate(dw);
            }
        }
        if (height == 0 || width == 0) return ErrorValue.Ref;

        long startRow = (long)baseRow + rowsOff;
        long startCol = (long)baseCol + colsOff;
        // Allow negative height/width: range extends upward/leftward from base
        long endRow = startRow + (height > 0 ? height - 1 : height + 1);
        long endCol = startCol + (width  > 0 ? width  - 1 : width  + 1);
        long r0Final = Math.Min(startRow, endRow);
        long r1Final = Math.Max(startRow, endRow);
        long c0Final = Math.Min(startCol, endCol);
        long c1Final = Math.Max(startCol, endCol);
        if (r0Final < 1 || c0Final < 1 ||
            r1Final > Freexcel.Core.Model.CellAddress.MaxRow ||
            c1Final > Freexcel.Core.Model.CellAddress.MaxCol)
            return ErrorValue.Ref;

        int rowSpan = (int)(r1Final - r0Final + 1);
        int colSpan = (int)(c1Final - c0Final + 1);
        if ((long)rowSpan * colSpan > 1_000_000L) return ErrorValue.Ref;

        if (rowSpan == 1 && colSpan == 1)
        {
            return baseSheet is not null
                ? context.GetCellValue(baseSheet, (uint)r0Final, (uint)c0Final)
                : context.GetCellValue((uint)r0Final, (uint)c0Final);
        }
        var cells = new ScalarValue[rowSpan, colSpan];
        for (int ri = 0; ri < rowSpan; ri++)
            for (int ci = 0; ci < colSpan; ci++)
            {
                cells[ri, ci] = baseSheet is not null
                    ? context.GetCellValue(baseSheet, (uint)(r0Final + ri), (uint)(c0Final + ci))
                    : context.GetCellValue((uint)(r0Final + ri), (uint)(c0Final + ci));
            }
        return new RangeValue(cells, (uint)r0Final, (uint)c0Final);
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

    private static bool IsSingleCellReferenceProvenanceArgument(string name, int argIndex) =>
        IsReferenceProvenanceAggregate(name) && (name != "NPV" || argIndex > 0);

    private static bool IsStructuredRangeFunction(string name) =>
        name is "VLOOKUP" or "HLOOKUP" or "INDEX" or "MATCH" or "XMATCH"
             or "SUMIF" or "COUNTIF" or "AVERAGEIF"
             or "SUMPRODUCT"
             or "LARGE" or "SMALL" or "RANK" or "RANK.EQ" or "RANK.AVG" or "DEVSQ"
             or "MULTINOMIAL" or "SERIESSUM"
             or "MMULT" or "MINVERSE" or "MDETERM"
             or "SUMIFS" or "COUNTIFS" or "AVERAGEIFS"
             or "XLOOKUP"
             or "WORKDAY" or "NETWORKDAYS" or "WORKDAY.INTL" or "NETWORKDAYS.INTL"
             or "CORREL" or "FORECAST" or "FORECAST.LINEAR"
             or "PERCENTILE" or "PERCENTILE.INC" or "PERCENTILE.EXC"
             or "QUARTILE" or "QUARTILE.INC"
             or "PERCENTRANK" or "PERCENTRANK.INC"
             or "LOOKUP"
             or "IRR"
             or "RANDARRAY"
             or "FILTER" or "SORT" or "SORTBY" or "TAKE" or "DROP" or "TRANSPOSE"
             or "CHOOSEROWS" or "CHOOSECOLS" or "VSTACK" or "HSTACK"
             or "TOROW" or "TOCOL" or "WRAPROWS" or "WRAPCOLS" or "EXPAND" or "UNIQUE"
             or "SUBTOTAL"
             or "DSUM" or "DAVERAGE" or "DCOUNT" or "DCOUNTA" or "DGET"
             or "DMAX" or "DMIN" or "DPRODUCT" or "DSTDEV" or "DSTDEVP"
             or "DVAR" or "DVARP"
             or "ROW" or "COLUMN" or "ROWS" or "COLUMNS" or "COUNTBLANK"
             or "AGGREGATE" or "CELL" or "GETPIVOTDATA"
             or "T.TEST" or "F.TEST" or "CHISQ.TEST"
             or "FREQUENCY"
             or "MIRR" or "XIRR" or "XNPV" or "FVSCHEDULE"
             or "MAP" or "REDUCE" or "SCAN" or "BYROW" or "BYCOL"
             or "TEXTJOIN" or "EXACT" or "CODE" or "CHAR" or "LEN" or "LEFT" or "RIGHT" or "MID" or "REPLACE"
             or "FIND" or "SEARCH"
             or "TRIM" or "UPPER" or "LOWER" or "PROPER" or "CLEAN"
             or "TEXT" or "VALUE"
             or "SUBSTITUTE" or "REPT" or "CONCATENATE"
             or "FIXED" or "DOLLAR" or "T" or "ENCODEURL" or "BAHTTEXT"
             or "ASC" or "DBCS"
             or "UNICHAR" or "UNICODE" or "NUMBERVALUE"
             or "ABS" or "SQRT" or "INT" or "SIGN"
             or "MOD" or "POWER" or "LOG" or "QUOTIENT" or "CEILING" or "FLOOR" or "MROUND"
             or "SIN" or "COS" or "TAN" or "DEGREES" or "RADIANS"
             or "ASIN" or "ACOS" or "ATAN" or "ATAN2" or "LN" or "EXP" or "FACT"
             or "ROUND" or "ROUNDUP" or "ROUNDDOWN" or "TRUNC"
             or "ISBLANK" or "ISNUMBER" or "ISTEXT" or "ISERROR" or "ISNA" or "ISLOGICAL"
             or "ISEVEN" or "ISODD" or "ODD" or "EVEN"
             or "YEAR" or "MONTH" or "DAY" or "HOUR" or "MINUTE" or "SECOND"
             or "WEEKDAY" or "WEEKNUM" or "ISOWEEKNUM" or "EDATE" or "EOMONTH"
             or "DATEVALUE" or "TIMEVALUE"
             or "DAYS" or "DAYS360" or "YEARFRAC"
             or "N" or "ERROR.TYPE"
             or "COMBIN" or "PERMUT"
             or "BITAND" or "BITOR" or "BITXOR" or "BITLSHIFT" or "BITRSHIFT"
             or "BIN2DEC" or "HEX2DEC" or "OCT2DEC"
             or "DEC2BIN" or "DEC2HEX" or "DEC2OCT"
             or "BIN2HEX" or "BIN2OCT" or "HEX2BIN" or "HEX2OCT" or "OCT2BIN" or "OCT2HEX"
             or "CONVERT"
             or "NORM.DIST" or "NORM.INV" or "NORM.S.DIST" or "NORM.S.INV" or "STANDARDIZE"
             or "GAMMA" or "GAMMALN" or "GAMMALN.PRECISE" or "GAMMA.DIST" or "GAMMA.INV"
             or "LOGNORM.DIST" or "LOGNORM.INV"
             or "BETA.DIST" or "BETA.INV"
             or "EXPON.DIST" or "WEIBULL.DIST" or "POISSON.DIST";

    private static bool IsSingleCellReferenceRangeFunction(string name) =>
        name is "ROW" or "COLUMN" or "ROWS" or "COLUMNS" or "COUNTBLANK" or "CELL" or "GETPIVOTDATA";

    private static ScalarValue CoerceToNumber(ScalarValue v) => v switch
    {
        ErrorValue e => e,
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

    // ── LET / LAMBDA evaluation ────────────────────────────────────────────

    private ScalarValue EvaluateLet(FunctionCallNode node, IEvalContext context)
    {
        // LET(name1, val1, ..., nameN, valN, calc_expr)
        // arg count must be odd and >= 3 (at least one binding pair + body)
        if (node.Arguments.Count < 3 || node.Arguments.Count % 2 == 0)
            return ErrorValue.Value;

        var bindings = new Dictionary<string, ScalarValue>(StringComparer.OrdinalIgnoreCase);
        var scoped = new ScopedEvalContext(context, bindings, this);

        int pairCount = (node.Arguments.Count - 1) / 2;
        for (int i = 0; i < pairCount; i++)
        {
            string? name = node.Arguments[i * 2] switch
            {
                NamedRangeNode nm => nm.Name,
                StringNode s     => s.Value,
                _                => null
            };
            if (name is null) return ErrorValue.Value;
            bindings[name] = EvaluateArrayOperand(node.Arguments[i * 2 + 1], scoped);
        }

        return EvaluateNode(node.Arguments[^1], scoped);
    }

    private static ScalarValue EvaluateLambda(FunctionCallNode node, IEvalContext _)
    {
        // LAMBDA([param1, param2, ...,] body)
        // All args except the last must be identifier (NamedRangeNode) parameter names.
        if (node.Arguments.Count < 1) return ErrorValue.Value;

        var paramNames = new List<string>(node.Arguments.Count - 1);
        for (int i = 0; i < node.Arguments.Count - 1; i++)
        {
            if (node.Arguments[i] is NamedRangeNode nm)
                paramNames.Add(nm.Name);
            else
                return ErrorValue.Value;
        }

        return new LambdaValue(paramNames, node.Arguments[^1]);
    }

    private ScalarValue InvokeLambdaWithArgs(LambdaValue lambda, IReadOnlyList<FormulaNode> argNodes, IEvalContext context)
    {
        if (argNodes.Count != lambda.Parameters.Count) return ErrorValue.Value;
        var args = new ScalarValue[argNodes.Count];
        for (int i = 0; i < argNodes.Count; i++)
            args[i] = EvaluateArrayOperand(argNodes[i], context);
        return context.InvokeLambda(lambda, args);
    }

    // ── Evaluation contexts ────────────────────────────────────────────────

    private sealed class SheetEvalContext : IEvalContext
    {
        private readonly Sheet _sheet;
        private readonly Freexcel.Core.Model.Workbook? _workbook;
        private readonly FormulaEvaluator _evaluator;
        private readonly Freexcel.Core.Model.CellAddress? _currentCellAddress;

        public SheetEvalContext(
            Sheet sheet,
            Freexcel.Core.Model.Workbook? workbook,
            FormulaEvaluator evaluator,
            Freexcel.Core.Model.CellAddress? currentCellAddress)
        {
            _sheet = sheet;
            _workbook = workbook;
            _evaluator = evaluator;
            _currentCellAddress = currentCellAddress;
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

        public Freexcel.Core.Model.Sheet? CurrentSheet => _sheet;

        public Freexcel.Core.Model.Workbook? CurrentWorkbook => _workbook;

        public Freexcel.Core.Model.CellAddress? CurrentCellAddress => _currentCellAddress;

        public Freexcel.Core.Model.Cell? TryGetCell(uint row, uint col) => _sheet.GetCell(row, col);

        public Freexcel.Core.Model.Cell? TryGetCell(string sheetName, uint row, uint col)
            => _workbook?.GetSheet(sheetName)?.GetCell(row, col);

        public ScalarValue? TryResolveLambdaBinding(string name) => null;

        public ScalarValue InvokeLambda(LambdaValue lambda, IReadOnlyList<ScalarValue> args)
        {
            if (args.Count != lambda.Parameters.Count) return ErrorValue.Value;
            var bindings = new Dictionary<string, ScalarValue>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < lambda.Parameters.Count; i++)
                bindings[lambda.Parameters[i]] = args[i];
            return _evaluator.EvaluateNode(lambda.Body, new ScopedEvalContext(this, bindings, _evaluator));
        }
    }

    // Wraps an IEvalContext with an extra layer of local name→value bindings (from LET).
    // Bindings in this layer shadow the inner context and can be mutated by EvaluateLet
    // before the body is evaluated (enabling forward references within the same LET).
    private sealed class ScopedEvalContext : IEvalContext
    {
        private readonly IEvalContext _inner;
        private readonly Dictionary<string, ScalarValue> _bindings;
        private readonly FormulaEvaluator _evaluator;

        public ScopedEvalContext(IEvalContext inner, Dictionary<string, ScalarValue> bindings, FormulaEvaluator evaluator)
        {
            _inner = inner;
            _bindings = bindings;
            _evaluator = evaluator;
        }

        public ScalarValue GetCellValue(uint row, uint col) => _inner.GetCellValue(row, col);
        public ScalarValue GetCellValue(string sn, uint row, uint col) => _inner.GetCellValue(sn, row, col);
        public IReadOnlyList<ScalarValue> GetRangeValues(uint r0, uint c0, uint r1, uint c1) => _inner.GetRangeValues(r0, c0, r1, c1);
        public IReadOnlyList<ScalarValue> GetRangeValues(string sn, uint r0, uint c0, uint r1, uint c1) => _inner.GetRangeValues(sn, r0, c0, r1, c1);
        public Freexcel.Core.Model.GridRange? TryResolveNamedRange(string name) => _inner.TryResolveNamedRange(name);
        public string? TryGetSheetName(Freexcel.Core.Model.SheetId id) => _inner.TryGetSheetName(id);
        public bool SheetExists(string sn) => _inner.SheetExists(sn);
        public bool IsRowHidden(uint row) => _inner.IsRowHidden(row);
        public Freexcel.Core.Model.Sheet? CurrentSheet => _inner.CurrentSheet;
        public Freexcel.Core.Model.Workbook? CurrentWorkbook => _inner.CurrentWorkbook;
        public Freexcel.Core.Model.CellAddress? CurrentCellAddress => _inner.CurrentCellAddress;
        public Freexcel.Core.Model.Cell? TryGetCell(uint row, uint col) => _inner.TryGetCell(row, col);
        public Freexcel.Core.Model.Cell? TryGetCell(string sn, uint row, uint col) => _inner.TryGetCell(sn, row, col);

        public ScalarValue? TryResolveLambdaBinding(string name) =>
            _bindings.TryGetValue(name, out var v) ? v : _inner.TryResolveLambdaBinding(name);

        public ScalarValue InvokeLambda(LambdaValue lambda, IReadOnlyList<ScalarValue> args)
        {
            if (args.Count != lambda.Parameters.Count) return ErrorValue.Value;
            var nb = new Dictionary<string, ScalarValue>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < lambda.Parameters.Count; i++) nb[lambda.Parameters[i]] = args[i];
            return _evaluator.EvaluateNode(lambda.Body, new ScopedEvalContext(this, nb, _evaluator));
        }
    }
}

/// <summary>A first-class function value created by LAMBDA. Holds parameter names and the unevaluated body AST.</summary>
public sealed record LambdaValue(IReadOnlyList<string> Parameters, FormulaNode Body) : ScalarValue;

internal sealed record DirectTextLiteralValue(string Value) : ScalarValue;
internal sealed record ReferencedScalarValue(ScalarValue Value) : ScalarValue;
