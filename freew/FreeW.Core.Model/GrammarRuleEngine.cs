using System.Text.RegularExpressions;

namespace FreeW.Core.Model;

/// <summary>One deterministic grammar rule: its stable id and its neutral (English) explanation.</summary>
public sealed record GrammarRule(string Id, string Message);

/// <summary>
/// One finding of <see cref="GrammarRuleEngine"/> in a paragraph's plain text. <see cref="Start"/> and
/// <see cref="Length"/> delimit the flagged text; each suggestion carries its own (usually smaller) edit
/// range so a fix touches as little text, and therefore as little formatting, as possible.
/// </summary>
public sealed record GrammarIssue(
    string RuleId,
    int Start,
    int Length,
    string Text,
    string Message,
    IReadOnlyList<ProofingSuggestion> Suggestions);

/// <summary>Which grammar rules run. Every rule is on by default and can be switched off by id.</summary>
public sealed class GrammarCheckOptions
{
    private readonly HashSet<string> _disabled;

    public GrammarCheckOptions(IEnumerable<string>? disabledRuleIds = null)
    {
        _disabled = new HashSet<string>(disabledRuleIds ?? [], StringComparer.Ordinal);
    }

    public static GrammarCheckOptions Default { get; } = new();

    public IReadOnlyCollection<string> DisabledRuleIds => _disabled;

    public bool IsEnabled(string ruleId) => !_disabled.Contains(ruleId);

    public GrammarCheckOptions WithRuleDisabled(string ruleId) => new(_disabled.Append(ruleId));

    public GrammarCheckOptions WithRuleEnabled(string ruleId) =>
        new(_disabled.Where(id => !string.Equals(id, ruleId, StringComparison.Ordinal)));
}

/// <summary>
/// A small, deterministic, English-only grammar and style checker over one paragraph of plain text.
/// It has no dictionary and no network: every rule is a local pattern chosen because it is safe to
/// flag, and every rule stays silent inside URLs, e-mail addresses, numbers, abbreviations and
/// code-like tokens. Callers decide language and no-proof eligibility through the excluded-offset
/// callback (see <see cref="ProofingDiagnosticPlanner"/>).
/// </summary>
public static partial class GrammarRuleEngine
{
    // How much neighbouring text a spacing issue may pull into its flagged range.
    private const int MaxContextLength = 24;

    public static IReadOnlyList<GrammarRule> Rules { get; } =
    [
        new(GrammarRuleIds.RepeatedWord, "The same word appears twice in a row."),
        new(GrammarRuleIds.SentenceCapitalization, "A sentence should begin with a capital letter."),
        new(GrammarRuleIds.MultipleSpaces, "There is more than one space between these words."),
        new(GrammarRuleIds.SpaceBeforePunctuation, "A punctuation mark should follow the preceding word without a space."),
        new(GrammarRuleIds.MissingSpaceAfterPunctuation, "A space is expected after this punctuation mark."),
        new(GrammarRuleIds.IndefiniteArticle, "Use \"a\" before a consonant sound and \"an\" before a vowel sound."),
        new(GrammarRuleIds.RepeatedPunctuation, "This punctuation mark is repeated."),
        new(GrammarRuleIds.UnpairedBracket, "This bracket has no matching partner in the paragraph."),
        new(GrammarRuleIds.UnpairedQuote, "This quotation mark has no matching partner in the paragraph."),
        new(GrammarRuleIds.CommonConfusion, "This phrase is a common mistake."),
    ];

    private static readonly HashSet<string> SentenceAbbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "mr", "mrs", "ms", "dr", "prof", "sr", "jr", "st", "vs", "etc", "no", "fig", "figs", "inc", "ltd",
        "co", "corp", "approx", "dept", "est", "vol", "pp", "ca", "cf", "al", "eq", "ed", "eds", "gen",
        "gov", "rev", "hon", "mt", "ave", "blvd", "jan", "feb", "mar", "apr", "jun", "jul", "aug", "sep",
        "sept", "oct", "nov", "dec", "viz", "resp", "incl", "esp", "abbr", "misc", "min", "max", "tel",
    };

    // Words after "a" that are far more likely a letter, a variable or a conjunction than a noun phrase.
    private static readonly HashSet<string> ArticleConnectorWords = new(StringComparer.Ordinal)
    {
        "and", "or", "is", "in", "of", "as", "at", "on", "if", "are", "it", "an", "to", "was", "were",
    };

    // "h" words where both articles are in educated use; never flagged either way.
    private static readonly HashSet<string> AmbiguousArticleWords = new(StringComparer.Ordinal)
    {
        "herb", "herbs", "herbal", "historic", "historical", "historian", "historically", "hotel",
        "homage", "heroic", "hysterical", "habitual", "hallucination",
    };

    private static readonly string[] SilentHPrefixes = ["hour", "honest", "honor", "honour", "heir"];

    // Vowel letters pronounced with a leading consonant sound ("yoo"/"wuh").
    private static readonly string[] ConsonantSoundPrefixes =
    [
        "eu", "ewe", "uni", "use", "usu", "usa", "usur", "util", "uten", "utop", "uter", "ukul", "uran",
        "urin", "uro", "ubiq", "unanim",
    ];

    // "un" + "i..." words where "uni" is NOT the "yoo-nee" prefix.
    private static readonly string[] UnPrefixedIWords = ["unin", "unim", "unid", "unir"];

    private static readonly HashSet<string> ConsonantSoundWords = new(StringComparer.Ordinal)
    {
        "one", "once", "ones", "oneself", "onetime", "url", "urls", "uri", "uris", "ufo", "ufos", "usb",
        "unix", "utf", "uuid", "uuids", "ui", "ux", "unidirectional",
    };

    [GeneratedRegex(
        @"\b(?<modal>could|should|would)(?:n['’]t)? +(?<of>of)\b(?! +course\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ModalOfRegex();

    [GeneratedRegex(@"\balot\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AlotRegex();

    // Only as a complete reply ("Your welcome!"): "your welcome mat" and "your welcome was warm" are fine.
    [GeneratedRegex(
        @"\b(?<your>your) +welcome\b(?=\s*(?:[.!,;?)]|$))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex YourWelcomeRegex();

    public static GrammarRule? FindRule(string? ruleId) =>
        Rules.FirstOrDefault(rule => string.Equals(rule.Id, ruleId, StringComparison.Ordinal));

    /// <summary>
    /// True when the proofing language is English or unspecified (an unspecified language follows the
    /// application default, which these rules are written for).
    /// </summary>
    public static bool IsEnglish(string? languageTag)
    {
        if (string.IsNullOrWhiteSpace(languageTag))
            return true;

        var tag = languageTag.Trim();
        return tag.StartsWith("en", StringComparison.OrdinalIgnoreCase)
            && (tag.Length == 2 || tag[2] is '-' or '_');
    }

    /// <summary>
    /// Checks one paragraph. <paramref name="isExcludedOffset"/> marks characters that must not be
    /// proofed (no-proof or non-English runs); an issue touching any of them is dropped. The
    /// repeated-word rule is not evaluated here: it is language-independent and owned by
    /// <see cref="ProofingDiagnosticPlanner"/>.
    /// </summary>
    public static IReadOnlyList<GrammarIssue> Check(
        string? text,
        GrammarCheckOptions? options = null,
        Func<int, bool>? isExcludedOffset = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        options ??= GrammarCheckOptions.Default;
        var scan = new Scan(text, isExcludedOffset);
        if (options.IsEnabled(GrammarRuleIds.MultipleSpaces))
            CheckMultipleSpaces(scan);
        if (options.IsEnabled(GrammarRuleIds.SpaceBeforePunctuation))
            CheckSpaceBeforePunctuation(scan);
        if (options.IsEnabled(GrammarRuleIds.MissingSpaceAfterPunctuation))
            CheckMissingSpaceAfterPunctuation(scan);
        if (options.IsEnabled(GrammarRuleIds.RepeatedPunctuation))
            CheckRepeatedPunctuation(scan);
        if (options.IsEnabled(GrammarRuleIds.SentenceCapitalization))
            CheckSentenceCapitalization(scan);
        if (options.IsEnabled(GrammarRuleIds.IndefiniteArticle))
            CheckIndefiniteArticle(scan);
        if (options.IsEnabled(GrammarRuleIds.UnpairedBracket))
            CheckUnpairedBrackets(scan);
        if (options.IsEnabled(GrammarRuleIds.UnpairedQuote))
            CheckUnpairedQuotes(scan);
        if (options.IsEnabled(GrammarRuleIds.CommonConfusion))
            CheckCommonConfusions(scan);

        return scan.Issues
            .OrderBy(issue => issue.Start)
            .ThenBy(issue => issue.RuleId, StringComparer.Ordinal)
            .ToArray();
    }

    // ── Rules ────────────────────────────────────────────────────────────────────────────────────

    private static void CheckMultipleSpaces(Scan scan)
    {
        var text = scan.Text;
        for (var i = 0; i < text.Length;)
        {
            if (text[i] != ' ')
            {
                i++;
                continue;
            }

            var start = i;
            while (i < text.Length && text[i] == ' ')
                i++;
            var length = i - start;

            // Leading/trailing spaces and long runs are indentation or manual alignment, not typos.
            if (length is < 2 or > 3
                || start == 0
                || i == text.Length
                || char.IsWhiteSpace(text[start - 1])
                || char.IsWhiteSpace(text[i]))
            {
                continue;
            }

            // Two spaces after a sentence or a colon is a typing convention, not an error.
            var before = start - 1;
            while (before > 0 && IsClosingMark(text[before]))
                before--;
            if (text[before] is '.' or '?' or '!' or ':' or '…')
                continue;

            var flaggedStart = Math.Max(scan.ChunkStartAt(start - 1), start - MaxContextLength);
            var flaggedEnd = Math.Min(scan.ChunkEndAt(i), i + MaxContextLength);
            scan.Add(
                GrammarRuleIds.MultipleSpaces,
                flaggedStart,
                flaggedEnd - flaggedStart,
                start,
                i,
                new ProofingSuggestion(" ", start, length));
        }
    }

    private static void CheckSpaceBeforePunctuation(Scan scan)
    {
        var text = scan.Text;
        for (var i = 2; i < text.Length; i++)
        {
            var mark = text[i];
            if (mark is not (',' or ';' or '!' or '?' or '.') || text[i - 1] != ' ')
                continue;

            // The mark must close a clause: followed by the end, whitespace or a closing quote. That
            // keeps ".NET", ".5", " ..." and emoticons out. A comma or semicolon glued to the NEXT word
            // ("apple ,then") is the same slip with the space on the wrong side.
            var closesClause = i + 1 == text.Length || char.IsWhiteSpace(text[i + 1]) || IsClosingQuote(text[i + 1]);
            var misplacedSpace = !closesClause && mark is ',' or ';' && char.IsLetter(text[i + 1]);
            if (!closesClause && !misplacedSpace)
                continue;

            // ". . ." is a spaced ellipsis.
            if (mark == '.' && i + 2 < text.Length && text[i + 1] == ' ' && text[i + 2] == '.')
                continue;

            var spaces = i - 1;
            while (spaces > 0 && text[spaces - 1] == ' ')
                spaces--;
            if (spaces == 0)
                continue;

            var before = text[spaces - 1];
            if (!char.IsLetterOrDigit(before) && !IsClosingMark(before))
                continue;

            var flaggedStart = Math.Max(scan.ChunkStartAt(spaces - 1), spaces - MaxContextLength);
            scan.Add(
                GrammarRuleIds.SpaceBeforePunctuation,
                flaggedStart,
                i + 1 - flaggedStart,
                spaces,
                i + 1,
                misplacedSpace
                    ? new ProofingSuggestion(mark + " ", spaces, i + 1 - spaces)
                    : new ProofingSuggestion(string.Empty, spaces, i - spaces));
        }
    }

    private static void CheckMissingSpaceAfterPunctuation(Scan scan)
    {
        var text = scan.Text;
        for (var i = 2; i < text.Length - 1; i++)
        {
            var mark = text[i];
            if (mark is not (',' or ';' or '!' or '?' or ':' or '.')
                || !char.IsLetter(text[i - 1])
                || !char.IsLetter(text[i + 1]))
            {
                continue;
            }

            // A chunk with a dot between letters is only left unprotected when it is two sentences run
            // together ("end.Next"); every other protected chunk is a URL, number, abbreviation or code.
            if (!scan.TryGetChunk(i, out var chunk) || chunk.IsProtected)
                continue;

            var leftStart = i;
            while (leftStart > chunk.Start && char.IsLetter(text[leftStart - 1]))
                leftStart--;
            var rightEnd = i + 1;
            while (rightEnd < chunk.End && char.IsLetter(text[rightEnd]))
                rightEnd++;
            if (i - leftStart < 2 || (mark == ':' && rightEnd - (i + 1) < 2))
                continue;

            scan.Add(
                GrammarRuleIds.MissingSpaceAfterPunctuation,
                leftStart,
                rightEnd - leftStart,
                leftStart,
                rightEnd,
                new ProofingSuggestion(" ", i + 1, 0));
        }
    }

    private static void CheckRepeatedPunctuation(Scan scan)
    {
        var text = scan.Text;
        for (var i = 0; i < text.Length;)
        {
            var mark = text[i];
            if (mark is not (',' or ';' or '.'))
            {
                i++;
                continue;
            }

            var start = i;
            while (i < text.Length && text[i] == mark)
                i++;
            var length = i - start;
            if (length < 2 || !scan.TryGetChunk(start, out var chunk) || chunk.IsProtected)
                continue;

            if (mark == '.')
            {
                // Exactly two dots closing a word; three or more is an ellipsis.
                if (length != 2
                    || start == 0
                    || !char.IsLetterOrDigit(text[start - 1])
                    || (i < text.Length && !char.IsWhiteSpace(text[i]) && !IsClosingMark(text[i])))
                {
                    continue;
                }

                scan.Add(
                    GrammarRuleIds.RepeatedPunctuation,
                    start,
                    length,
                    start,
                    i,
                    new ProofingSuggestion(".", start, length),
                    new ProofingSuggestion("...", start, length));
                continue;
            }

            scan.Add(
                GrammarRuleIds.RepeatedPunctuation,
                start,
                length,
                start,
                i,
                new ProofingSuggestion(mark.ToString(), start, length));
        }
    }

    private static void CheckSentenceCapitalization(Scan scan)
    {
        var text = scan.Text;

        // The paragraph's first word, but only when the paragraph reads as a sentence: fragments such
        // as list items, labels and table-like lines legitimately start in lower case.
        if (scan.Chunks.Count >= 3 && EndsWithSentenceTerminator(text))
            AddCapitalizationIssue(scan, scan.Chunks[0].Start);

        for (var i = 1; i < text.Length - 1; i++)
        {
            var mark = text[i];
            if (mark is not ('.' or '?' or '!') || !char.IsWhiteSpace(text[i + 1]))
                continue;

            if (!scan.TryGetChunk(i, out var chunk) || chunk.IsProtected)
                continue;

            if (mark == '.')
            {
                if (text[i - 1] == '.')
                    continue;

                var wordStart = i;
                while (wordStart > chunk.Start && char.IsLetter(text[wordStart - 1]))
                    wordStart--;
                var word = text[wordStart..i];
                if (word.Length <= 1 || SentenceAbbreviations.Contains(word))
                    continue;
            }

            var next = i + 1;
            while (next < text.Length && char.IsWhiteSpace(text[next]))
                next++;
            if (next < text.Length)
                AddCapitalizationIssue(scan, next);
        }
    }

    private static void AddCapitalizationIssue(Scan scan, int start)
    {
        var text = scan.Text;
        if (text[start] is < 'a' or > 'z' || !scan.TryGetChunk(start, out var chunk) || chunk.IsProtected)
            return;

        var end = start;
        while (end < chunk.End && (IsAsciiLetter(text[end]) || text[end] is '\'' or '’'))
            end++;

        // "a)" / "b." are enumeration markers, not sentence openers.
        if (end - start == 1 && end < chunk.End && text[end] is ')' or '.' or ']')
            return;

        scan.Add(
            GrammarRuleIds.SentenceCapitalization,
            start,
            end - start,
            start,
            end,
            new ProofingSuggestion(char.ToUpperInvariant(text[start]).ToString(), start, 1));
    }

    private static void CheckIndefiniteArticle(Scan scan)
    {
        var text = scan.Text;
        for (var index = 0; index < scan.Chunks.Count - 1; index++)
        {
            var chunk = scan.Chunks[index];
            var length = chunk.End - chunk.Start;
            if (length is < 1 or > 2 || text[chunk.Start] is not ('a' or 'A'))
                continue;
            var isAn = length == 2;
            if (isAn && text[chunk.Start + 1] != 'n')
                continue;

            // A capital "A" mid-sentence is a name or a label ("Plan A is ..."), not an article.
            if (text[chunk.Start] == 'A' && !IsSentenceStart(scan, index))
                continue;

            var next = scan.Chunks[index + 1];
            if (next.IsProtected || !OnlySpacesBetween(text, chunk.End, next.Start))
                continue;

            var wordEnd = next.Start;
            while (wordEnd < next.End && IsAsciiLetter(text[wordEnd]))
                wordEnd++;
            var word = text[next.Start..wordEnd];

            // Single letters and acronyms are pronounced letter by letter ("an MRI", "a UN envoy").
            if (word.Length < 2 || word.All(char.IsUpper))
                continue;

            var lower = word.ToLowerInvariant();
            if (StartsWithVowelSound(lower) is not { } vowelSound)
                continue;

            string? replacement = null;
            if (!isAn && vowelSound && !ArticleConnectorWords.Contains(lower))
                replacement = text[chunk.Start] == 'A' ? "An" : "an";
            else if (isAn && !vowelSound && lower.Length >= 3 && lower.Any(IsVowelLetter))
                replacement = text[chunk.Start] == 'A' ? "A" : "a";
            if (replacement is null)
                continue;

            scan.Add(
                GrammarRuleIds.IndefiniteArticle,
                chunk.Start,
                length,
                chunk.Start,
                wordEnd,
                new ProofingSuggestion(replacement, chunk.Start, length));
        }
    }

    private static void CheckUnpairedBrackets(Scan scan)
    {
        var text = scan.Text;
        var open = new List<int>();
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch is not ('(' or ')' or '[' or ']')
                || !scan.TryGetChunk(i, out var chunk)
                || chunk.IsProtected
                || IsEmoticonBracket(text, i))
            {
                continue;
            }

            if (ch is '(' or '[')
            {
                open.Add(i);
                continue;
            }

            var opener = ch == ')' ? '(' : '[';
            var match = open.FindLastIndex(position => text[position] == opener);
            if (match < 0)
            {
                // "a)" / "ii)" opening a chunk is an enumeration marker.
                var marker = i - chunk.Start;
                if (ch == ')' && marker is >= 1 and <= 3 && text[chunk.Start..i].All(char.IsLetter))
                    continue;

                scan.Add(GrammarRuleIds.UnpairedBracket, i, 1, i, i + 1);
                continue;
            }

            // Openers of the other kind left inside the closed pair can never be matched.
            for (var unmatched = open.Count - 1; unmatched > match; unmatched--)
                scan.Add(GrammarRuleIds.UnpairedBracket, open[unmatched], 1, open[unmatched], open[unmatched] + 1);
            open.RemoveRange(match, open.Count - match);
        }

        foreach (var position in open)
            scan.Add(GrammarRuleIds.UnpairedBracket, position, 1, position, position + 1);
    }

    private static void CheckUnpairedQuotes(Scan scan)
    {
        var text = scan.Text;

        // A quotation continued from the previous paragraph opens with a quote and never closes it.
        var first = 0;
        while (first < text.Length && char.IsWhiteSpace(text[first]))
            first++;
        var continuesQuotation = first < text.Length && text[first] is '"' or '“';

        var straight = new List<int>();
        var openCurly = new List<int>();
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch is not ('"' or '“' or '”') || !scan.TryGetChunk(i, out var chunk) || chunk.IsProtected)
                continue;

            switch (ch)
            {
                case '"':
                    straight.Add(i);
                    break;
                case '“':
                    openCurly.Add(i);
                    break;
                default:
                    if (openCurly.Count > 0)
                        openCurly.RemoveAt(openCurly.Count - 1);
                    else
                        scan.Add(GrammarRuleIds.UnpairedQuote, i, 1, i, i + 1);
                    break;
            }
        }

        if (continuesQuotation)
            return;

        if (straight.Count % 2 == 1)
            scan.Add(GrammarRuleIds.UnpairedQuote, straight[^1], 1, straight[^1], straight[^1] + 1);
        foreach (var position in openCurly)
            scan.Add(GrammarRuleIds.UnpairedQuote, position, 1, position, position + 1);
    }

    private static void CheckCommonConfusions(Scan scan)
    {
        var text = scan.Text;
        if (text.Contains(" of", StringComparison.OrdinalIgnoreCase))
        {
            foreach (Match match in ModalOfRegex().Matches(text))
            {
                var of = match.Groups["of"];
                AddConfusion(scan, match, of.Index, of.Length, MatchCase(of.Value, "have"));
            }
        }

        if (text.Contains("alot", StringComparison.OrdinalIgnoreCase))
        {
            foreach (Match match in AlotRegex().Matches(text))
                AddConfusion(scan, match, match.Index, match.Length, MatchCase(match.Value, "a lot"));
        }

        if (text.Contains("welcome", StringComparison.OrdinalIgnoreCase))
        {
            foreach (Match match in YourWelcomeRegex().Matches(text))
            {
                var your = match.Groups["your"];
                AddConfusion(scan, match, your.Index, your.Length, MatchCase(your.Value, "you're"));
            }
        }
    }

    private static void AddConfusion(Scan scan, Match match, int editStart, int editLength, string replacement)
    {
        for (var i = match.Index; i < match.Index + match.Length; i++)
        {
            if (scan.TryGetChunk(i, out var chunk) && chunk.IsProtected)
                return;
        }

        scan.Add(
            GrammarRuleIds.CommonConfusion,
            match.Index,
            match.Length,
            match.Index,
            match.Index + match.Length,
            new ProofingSuggestion(replacement, editStart, editLength));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private static string MatchCase(string source, string replacement)
    {
        if (source.Length > 1 && source.All(ch => !char.IsLetter(ch) || char.IsUpper(ch)))
            return replacement.ToUpperInvariant();
        if (source.Length > 0 && char.IsUpper(source[0]))
            return char.ToUpperInvariant(replacement[0]) + replacement[1..];
        return replacement;
    }

    /// <summary>
    /// True for a vowel sound, false for a consonant sound, null when the word is genuinely ambiguous
    /// ("an historic" / "a historic") and must not be flagged.
    /// </summary>
    internal static bool? StartsWithVowelSound(string lowerWord)
    {
        if (lowerWord.Length == 0 || AmbiguousArticleWords.Contains(lowerWord))
            return null;
        if (ConsonantSoundWords.Contains(lowerWord))
            return false;
        if (SilentHPrefixes.Any(prefix => lowerWord.StartsWith(prefix, StringComparison.Ordinal)))
            return true;
        if (UnPrefixedIWords.Any(prefix => lowerWord.StartsWith(prefix, StringComparison.Ordinal)))
            return true;
        if (ConsonantSoundPrefixes.Any(prefix => lowerWord.StartsWith(prefix, StringComparison.Ordinal)))
            return false;
        return IsVowelLetter(lowerWord[0]);
    }

    private static bool IsSentenceStart(Scan scan, int chunkIndex)
    {
        if (chunkIndex == 0)
            return true;

        var previous = scan.Chunks[chunkIndex - 1];
        var last = previous.End - 1;
        while (last > previous.Start && IsClosingMark(scan.Text[last]))
            last--;
        return scan.Text[last] is '.' or '?' or '!' or ':';
    }

    private static bool EndsWithSentenceTerminator(string text)
    {
        var last = text.Length - 1;
        while (last > 0 && (char.IsWhiteSpace(text[last]) || IsClosingMark(text[last])))
            last--;
        return text[last] is '.' or '?' or '!';
    }

    private static bool OnlySpacesBetween(string text, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            if (text[i] != ' ')
                return false;
        }

        return end > start;
    }

    private static bool IsEmoticonBracket(string text, int index)
    {
        if (index == 0)
            return false;

        var previous = text[index - 1];
        if (previous is ':' or ';' or '=')
            return true;
        return previous == '-' && index > 1 && text[index - 2] is ':' or ';';
    }

    private static bool IsAsciiLetter(char ch) => ch is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');

    private static bool IsVowelLetter(char ch) => ch is 'a' or 'e' or 'i' or 'o' or 'u';

    private static bool IsClosingQuote(char ch) => ch is '"' or '\'' or '”' or '’';

    private static bool IsClosingMark(char ch) => IsClosingQuote(ch) || ch is ')' or ']';

    private readonly record struct Chunk(int Start, int End, bool IsProtected);

    /// <summary>One paragraph split into whitespace-delimited chunks, each classified once.</summary>
    private sealed class Scan
    {
        private readonly Func<int, bool>? _isExcludedOffset;
        private readonly int[] _chunkOf;

        public Scan(string text, Func<int, bool>? isExcludedOffset)
        {
            Text = text;
            _isExcludedOffset = isExcludedOffset;
            _chunkOf = new int[text.Length];
            Array.Fill(_chunkOf, -1);

            var chunks = new List<Chunk>();
            for (var i = 0; i < text.Length;)
            {
                if (char.IsWhiteSpace(text[i]))
                {
                    i++;
                    continue;
                }

                var start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]))
                    i++;
                for (var offset = start; offset < i; offset++)
                    _chunkOf[offset] = chunks.Count;
                chunks.Add(new Chunk(start, i, IsProtectedChunk(text.AsSpan(start, i - start))));
            }

            Chunks = chunks;
        }

        public string Text { get; }

        public IReadOnlyList<Chunk> Chunks { get; }

        public List<GrammarIssue> Issues { get; } = [];

        public bool TryGetChunk(int offset, out Chunk chunk)
        {
            var index = offset >= 0 && offset < _chunkOf.Length ? _chunkOf[offset] : -1;
            chunk = index < 0 ? default : Chunks[index];
            return index >= 0;
        }

        public int ChunkStartAt(int offset) => TryGetChunk(offset, out var chunk) ? chunk.Start : offset;

        public int ChunkEndAt(int offset) => TryGetChunk(offset, out var chunk) ? chunk.End : offset;

        public void Add(
            string ruleId,
            int start,
            int length,
            int triggerStart,
            int triggerEnd,
            params ProofingSuggestion[] suggestions)
        {
            if (_isExcludedOffset is not null)
            {
                for (var offset = Math.Min(start, triggerStart); offset < Math.Max(start + length, triggerEnd); offset++)
                {
                    if (_isExcludedOffset(offset))
                        return;
                }
            }

            Issues.Add(new GrammarIssue(
                ruleId,
                start,
                length,
                Text.Substring(start, length),
                FindRule(ruleId)!.Message,
                suggestions));
        }

        // URLs, e-mail addresses, numbers, abbreviations with inner dots, file names, paths and
        // code-like tokens: nothing inside such a chunk is ever flagged.
        private static bool IsProtectedChunk(ReadOnlySpan<char> chunk)
        {
            if (chunk.Contains("://", StringComparison.Ordinal)
                || chunk.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var slashes = 0;
            var innerDot = false;
            for (var i = 0; i < chunk.Length; i++)
            {
                var ch = chunk[i];
                if (char.IsDigit(ch))
                    return true;

                switch (ch)
                {
                    case '@' or '_' or '\\' or '=' or '<' or '>' or '{' or '}' or '#' or '$' or '%' or '^'
                        or '*' or '|' or '~' or '`' or '+':
                        return true;
                    case '/':
                        slashes++;
                        break;
                }

                if (i == 0)
                    continue;

                var previous = chunk[i - 1];
                if ((char.IsLower(previous) && char.IsUpper(ch))
                    || (ch == ':' && previous == ':')
                    || (ch == ')' && previous == '('))
                {
                    return true;
                }

                if (ch == '.' && i < chunk.Length - 1 && char.IsLetter(previous) && char.IsLetter(chunk[i + 1]))
                    innerDot = true;
            }

            if (slashes > 1 || (slashes == 1 && (chunk[0] == '/' || chunk.Contains('.'))))
                return true;

            return innerDot && !IsSentenceJoin(chunk);
        }

        // "end.Next": one dot between a lower-case word and a capitalised word, i.e. two sentences run
        // together rather than a domain, a file name or an abbreviation.
        private static bool IsSentenceJoin(ReadOnlySpan<char> chunk)
        {
            var start = 0;
            while (start < chunk.Length && !char.IsLetter(chunk[start]))
                start++;
            var end = chunk.Length;
            while (end > start && !char.IsLetter(chunk[end - 1]))
                end--;

            var core = chunk[start..end];
            var dot = core.IndexOf('.');
            if (dot < 2 || dot != core.LastIndexOf('.') || core.Length - dot - 1 < 2)
                return false;

            for (var i = 0; i < core.Length; i++)
            {
                if (i == dot)
                    continue;
                var ch = core[i];
                if (!char.IsLetter(ch))
                    return false;
                var mustBeUpper = i == dot + 1;
                var mayBeUpper = i == 0 || mustBeUpper;
                if ((mustBeUpper && !char.IsUpper(ch)) || (!mayBeUpper && !char.IsLower(ch)))
                    return false;
            }

            return true;
        }
    }
}
