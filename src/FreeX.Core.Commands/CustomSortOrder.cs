namespace FreeX.Core.Commands;

/// <summary>
/// Excel "Custom List" sort order (the Sort Options "First key sort order" choice, e.g.
/// "Jan, Feb, Mar, ..."). Pure value type: parses a comma-separated list into ordered
/// tokens and ranks text by list position, placing non-members after all list members.
/// Matching is case-insensitive, mirroring Excel.
/// </summary>
public sealed class CustomSortOrder
{
    private const string NormalOrder = "Normal";

    private readonly IReadOnlyList<string> _tokens;
    private readonly Dictionary<string, int> _rankByToken;

    private CustomSortOrder(IReadOnlyList<string> tokens)
    {
        _tokens = tokens;
        _rankByToken = new Dictionary<string, int>(tokens.Count, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < tokens.Count; i++)
            _rankByToken.TryAdd(tokens[i], i);
    }

    public IReadOnlyList<string> Tokens => _tokens;

    /// <summary>
    /// Parses a comma-separated custom-list string. Returns false for null/blank input or the
    /// sentinel "Normal" order (which means "use the standard value comparison").
    /// </summary>
    public static bool TryParse(string? order, out CustomSortOrder? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(order) ||
            string.Equals(order.Trim(), NormalOrder, StringComparison.OrdinalIgnoreCase))
            return false;

        var tokens = order
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (tokens.Count == 0)
            return false;

        result = new CustomSortOrder(tokens);
        return true;
    }

    /// <summary>Zero-based position of <paramref name="value"/> in the list, or -1 if absent.</summary>
    public int IndexOf(string? value) =>
        value is not null && _rankByToken.TryGetValue(value, out var rank) ? rank : -1;

    /// <summary>
    /// Compares two text values by their custom-list position. List members sort by position
    /// (list membership itself is always matched case-insensitively, mirroring Excel's custom
    /// lists); a list member precedes a non-member; two non-members fall back to an ordinal
    /// comparison honoring <paramref name="caseSensitive"/> — the same flag the caller applies
    /// to its own non-custom-list text tie-break (Sort Options &gt; Case sensitive).
    /// </summary>
    public int Compare(string? a, string? b, bool caseSensitive = false)
    {
        var ai = IndexOf(a);
        var bi = IndexOf(b);

        if (ai >= 0 && bi >= 0)
            return ai.CompareTo(bi);
        if (ai >= 0)
            return -1; // list member before non-member
        if (bi >= 0)
            return 1;

        return caseSensitive
            ? CompareCaseSensitiveText(a ?? "", b ?? "")
            : string.Compare(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
    }

    // R51-commands-sort-custom-multilevel-3-2: Excel's "Case sensitive" sort does NOT switch to raw
    // ordinal/codepoint order (which would clump all uppercase-leading words ahead of all lowercase
    // ones). It sorts alphabetically first; case only breaks a tie between otherwise letter-for-letter
    // identical strings, and in that tiebreak lowercase sorts before uppercase (MS: "in a case-sensitive
    // sort, lowercase letters sort before uppercase"). Mirrors SortCommand.CompareCaseSensitiveText
    // (not reachable from here — different, more narrowly-scoped source file).
    private static int CompareCaseSensitiveText(string a, string b)
    {
        var primary = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        if (primary != 0)
            return primary;

        var len = Math.Min(a.Length, b.Length);
        for (var i = 0; i < len; i++)
        {
            var ca = a[i];
            var cb = b[i];
            if (ca == cb)
                continue;

            var aLower = char.IsLower(ca);
            var bLower = char.IsLower(cb);
            if (aLower != bLower)
                return aLower ? -1 : 1; // lowercase before uppercase, same-letter tiebreak only

            return ca.CompareTo(cb);
        }

        return a.Length.CompareTo(b.Length);
    }
}
