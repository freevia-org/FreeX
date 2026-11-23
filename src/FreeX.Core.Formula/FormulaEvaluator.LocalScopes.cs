using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public sealed partial class FormulaEvaluator
{
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
                _                => null
            };
            if (name is not { } localName || !IsValidLocalFunctionName(localName)) return ErrorValue.Value;
            // Excel rejects a LET that defines the same binding name twice within the same
            // call ("You can't define the same name twice in a LET function"); a nested LET's
            // own binding of the same name is a separate call/dictionary and is unaffected.
            if (bindings.ContainsKey(localName)) return ErrorValue.Value;
            var value = EvaluateArrayOperand(node.Arguments[i * 2 + 1], scoped);
            // A malformed LAMBDA definition (duplicate/invalid parameter names) is refused outright; Excel rejects the whole
            // formula at entry.
            if (value is ErrorValue lambdaDefinitionError && node.Arguments[i * 2 + 1] is FunctionCallNode { FunctionName: "LAMBDA" })
                return lambdaDefinitionError;
            // An error value is just a binding: Excel only surfaces it when the calc expression uses it
            // (LET(x,1/0,IFERROR(x,"e")) is "e", LET(x,1/0,5) is 5).
            bindings[localName] = value;
        }

        // The final calc_expr is the LET's overall result and must be evaluated array-aware
        // (mirroring the bindings above and EvaluateSpilling's top-level treatment), so a bare
        // range/full-column/full-row/named-range body yields a RangeValue that can spill instead
        // of silently collapsing to its top-left cell via implicit intersection.
        return EvaluateArrayOperand(node.Arguments[^1], scoped);
    }

    private static ScalarValue EvaluateLambda(FunctionCallNode node, IEvalContext context)
    {
        // LAMBDA([param1, param2, ...,] body)
        // All args except the last must be identifier (NamedRangeNode) parameter names.
        if (node.Arguments.Count < 1) return ErrorValue.Value;

        var paramNames = new List<string>(node.Arguments.Count - 1);
        var seenParamNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < node.Arguments.Count - 1; i++)
        {
            // An optional parameter is written [name]; the lexer reads it as a one-part structured reference.
            var declared = node.Arguments[i] switch
            {
                NamedRangeNode nm => nm.Name,
                StructuredReferenceNode { TableName: "" } optional => optional.ColumnName,
                _ => null
            };
            if (declared is not null)
            {
                if (!IsValidLambdaParameterName(declared)) return ErrorValue.Value;
                if (!seenParamNames.Add(declared)) return ErrorValue.Value;
                paramNames.Add(declared);
            }
            else
                return ErrorValue.Value;
        }

        // Capture the definition-site environment (e.g. an enclosing LET's bindings) so free
        // variables in the body resolve lexically, not against whatever context happens to be
        // active when the lambda is later invoked (Excel LAMBDA is a lexical closure).
        return new LambdaValue(paramNames, node.Arguments[^1], context);
    }

    private static bool IsValidLocalFunctionName(string? name)
    {
        if (!IsValidExcelLocalName(name)) return false;

        return !ConflictsWithR1C1Reference(name!);
    }

    private static bool IsValidLambdaParameterName(string? name)
    {
        if (!IsValidExcelLocalName(name)) return false;
        if (name!.Contains('.', StringComparison.Ordinal)) return false;

        return !ConflictsWithR1C1Reference(name);
    }

    private static bool IsValidExcelLocalName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;

        char first = name[0];
        if (!char.IsLetter(first) && first != '_' && first != '\\') return false;

        for (int i = 1; i < name.Length; i++)
        {
            char ch = name[i];
            if (!char.IsLetterOrDigit(ch) && ch != '_' && ch != '.' && ch != '\\')
                return false;
        }

        return true;
    }

    // Excel (verified against Excel 16 through COM, en-US) rejects a LET/LAMBDA name only when it is
    // exactly R<n>, C<n> or RC<n> with n >= 1 (r1, c1, r10, rc2 ...). The bare letters r and c, rc,
    // r1c1, R2C3, r0 and every A1-looking name such as x1 or a1 are accepted.
    private static bool ConflictsWithR1C1Reference(string name)
    {
        var upper = name.ToUpperInvariant();
        int start;
        if (upper.StartsWith("RC", StringComparison.Ordinal)) start = 2;
        else if (upper[0] is 'R' or 'C') start = 1;
        else return false;

        return start < upper.Length && upper[start] != '0' && AllDigits(upper, start, upper.Length);
    }

    private static bool AllDigits(string text, int start, int end)
    {
        if (start >= end) return false;
        for (int i = start; i < end; i++)
            if (!char.IsDigit(text[i]))
                return false;
        return true;
    }

    private ScalarValue InvokeLambdaWithArgs(LambdaValue lambda, IReadOnlyList<FormulaNode> argNodes, IEvalContext context)
    {
        // Excel allows trailing optional parameters to be omitted either by simply supplying
        // fewer arguments (f(5) for a 2-parameter lambda) or via an explicit trailing comma
        // (f(5,)) -- both must bind the missing parameter to the "omitted" sentinel so
        // ISOMITTED can detect it. Only a call that supplies MORE arguments than the lambda
        // declares parameters is an error.
        if (argNodes.Count > lambda.Parameters.Count) return ErrorValue.Value;
        if (lambda.Parameters.Any(ConflictsWithR1C1Reference)) return ErrorValue.Value;

        var args = new ScalarValue[lambda.Parameters.Count];
        for (int i = 0; i < lambda.Parameters.Count; i++)
            args[i] = i >= argNodes.Count || argNodes[i] is OmittedArgumentNode
                ? OmittedLambdaArgumentValue.Instance
                : EvaluateArrayOperand(argNodes[i], context);
        return context.InvokeLambda(lambda, args);
    }
}
