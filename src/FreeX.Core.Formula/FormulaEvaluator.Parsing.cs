namespace FreeX.Core.Formula;

public sealed partial class FormulaEvaluator
{
    private static readonly object ParsedFormulaCacheGate = new();
    private static readonly Dictionary<string, FormulaNode> ParsedFormulaCache = new(StringComparer.Ordinal);
    private static readonly Queue<string> ParsedFormulaCacheOrder = new();

    private ParsedFormulaEntry? _lastParsedFormula;

    private sealed record ParsedFormulaEntry(string FormulaText, FormulaNode Node);

    private FormulaNode GetOrParseFormulaForInstance(string formulaText)
    {
        var last = _lastParsedFormula;
        if (last is not null && string.Equals(last.FormulaText, formulaText, StringComparison.Ordinal))
            return last.Node;

        var parsed = GetOrParseFormula(formulaText);
        _lastParsedFormula = new ParsedFormulaEntry(formulaText, parsed);
        return parsed;
    }


    /// <summary>
    /// Parse a formula string using the shared text-to-AST cache. The returned AST is shared and should be treated as immutable.
    /// </summary>
    public static FormulaNode ParseFormula(string formulaText) =>
        GetOrParseFormula(formulaText);

    private static FormulaNode GetOrParseFormula(string formulaText)
    {
        formulaText = NormalizeFormulaCacheKey(formulaText);

        lock (ParsedFormulaCacheGate)
        {
            if (ParsedFormulaCache.TryGetValue(formulaText, out var cached))
                return cached;
        }

        var parsed = ParseFormulaUncached(formulaText);

        lock (ParsedFormulaCacheGate)
        {
            if (ParsedFormulaCache.TryGetValue(formulaText, out var cached))
                return cached;

            if (ParsedFormulaCache.Count >= FormulaSafetyLimits.MaxParsedFormulaCacheEntries &&
                ParsedFormulaCacheOrder.TryDequeue(out var oldest))
            {
                ParsedFormulaCache.Remove(oldest);
            }

            ParsedFormulaCache[formulaText] = parsed;
            ParsedFormulaCacheOrder.Enqueue(formulaText);
        }

        return parsed;
    }

    private static FormulaNode ParseFormulaUncached(string formulaText)
    {
        var lexer = new Lexer(formulaText);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        return parser.Parse();
    }

    private static string NormalizeFormulaCacheKey(string formulaText) =>
        formulaText is { Length: > 0 } && formulaText[0] == '='
            ? formulaText[1..]
            : formulaText;
}
