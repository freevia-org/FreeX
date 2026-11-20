using System.Text;

namespace FreeX.Core.Formula;

/// <summary>
/// Rewrites the original text inside one structured reference's outer brackets.
/// The caller must identify that reference and its owning table; this does not
/// scan formulas or decide whether an unqualified reference belongs to a table.
/// </summary>
internal static class StructuredSelectorRename
{
    /// <summary>
    /// Returns false for syntax that cannot be safely identified; in that case
    /// <paramref name="rewritten"/> is the untouched input. Mappings are applied
    /// simultaneously and matched case-insensitively.
    /// </summary>
    internal static bool TryRewrite(string selector, IReadOnlyDictionary<string, string> names, out string rewritten)
    {
        rewritten = selector;
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in names)
        {
            if (!seenNames.Add(pair.Key) || string.IsNullOrEmpty(pair.Key) || string.IsNullOrEmpty(pair.Value) ||
                pair.Key.IndexOfAny(['\r', '\n', '"']) >= 0 || pair.Value.IndexOfAny(['\r', '\n', '"']) >= 0)
                return false;
        }
        if (selector.Length == 0) return true;
        if (selector.IndexOfAny(['\r', '\n', '"']) >= 0) return false;

        var replacements = new List<(int Start, int Length, string Text)>();
        int pos = 0;
        bool currentRow = selector[pos] == '@';
        if (currentRow) pos++;
        if (pos == selector.Length) return true; // whole current row

        if (selector[pos] != '[')
        {
            if (!TryColumn(selector, pos, selector.Length - pos, names, replacements, bracketed: false))
                return false;
        }
        else
        {
            bool expectAtom = true;
            var atomKinds = new List<bool>(); // true means a section token
            var separators = new StringBuilder();
            while (pos < selector.Length)
            {
                if (expectAtom)
                {
                    if (selector[pos] != '[') return false;
                    int start = ++pos;
                    while (pos < selector.Length)
                    {
                        char c = selector[pos];
                        if (c == '\'' && pos + 1 < selector.Length && IsEscapable(selector[pos + 1]))
                        {
                            pos += 2;
                            continue;
                        }
                        if (c == '[') return false;
                        if (c == ']') break;
                        pos++;
                    }
                    if (pos >= selector.Length || !TryColumn(selector, start, pos - start, names, replacements, bracketed: true))
                        return false;
                    atomKinds.Add(selector[start] == '#');
                    pos++;
                    expectAtom = false;
                }
                else
                {
                    if (selector[pos] is not (',' or ':')) return false;
                    separators.Append(selector[pos]);
                    pos++;
                    expectAtom = true;
                }
            }
            if (expectAtom || !IsSupportedShape(currentRow, atomKinds, separators.ToString())) return false;
        }

        if (replacements.Count == 0) return true;
        var builder = new StringBuilder(selector);
        for (int i = replacements.Count - 1; i >= 0; i--)
        {
            var replacement = replacements[i];
            builder.Remove(replacement.Start, replacement.Length);
            builder.Insert(replacement.Start, replacement.Text);
        }
        rewritten = builder.ToString();
        return true;
    }

    private static bool TryColumn(string text, int start, int length, IReadOnlyDictionary<string, string> names,
        List<(int Start, int Length, string Text)> replacements, bool bracketed)
    {
        if (length == 0) return false;
        var raw = text.AsSpan(start, length);
        // Bare colons may denote a range or a literal header depending on the
        // table; commas may likewise have selector semantics. We have no table
        // context here, so never report an unchanged success for either form.
        if (!bracketed && raw.IndexOfAny(',', ':') >= 0) return false;
        // Parser/resolver trim selector text; treating these spaces as part of
        // a different name would silently miss a reference that does match.
        if (char.IsWhiteSpace(raw[0]) || char.IsWhiteSpace(raw[^1])) return false;
        if (raw[0] == '#')
            return IsSection(raw);
        var decoded = new StringBuilder(length);
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (c == '\'' && i + 1 < raw.Length && IsEscapable(raw[i + 1]))
                decoded.Append(raw[++i]);
            else if (c == ']' && !bracketed && i + 1 < raw.Length && raw[i + 1] == ']')
            {
                decoded.Append(']');
                i++;
            }
            else if (c is '[' or ']' or '#' or '"')
                return false;
            else
                decoded.Append(c);
        }
        string original = decoded.ToString();
        if (original[0] == '#' || (bracketed && original.IndexOfAny(['[', ']', ',', ':']) >= 0))
            return false;
        foreach (var pair in names)
        {
            if (!string.Equals(pair.Key, original, StringComparison.OrdinalIgnoreCase)) continue;
            // The lexer drops escape provenance, and combined-selector resolution
            // strips brackets. These names cannot safely survive that pipeline.
            if (char.IsWhiteSpace(pair.Value[0]) || char.IsWhiteSpace(pair.Value[^1]) ||
                pair.Value[0] == '#' ||
                (bracketed && pair.Value.IndexOfAny(['[', ']', ',', ':']) >= 0) ||
                (!bracketed && (pair.Value.IndexOfAny([',', ':']) >= 0 || pair.Value[0] == '@')))
                return false;
            var escaped = new StringBuilder(pair.Value.Length);
            foreach (char c in pair.Value)
            {
                if (IsEscapable(c)) escaped.Append('\'');
                escaped.Append(c);
            }
            replacements.Add((start, length, escaped.ToString()));
            break;
        }
        return true;
    }

    private static bool IsSupportedShape(bool currentRow, List<bool> sections, string separators)
    {
        if (currentRow)
            return sections.All(section => !section) &&
                   (sections.Count == 1 || (sections.Count == 2 && separators == ":"));
        return (sections.Count == 2 &&
                ((sections[0] && !sections[1] && separators == ",") ||
                 (!sections[0] && !sections[1] && separators == ":"))) ||
               (sections.Count == 3 && sections[0] && !sections[1] && !sections[2] && separators == ",:");
    }

    private static bool IsEscapable(char c) => c is '[' or ']' or '#' or '\'';
    private static bool IsSection(ReadOnlySpan<char> value) =>
        value.Equals("#All", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("#Data", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("#Headers", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("#Totals", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("#This Row", StringComparison.OrdinalIgnoreCase);
}
