using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Free.Shared.TextSearch;

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
    /// boundaries (see <see cref="IsWholeWordMatch"/>). When <paramref name="useWildcards"/> is true
    /// the needle is interpreted as a Word-style wildcard pattern (*, ?, [set], &lt;, &gt;) and translated
    /// to a regex; <paramref name="wholeWord"/> is ignored in that mode (word-boundary anchors can be
    /// expressed with &lt; and &gt; directly). An empty needle yields no matches.
    /// </summary>
    public static IEnumerable<(int Start, int Length)> FindAll(
        string haystack,
        string needle,
        bool matchCase,
        bool wholeWord,
        bool useWildcards = false)
    {
        ArgumentNullException.ThrowIfNull(haystack);
        ArgumentNullException.ThrowIfNull(needle);

        if (needle.Length == 0)
            yield break;

        if (useWildcards)
        {
            Regex regex;
            try
            {
                var regexOptions = matchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
                regex = new Regex(WildcardToRegex(needle), regexOptions);
            }
            catch (ArgumentException)
            {
                // Malformed wildcard pattern — yield nothing rather than throwing.
                yield break;
            }

            var from = 0;
            while (from <= haystack.Length)
            {
                var m = regex.Match(haystack, from);
                if (!m.Success)
                    yield break;

                yield return (m.Index, m.Length);
                // Advance at least one character to avoid infinite loops on zero-length matches.
                from = m.Index + Math.Max(1, m.Length);
            }
            yield break;
        }

        foreach (var match in PlainTextSearch.FindAll(haystack, needle, matchCase, wholeWord))
            yield return match;
    }

    /// <summary>
    /// Translates a Word-style wildcard pattern to a .NET regular expression pattern string.
    /// <list type="bullet">
    /// <item><c>*</c> → zero-or-more characters, non-greedy (<c>.*?</c>) — matches the shortest sequence so patterns like <c>h*o</c> find each word separately rather than one giant span.</item>
    /// <item><c>?</c> → any single character (<c>.</c>).</item>
    /// <item><c>[abc]</c> / <c>[a-z]</c> → character class (passed through as-is after escaping outer literal chars).</item>
    /// <item><c>[!abc]</c> → negated class (<c>[^abc]</c>).</item>
    /// <item><c>&lt;</c> → word-start boundary (<c>\b(?=\w)</c>).</item>
    /// <item><c>&gt;</c> → word-end boundary (<c>\b(?&lt;=\w)</c>).</item>
    /// <item>All other regex metacharacters are escaped so they are treated as literals.</item>
    /// </list>
    /// </summary>
    public static string WildcardToRegex(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        var sb = new System.Text.StringBuilder();
        var i = 0;
        while (i < pattern.Length)
        {
            var c = pattern[i];
            switch (c)
            {
                case '*':
                    sb.Append(".*?");
                    i++;
                    break;

                case '?':
                    sb.Append('.');
                    i++;
                    break;

                case '<':
                    // Word-start boundary: position where a word character follows.
                    sb.Append(@"\b(?=\w)");
                    i++;
                    break;

                case '>':
                    // Word-end boundary: position preceded by a word character.
                    sb.Append(@"\b(?<=\w)");
                    i++;
                    break;

                case '[':
                {
                    // Collect the bracketed class including the closing ']'.
                    var j = i + 1;
                    // Allow '[!' negation marker without consuming ']'.
                    if (j < pattern.Length && pattern[j] == '!')
                        j++;
                    // Skip past any ']' that appears immediately inside (literal ']' in a class).
                    if (j < pattern.Length && pattern[j] == ']')
                        j++;
                    while (j < pattern.Length && pattern[j] != ']')
                        j++;

                    if (j < pattern.Length)
                    {
                        // We found the closing ']': emit the class verbatim, translating '[!' to '[^'.
                        var inner = pattern.Substring(i + 1, j - i - 1);
                        if (inner.StartsWith("!"))
                            sb.Append("[^").Append(inner.Substring(1)).Append(']');
                        else
                            sb.Append('[').Append(inner).Append(']');
                        i = j + 1;
                    }
                    else
                    {
                        // No closing ']': treat '[' as a literal.
                        sb.Append(@"\[");
                        i++;
                    }
                    break;
                }

                default:
                    // Escape any regex metacharacter so it is treated as a literal.
                    sb.Append(Regex.Escape(c.ToString()));
                    i++;
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Returns true when the span [<paramref name="start"/>, <paramref name="start"/> +
    /// <paramref name="length"/>) in <paramref name="text"/> stands on word boundaries: the character
    /// immediately before the span and the character immediately after it are both non-word characters
    /// (or the document edge). A word character is a letter, a digit, or an underscore
    /// (<see cref="char.IsLetterOrDigit(char)"/> or <c>'_'</c>).
    /// </summary>
    public static bool IsWholeWordMatch(string text, int start, int length) =>
        PlainTextSearch.IsWholeWordMatch(text, start, length);
}
