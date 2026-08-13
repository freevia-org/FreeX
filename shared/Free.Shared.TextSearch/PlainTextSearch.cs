namespace Free.Shared.TextSearch;

/// <summary>
/// Finds non-overlapping literal text spans using ordinal comparison and a portable whole-word
/// policy. A word character is a Unicode letter or digit, or an underscore.
/// </summary>
public static class PlainTextSearch
{
    /// <summary>
    /// Enumerates non-overlapping occurrences of <paramref name="needle"/> in
    /// <paramref name="haystack"/> from left to right. An empty needle yields no matches.
    /// </summary>
    public static IEnumerable<(int Start, int Length)> FindAll(
        string haystack,
        string needle,
        bool matchCase,
        bool wholeWord)
    {
        ArgumentNullException.ThrowIfNull(haystack);
        ArgumentNullException.ThrowIfNull(needle);

        if (needle.Length == 0)
            yield break;

        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var start = 0;
        while (start <= haystack.Length - needle.Length)
        {
            var index = haystack.IndexOf(needle, start, comparison);
            if (index < 0)
                yield break;

            if (wholeWord && !IsWholeWordMatch(haystack, index, needle.Length))
            {
                start = index + 1;
                continue;
            }

            yield return (index, needle.Length);
            start = index + needle.Length;
        }
    }

    /// <summary>
    /// Returns whether the supplied span is bordered by non-word characters or string edges.
    /// </summary>
    public static bool IsWholeWordMatch(string text, int start, int length)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (start < 0 || length < 0 || start > text.Length - length)
            throw new ArgumentOutOfRangeException(nameof(start));

        if (start > 0 && IsWordCharacter(text[start - 1]))
            return false;

        var after = start + length;
        return after >= text.Length || !IsWordCharacter(text[after]);
    }

    private static bool IsWordCharacter(char character) =>
        char.IsLetterOrDigit(character) || character == '_';
}
