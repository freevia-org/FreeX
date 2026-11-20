using System.Text;

namespace FreeX.Core.Formula;

/// <summary>Preserves a moved formula's source-table binding.</summary>
public static class StructuredFormulaMoveQualification
{
    /// <summary>Qualifies unqualified selectors while preserving existing qualified references.</summary>
    public static bool TryQualify(string formula, string tableName, out string qualified)
    {
        qualified = formula;
        if (string.IsNullOrWhiteSpace(tableName)) return false;
        List<Token> tokens;
        try
        {
            tokens = new Lexer(formula).Tokenize();
            _ = new Parser(tokens).Parse();
        }
        catch (FormulaParseException) { return false; }
        var offset = formula.StartsWith('=') ? 1 : 0;
        var positions = new List<int>();
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Type != TokenType.StructuredReferenceSelector) continue;
            var start = tokens[i].Position + offset;
            if (start >= formula.Length || formula[start] != '[' || i + 1 >= tokens.Count) return false;
            var end = tokens[i + 1].Position + offset;
            while (end > start && char.IsWhiteSpace(formula[end - 1])) end--;
            if (end <= start + 1 || end > formula.Length || formula[end - 1] != ']') return false;
            if (i > 0 && tokens[i - 1].Type == TokenType.NamedRange) continue;
            positions.Add(start);
        }
        if (positions.Count == 0) return true;
        var result = new StringBuilder(formula);
        for (var i = positions.Count - 1; i >= 0; i--) result.Insert(positions[i], tableName);
        qualified = result.ToString();
        return true;
    }
}
