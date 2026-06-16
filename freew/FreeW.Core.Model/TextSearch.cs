using System;
using System.Collections.Generic;

namespace FreeW.Core.Model;

/// <summary>
/// Pure, WPF-free text matching used by the editor's Find &amp; Replace surface. Lives in the model
/// project so it can be unit-tested without any UI. Matches are reported as (Start, Length) spans
/// over a haystack string and never overlap: after a match is found, scanning resumes at the end of
/// that match.
/// </summary>
public static class TextSearch
{
    /// <summary>
    /// Enumerates every non-overlapping occurrence of <paramref name="needle"/> in
    /// <paramref name="haystack"/>, in left-to-right order, as (Start, Length) spans. Honours
    /// <paramref name="matchCase"/> (ordinal when true, ordinal-ignore-case when false) and, when
    /// <paramref name="wholeWord"/> is set, only yields matches whose surrounding characters are word
    /// boundaries (see <see cref="IsWholeWordMatch"/>). An empty needle yields no matches.
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
        var from = 0;
        while (from <= haystack.Length - needle.Length)
        {
            var index = haystack.IndexOf(needle, from, comparison);
            if (index < 0)
                yield break;

            if (!wholeWord || IsWholeWordMatch(haystack, index, needle.Length))
            {
                yield return (index, needle.Length);
                // Advance past the match so spans never overlap.
                from = index + needle.Length;
            }
            else
            {
                // Not a whole-word hit: step one character so the next candidate can still be found.
                from = index + 1;
            }
        }
    }

    /// <summary>
    /// Returns true when the span [<paramref name="start"/>, <paramref name="start"/> +
    /// <paramref name="length"/>) in <paramref name="text"/> stands on word boundaries: the character
    /// immediately before the span and the character immediately after it are both non-word characters
    /// (or the document edge). A word character is a letter, a digit, or an underscore
    /// (<see cref="char.IsLetterOrDigit(char)"/> or <c>'_'</c>).
    /// </summary>
    public static bool IsWholeWordMatch(string text, int start, int length)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (start < 0 || length < 0 || start + length > text.Length)
            throw new ArgumentOutOfRangeException(nameof(start));

        if (start > 0 && IsWordChar(text[start - 1]))
            return false;

        var after = start + length;
        if (after < text.Length && IsWordChar(text[after]))
            return false;

        return true;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
