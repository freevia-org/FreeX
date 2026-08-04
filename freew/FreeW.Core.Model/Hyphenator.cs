using System.Globalization;
using System.Text;

namespace FreeW.Core.Model;

/// <summary>
/// A small, pure English hyphenation helper: given a word it returns the positions at which a soft hyphen
/// may be inserted (i.e. the indices after which the word can be broken across a line). The algorithm is a
/// deterministic vowel/consonant syllable heuristic — a reasonable approximation of Word's automatic
/// hyphenation, not a perfect Liang TeX pattern set — augmented with a handful of common prefix/suffix and
/// digraph rules so that frequent words break in familiar places (e.g. <c>hy-phen-ation</c>,
/// <c>com-puter</c>). It is intentionally conservative: it never proposes a break that would leave fewer
/// than two letters on either side, never breaks inside a vowel/consonant digraph, and only ever operates
/// on alphabetic runs (words with digits, apostrophes or other punctuation are left whole).
///
/// <para>
/// The helper is UI-free and fully testable: <see cref="BreakPoints"/> returns the raw indices for a single
/// word, and <see cref="Hyphenate(string)"/> inserts U+00AD (SOFT HYPHEN) at each break so callers (the live
/// editor) can let the layout engine decide where to actually break. Soft hyphens are zero-width unless a
/// line break lands on one, so the inserted characters are invisible in normal flow.
/// </para>
/// </summary>
public static class Hyphenator
{
    /// <summary>The Unicode SOFT HYPHEN (U+00AD): a zero-width break opportunity rendered only at line ends.</summary>
    public const char SoftHyphen = '­';

    /// <summary>The Unicode NON-BREAKING HYPHEN (U+2011): a visible hyphen that cannot end a line.</summary>
    public const char NoBreakHyphen = '‑';

    // A word must have at least this many letters before it is worth hyphenating, and a break must always
    // leave at least MinEdge letters on each side. These mirror Word's defaults (it does not hyphenate very
    // short words and keeps a two-letter minimum fragment).
    private const int MinWordLength = 5;
    private const int MinEdge = 2;

    private const string Vowels = "aeiouy";

    /// <summary>
    /// Returns the break-point indices for <paramref name="word"/>: each value <c>i</c> means a soft hyphen
    /// may be inserted between <c>word[i-1]</c> and <c>word[i]</c> (so the fragment <c>word[0..i]</c> can sit
    /// at a line end). The list is strictly increasing and never includes a position closer than
    /// <see cref="MinEdge"/> to either end. Words shorter than <see cref="MinWordLength"/>, or that contain
    /// any non-letter, yield an empty list (they are left whole). The input is not modified.
    /// </summary>
    public static IReadOnlyList<int> BreakPoints(string word)
    {
        if (string.IsNullOrEmpty(word) || word.Length < MinWordLength)
            return [];

        // Only hyphenate pure alphabetic words. Anything with a digit, apostrophe, hyphen or symbol is left
        // whole (matching Word, which does not break such tokens automatically).
        foreach (var ch in word)
            if (!char.IsLetter(ch))
                return [];

        var lower = word.ToLower(CultureInfo.InvariantCulture);
        var candidates = new List<int>();

        // Pass 1: prefix/suffix affixes give the most natural-looking breaks, so seed them first.
        AddAffixBreaks(lower, candidates);

        // Pass 2: the core syllable heuristic. Walk the letters tracking vowel/consonant transitions and
        // propose a break at each consonant→vowel boundary (the classic "split between syllables" rule),
        // with special handling for a doubled consonant (split between the pair: rab-bit) and a
        // consonant cluster (split before the last consonant: mon-ster).
        AddSyllableBreaks(lower, candidates);

        // Normalise: clamp to the legal edge window, dedupe, sort, and forbid adjacent breaks (no
        // single-letter fragments in the middle either).
        return Normalise(candidates, word.Length);
    }

    /// <summary>
    /// Returns <paramref name="word"/> with a soft hyphen inserted at each <see cref="BreakPoints"/> index.
    /// A word with no break points is returned unchanged (reference-equal where possible).
    /// </summary>
    public static string Hyphenate(string word)
    {
        var points = BreakPoints(word);
        if (points.Count == 0)
            return word;

        var sb = new StringBuilder(word.Length + points.Count);
        var next = 0;
        for (var i = 0; i < word.Length; i++)
        {
            if (next < points.Count && points[next] == i)
            {
                sb.Append(SoftHyphen);
                next++;
            }
            sb.Append(word[i]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Inserts soft hyphens into every whitespace-delimited word of <paramref name="text"/> (punctuation
    /// attached to a word is preserved — only the alphabetic core is hyphenated). Returns the text unchanged
    /// when nothing was hyphenated. Used by the live editor to make a run's text breakable; the inserted
    /// U+00AD characters are invisible unless a line break lands on one.
    /// </summary>
    public static string HyphenateText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var sb = new StringBuilder(text.Length + 8);
        var start = 0;
        var changed = false;
        for (var i = 0; i <= text.Length; i++)
        {
            var atEnd = i == text.Length;
            if (!atEnd && !char.IsWhiteSpace(text[i]))
                continue;

            // Token = text[start..i] (the run of non-whitespace just ended). Hyphenate its alphabetic core.
            if (i > start)
            {
                var token = text.Substring(start, i - start);
                var hyphenated = HyphenateToken(token);
                if (!ReferenceEquals(hyphenated, token) && hyphenated != token)
                    changed = true;
                sb.Append(hyphenated);
            }
            if (!atEnd)
                sb.Append(text[i]);
            start = i + 1;
        }
        return changed ? sb.ToString() : text;
    }

    // Hyphenate the alphabetic core of a single token, leaving any leading/trailing punctuation untouched
    // (e.g. "(hyphenation)," -> "(hy­phen­ation),"). A token whose core is not a pure word is returned as-is.
    private static string HyphenateToken(string token)
    {
        var first = 0;
        while (first < token.Length && !char.IsLetter(token[first]))
            first++;
        var last = token.Length - 1;
        while (last >= first && !char.IsLetter(token[last]))
            last--;
        if (last < first)
            return token;

        var core = token.Substring(first, last - first + 1);
        var hyphenated = Hyphenate(core);
        if (hyphenated == core)
            return token;

        return string.Concat(token.AsSpan(0, first), hyphenated, token.AsSpan(last + 1));
    }

    // Common English prefixes and suffixes that read naturally as a break boundary. Kept short and
    // high-frequency; the syllable pass fills in the rest.
    private static readonly string[] Prefixes =
        ["anti", "auto", "circum", "co", "com", "con", "contra", "counter", "de", "dis", "en", "ex", "extra",
         "hyper", "inter", "intra", "intro", "mis", "non", "over", "post", "pre", "pro", "re", "semi", "sub",
         "super", "trans", "un", "under"];

    private static readonly string[] Suffixes =
        ["able", "ably", "al", "ance", "ation", "ed", "en", "ence", "ent", "er", "est", "ful", "ible", "ic",
         "ing", "ion", "ity", "ive", "ize", "less", "ly", "ment", "ness", "ous", "sion", "tion", "ward"];

    private static void AddAffixBreaks(string lower, List<int> candidates)
    {
        // Pick the LONGEST matching prefix/suffix so a longer affix wins over a shorter one it contains
        // (e.g. "com" over "co" for "computer"), giving the most natural boundary.
        var bestPrefix = 0;
        foreach (var prefix in Prefixes)
            if (prefix.Length > bestPrefix
                && lower.Length > prefix.Length + MinEdge
                && lower.StartsWith(prefix, StringComparison.Ordinal))
                bestPrefix = prefix.Length;
        if (bestPrefix > 0)
            candidates.Add(bestPrefix);

        var bestSuffix = 0;
        foreach (var suffix in Suffixes)
            if (suffix.Length > bestSuffix
                && lower.Length > suffix.Length + MinEdge
                && lower.EndsWith(suffix, StringComparison.Ordinal))
                bestSuffix = suffix.Length;
        if (bestSuffix > 0)
            candidates.Add(lower.Length - bestSuffix);
    }

    private static void AddSyllableBreaks(string lower, List<int> candidates)
    {
        // Find maximal consonant clusters between vowels and place a break inside/before them.
        var i = 0;
        var sawVowel = false;
        while (i < lower.Length)
        {
            if (IsVowel(lower[i]))
            {
                sawVowel = true;
                i++;
                continue;
            }

            // Start of a consonant cluster following at least one vowel.
            if (!sawVowel)
            {
                i++;
                continue;
            }

            var clusterStart = i;
            while (i < lower.Length && !IsVowel(lower[i]))
                i++;
            var clusterEnd = i; // exclusive; lower[clusterEnd] is a vowel (or end of word)

            // Only break when a vowel actually follows the cluster (an inter-syllable consonant cluster).
            if (clusterEnd >= lower.Length)
                break;

            var clusterLen = clusterEnd - clusterStart;
            int breakAt;
            if (clusterLen == 1)
            {
                // V C V : break before the single consonant (open syllable) -> "ho-tel", "ba-sic".
                breakAt = clusterStart;
            }
            else if (clusterLen == 2 && lower[clusterStart] == lower[clusterStart + 1])
            {
                // Doubled consonant: split the pair -> "rab-bit", "let-ter".
                breakAt = clusterStart + 1;
            }
            else if (IsConsonantDigraph(lower[clusterEnd - 2], lower[clusterEnd - 1]))
            {
                // Keep an inseparable digraph (th, ch, sh, ph, gh, wh, ck, ng) with the following syllable.
                breakAt = clusterEnd - 2;
            }
            else
            {
                // General cluster: keep the last consonant with the next syllable -> "mon-ster", "com-puter".
                breakAt = clusterEnd - 1;
            }

            candidates.Add(breakAt);
            sawVowel = false;
        }
    }

    private static bool IsVowel(char c) => Vowels.IndexOf(c) >= 0;

    private static bool IsConsonantDigraph(char a, char b)
    {
        return (a, b) switch
        {
            ('t', 'h') or ('c', 'h') or ('s', 'h') or ('p', 'h') or ('g', 'h')
                or ('w', 'h') or ('c', 'k') or ('n', 'g') or ('q', 'u') => true,
            _ => false
        };
    }

    // Clamp candidates to the [MinEdge, length-MinEdge] window, dedupe and sort, and drop any break that
    // sits immediately next to a kept one (which would leave a single-letter fragment in the middle).
    private static IReadOnlyList<int> Normalise(List<int> candidates, int length)
    {
        var min = MinEdge;
        var max = length - MinEdge;
        var sorted = new SortedSet<int>();
        foreach (var c in candidates)
            if (c >= min && c <= max)
                sorted.Add(c);

        var result = new List<int>(sorted.Count);
        var last = -MinEdge; // so the first candidate (>= MinEdge) is always far enough from the "previous"
        foreach (var c in sorted)
        {
            if (c - last < MinEdge)
                continue;
            result.Add(c);
            last = c;
        }
        return result;
    }
}
