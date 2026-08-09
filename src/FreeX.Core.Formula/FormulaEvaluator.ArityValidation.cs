namespace FreeX.Core.Formula;

public sealed partial class FormulaEvaluator
{
    /// <summary>
    /// Validates that every well-known built-in function call reachable from
    /// <paramref name="root"/> is invoked with an argument count within its registered
    /// (<c>MinArgs</c>, <c>MaxArgs</c>) arity (see <see cref="BuiltInFunctions.TryGet"/>),
    /// throwing a <see cref="FormulaParseException"/> the first time one is out of range
    /// (R120-formula-entry-arity-validation).
    ///
    /// Real Excel's formula-entry compiler validates a built-in function's argument count as
    /// part of accepting the typed formula: typing e.g. <c>=IF(A1&gt;0)</c> (1 argument; IF
    /// requires 2 or 3) or <c>=LEFT("x",1,2,3)</c> (4 arguments; LEFT allows at most 2) pops
    /// Excel's "You've entered too few/too many arguments for this function" dialog and refuses
    /// to leave edit mode -- the malformed text never gets committed to the cell. Previously,
    /// FreeX enforced arity only inside <see cref="EvaluateFunction"/> (the generic
    /// <c>node.Arguments.Count &lt; minArgs</c>/<c>&gt; maxArgs</c> check) and inside each
    /// short-circuited control-flow function's own dedicated Evaluate* method (e.g. IF's
    /// <c>node.Arguments.Count is &lt; 2 or &gt; 3</c> in FormulaEvaluator.ControlFlow.cs) --
    /// both reachable only during recalculation, after the malformed formula had already been
    /// parsed and committed to the cell. Calling this from the formula-entry path (see
    /// FreeX.App.Services.CellEntryParser.CreateCell) lets the same shapes be rejected at entry,
    /// matching Excel.
    ///
    /// Aggregate functions (SUM, AVERAGE, MEDIAN, AND, OR, CONCAT(ENATE), etc.) are genuinely
    /// variadic, but only up to Excel's hard 255-argument syntax limit -- which is already their
    /// registered MaxArgs (see <see cref="BuiltInFunctions"/>). Real Excel refuses to commit a
    /// formula with a 256th argument to any of these; there is no unbounded exemption. This
    /// validator therefore enforces the registered maxArgs uniformly, matching
    /// <see cref="EvaluateFunction"/>'s own recalculation-time check (R126-aggregate-arg-cap).
    ///
    /// Only names known to <see cref="BuiltInFunctions.TryGet"/> are checked; LET/LAMBDA/SINGLE/
    /// ANCHORARRAY (AST-aware special forms, never in the registry) and any Name-Manager-defined
    /// custom function are left alone, matching <see cref="EvaluateFunction"/>'s own carve-outs.
    /// </summary>
    public static void ValidateBuiltInFunctionArity(FormulaNode root)
    {
        switch (root)
        {
            case FunctionCallNode call:
                if (BuiltInFunctions.TryGet(call.FunctionName, out var entry))
                {
                    var (_, minArgs, maxArgs) = entry;
                    var count = call.Arguments.Count;

                    if (count < minArgs)
                    {
                        throw new FormulaParseException(
                            $"Too few arguments for function {call.FunctionName}(). " +
                            $"Requires at least {minArgs}, got {count}.");
                    }

                    if (count > maxArgs)
                    {
                        throw new FormulaParseException(
                            $"Too many arguments for function {call.FunctionName}(). " +
                            $"Allows at most {maxArgs}, got {count}.");
                    }
                }

                foreach (var argument in call.Arguments)
                    ValidateBuiltInFunctionArity(argument);
                break;

            case BinaryOpNode binaryOp:
                ValidateBuiltInFunctionArity(binaryOp.Left);
                ValidateBuiltInFunctionArity(binaryOp.Right);
                break;

            case UnaryOpNode unaryOp:
                ValidateBuiltInFunctionArity(unaryOp.Operand);
                break;

            case IntersectionNode intersection:
                ValidateBuiltInFunctionArity(intersection.Left);
                ValidateBuiltInFunctionArity(intersection.Right);
                break;

            case NamedRangeEndpointNode endpoint:
                ValidateBuiltInFunctionArity(endpoint.Start);
                ValidateBuiltInFunctionArity(endpoint.End);
                break;

            case UnionNode union:
                foreach (var area in union.Areas)
                    ValidateBuiltInFunctionArity(area);
                break;

            case ArrayConstantNode array:
                foreach (var row in array.Rows)
                    foreach (var cell in row)
                        ValidateBuiltInFunctionArity(cell);
                break;

            // NumberNode, StringNode, BooleanNode, OmittedArgumentNode, CellRefNode, RangeRefNode,
            // FullColumnRangeRefNode, FullRowRangeRefNode, NamedRangeNode, StructuredReferenceNode,
            // StructuredCurrentRowReferenceNode, and ErrorNode are all leaves with no nested
            // FormulaNode operands to walk.
        }
    }
}
