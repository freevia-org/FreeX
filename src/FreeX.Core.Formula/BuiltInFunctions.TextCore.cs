using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    // Core text functions are split into focused TextCore partial files.

    private static readonly ConcurrentDictionary<string, Regex> SearchCache = new();

    private static Regex GetSearchRegex(string findText)
    {
        if (!SearchCache.ContainsKey(findText) &&
            SearchCache.Count >= FormulaSafetyLimits.MaxRegexCacheEntries)
        {
            SearchCache.Clear();
        }

        return SearchCache.GetOrAdd(findText, pattern =>
            new Regex(
                WildcardToRegexPattern(pattern, anchored: false),
                RegexOptions.IgnoreCase | RegexOptions.Compiled,
                FormulaSafetyLimits.RegexTimeout));
    }
}
