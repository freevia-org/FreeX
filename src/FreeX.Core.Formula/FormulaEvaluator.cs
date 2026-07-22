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
    /// on <see cref="ExpandedMaxEvalDepth"/>) to be safe on the default ~1MB thread stack even for
    /// the tightest recursion shape (a bare self-call with no intervening arithmetic).
    /// Trade-off: a formula that legitimately needs to recurse deeper than this (e.g. an ordinary
    /// recursive LAMBDA to ~200+ levels) is retried on a dedicated large-stack worker thread with
    /// <see cref="ExpandedMaxEvalDepth"/> — see <see cref="RunWithDepthEscalation"/> — rather than
    /// simply raising this constant, because raising it directly was found (empirically — an
    /// earlier attempt to raise this to 1024 crashed the process with a real
    /// StackOverflowException around ~430 real recursion levels, well before the raised guard
    /// itself would have tripped) to risk a genuine stack overflow on the default thread stack.
    /// </summary>
    private const int MaxEvalDepth = 256;

    /// <summary>
    /// Expanded recursion budget used only on the dedicated large-stack worker thread that
    /// <see cref="RunWithDepthEscalation"/> spins up when the default (<see cref="MaxEvalDepth"/>)
    /// budget is exhausted by genuine recursion (as opposed to some other #NUM! cause). Each
    /// self-recursive LAMBDA call (e.g. LET(f, LAMBDA(n, IF(n=0,0,n+f(n-1))), f(n))) consumes ~3
    /// EvaluateNode levels per real recursion level (the FunctionCallNode invocation, the IF
    /// condition, and the arithmetic on the recursive result), so 4096 permits ~1365 real
    /// recursion levels — comfortably above the ~200 levels Excel itself supports for ordinary
    /// recursive LAMBDA formulas — while <see cref="LargeStackSizeBytes"/>'s 16x-larger stack
    /// keeps this within the empirically-established safe ratio (~430 raw eval-depth units per
    /// 1MB of stack), still cutting off a genuinely infinite recursion with #NUM! instead of a
    /// StackOverflowException.
    /// </summary>
    private const int ExpandedMaxEvalDepth = 4096;

    /// <summary>
    /// Stack size for the dedicated worker thread <see cref="RunWithDepthEscalation"/> spins up to
    /// retry a formula whose recursion exceeded <see cref="MaxEvalDepth"/> on the caller's own
    /// (default-sized, ~1MB) thread stack.
    /// </summary>
    private const int LargeStackSizeBytes = 16 * 1024 * 1024;

    /// <summary>
    /// Per-thread evaluation depth counter. ThreadStatic avoids the need to thread
    /// the counter through every EvaluateNode call or add it to IEvalContext
    /// (which has many implementations). Reset to 0 at each public Evaluate() entry.
    /// </summary>
    [ThreadStatic]
    private static int _evalDepth;

    /// <summary>
    /// Per-thread effective recursion budget EvaluateNode checks against — <see cref="MaxEvalDepth"/>
    /// for a normal (caller's-thread) evaluation attempt, or <see cref="ExpandedMaxEvalDepth"/> when
    /// running on the large-stack retry thread <see cref="RunWithDepthEscalation"/> spins up. Set at
    /// the start of every evaluation attempt (see <see cref="RunWithDepthEscalation"/>).
    /// </summary>
    [ThreadStatic]
    private static int _effectiveMaxEvalDepth;

    /// <summary>
    /// Per-thread flag set by EvaluateNode when it actually cuts off recursion by returning
    /// #NUM! because <see cref="_evalDepth"/> reached <see cref="_effectiveMaxEvalDepth"/> — as
    /// opposed to a formula that legitimately computes #NUM! some other way (e.g. a genuine
    /// numeric-domain error). <see cref="RunWithDepthEscalation"/> uses this to decide whether an
    /// initial #NUM! result is worth retrying on the large-stack thread.
    /// </summary>
    [ThreadStatic]
    private static bool _depthGuardTripped;

    /// <summary>
    /// Runs <paramref name="attempt"/> (a top-level evaluation) with <see cref="MaxEvalDepth"/> as
    /// its recursion budget. If that budget is exhausted by genuine recursion (see
    /// <see cref="_depthGuardTripped"/>) — most commonly an ordinary recursive LAMBDA nesting more
    /// deeply than is safe on the default ~1MB thread stack — <paramref name="attempt"/> is retried
    /// once on a dedicated worker thread with a <see cref="LargeStackSizeBytes"/>-sized stack and
    /// the much higher <see cref="ExpandedMaxEvalDepth"/> budget, so an ordinary deep-but-finite
    /// recursive formula (up to ~200+ real levels) computes its correct result instead of #NUM!,
    /// while a genuinely infinite recursion still exhausts the expanded budget and returns #NUM!
    /// rather than crashing the process. This keeps the common (shallow-formula) path entirely on
    /// the caller's own thread with zero added cost — the extra thread is only created for the
    /// rare pathologically-deep formula.
    /// </summary>
    private static ScalarValue RunWithDepthEscalation(Func<ScalarValue> attempt)
    {
        _effectiveMaxEvalDepth = MaxEvalDepth;
        _depthGuardTripped = false;
        var result = attempt();

        if (result is not ErrorValue { Code: "#NUM!" } || !_depthGuardTripped)
            return result;

        ScalarValue? escalatedResult = null;
        ExceptionDispatchInfo? capturedException = null;
        var worker = new Thread(() =>
        {
            _evalDepth = 0;
            _effectiveMaxEvalDepth = ExpandedMaxEvalDepth;
            _depthGuardTripped = false;
            try
            {
                escalatedResult = attempt();
            }
            catch (Exception ex)
            {
                capturedException = ExceptionDispatchInfo.Capture(ex);
            }
        }, LargeStackSizeBytes)
        {
            IsBackground = true,
        };
        worker.Start();
        worker.Join();

        capturedException?.Throw();
        return escalatedResult!;
    }

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
            return RunWithDepthEscalation(() =>
            {
                _evalDepth = 0;
                return NormalizeTopLevelResult(EvaluateNode(ast, context));
            });
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
        FreeX.Core.Model.CellAddress? currentCell = null)
    {
        try
        {
            var context = workbook is null && currentCell is null
                ? GetSingleSheetEvalContext(sheet)
                : new SheetEvalContext(sheet, workbook, this, currentCell);
            return RunWithDepthEscalation(() =>
            {
                _evalDepth = 0;
                return NormalizeTopLevelResult(EvaluateNode(ast, context));
            });
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
        FreeX.Core.Model.CellAddress? currentCell = null)
    {
        try
        {
            var context = workbook is null && currentCell is null
                ? GetSingleSheetEvalContext(sheet)
                : new SheetEvalContext(sheet, workbook, this, currentCell);

            return RunWithDepthEscalation(() =>
            {
                _evalDepth = 0;

                // Only top-level reference nodes need the spilling treatment; every other node
                // already produces a RangeValue when it yields an array (functions, operators,
                // array constants).
                var result = ast is RangeRefNode or FullColumnRangeRefNode or FullRowRangeRefNode
                        or NamedRangeNode or StructuredReferenceNode or StructuredCurrentRowReferenceNode
                        or IntersectionNode or NamedRangeEndpointNode
                    ? EvaluateArrayOperand(ast, context)
                    : EvaluateNode(ast, context);

                return NormalizeTopLevelResult(result);
            });
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
            _ => value,
        };

    /// <summary>
    /// Evaluate an AST node recursively.
    /// </summary>
    internal ScalarValue EvaluateNode(FormulaNode node, IEvalContext context)
    {
        if (_evalDepth >= _effectiveMaxEvalDepth)
        {
            _depthGuardTripped = true;
            return ErrorValue.Num;
        }

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
