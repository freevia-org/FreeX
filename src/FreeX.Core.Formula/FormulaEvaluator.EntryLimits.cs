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
    public static void ValidateFunctionNestingDepth(FormulaNode root)
    {
        if (GetFunctionNestingDepth(root) > MaxNestedFunctionLevels)
        {
            throw new FormulaParseException(
                $"Formula contains too many nested function levels; maximum is {MaxNestedFunctionLevels}.");
        }
    }

    /// <summary>
    /// Returns the deepest chain of <see cref="FunctionCallNode"/> nesting reachable from
    /// <paramref name="root"/> -- 0 if no function call is reachable at all, otherwise 1 plus the
    /// deepest nesting reachable through any of a function call's own arguments. Non-function
    /// structural nodes (binary/unary operators, unions, array constants, etc.) are transparent:
    /// they don't add a level themselves, they just pass the deepest level found in their operands
    /// through, mirroring how Excel only counts actual function-in-function nesting, not every
    /// syntactic grouping. Covers the exact same <see cref="FormulaNode"/> subtype family as
    /// <see cref="ValidateBuiltInFunctionArity"/>'s walk.
    ///
    /// Deliberately iterative (an explicit heap-allocated <see cref="Stack{T}"/> of
    /// work-items) rather than a natively-recursive method call per node. A chained-operator
    /// formula such as <c>1+1+1+...+1</c> parses to a purely left-deep <see cref="BinaryOpNode"/>
    /// chain -- <see cref="Parser.ParseAddition"/> and its sibling precedence levels build that
    /// shape with a <c>while</c> loop, so it is NOT bounded by <see cref="Parser.EnterParseFrame"/>'s
    /// recursion-depth guard (<see cref="FormulaSafetyLimits.MaxParseDepth"/>) the way genuinely
    /// recursive-descent constructs (nested parens/calls/braces) are; only its raw token count is
    /// bounded (<see cref="FormulaSafetyLimits.MaxParseTokens"/> = 16,384). A chain thousands of
    /// levels deep -- entirely possible within Excel's own 8,192-character formula-length limit --
    /// would overflow the native call stack if this walk recursed one C# method call per
    /// <see cref="BinaryOpNode"/> level; using an explicit stack keeps the walk safe regardless of
    /// how deep that chain gets.
    /// </summary>
    private static int GetFunctionNestingDepth(FormulaNode root)
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
                    var childDepth = functionDepth + 1;
                    if (childDepth > maxDepth)
                        maxDepth = childDepth;

                    foreach (var argument in call.Arguments)
                        pending.Push((argument, childDepth));
                    break;
                }

                case BinaryOpNode binaryOp:
                    pending.Push((binaryOp.Left, functionDepth));
                    pending.Push((binaryOp.Right, functionDepth));
                    break;

                case UnaryOpNode unaryOp:
                    pending.Push((unaryOp.Operand, functionDepth));
                    break;

                case IntersectionNode intersection:
                    pending.Push((intersection.Left, functionDepth));
                    pending.Push((intersection.Right, functionDepth));
                    break;

                case NamedRangeEndpointNode endpoint:
                    pending.Push((endpoint.Start, functionDepth));
                    pending.Push((endpoint.End, functionDepth));
                    break;

                case UnionNode union:
                    foreach (var area in union.Areas)
                        pending.Push((area, functionDepth));
                    break;

                case ArrayConstantNode array:
                    foreach (var row in array.Rows)
                        foreach (var cell in row)
                            pending.Push((cell, functionDepth));
                    break;

                // NumberNode, StringNode, BooleanNode, OmittedArgumentNode, CellRefNode,
                // RangeRefNode, FullColumnRangeRefNode, FullRowRangeRefNode, NamedRangeNode,
                // StructuredReferenceNode, StructuredCurrentRowReferenceNode, and ErrorNode are all
                // leaves with no nested FormulaNode operands to walk -- same leaf set
                // ValidateBuiltInFunctionArity's own switch documents.
                default:
                    break;
            }
        }

        return maxDepth;
    }
}
