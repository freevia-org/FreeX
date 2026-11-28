using System.Collections.Concurrent;
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
/// Limits (this is a dictionary-free approximation of Word's proprietary proofing-tool matching):
/// <b>sounds like</b> compares Metaphone keys (Lawrence Philips' public algorithm, implemented here without
/// a key-length cap) on Latin-script words, so it is a spelling-driven pronunciation guess that ignores
/// vowel quality; words in other scripts, and words containing digits, match only themselves.
/// <b>Word forms</b> works generatively: the Find what word is reduced to the base forms (lemmas) it could
/// be an inflection of, each lemma is accepted only when the generator can reproduce the Find what word
/// from it, and the haystack words are then compared with the set of forms generated from those lemmas.
/// The generator knows an embedded table of irregular verbs, irregular plurals and irregular comparatives
/// plus the regular orthography rules (-s/-es/-ies/-ves, -ed/-ied, -ing with e-drop and consonant doubling,
/// -er/-est only for listed gradable adjectives). It has no lexicon beyond those small tables, so it
/// still cannot tell every look-alike from a true inflection.</para>
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

        var matchers = new List<Func<string, bool>>(wanted.Count);
        foreach (var token in wanted)
        {
            if (options.SoundsLike)
            {
                var text = token.Text;
                matchers.Add(candidate => SoundsLike(text, candidate));
            }
            else
            {
                var forms = WordForms(token.Text);
                matchers.Add(candidate => forms.Contains(NormalizeWord(candidate)));
            }
        }

        var found = Tokenize(haystack);
        var i = 0;
        while (i + wanted.Count <= found.Count)
        {
            var ok = true;
            for (var j = 0; j < wanted.Count && ok; j++)
            {
                ok = matchers[j](found[i + j].Text)
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

    // ---------------------------------------------------------------------------------------------
    // Sounds like: Metaphone
    // ---------------------------------------------------------------------------------------------

    /// <summary>Metaphone-key equality; non-Latin or digit-bearing words match only themselves.</summary>
    public static bool SoundsLike(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            return true;
        var keyA = PhoneticKey(a);
        return keyA is not null && keyA == PhoneticKey(b);
    }

    /// <summary>
    /// The Metaphone key of <paramref name="word"/> (uppercase, uncapped length; "0" stands for the
    /// "th" sound), or null when the word is not made only of Latin letters (after folding diacritics and
    /// dropping apostrophes).
    /// </summary>
    public static string? PhoneticKey(string word)
    {
        ArgumentNullException.ThrowIfNull(word);

        var sb = new StringBuilder();
        foreach (var c in word.Normalize(NormalizationForm.FormD))
        {
            if (c is '\'' or '’' || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            var upper = char.ToUpperInvariant(c);
            if (upper is < 'A' or > 'Z')
                return null;
            sb.Append(upper);
        }

        if (sb.Length == 0)
            return null;

        var w = sb.ToString();
        var n = w.Length;
        var key = new StringBuilder();
        var i = 0;
        var firstVowelAllowed = true;

        if (n >= 2)
        {
            switch (w[..2])
            {
                case "AE":
                    key.Append('E');
                    i = 2;
                    break;
                case "GN" or "KN" or "PN" or "WR":
                    i = 1;
                    break;
                case "WH":
                    key.Append('W');
                    i = 2;
                    break;
            }
        }

        if (i == 0 && w[0] == 'X')
        {
            key.Append('S');
            i = 1;
        }

        if (i > 0)
            firstVowelAllowed = false;

        char At(int index) => index >= 0 && index < n ? w[index] : '\0';
        static bool Vowel(char c) => c is 'A' or 'E' or 'I' or 'O' or 'U';

        for (; i < n; i++)
        {
            var c = w[i];
            var prev = At(i - 1);
            var next = At(i + 1);
            var next2 = At(i + 2);

            if (c == prev && c != 'C')
                continue;

            switch (c)
            {
                case 'A' or 'E' or 'I' or 'O' or 'U':
                    if (i == 0 && firstVowelAllowed)
                        key.Append(c);
                    break;
                case 'B':
                    if (!(i == n - 1 && prev == 'M'))
                        key.Append('B');
                    break;
                case 'C':
                    if (next == 'I' && next2 == 'A')
                    {
                        key.Append('X');
                    }
                    else if (next == 'H')
                    {
                        key.Append(prev == 'S' || next2 == 'R' ? 'K' : 'X');
                        i++;
                    }
                    else if (next is 'I' or 'E' or 'Y')
                    {
                        if (prev != 'S')
                            key.Append('S');
                    }
                    else
                    {
                        key.Append('K');
                    }

                    break;
                case 'D':
                    if (next == 'G' && next2 is 'E' or 'I' or 'Y')
                    {
                        key.Append('J');
                        i += 2;
                    }
                    else
                    {
                        key.Append('T');
                    }

                    break;
                case 'G':
                {
                    if (next == 'H')
                    {
                        if (i + 2 >= n)
                        {
                            // Word-final GH: "laugh"/"tough"-style F after U (but not OUGH); otherwise silent.
                            if (prev == 'U' && At(i - 2) != 'O')
                                key.Append('F');
                        }
                        else if (Vowel(next2))
                        {
                            key.Append('K');
                        }

                        // GH before a consonant ("night") is silent.
                        i++;
                        break;
                    }

                    if (next == 'N')
                    {
                        var atEnd = i + 2 >= n;
                        var gned = next2 == 'E' && At(i + 3) == 'D' && i + 4 >= n;
                        if (!atEnd && !gned)
                            key.Append('K');
                        break;
                    }

                    var j = i + 1;
                    while (j < n && w[j] == 'G')
                        j++;
                    var after = At(j);
                    key.Append(after is 'I' or 'E' or 'Y' ? 'J' : 'K');
                    break;
                }
                case 'H':
                    if (Vowel(next) && prev is not ('C' or 'S' or 'P' or 'T' or 'G'))
                        key.Append('H');
                    break;
                case 'K':
                    if (prev != 'C')
                        key.Append('K');
                    break;
                case 'P':
                    if (next == 'H')
                    {
                        key.Append('F');
                        i++;
                    }
                    else
                    {
                        key.Append('P');
                    }

                    break;
                case 'Q':
                    key.Append('K');
                    break;
                case 'S':
                    if (next == 'H')
                    {
                        key.Append('X');
                        i++;
                    }
                    else if (next == 'I' && next2 is 'O' or 'A')
                    {
                        key.Append('X');
                    }
                    else
                    {
                        key.Append('S');
                    }

                    break;
                case 'T':
                    if (next == 'I' && next2 is 'O' or 'A')
                    {
                        key.Append('X');
                    }
                    else if (next == 'H')
                    {
                        key.Append('0');
                        i++;
                    }
                    else if (!(next == 'C' && next2 == 'H'))
                    {
                        key.Append('T');
                    }

                    break;
                case 'V':
                    key.Append('F');
                    break;
                case 'W' or 'Y':
                    if (Vowel(next))
                        key.Append(c);
                    break;
                case 'X':
                    key.Append("KS");
                    break;
                case 'Z':
                    key.Append('S');
                    break;
                default: // F J L M N R
                    key.Append(c);
                    break;
            }
        }

        return key.ToString();
    }

    // ---------------------------------------------------------------------------------------------
    // Word forms: generator
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// True when <paramref name="b"/> is one of the forms generated from the base forms of
    /// <paramref name="a"/> (see the type remarks for the limits).
    /// </summary>
    public static bool AreWordForms(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            return true;
        return WordForms(a).Contains(NormalizeWord(b));
    }

    /// <summary>
    /// The base forms (lemmas) <paramref name="word"/> could be an inflection of, always including the
    /// word itself; every returned lemma regenerates the word.
    /// </summary>
    public static IReadOnlyCollection<string> BaseCandidates(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        var w = NormalizeWord(word);
        return IsAllLetters(w) ? Lemmas(w) : new HashSet<string>(StringComparer.Ordinal) { w };
    }

    /// <summary>All forms the generator derives from the base forms of <paramref name="word"/> (normalized, lowercase).</summary>
    public static IReadOnlyCollection<string> WordForms(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        var w = NormalizeWord(word);
        var set = new HashSet<string>(StringComparer.Ordinal) { w };
        if (!IsAllLetters(w))
            return set;
        foreach (var lemma in Lemmas(w))
        {
            foreach (var form in GenerateForms(lemma))
                set.Add(form);
        }

        return set;
    }

    /// <summary>Lowercases, folds the curly apostrophe, and removes possessive endings and apostrophes.</summary>
    private static string NormalizeWord(string word)
    {
        var w = word.ToLowerInvariant().Replace('’', '\'');
        if (w.Length > 2 && w.EndsWith("'s", StringComparison.Ordinal))
            w = w[..^2];
        else if (w.Length > 2 && w.EndsWith("s'", StringComparison.Ordinal))
            w = w[..^1];
        return w.Replace("'", string.Empty);
    }

    private static bool IsAllLetters(string w)
    {
        if (w.Length == 0)
            return false;
        foreach (var c in w)
        {
            if (!char.IsLetter(c))
                return false;
        }

        return true;
    }

    private static bool IsVowelLetter(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u';

    private static HashSet<string> Lemmas(string w)
    {
        var set = new HashSet<string>(StringComparer.Ordinal) { w };

        if (FormToLemmas.TryGetValue(w, out var direct))
            set.UnionWith(direct);

        foreach (var prefix in Prefixes)
        {
            if (w.Length > prefix.Length + 2 && w.StartsWith(prefix, StringComparison.Ordinal)
                && VerbFormToLemmas.TryGetValue(w[prefix.Length..], out var rest))
            {
                foreach (var lemma in rest)
                    set.Add(prefix + lemma);
            }
        }

        foreach (var stem in StemCandidates(w))
        {
            if (stem.Length < 3 || !HasVowel(stem) || set.Contains(stem))
                continue;
            // A derived form must be reproducible from its base: this is what rejects look-alikes such as
            // number/numb (not a gradable adjective) and news/new (adjective-only).
            if (GenerateForms(stem).Contains(w))
                set.Add(stem);
        }

        return set;
    }

    private static bool HasVowel(string s)
    {
        foreach (var c in s)
        {
            if (IsVowelLetter(c) || c == 'y')
                return true;
        }

        return false;
    }

    private static string Undouble(string s) =>
        s.Length >= 2 && s[^1] == s[^2] && !IsVowelLetter(s[^1]) ? s[..^1] : s;

    private static IEnumerable<string> StemCandidates(string w)
    {
        if (w.EndsWith("ies", StringComparison.Ordinal))
            yield return w[..^3] + "y";
        if (w.EndsWith("ves", StringComparison.Ordinal))
        {
            yield return w[..^3] + "f";
            yield return w[..^3] + "fe";
        }

        if (w.EndsWith("es", StringComparison.Ordinal))
        {
            var stem = w[..^2];
            yield return stem;
            yield return Undouble(stem);
        }

        if (w.EndsWith('s')
            && !w.EndsWith("ss", StringComparison.Ordinal)
            && !w.EndsWith("us", StringComparison.Ordinal)
            && !w.EndsWith("is", StringComparison.Ordinal))
        {
            yield return w[..^1];
        }

        if (w.EndsWith("ied", StringComparison.Ordinal) || w.EndsWith("ier", StringComparison.Ordinal))
            yield return w[..^3] + "y";
        if (w.EndsWith("iest", StringComparison.Ordinal))
            yield return w[..^4] + "y";
        if (w.EndsWith("ying", StringComparison.Ordinal))
            yield return w[..^4] + "ie";

        foreach (var suffix in VowelSuffixes)
        {
            if (!w.EndsWith(suffix, StringComparison.Ordinal))
                continue;
            var stem = w[..^suffix.Length];
            yield return stem;
            yield return stem + "e";
            yield return Undouble(stem);
            if (stem.EndsWith("ck", StringComparison.Ordinal))
                yield return stem[..^1];
        }
    }

    private static readonly string[] VowelSuffixes = ["ing", "ed", "er", "est"];

    private static HashSet<string> GenerateForms(string lemma)
    {
        HashSet<string> forms;
        if (Entries.TryGetValue(lemma, out var entry))
        {
            forms = new HashSet<string>(entry, StringComparer.Ordinal);
            if (Gradable.Contains(lemma))
                AddComparativeForms(lemma, forms);
        }
        else
        {
            forms = RegularForms(lemma);
            foreach (var prefix in Prefixes)
            {
                if (lemma.Length > prefix.Length + 1 && lemma.StartsWith(prefix, StringComparison.Ordinal)
                    && VerbEntries.TryGetValue(lemma[prefix.Length..], out var inner))
                {
                    foreach (var form in inner)
                        forms.Add(prefix + form);
                }
            }
        }

        forms.RemoveWhere(f => f != lemma && Lexicalized.Contains(f));
        return forms;
    }

    private static HashSet<string> RegularForms(string b)
    {
        var s = new HashSet<string>(StringComparer.Ordinal) { b };
        if (b.Length < 3)
            return s;
        if (NoInflection.Contains(b))
        {
            s.Add(b + "s");
            return s;
        }

        var adjectiveOnly = AdjectiveOnly.Contains(b);
        if (!adjectiveOnly)
        {
            AddSForms(b, s);
            AddVerbForms(b, s);
        }

        if (adjectiveOnly || Gradable.Contains(b))
            AddComparativeForms(b, s);
        return s;
    }

    private static bool EndsConsonantY(string b) =>
        b.Length >= 2 && b[^1] == 'y' && !IsVowelLetter(b[^2]);

    private static void AddSForms(string b, HashSet<string> s)
    {
        if (EndsConsonantY(b))
        {
            s.Add(b[..^1] + "ies");
            return;
        }

        if (b.EndsWith('s') || b.EndsWith('x') || b.EndsWith('z')
            || b.EndsWith("ch", StringComparison.Ordinal) || b.EndsWith("sh", StringComparison.Ordinal))
        {
            s.Add(b + "es");
            if ((b.EndsWith('s') || b.EndsWith('z')) && CvcAtEnd(b) && SyllableGroups(b) == 1)
                s.Add(b + b[^1] + "es");
            return;
        }

        s.Add(b + "s");
        if (b.EndsWith('o') && b.Length >= 2 && !IsVowelLetter(b[^2]))
            s.Add(b + "es");
        if (b.EndsWith("fe", StringComparison.Ordinal))
            s.Add(b[..^2] + "ves");
        else if (b.EndsWith('f') && !b.EndsWith("ff", StringComparison.Ordinal))
            s.Add(b[..^1] + "ves");
    }

    private static void AddVerbForms(string b, HashSet<string> s)
    {
        if (b.EndsWith("ie", StringComparison.Ordinal))
        {
            s.Add(b + "d");
            s.Add(b[..^2] + "ying");
            return;
        }

        if (b.EndsWith('e'))
        {
            s.Add(b + "d");
            // see/dye/hoe keep the e before -ing; change/make drop it.
            s.Add(b.EndsWith("ee", StringComparison.Ordinal) || b.EndsWith("ye", StringComparison.Ordinal)
                || b.EndsWith("oe", StringComparison.Ordinal)
                ? b + "ing"
                : b[..^1] + "ing");
            return;
        }

        if (EndsConsonantY(b))
        {
            s.Add(b[..^1] + "ied");
            s.Add(b + "ing");
            return;
        }

        s.Add(b + "ed");
        s.Add(b + "ing");
        if (DoublesFinalConsonant(b))
        {
            s.Add(b + b[^1] + "ed");
            s.Add(b + b[^1] + "ing");
        }
        else if (b.EndsWith('l') && CvcAtEnd(b) && SyllableGroups(b) > 1)
        {
            // British spelling: travelled / travelling.
            s.Add(b + "led");
            s.Add(b + "ling");
        }
        else if (b.EndsWith("ic", StringComparison.Ordinal))
        {
            s.Add(b + "ked");
            s.Add(b + "king");
        }
    }

    private static void AddComparativeForms(string b, HashSet<string> s)
    {
        if (EndsConsonantY(b))
        {
            s.Add(b[..^1] + "ier");
            s.Add(b[..^1] + "iest");
        }
        else if (b.EndsWith('e'))
        {
            s.Add(b + "r");
            s.Add(b + "st");
        }
        else
        {
            s.Add(b + "er");
            s.Add(b + "est");
            if (DoublesFinalConsonant(b))
            {
                s.Add(b + b[^1] + "er");
                s.Add(b + b[^1] + "est");
            }
        }
    }

    /// <summary>True when the last three letters are consonant, single vowel, consonant (not w/x/y).</summary>
    private static bool CvcAtEnd(string b)
    {
        if (b.Length < 3)
            return false;
        var last = b[^1];
        var vowel = b[^2];
        var first = b[^3];
        if (!IsVowelLetter(vowel) || IsVowelLetter(last) || last is 'w' or 'x' or 'y')
            return false;
        if (!IsVowelLetter(first))
            return true;
        // "qu" counts as a consonant cluster: quit -> quitting.
        return first == 'u' && b.Length >= 4 && b[^4] == 'q';
    }

    private static int SyllableGroups(string b)
    {
        var groups = 0;
        var inVowel = false;
        for (var i = 0; i < b.Length; i++)
        {
            var isVowel = IsVowelLetter(b[i]) || (b[i] == 'y' && i > 0);
            if (isVowel && !inVowel)
                groups++;
            inVowel = isVowel;
        }

        if (groups > 1 && b.EndsWith('e') && !b.EndsWith("le", StringComparison.Ordinal))
            groups--;
        return groups;
    }

    private static bool DoublesFinalConsonant(string b)
    {
        if (!CvcAtEnd(b))
            return false;
        if (SyllableGroups(b) == 1 || StressedFinal.Contains(b))
            return true;
        foreach (var prefix in Prefixes)
        {
            if (b.Length > prefix.Length + 2 && b.StartsWith(prefix, StringComparison.Ordinal))
            {
                var rest = b[prefix.Length..];
                if (CvcAtEnd(rest) && SyllableGroups(rest) == 1)
                    return true;
            }
        }

        return false;
    }

    // ---------------------------------------------------------------------------------------------
    // Word forms: embedded tables
    // ---------------------------------------------------------------------------------------------

    private static readonly string[] Prefixes =
        ["under", "over", "with", "fore", "down", "inter", "out", "mis", "up", "un", "re", "be"];

    private static HashSet<string> WordSet(string words) =>
        new(words.Split([' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);

    /// <summary>Multi-syllable bases whose final stressed syllable doubles its consonant (begin -> beginning).</summary>
    private static readonly HashSet<string> StressedFinal = WordSet(
        "begin forget regret admit commit permit submit omit transmit refer prefer defer confer infer occur recur " +
        "incur concur control patrol compel expel repel propel rebel excel equip overlap outwit upset acquit " +
        "deter abhor allot annul beset handicap kidnap wiretap forbid format emit remit refit unwrap enrol enthral " +
        "inter debar bestir recap");

    /// <summary>Bases that never take the regular verb/noun endings the generator would otherwise invent.</summary>
    private static readonly HashSet<string> NoInflection = WordSet("fee bee gee lee wee tee pee nee zee dee");

    /// <summary>Words that merely look like inflections ("evening" is not a form of "even").</summary>
    private static readonly HashSet<string> Lexicalized = WordSet(
        "morning evening ceiling sibling darling earring shilling farthing herring sterling inning");

    /// <summary>Adjectives that take no noun or verb endings: new has no "news" form.</summary>
    private static readonly HashSet<string> AdjectiveOnly = WordSet(
        "new old big tall wide young late nice rich poor great brave cute huge tiny polite rude shy proud " +
        "strange simple gentle noble pure rare wise ripe lame crude vague");

    /// <summary>Gradable adjectives (also usable as nouns or verbs) that take -er/-est.</summary>
    private static readonly HashSet<string> Gradable = WordSet(
        "dark light warm cool cold hot clean close slow fast quick quiet loud soft hard harsh smooth rough " +
        "sharp blunt thin thick fat slim flat deep shallow high low long short strong weak sweet sour bitter " +
        "fresh stale cheap safe fine kind mild wild tame bold calm clear dull dense dry wet damp full empty " +
        "plain fair firm grand grim keen lean mean neat proud sad glad mad bad red blue green pale tight loose " +
        "straight steep stiff stout swift tough vast wealthy lovely happy easy busy heavy lazy pretty ugly " +
        "angry hungry dirty funny silly early lucky noisy tiny healthy sunny cloudy windy rainy snowy shiny " +
        "friendly lonely gloomy greedy handy hasty icy jolly messy nasty needy pretty risky rusty salty " +
        "shaky sleepy sticky stormy sturdy tasty thirsty tidy trendy wavy weary windy worthy young tender " +
        "slender clever narrow shallow gentle simple able humble feeble noble subtle brief crisp fierce " +
        "moist rigid sane serene severe sincere stern thorough vivid wide bright cute huge nice rich poor " +
        "great brave late old tall big new proud shy polite rude strange pure rare wise ripe lame crude vague");

    // Filled once by the static constructor and only read afterwards; concurrent types keep the shared
    // static collection guard (r517) satisfied without a lock on the read path.
    private static readonly ConcurrentDictionary<string, string[]> Entries = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, string[]> VerbEntries = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, HashSet<string>> FormToLemmas = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, HashSet<string>> VerbFormToLemmas = new(StringComparer.Ordinal);

    static FindWordMatching()
    {
        BuildEntries();
    }

    private static void BuildEntries()
    {
        // "v:lemma past participle" lines are irregular verbs: the table supplies the past and participle
        // (alternatives separated by "/") and the generator supplies -s and -ing. "x:" lines list every form
        // literally (be/have, irregular comparatives); "n:" lines list irregular noun plurals.
        const string table = """
            v:arise arose arisen
            v:awake awoke/awaked awoken/awaked
            v:bear bore born/borne
            v:beat beat beaten
            v:begin began begun
            v:bend bent bent
            v:bet bet/betted bet/betted
            v:bid bid/bade bid/bidden
            v:bind bound bound
            v:bite bit bitten
            v:bleed bled bled
            v:blow blew blown
            v:break broke broken
            v:breed bred bred
            v:bring brought brought
            v:build built built
            v:burn burnt/burned burnt/burned
            v:burst burst burst
            v:buy bought bought
            v:cast cast cast
            v:catch caught caught
            v:choose chose chosen
            v:cling clung clung
            v:come came come
            v:cost cost cost
            v:creep crept crept
            v:cut cut cut
            v:deal dealt dealt
            v:dig dug dug
            v:dive dove/dived dived
            v:do did done
            v:draw drew drawn
            v:dream dreamt/dreamed dreamt/dreamed
            v:drink drank drunk
            v:drive drove driven
            v:dwell dwelt dwelt
            v:eat ate eaten
            v:fall fell fallen
            v:feed fed fed
            v:feel felt felt
            v:fight fought fought
            v:find found found
            v:flee fled fled
            v:fling flung flung
            v:fly flew flown
            v:forbid forbade/forbad forbidden
            v:forget forgot forgotten/forgot
            v:forgive forgave forgiven
            v:forsake forsook forsaken
            v:freeze froze frozen
            v:get got gotten/got
            v:give gave given
            v:go went gone/been
            v:grind ground ground
            v:grow grew grown
            v:hang hung/hanged hung/hanged
            v:hear heard heard
            v:hew hewed hewn/hewed
            v:hide hid hidden
            v:hit hit hit
            v:hold held held
            v:hurt hurt hurt
            v:keep kept kept
            v:kneel knelt/kneeled knelt/kneeled
            v:know knew known
            v:lay laid laid
            v:lead led led
            v:lean leant/leaned leant/leaned
            v:leap leapt/leaped leapt/leaped
            v:learn learnt/learned learnt/learned
            v:leave left left
            v:lend lent lent
            v:let let let
            v:lie lay/lied lain/lied
            v:light lit/lighted lit/lighted
            v:lose lost lost
            v:make made made
            v:mean meant meant
            v:meet met met
            v:mistake mistook mistaken
            v:mow mowed mown/mowed
            v:pay paid paid
            v:prove proved proven/proved
            v:put put put
            v:quit quit/quitted quit/quitted
            v:read read read
            v:rend rent rent
            v:rid rid/ridded rid/ridded
            v:ride rode ridden
            v:ring rang rung
            v:rise rose risen
            v:run ran run
            v:saw sawed sawn/sawed
            v:say said said
            v:see saw seen
            v:seek sought sought
            v:sell sold sold
            v:send sent sent
            v:set set set
            v:sew sewed sewn/sewed
            v:shake shook shaken
            v:shear sheared shorn/sheared
            v:shed shed shed
            v:shine shone/shined shone/shined
            v:shoot shot shot
            v:show showed shown/showed
            v:shrink shrank/shrunk shrunk/shrunken
            v:shut shut shut
            v:sing sang sung
            v:sink sank/sunk sunk/sunken
            v:sit sat sat
            v:slay slew/slayed slain/slayed
            v:sleep slept slept
            v:slide slid slid
            v:sling slung slung
            v:slit slit slit
            v:smell smelt/smelled smelt/smelled
            v:smite smote smitten
            v:sow sowed sown/sowed
            v:speak spoke spoken
            v:speed sped/speeded sped/speeded
            v:spell spelt/spelled spelt/spelled
            v:spend spent spent
            v:spill spilt/spilled spilt/spilled
            v:spin spun/span spun
            v:spit spat/spit spat/spit
            v:split split split
            v:spoil spoilt/spoiled spoilt/spoiled
            v:spread spread spread
            v:spring sprang/sprung sprung
            v:stand stood stood
            v:steal stole stolen
            v:stick stuck stuck
            v:sting stung stung
            v:stink stank/stunk stunk
            v:stride strode stridden
            v:strike struck struck/stricken
            v:string strung strung
            v:strive strove/strived striven/strived
            v:swear swore sworn
            v:sweep swept swept
            v:swell swelled swollen/swelled
            v:swim swam swum
            v:swing swung swung
            v:take took taken
            v:teach taught taught
            v:tear tore torn
            v:tell told told
            v:think thought thought
            v:thrive throve/thrived thriven/thrived
            v:throw threw thrown
            v:thrust thrust thrust
            v:tread trod trodden/trod
            v:wake woke/waked woken/waked
            v:wear wore worn
            v:weave wove/weaved woven/weaved
            v:wed wed/wedded wed/wedded
            v:weep wept wept
            v:wet wet/wetted wet/wetted
            v:win won won
            v:wind wound wound
            v:wring wrung wrung
            v:write wrote written
            x:be am is are was were been being
            x:have has had having
            n:man men
            n:woman women
            n:child children
            n:person people persons
            n:foot feet
            n:tooth teeth
            n:mouse mice
            n:louse lice
            n:goose geese
            n:ox oxen
            n:sheep
            n:deer
            n:fish fishes
            n:series
            n:species
            n:aircraft
            n:cactus cacti cactuses
            n:fungus fungi funguses
            n:nucleus nuclei
            n:radius radii radiuses
            n:stimulus stimuli
            n:alumnus alumni
            n:syllabus syllabi syllabuses
            n:analysis analyses
            n:basis bases
            n:crisis crises
            n:thesis theses
            n:hypothesis hypotheses
            n:diagnosis diagnoses
            n:parenthesis parentheses
            n:axis axes
            n:phenomenon phenomena
            n:criterion criteria
            n:datum data
            n:medium media mediums
            n:bacterium bacteria
            n:curriculum curricula curriculums
            n:appendix appendices appendixes
            n:index indices indexes
            n:matrix matrices matrixes
            n:vertex vertices vertexes
            n:formula formulae formulas
            n:larva larvae
            n:alga algae
            x:good better best
            x:bad worse worst
            x:far farther further farthest furthest
            x:old older elder oldest eldest
            x:many more most
            x:much more most
            x:little less least
            """;

        foreach (var raw in table.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;
            var verb = line[0] == 'v';
            var parts = line[2..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var lemma = parts[0];
            var forms = new HashSet<string>(StringComparer.Ordinal) { lemma };
            if (verb)
            {
                AddSForms(lemma, forms);
                var regular = new HashSet<string>(StringComparer.Ordinal);
                AddVerbForms(lemma, regular);
                foreach (var f in regular)
                {
                    if (f.EndsWith("ing", StringComparison.Ordinal))
                        forms.Add(f);
                }

                foreach (var part in parts.Skip(1))
                {
                    foreach (var alt in part.Split('/'))
                        forms.Add(alt);
                }
            }
            else
            {
                foreach (var part in parts.Skip(1))
                    forms.Add(part);
                // "n:" nouns keep their regular forms too (man -> manned/manning, fish -> fished).
                if (line[0] == 'n')
                    forms.UnionWith(RegularForms(lemma));
            }

            var array = forms.ToArray();
            Entries[lemma] = array;
            if (verb)
                VerbEntries[lemma] = array;
            foreach (var form in array)
            {
                Index(FormToLemmas, form, lemma);
                if (verb)
                    Index(VerbFormToLemmas, form, lemma);
            }
        }

        static void Index(ConcurrentDictionary<string, HashSet<string>> map, string form, string lemma)
        {
            if (!map.TryGetValue(form, out var set))
                map[form] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(lemma);
        }
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
