using FreeX.Core.Model;

namespace FreeX.Core.Formula;

/// <summary>
/// Evaluates a formula AST against a worksheet to produce a ScalarValue.
/// This is the heart of the formula engine.
/// </summary>
public sealed partial class FormulaEvaluator
{
    /// <summary>
    /// Maximum recursive evaluation depth before returning #NUM!.
    /// A single formula can nest at most this many EvaluateNode calls deep before we
    /// cut off. This prevents deeply-nested or circular-looking formulas from
    /// causing a StackOverflowException that would crash the process.
    /// Trade-off: extremely pathological nesting (>256 levels) returns #NUM!
    /// rather than the "correct" result, but such formulas don't arise in practice.
    /// </summary>
    private const int MaxEvalDepth = 256;

    /// <summary>
    /// Per-thread evaluation depth counter. ThreadStatic avoids the need to thread
    /// the counter through every EvaluateNode call or add it to IEvalContext
    /// (which has many implementations). Reset to 0 at each public Evaluate() entry.
    /// </summary>
    [ThreadStatic]
    private static int _evalDepth;

    private static readonly BoolValue TrueValue = new(true);
    private static readonly BoolValue FalseValue = new(false);

    /// <summary>
    /// Parse and evaluate a formula string against a sheet.
    /// </summary>
    public ScalarValue Evaluate(
        string formulaText,
        Sheet sheet,
        FreeX.Core.Model.Workbook? workbook = null,
        FreeX.Core.Model.CellAddress? currentCell = null)
    {
        try
        {
            _evalDepth = 0;
            var ast = GetOrParseFormulaForInstance(formulaText);
            var context = workbook is null && currentCell is null
                ? GetSingleSheetEvalContext(sheet)
                : new SheetEvalContext(sheet, workbook, this, currentCell);
            return NormalizeTopLevelResult(EvaluateNode(ast, context));
        }
        catch (FormulaEvalException ex)
        {
            return ErrorFromCode(ex.ErrorCode);
        }
    }

    /// <summary>
    /// Evaluate a pre-parsed AST against a sheet.
    /// </summary>
    public ScalarValue Evaluate(
        FormulaNode ast,
        Sheet sheet,
        FreeX.Core.Model.Workbook? workbook = null,
        FreeX.Core.Model.CellAddress? currentCell = null)
    {
        try
        {
            _evalDepth = 0;
            var context = workbook is null && currentCell is null
                ? GetSingleSheetEvalContext(sheet)
                : new SheetEvalContext(sheet, workbook, this, currentCell);
            return NormalizeTopLevelResult(EvaluateNode(ast, context));
        }
        catch (FormulaEvalException ex)
        {
            return ErrorFromCode(ex.ErrorCode);
        }
    }

    private static ScalarValue NormalizeTopLevelResult(ScalarValue value) =>
        value is LambdaValue ? ErrorValue.Calc : value;

    /// <summary>
    /// Evaluate an AST node recursively.
    /// </summary>
    internal ScalarValue EvaluateNode(FormulaNode node, IEvalContext context)
    {
        if (_evalDepth >= MaxEvalDepth)
            return ErrorValue.Num;

        _evalDepth++;
        try
        {
            return node switch
            {
                NumberNode n => new NumberValue(n.Value),
                StringNode s => new TextValue(s.Value),
                BooleanNode b => b.Value ? TrueValue : FalseValue,
                OmittedArgumentNode => BlankValue.Instance,
                ArrayConstantNode array => EvaluateArrayConstant(array, context),
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
        finally
        {
            _evalDepth--;
        }
    }

    private ScalarValue EvaluateArrayConstant(ArrayConstantNode node, IEvalContext context)
    {
        int rowCount = node.Rows.Count;
        int colCount = node.Rows[0].Count;
        var cells = new ScalarValue[rowCount, colCount];

        for (int r = 0; r < rowCount; r++)
            for (int c = 0; c < colCount; c++)
                cells[r, c] = EvaluateNode(node.Rows[r][c], context);

        return new RangeValue(cells);
    }

}

/// <summary>A first-class function value created by LAMBDA. Holds parameter names and the unevaluated body AST.</summary>
public sealed record LambdaValue(IReadOnlyList<string> Parameters, FormulaNode Body) : ScalarValue;

internal sealed record DirectTextLiteralValue(string Value) : ScalarValue;
internal sealed record ReferencedScalarValue(ScalarValue Value) : ScalarValue;
internal sealed record OmittedLambdaArgumentValue : ScalarValue
{
    public static readonly OmittedLambdaArgumentValue Instance = new();
}
