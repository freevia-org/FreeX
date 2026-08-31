namespace FreeX.Core.Formula;

public sealed partial class FormulaEvaluator
{
    /// <summary>
    /// Excel's documented formula-length cap: a typed formula (including the leading "=") longer
    /// than this is refused by Excel's formula bar at entry, rather than committed
    /// (R120-formula-entry-nesting-length-validation).
    /// </summary>
    public const int MaxFormulaEntryLength = 8_192;

    /// <summary>
    /// Excel's documented function-nesting cap: a formula whose deepest chain of one function
    /// call nested as an argument of another exceeds this many levels is refused by Excel's
    /// formula bar at entry (R120-formula-entry-nesting-length-validation).
    /// </summary>
    public const int MaxNestedFunctionLevels = 64;

    /// <summary>
    /// Validates that <paramref name="enteredText"/> (the raw text typed into the formula bar,
    /// including its leading "=") does not exceed Excel's documented 8,192-character formula-length
    /// limit, throwing a <see cref="FormulaParseException"/> if it does
    /// (R120-formula-entry-nesting-length-validation).
    ///
    /// This is distinct from <see cref="FormulaSafetyLimits.MaxParseTokens"/>/
    /// <see cref="FormulaSafetyLimits.MaxParseDepth"/>/<see cref="FormulaSafetyLimits.MaxParseNesting"/>,
    /// which are purely internal DoS guards bounding recursion/stack depth for pathological input
    /// and are (intentionally) far larger than any real Excel-authorable formula. This check exists
    /// to match Excel's actual, much smaller, documented limit at formula-entry time (see
    /// <see cref="FreeX.App.Services.CellEntryParser.CreateCell"/>), not to guard against abuse.
    /// </summary>
    public static void ValidateFormulaEntryLength(string enteredText)
    {
        if (enteredText.Length > MaxFormulaEntryLength)
        {
            throw new FormulaParseException(
                $"Formula is too long; maximum length is {MaxFormulaEntryLength} characters, got {enteredText.Length}.");
        }
    }

    /// <summary>
    /// Validates that <paramref name="root"/>'s deepest chain of one function call nested inside
    /// another function call's argument list does not exceed Excel's documented 64-level
    /// function-nesting limit, throwing a <see cref="FormulaParseException"/> if it does
    /// (R120-formula-entry-nesting-length-validation).
    ///
    /// Real Excel refuses to leave edit mode for a formula built with more than 64 nested function
    /// levels (e.g. 100 nested <c>IF()</c> calls), popping its "too many levels of nesting" error
    /// instead of committing the text. FreeX previously only bounded nesting via
    /// <see cref="Parser.EnterNesting"/>'s <see cref="FormulaSafetyLimits.MaxParseNesting"/> (256)
    /// -- a generic paren/brace/call recursion-depth DoS guard, not Excel's actual function-nesting
    /// limit, and one that also counts plain grouping parens and array-constant braces alongside
    /// function calls. This walks the already-parsed AST and counts only actual
    /// <see cref="FunctionCallNode"/> nesting (mirroring <see cref="ValidateBuiltInFunctionArity"/>'s
    /// own walk over every <see cref="FormulaNode"/> subtype), so a 100-level-deep chain of
    /// <c>IF()</c> calls is rejected at entry exactly like Excel, while a formula with 100 sibling
    /// (non-nested) function calls -- e.g. <c>=SUM(f1(),f2(),...,f100())</c> -- is left alone, since
    /// none of those individually nest more than one level deep.
    /// </summary>
    public static void ValidateFunctionNestingDepth(FormulaNode root) =>
        ValidateFormulaEntryAstCore(root, validateArity: false, validateNesting: true);

    /// <summary>
    /// Validates all entry-time constraints that require walking a parsed formula tree. Arity is
    /// checked before a recorded nesting violation is reported, preserving the historical
    /// validation order while traversing the tree only once.
    /// </summary>
    internal static void ValidateFormulaEntryAst(FormulaNode root) =>
        ValidateFormulaEntryAstCore(root, validateArity: true, validateNesting: true);

    /// <summary>
    /// Iteratively walks the complete formula-node family. Children are pushed in reverse source
    /// order so the stack processes them in the same left-to-right depth-first order as the former
    /// recursive arity validator. This also keeps left-deep operator chains with thousands of nodes
    /// within the formula-entry limit on the managed heap instead of consuming one native call-stack
    /// frame per node.
    /// </summary>
    private static void ValidateFormulaEntryAstCore(
        FormulaNode root,
        bool validateArity,
        bool validateNesting)
    {
        var pending = new Stack<(FormulaNode Node, int FunctionDepth)>();
        pending.Push((root, 0));
        var maxDepth = 0;

        while (pending.TryPop(out var item))
        {
            var (node, functionDepth) = item;

            switch (node)
            {
                case FunctionCallNode call:
                {
                    if (validateArity)
                        ValidateBuiltInCallArity(call);

                    var childDepth = validateNesting ? functionDepth + 1 : 0;
                    if (validateNesting && childDepth > maxDepth)
                        maxDepth = childDepth;

                    for (var i = call.Arguments.Count - 1; i >= 0; i--)
                        pending.Push((call.Arguments[i], childDepth));
                    break;
                }

                case BinaryOpNode binaryOp:
                    pending.Push((binaryOp.Right, functionDepth));
                    pending.Push((binaryOp.Left, functionDepth));
                    break;

                case UnaryOpNode unaryOp:
                    pending.Push((unaryOp.Operand, functionDepth));
                    break;

                case IntersectionNode intersection:
                    pending.Push((intersection.Right, functionDepth));
                    pending.Push((intersection.Left, functionDepth));
                    break;

                case NamedRangeEndpointNode endpoint:
                    pending.Push((endpoint.End, functionDepth));
                    pending.Push((endpoint.Start, functionDepth));
                    break;

                case UnionNode union:
                    for (var i = union.Areas.Count - 1; i >= 0; i--)
                        pending.Push((union.Areas[i], functionDepth));
                    break;

                case ArrayConstantNode array:
                    for (var rowIndex = array.Rows.Count - 1; rowIndex >= 0; rowIndex--)
                    {
                        var row = array.Rows[rowIndex];
                        for (var cellIndex = row.Count - 1; cellIndex >= 0; cellIndex--)
                            pending.Push((row[cellIndex], functionDepth));
                    }
                    break;

                // NumberNode, StringNode, BooleanNode, OmittedArgumentNode, CellRefNode,
                // RangeRefNode, FullColumnRangeRefNode, FullRowRangeRefNode, NamedRangeNode,
                // StructuredReferenceNode, StructuredCurrentRowReferenceNode, and ErrorNode are all
                // leaves for this validation walk. RangeRefNode's endpoints are sealed CellRefNode
                // leaves, so no function subtree can be hidden inside them.
                default:
                    break;
            }
        }

        if (validateNesting && maxDepth > MaxNestedFunctionLevels)
        {
            throw new FormulaParseException(
                $"Formula contains too many nested function levels; maximum is {MaxNestedFunctionLevels}.");
        }
    }
}
