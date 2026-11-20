using System.Text;

namespace FreeX.Core.Formula;

/// <summary>Invalidates references to removed table columns using original source spans.</summary>
public static class StructuredFormulaColumnDelete
{
    /// <summary>
    /// Matches either table alias and, for unqualified selectors, the supplied owner. A
    /// selector whose endpoint names a deleted column becomes #REF!; interior deletions
    /// do not invalidate a range. False means the text could not be handled safely.
    /// </summary>
    public static bool TryRewrite(string formula, string tableName, string? displayName, string? owner,
        IReadOnlyCollection<string> deletedColumns, bool wholeTable, out string rewritten)
    {
        rewritten = formula;
        if (string.IsNullOrWhiteSpace(tableName)) return false;
        bool IsTarget(string? candidate) => string.Equals(candidate, tableName, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(displayName) && string.Equals(candidate, displayName, StringComparison.OrdinalIgnoreCase));
        List<Token> tokens;
        try
        {
            tokens = new Lexer(formula).Tokenize();
            _ = new Parser(tokens).Parse();
        }
        catch (FormulaParseException) { return false; }

        var markers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var marker = "FreexDeletedColumnMarker";
        while (deletedColumns.Contains(marker, StringComparer.OrdinalIgnoreCase)) marker += "_";
        foreach (var name in deletedColumns)
            if (string.IsNullOrEmpty(name) || !markers.TryAdd(name, marker)) return false;
        var edits = new List<(int Start, int End)>();
        var offset = formula.StartsWith('=') ? 1 : 0;
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Type != TokenType.StructuredReferenceSelector) continue;
            var qualified = i > 0 && tokens[i - 1].Type == TokenType.NamedRange;
            var qualifier = qualified ? tokens[i - 1].Value : null;
            if (qualifier is null && owner is null) return false;
            if (!IsTarget(qualifier ?? owner)) continue;
            var selectorStart = tokens[i].Position + offset;
            if (selectorStart >= formula.Length || formula[selectorStart] != '[' || i + 1 >= tokens.Count) return false;
            var end = tokens[i + 1].Position + offset;
            while (end > selectorStart && char.IsWhiteSpace(formula[end - 1])) end--;
            if (end <= selectorStart + 1 || end > formula.Length || formula[end - 1] != ']') return false;
            var selector = formula[(selectorStart + 1)..(end - 1)];
            var transformed = selector;
            if (!wholeTable && !StructuredSelectorRename.TryRewrite(selector, markers, out transformed)) return false;
            if (wholeTable || transformed != selector)
                edits.Add((qualified ? tokens[i - 1].Position + offset : selectorStart, end));
        }
        if (edits.Count == 0) return true;
        var builder = new StringBuilder(formula);
        for (var i = edits.Count - 1; i >= 0; i--)
        {
            var (start, end) = edits[i];
            builder.Remove(start, end - start);
            builder.Insert(start, "#REF!");
        }
        rewritten = builder.ToString();
        return true;
    }
}
