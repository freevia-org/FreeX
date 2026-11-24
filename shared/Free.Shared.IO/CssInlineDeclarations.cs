namespace Free.Shared.IO;

/// <summary>
/// Splits an inline CSS declaration block (the text of a <c>style="..."</c> attribute, e.g.
/// <c>"font-weight:bold; color:#ff0000"</c>) into lower-cased property names and trimmed values.
/// FreeX's HTML table reader and FreeW's HTML adapter each carried a private copy of this scan; what
/// each app does with the pairs (cell style, run formatting) stays its own.
///
/// Rules: declarations are separated by <c>;</c>; blank declarations, declarations without a colon,
/// with an empty property name or with an empty value are skipped; property names are lower-cased
/// (invariant); declaration order is preserved and duplicates are NOT collapsed (callers decide whether
/// the first or the last one wins).
/// </summary>
public static class CssInlineDeclarations
{
    public static IEnumerable<(string Property, string Value)> Enumerate(string? css)
    {
        if (string.IsNullOrWhiteSpace(css))
            yield break;

        foreach (var part in css.Split(';'))
        {
            var decl = part.Trim();
            if (decl.Length == 0)
                continue;
            int colon = decl.IndexOf(':');
            if (colon <= 0 || colon == decl.Length - 1)
                continue;
            var property = decl[..colon].Trim().ToLowerInvariant();
            var value = decl[(colon + 1)..].Trim();
            if (property.Length > 0 && value.Length > 0)
                yield return (property, value);
        }
    }
}
