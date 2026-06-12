using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace FreeX.Core.Formula;

/// <summary>
/// Shared helper for converting Excel-style wildcard patterns to compiled <see cref="Regex"/> instances.
/// Provides a bounded cache to avoid allocating a new regex per evaluation call.
/// </summary>
/// <remarks>
/// Excel wildcard rules: <c>*</c> matches any sequence of characters, <c>?</c> matches exactly one
/// character, and <c>~</c> escapes the next character (<c>~*</c>, <c>~?</c>, <c>~~</c>).
/// Surrogate pairs are treated as a single logical character.
/// </remarks>
internal static class FormulaWildcardHelper
{
    // Matches a single Unicode text element — either a surrogate pair or a non-surrogate BMP char.
    internal const string RegexTextElement = @"(?:[\uD800-\uDBFF][\uDC00-\uDFFF]|[^\uD800-\uDFFF])";

    private static readonly ConcurrentDictionary<(string Pattern, bool IgnoreCase, bool Anchored), Regex> Cache = new();

    /// <summary>
    /// Returns a cached <see cref="Regex"/> that matches strings according to the given Excel
    /// wildcard <paramref name="pattern"/>.
    /// </summary>
    /// <param name="pattern">The Excel wildcard pattern.</param>
    /// <param name="ignoreCase">Whether the match is case-insensitive.</param>
    /// <param name="anchored">
    /// When <see langword="true"/> the pattern is anchored to the full string (<c>^…$</c>).
    /// When <see langword="false"/> the pattern can match anywhere in the string.
    /// </param>
    internal static Regex GetOrCreateRegex(string pattern, bool ignoreCase, bool anchored = true)
    {
        var key = (pattern, ignoreCase, anchored);
        if (!Cache.ContainsKey(key) &&
            Cache.Count >= FormulaSafetyLimits.MaxRegexCacheEntries)
        {
            Cache.Clear();
        }

        return Cache.GetOrAdd(key, k =>
        {
            var opts = k.IgnoreCase
                ? RegexOptions.IgnoreCase | RegexOptions.Compiled
                : RegexOptions.Compiled;
            return new Regex(BuildPattern(k.Pattern, k.Anchored), opts, FormulaSafetyLimits.RegexTimeout);
        });
    }

    /// <summary>
    /// Converts an Excel wildcard string to a regex pattern string.
    /// When <paramref name="anchored"/> is <see langword="true"/> the pattern is wrapped in
    /// <c>^</c>…<c>$</c> anchors so the whole string must match.
    /// </summary>
    internal static string BuildPattern(string pattern, bool anchored)
    {
        var sb = new System.Text.StringBuilder(anchored ? "^" : "");
        for (int i = 0; i < pattern.Length; i++)
        {
            char ch = pattern[i];
            if (ch == '~' && i + 1 < pattern.Length && pattern[i + 1] is '*' or '?' or '~')
            {
                sb.Append(Regex.Escape(pattern[++i].ToString()));
                continue;
            }

            switch (ch)
            {
                case '*': sb.Append(RegexTextElement).Append('*'); break;
                case '?': sb.Append(RegexTextElement); break;
                default:  sb.Append(Regex.Escape(ch.ToString())); break;
            }
        }
        if (anchored) sb.Append('$');
        return sb.ToString();
    }
}
