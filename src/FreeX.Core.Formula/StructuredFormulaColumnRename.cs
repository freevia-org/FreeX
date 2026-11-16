using System.Text;

namespace FreeX.Core.Formula;

/// <summary>Rewrites table-column references using original formula source spans.</summary>
public static class StructuredFormulaColumnRename
{
    /// <summary>
    /// Rewrites only references to <paramref name="tableName"/>. The owner is
    /// the table containing the formula cell; null means the owner is unknown,
    /// so encountering any unqualified selector is reported as unsupported.
    /// False leaves the entire formula unchanged. Mappings are simultaneous.
    /// </summary>
    public static bool TryRewrite(string formula, string tableName, string? owningTableName,
        IReadOnlyDictionary<string, string> names, out string rewritten)
        => TryRewrite(formula, tableName, null, owningTableName, names, out rewritten);

    /// <summary>
    /// Matches both the table's internal name and its optional display name.
    /// The owning table name may be either alias; null means unknown ownership.
    /// </summary>
    public static bool TryRewrite(string formula, string tableName, string? tableDisplayName, string? owningTableName,
        IReadOnlyDictionary<string, string> names, out string rewritten)
    {
        rewritten = formula;
        if (string.IsNullOrWhiteSpace(tableName)) return false;
        bool IsTarget(string? candidate) =>
            string.Equals(candidate, tableName, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(tableDisplayName) &&
             string.Equals(candidate, tableDisplayName, StringComparison.OrdinalIgnoreCase));
        List<Token> tokens;
        try
        {
            tokens = new Lexer(formula).Tokenize();
            _ = new Parser(tokens).Parse();
        }
        catch (FormulaParseException)
        {
            return false;
        }

        int offset = formula.StartsWith('=') ? 1 : 0;
        var edits = new List<(int Start, int Length, string Text)>();
        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Type != TokenType.StructuredReferenceSelector) continue;
            string? qualifier = i > 0 && tokens[i - 1].Type == TokenType.NamedRange
                ? tokens[i - 1].Value : null;
            if (qualifier is null && owningTableName is null) return false;
            if (!IsTarget(qualifier ?? owningTableName)) continue;

            int start = token.Position + offset;
            if (start >= formula.Length || formula[start] != '[' || i + 1 >= tokens.Count) return false;
            int end = tokens[i + 1].Position + offset;
            while (end > start && char.IsWhiteSpace(formula[end - 1])) end--;
            if (end <= start + 1 || end > formula.Length || formula[end - 1] != ']') return false;
            string rawSelector = formula[(start + 1)..(end - 1)];
            if (!StructuredSelectorRename.TryRewrite(rawSelector, names, out var replacement)) return false;
            if (!string.Equals(rawSelector, replacement, StringComparison.Ordinal))
                edits.Add((start + 1, end - start - 2, replacement));
        }
        if (edits.Count == 0) return true;
        var builder = new StringBuilder(formula);
        for (int i = edits.Count - 1; i >= 0; i--)
        {
            var edit = edits[i];
            builder.Remove(edit.Start, edit.Length);
            builder.Insert(edit.Start, edit.Text);
        }
        rewritten = builder.ToString();
        return true;
    }
}
