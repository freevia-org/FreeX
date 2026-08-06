using System.Runtime.ExceptionServices;
using FreeX.Core.Model;

namespace FreeX.Core.Formula;

/// <summary>
/// Evaluates a formula AST against a worksheet to produce a ScalarValue.
/// This is the heart of the formula engine.
/// </summary>
public sealed partial class FormulaEvaluator
{
    /// <summary>
    /// Default maximum recursive evaluation depth before returning #NUM!, used for every
    /// evaluation attempt on the caller's own thread. A single formula can nest at most this many
    /// EvaluateNode calls deep before we cut off. This prevents deeply-nested or circular-looking
    /// formulas from causing a StackOverflowException that would crash the process. This value
    /// (256) is deliberately conservative and was verified empirically (see this class's remarks
    /// on <c>the former expanded depth budget</c>) to be safe on the default ~1MB thread stack even for
    /// the tightest recursion shape (a bare self-call with no intervening arithmetic).
    /// Trade-off: a formula that legitimately needs to recurse deeper than this (e.g. an ordinary
    /// recursive LAMBDA to ~200+ levels) simply returns #NUM! rather than being retried with a
    /// raised budget, because raising this constant directly was found (empirically — an
    /// earlier attempt to raise this to 1024 crashed the process with a real
    /// StackOverflowException around ~430 real recursion levels, well before the raised guard
    /// itself would have tripped) to risk a genuine stack overflow on the default thread stack.
    /// (Round 72 tried a large-stack worker-thread escalation instead of raising this constant, but
    /// round 75 found it could not bound a truly-infinite recursion before its own stack overflowed
    /// — an uncatchable StackOverflowException that terminated the whole process — so it was
    /// removed; a stack-SAFE deep-recursion path is deferred to its own task.)
    /// </summary>
    private const int MaxEvalDepth = 256;

    /// <summary>
    /// Per-thread evaluation depth counter. ThreadStatic avoids the need to thread
    /// the counter through every EvaluateNode call or add it to IEvalContext
    /// (which has many implementations). Reset to 0 at each public Evaluate() entry.
    /// </summary>
    [ThreadStatic]
    private static int _evalDepth;

    /// <summary>
    /// Per-thread effective recursion budget EvaluateNode checks against — always <see cref="MaxEvalDepth"/>
    /// for a normal (caller's-thread) evaluation attempt (the round-72 large-stack escalation that used
    /// to raise this on a worker thread was removed in round 75; see <see cref="MaxEvalDepth"/>'s remarks).
    /// Set at the start of every public Evaluate() entry point, immediately before resetting
    /// <see cref="_evalDepth"/> to 0.
    /// </summary>
    [ThreadStatic]
    private static int _effectiveMaxEvalDepth;

    private const int CachedIntegerNumberMax = 64;
    private static readonly BoolValue TrueValue = new(true);
    private static readonly BoolValue FalseValue = new(false);
    private static readonly TextValue EmptyTextValue = new(string.Empty);
    private static readonly NumberValue[] CachedIntegerNumberValues = CreateCachedIntegerNumberValues();

    private static NumberValue NumberValueFor(double value)
    {
        if (value == 0d)
            return BitConverter.DoubleToInt64Bits(value) == 0L
                ? CachedIntegerNumberValues[0]
                : new NumberValue(value);

        if (value > 0d && value <= CachedIntegerNumberMax)
        {
            var integer = (int)value;
            if (integer == value)
                return CachedIntegerNumberValues[integer];
        }

        return new NumberValue(value);
    }

    private static NumberValue[] CreateCachedIntegerNumberValues()
    {
        var values = new NumberValue[CachedIntegerNumberMax + 1];
        for (var value = 0; value < values.Length; value++)
            values[value] = new NumberValue(value);
        return values;
    }


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
            var ast = GetOrParseFormulaForInstance(formulaText);
            var context = workbook is null && currentCell is null
                ? GetSingleSheetEvalContext(sheet)
                : new SheetEvalContext(sheet, workbook, this, currentCell);
            _effectiveMaxEvalDepth = MaxEvalDepth;
            _evalDepth = 0;
            return NormalizeTopLevelResult(EvaluateNode(ast, context));
        }
        catch (FormulaEvalException ex)
        {
            return ErrorFromCode(ex.ErrorCode);
        }
        catch (FormulaParseException)
        {
            // An unparseable formula (e.g. a phone number typed as "+389 78 609-030") is an error, not
            // a crash. Return #VALUE! so direct callers behave like a recalc, which already does this.
            return ErrorValue.Value;
        }
    }

    /// <summary>
    /// Evaluate a pre-parsed AST against a sheet.
    /// </summary>
    public ScalarValue Evaluate(
        FormulaNode ast,
        Sheet sheet,
        FreeX.Core.Model.Workbook? workbook = null,
        FreeX.Core.Model.CellAddress? currentCell = null,
        bool isIterativeCalculationPass = false)
    {
        try
        {
            var context = workbook is null && currentCell is null && !isIterativeCalculationPass
                ? GetSingleSheetEvalContext(sheet)
                : new SheetEvalContext(sheet, workbook, this, currentCell, isIterativeCalculationPass);
            _effectiveMaxEvalDepth = MaxEvalDepth;
            _evalDepth = 0;
            return NormalizeTopLevelResult(EvaluateNode(ast, context));
        }
        catch (FormulaEvalException ex)
        {
            return ErrorFromCode(ex.ErrorCode);
        }
    }

    /// <summary>
    /// Evaluate a pre-parsed AST in dynamic-array (spilling) context. Identical to
    /// <see cref="Evaluate(FormulaNode, Sheet, FreeX.Core.Model.Workbook?, FreeX.Core.Model.CellAddress?)"/>
    /// except that a top-level reference-like node (a bare range, full row/column, named range, or
    /// structured reference) returns the entire referenced range as a <see cref="RangeValue"/> so it
    /// spills, instead of collapsing to its top-left cell via implicit intersection. This matches
    /// Excel's behaviour for a modern dynamic-array formula whose body is a bare range (e.g. =A1:C3).
    /// </summary>
    public ScalarValue EvaluateSpilling(
        FormulaNode ast,
        Sheet sheet,
        FreeX.Core.Model.Workbook? workbook = null,
        FreeX.Core.Model.CellAddress? currentCell = null,
        bool isIterativeCalculationPass = false)
    {
        try
        {
            var context = workbook is null && currentCell is null && !isIterativeCalculationPass
                ? GetSingleSheetEvalContext(sheet)
                : new SheetEvalContext(sheet, workbook, this, currentCell, isIterativeCalculationPass);

            _effectiveMaxEvalDepth = MaxEvalDepth;
            _evalDepth = 0;

            // Only top-level reference nodes need the spilling treatment; every other node
            // already produces a RangeValue when it yields an array (functions, operators,
            // array constants).
            var result = ast is RangeRefNode or FullColumnRangeRefNode or FullRowRangeRefNode
                    or NamedRangeNode or StructuredReferenceNode or StructuredCurrentRowReferenceNode
                    or IntersectionNode or NamedRangeEndpointNode
                ? EvaluateArrayOperand(ast, context)
                : EvaluateNode(ast, context);

            // A bare union reference as a formula's entire body (e.g. =(A1:B2,D5) with no
            // enclosing function) has no scalar/array reduction in Excel either -- entering that
            // directly in a cell yields #VALUE! there too, since a union is only meaningful as a
            // reference-taking function argument (AREAS, SUM, ...). NormalizeTopLevelResult below
            // handles the ordinary Evaluate() entry point's equivalent case.

            return NormalizeTopLevelResult(result);
        }
        catch (FormulaEvalException ex)
        {
            return ErrorFromCode(ex.ErrorCode);
        }
    }

    private static ScalarValue NormalizeTopLevelResult(ScalarValue value) =>
        value switch
        {
            // Excel rule: a formula whose final result is blank (i.e. a bare reference to an empty
            // cell, e.g. =A1 where A1 is empty) evaluates to 0, not blank.
            // ISBLANK / concatenation / comparisons are unaffected because they receive BlankValue
            // as an *argument* (before this normalization step) and handle it internally.
            BlankValue => NumberValueFor(0d),
            LambdaValue => ErrorValue.Calc,
            // A UnionValue reaching the top level (bare "=(A1:B2,D5)" formula body, outside any
            // function argument position) mirrors Excel: a union reference is only valid as an
            // argument to a reference-taking function, never as a standalone value.
            UnionValue => ErrorValue.Value,
            _ => value,
        };

    /// <summary>
    /// Evaluate an AST node recursively.
    /// </summary>
    internal ScalarValue EvaluateNode(FormulaNode node, IEvalContext context)
    {
        if (_evalDepth >= _effectiveMaxEvalDepth)
            return ErrorValue.Num;

        _evalDepth++;
        try
        {
            return node switch
            {
                NumberNode n => NumberValueFor(n.Value),
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
                IntersectionNode intersection => EvaluateIntersectionNode(intersection, context),
                NamedRangeEndpointNode endpoint => EvaluateNamedRangeEndpointNode(endpoint, context),
                UnionNode union => EvaluateUnionNode(union, context),
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

/// <summary>
/// A first-class function value created by LAMBDA. Holds parameter names, the unevaluated body AST,
/// and the lexical environment (<see cref="Closure"/>) captured at the point the LAMBDA(...) expression
/// was evaluated — e.g. the LET scope that defined it. Excel LAMBDA is lexically scoped: free variables
/// in the body resolve against the definition-site environment, never the call site's, so this is
/// evaluated against <c>Closure</c> (falling back to the call-site context only for a LambdaValue that
/// captured no enclosing scope, i.e. a bare top-level LAMBDA).
/// </summary>
public sealed record LambdaValue(IReadOnlyList<string> Parameters, FormulaNode Body, IEvalContext? Closure) : ScalarValue;

internal sealed record DirectTextLiteralValue(string Value) : ScalarValue;
internal sealed record ReferencedScalarValue(ScalarValue Value) : ScalarValue;
internal sealed record OmittedLambdaArgumentValue : ScalarValue
{
    public static readonly OmittedLambdaArgumentValue Instance = new();
}

/// <summary>
/// Sentinel substituted for TEXTSPLIT's pad_with argument (argument index 5) only when the raw
/// AST shows the argument slot itself was genuinely left empty (a trailing comma with nothing
/// after it, or the argument omitted entirely) -- as opposed to an explicit argument that merely
/// evaluates to a blank value (e.g. a reference to an empty cell). Both cases would otherwise
/// collapse to the same <see cref="BlankValue.Instance"/> singleton by the time
/// BuiltInFunctions.TextSplit.cs sees them, making the two indistinguishable; this sentinel
/// preserves the distinction through the generic value-expansion pipeline in
/// FormulaEvaluator.Functions.cs, mirroring OmittedLambdaArgumentValue's identical role for
/// LAMBDA/ISOMITTED.
/// </summary>
internal sealed record TextSplitOmittedPadArgumentValue : ScalarValue
{
    public static readonly TextSplitOmittedPadArgumentValue Instance = new();
}
