using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FreeW.Core.Model;

/// <summary>
/// Word's "Sounds like (English)" and "Find all word forms (English)" Find options, implemented as
/// whole-word matching over the haystack's word tokens.
/// </summary>
/// <remarks>
/// Neither option is a literal substring search. Each word of the Find what text is compared with each
/// word of the haystack, and a multi-word Find what must match consecutive haystack words separated only
/// by spaces or tabs. The comparison is case-insensitive.
/// <para>
/// Limits (this is a conservative, dictionary-free approximation of Word's proprietary proofing-tool
/// matching): <b>sounds like</b> uses American Soundex on Latin-script words, so it is coarser than a
/// true pronunciation match and only compares the first letter plus consonant classes; words in other
/// scripts, and words containing digits, match only themselves. <b>Word forms</b> uses a fixed table of
/// common English irregular forms plus regular plural / -s / -es / -ed / -ing / -er / -est / possessive
/// rules, with no lexicon; it can therefore also relate unrelated words that look like inflections of
/// each other (for example "number" and "numb") and misses irregular forms that are not in the table.</para>
/// </remarks>
public static class FindWordMatching
{
    private static readonly Regex WordPattern = new(
        @"[\p{L}\p{Nd}]+(?:['’][\p{L}\p{Nd}]+)*",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>True when the options ask for one of the two word-based searches.</summary>
    public static bool IsWordSearch(TextSearchOptions options) =>
        !options.UseWildcards && (options.SoundsLike || options.FindAllWordForms);

    /// <summary>
    /// Enumerates the non-overlapping spans of <paramref name="haystack"/> whose words are phonetic
    /// matches (or word-form matches) of the words of <paramref name="needle"/>.
    /// </summary>
    public static IEnumerable<(int Start, int Length)> FindAll(
        string haystack,
        string needle,
        TextSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(haystack);
        ArgumentNullException.ThrowIfNull(needle);

        var wanted = Tokenize(needle);
        if (wanted.Count == 0)
            yield break;

        Func<string, string, bool> same = options.SoundsLike ? SoundsLike : AreWordForms;
        var found = Tokenize(haystack);
        var i = 0;
        while (i + wanted.Count <= found.Count)
        {
            var ok = true;
            for (var j = 0; j < wanted.Count && ok; j++)
            {
                ok = same(wanted[j].Text, found[i + j].Text)
                    && (j == 0 || IsBlankGap(haystack, found[i + j - 1], found[i + j]));
            }

            if (!ok)
            {
                i++;
                continue;
            }

            var first = found[i];
            var last = found[i + wanted.Count - 1];
            yield return (first.Start, last.Start + last.Text.Length - first.Start);
            i += wanted.Count;
        }
    }

    /// <summary>American Soundex equality; non-Latin or digit-bearing words match only themselves.</summary>
    public static bool SoundsLike(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            return true;
        var codeA = Soundex(a);
        return codeA is not null && codeA == Soundex(b);
    }

    /// <summary>
    /// The four-character American Soundex code of <paramref name="word"/>, or null when the word is not
    /// made only of Latin letters (after folding diacritics and dropping apostrophes).
    /// </summary>
    public static string? Soundex(string word)
    {
        ArgumentNullException.ThrowIfNull(word);

        var letters = new StringBuilder();
        foreach (var c in word.Normalize(NormalizationForm.FormD))
        {
            if (c is '\'' or '’' || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            var lower = char.ToLowerInvariant(c);
            if (lower is < 'a' or > 'z')
                return null;
            letters.Append(lower);
        }

        if (letters.Length == 0)
            return null;

        var code = new StringBuilder(4).Append(char.ToUpperInvariant(letters[0]));
        var previous = SoundexDigit(letters[0]);
        for (var i = 1; i < letters.Length && code.Length < 4; i++)
        {
            var c = letters[i];
            var digit = SoundexDigit(c);
            if (digit != '0' && digit != previous)
                code.Append(digit);
            // H and W do not separate letters of the same class; vowels do.
            if (c is not ('h' or 'w'))
                previous = digit;
        }

        while (code.Length < 4)
            code.Append('0');
        return code.ToString();
    }

    private static char SoundexDigit(char c) => c switch
    {
        'b' or 'f' or 'p' or 'v' => '1',
        'c' or 'g' or 'j' or 'k' or 'q' or 's' or 'x' or 'z' => '2',
        'd' or 't' => '3',
        'l' => '4',
        'm' or 'n' => '5',
        'r' => '6',
        _ => '0',
    };

    /// <summary>
    /// True when the two words are forms of one English word: equal, one an inflection of the other, or
    /// both inflections of a common base (see the type remarks for the limits).
    /// </summary>
    public static bool AreWordForms(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            return true;
        var left = BaseCandidateSet(a);
        var right = BaseCandidateSet(b);
        return left.Overlaps(right);
    }

    /// <summary>The set of base forms the word could be an inflection of, always including the word.</summary>
    public static IReadOnlyCollection<string> BaseCandidates(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        return BaseCandidateSet(word);
    }

    private static HashSet<string> BaseCandidateSet(string word)
    {
        var w = word.ToLowerInvariant().Replace('’', '\'');
        var set = new HashSet<string>(StringComparer.Ordinal) { w };

        // Possessives: dog's, dogs'.
        if (w.EndsWith("'s", StringComparison.Ordinal))
            w = w[..^2];
        else if (w.EndsWith("s'", StringComparison.Ordinal))
            w = w[..^1];
        w = w.Replace("'", string.Empty);
        set.Add(w);

        if (Irregular.TryGetValue(w, out var lemma))
            set.Add(lemma);

        void Add(string stem)
        {
            if (stem.Length >= 3)
                set.Add(stem);
        }

        // Each rule contributes candidate bases; derived stems must keep at least three letters so
        // short function words ("is", "its", "bed", "sing") are not collapsed onto unrelated stems.
        if (w.EndsWith("ies", StringComparison.Ordinal)) Add(w[..^3] + "y");
        if (w.EndsWith("ied", StringComparison.Ordinal)) Add(w[..^3] + "y");
        if (w.EndsWith("ier", StringComparison.Ordinal)) Add(w[..^3] + "y");
        if (w.EndsWith("iest", StringComparison.Ordinal)) Add(w[..^4] + "y");
        if (w.EndsWith("ves", StringComparison.Ordinal))
        {
            Add(w[..^3] + "f");
            Add(w[..^3] + "fe");
        }

        if (w.EndsWith("ying", StringComparison.Ordinal)) Add(w[..^4] + "ie");

        if (w.EndsWith("es", StringComparison.Ordinal)
            && w.Length > 3
            && (w.EndsWith("ses", StringComparison.Ordinal)
                || w.EndsWith("xes", StringComparison.Ordinal)
                || w.EndsWith("zes", StringComparison.Ordinal)
                || w.EndsWith("ches", StringComparison.Ordinal)
                || w.EndsWith("shes", StringComparison.Ordinal)
                || w.EndsWith("oes", StringComparison.Ordinal)))
        {
            Add(w[..^2]);
        }

        if (w.EndsWith('s') && !w.EndsWith("ss", StringComparison.Ordinal) && !w.EndsWith("us", StringComparison.Ordinal))
            Add(w[..^1]);

        foreach (var suffix in VowelSuffixes)
        {
            if (!w.EndsWith(suffix, StringComparison.Ordinal))
                continue;
            var stem = w[..^suffix.Length];
            Add(stem);
            Add(stem + "e");
            if (stem.Length >= 2 && stem[^1] == stem[^2] && !"aeiou".Contains(stem[^1]))
                Add(stem[..^1]);
        }

        // Fold irregular lemmas for any base we reached ("goes" -> "go", "children's" -> "child").
        foreach (var candidate in set.ToArray())
        {
            if (Irregular.TryGetValue(candidate, out var l))
                set.Add(l);
        }

        return set;
    }

    private static readonly string[] VowelSuffixes = ["ing", "ed", "er", "est"];

    private static readonly IReadOnlyDictionary<string, string> Irregular = BuildIrregular();

    private static Dictionary<string, string> BuildIrregular()
    {
        // lemma -> forms. Covers the common irregular verbs, nouns and adjectives of everyday English.
        string[][] groups =
        [
            ["be", "am", "is", "are", "was", "were", "been", "being"],
            ["have", "has", "had", "having"],
            ["do", "does", "did", "done", "doing"],
            ["go", "goes", "went", "gone", "going"],
            ["say", "says", "said", "saying"],
            ["make", "makes", "made", "making"],
            ["take", "takes", "took", "taken", "taking"],
            ["see", "sees", "saw", "seen", "seeing"],
            ["come", "comes", "came", "coming"],
            ["get", "gets", "got", "gotten", "getting"],
            ["give", "gives", "gave", "given", "giving"],
            ["know", "knows", "knew", "known", "knowing"],
            ["think", "thinks", "thought", "thinking"],
            ["find", "finds", "found", "finding"],
            ["tell", "tells", "told", "telling"],
            ["run", "runs", "ran", "running"],
            ["write", "writes", "wrote", "written", "writing"],
            ["read", "reads", "reading"],
            ["begin", "begins", "began", "begun", "beginning"],
            ["bring", "brings", "brought", "bringing"],
            ["buy", "buys", "bought", "buying"],
            ["build", "builds", "built", "building"],
            ["choose", "chooses", "chose", "chosen", "choosing"],
            ["eat", "eats", "ate", "eaten", "eating"],
            ["fall", "falls", "fell", "fallen", "falling"],
            ["feel", "feels", "felt", "feeling"],
            ["fly", "flies", "flew", "flown", "flying"],
            ["forget", "forgets", "forgot", "forgotten", "forgetting"],
            ["hold", "holds", "held", "holding"],
            ["keep", "keeps", "kept", "keeping"],
            ["leave", "leaves", "left", "leaving"],
            ["lose", "loses", "lost", "losing"],
            ["mean", "means", "meant", "meaning"],
            ["meet", "meets", "met", "meeting"],
            ["pay", "pays", "paid", "paying"],
            ["put", "puts", "putting"],
            ["rise", "rises", "rose", "risen", "rising"],
            ["sell", "sells", "sold", "selling"],
            ["send", "sends", "sent", "sending"],
            ["sit", "sits", "sat", "sitting"],
            ["speak", "speaks", "spoke", "spoken", "speaking"],
            ["stand", "stands", "stood", "standing"],
            ["teach", "teaches", "taught", "teaching"],
            ["understand", "understands", "understood", "understanding"],
            ["win", "wins", "won", "winning"],
            ["man", "men"],
            ["woman", "women"],
            ["child", "children"],
            ["person", "people"],
            ["foot", "feet"],
            ["tooth", "teeth"],
            ["mouse", "mice"],
            ["goose", "geese"],
            ["life", "lives"],
            ["good", "better", "best"],
            ["bad", "worse", "worst"],
            ["many", "more", "most"],
            ["little", "less", "least"],
        ];

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            foreach (var form in group)
                map[form] = group[0];
        }

        return map;
    }

    private static List<(int Start, string Text)> Tokenize(string text)
    {
        var tokens = new List<(int, string)>();
        foreach (Match match in WordPattern.Matches(text))
            tokens.Add((match.Index, match.Value));
        return tokens;
    }

    private static bool IsBlankGap(string haystack, (int Start, string Text) previous, (int Start, string Text) next)
    {
        var from = previous.Start + previous.Text.Length;
        if (next.Start <= from)
            return false;
        for (var i = from; i < next.Start; i++)
        {
            if (haystack[i] is not (' ' or '\t' or ' '))
                return false;
        }

        return true;
    }
}
