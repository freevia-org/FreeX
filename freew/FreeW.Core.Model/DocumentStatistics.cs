namespace FreeW.Core.Model;

/// <summary>
/// A richer, read-only statistics summary for a document: the basic counts from
/// <see cref="WordCount"/> (words / characters / paragraphs) plus sentence count, an estimated
/// reading time, average words per sentence, and a Flesch Reading Ease readability score.
///
/// Pure data produced by <see cref="Compute(TextDocument)"/>; carries no WPF or I/O dependency so it
/// stays unit-testable in the model project. The boundary rules are:
///
/// <list type="bullet">
/// <item><b>Words / characters / paragraphs</b>: delegated to <see cref="WordCount"/> (words are
///   maximal non-whitespace runs; paragraphs include table-cell paragraphs).</item>
/// <item><b>Sentences</b>: maximal runs of the terminators <c>.</c> / <c>!</c> / <c>?</c>. A run such
///   as <c>"?!"</c> or an ellipsis <c>"..."</c> counts as one sentence end, so terminator runs never
///   inflate the count. Text that has words but no terminator is treated as one sentence so the
///   per-sentence averages stay meaningful (and divide-by-zero is impossible).</item>
/// <item><b>Reading time</b>: <c>ceil(words / 200)</c> minutes (200 wpm is a common average adult
///   silent-reading rate). Any document with words reads in at least one minute.</item>
/// <item><b>Average words per sentence</b>: <c>words / sentences</c>.</item>
/// <item><b>Flesch Reading Ease</b>:
///   <c>206.835 − 1.015 × (words/sentences) − 84.6 × (syllables/words)</c>. Higher is easier
///   (100+ very easy, 0 very hard). Syllables use a simple vowel-group heuristic
///   (see <see cref="CountWordSyllables"/>): the number of vowel groups in a word, discounting a
///   silent trailing "e", with a floor of one syllable per word.</item>
/// </list>
///
/// All outputs are deterministic functions of the input text and divide-by-zero is guarded
/// everywhere (an empty document returns <see cref="Empty"/>).
/// </summary>
public readonly record struct DocumentStatistics(
    int Words,
    int CharactersWithSpaces,
    int CharactersWithoutSpaces,
    int Paragraphs,
    int Sentences,
    int Syllables,
    int ReadingTimeMinutes,
    double AverageWordsPerSentence,
    double FleschReadingEase)
{
    /// <summary>An all-zero summary (an empty document). Flesch ease defaults to 0.</summary>
    public static readonly DocumentStatistics Empty = new(0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>Words read per minute used for the reading-time estimate (a common adult average).</summary>
    public const int WordsPerMinute = 200;

    /// <summary>Computes the full statistics summary for a whole document.</summary>
    public static DocumentStatistics Compute(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        // Reuse the audited basic counts (words/chars/paragraphs, including table-cell paragraphs),
        // and flatten the document (paragraphs joined by newlines) for sentence/syllable scanning.
        var basic = WordCount.Of(document);
        return Build(basic, document.PlainText);
    }

    /// <summary>
    /// Computes the statistics summary for a single block of plain text. Paragraphs are counted as the
    /// number of non-empty newline-delimited lines (at least one when any text is present), so the
    /// helper is usable without a <see cref="TextDocument"/>.
    /// </summary>
    public static DocumentStatistics Compute(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return Empty;

        var basic = new DocumentStats(
            Words: WordCount.Words(text),
            CharactersWithSpaces: WordCount.Characters(text, includeSpaces: true),
            CharactersWithoutSpaces: WordCount.Characters(text, includeSpaces: false),
            Paragraphs: CountTextParagraphs(text));
        return Build(basic, text);
    }

    private static DocumentStatistics Build(DocumentStats basic, string text)
    {
        if (basic.Words == 0)
        {
            // No words: nothing meaningful to read or score. Keep the counts that still apply
            // (characters/paragraphs) but zero out the sentence-derived metrics.
            return Empty with
            {
                CharactersWithSpaces = basic.CharactersWithSpaces,
                CharactersWithoutSpaces = basic.CharactersWithoutSpaces,
                Paragraphs = basic.Paragraphs
            };
        }

        var sentences = CountSentences(text);
        var syllables = CountSyllables(text);
        var readingTime = (int)Math.Ceiling(basic.Words / (double)WordsPerMinute);
        var avgWordsPerSentence = basic.Words / (double)sentences;
        var flesch = 206.835
            - 1.015 * avgWordsPerSentence
            - 84.6 * (syllables / (double)basic.Words);

        return new DocumentStatistics(
            Words: basic.Words,
            CharactersWithSpaces: basic.CharactersWithSpaces,
            CharactersWithoutSpaces: basic.CharactersWithoutSpaces,
            Paragraphs: basic.Paragraphs,
            Sentences: sentences,
            Syllables: syllables,
            ReadingTimeMinutes: readingTime,
            AverageWordsPerSentence: avgWordsPerSentence,
            FleschReadingEase: flesch);
    }

    /// <summary>
    /// Counts sentences as maximal runs of the terminators <c>.</c> / <c>!</c> / <c>?</c>: each run is
    /// one sentence end, so <c>"Wait... really?!"</c> is two sentences. Guaranteed at least 1 for any
    /// non-empty text (text without a terminator is one sentence), so callers can divide by it safely.
    /// </summary>
    public static int CountSentences(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var sentences = 0;
        var inTerminatorRun = false;
        foreach (var ch in text)
        {
            if (ch is '.' or '!' or '?')
            {
                if (!inTerminatorRun)
                {
                    sentences++;
                    inTerminatorRun = true;
                }
            }
            else
            {
                inTerminatorRun = false;
            }
        }

        // Content with no terminator (or trailing text after the last terminator) still reads as at
        // least one sentence.
        return Math.Max(sentences, 1);
    }

    /// <summary>
    /// Estimates the total number of syllables in <paramref name="text"/> by summing the per-word
    /// syllable estimate (see <see cref="CountWordSyllables"/>) over each whitespace-delimited word.
    /// </summary>
    public static int CountSyllables(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var total = 0;
        foreach (var word in SplitWords(text))
            total += CountWordSyllables(word);
        return total;
    }

    /// <summary>
    /// Heuristic syllable count for a single word: the number of vowel groups (maximal runs of
    /// a/e/i/o/u/y), discounting a silent trailing "e", with a floor of one syllable for any word that
    /// contains at least one letter. Non-letter "words" (e.g. a standalone number) yield 0. This is the
    /// classic vowel-group approximation used by lightweight readability tools — deliberately simple and
    /// language-agnostic rather than dictionary-accurate.
    /// </summary>
    public static int CountWordSyllables(string? word)
    {
        if (string.IsNullOrEmpty(word))
            return 0;

        var hasLetter = false;
        var groups = 0;
        var inVowelGroup = false;
        char lastLetter = '\0';

        foreach (var raw in word)
        {
            var ch = char.ToLowerInvariant(raw);
            if (!char.IsLetter(ch))
            {
                inVowelGroup = false;
                continue;
            }

            hasLetter = true;
            if (IsVowel(ch))
            {
                if (!inVowelGroup)
                    groups++;
                inVowelGroup = true;
            }
            else
            {
                inVowelGroup = false;
            }
            lastLetter = ch;
        }

        if (!hasLetter)
            return 0;

        // Discount a silent trailing "e" (e.g. "make", "huge"), but never below one syllable.
        if (lastLetter == 'e' && groups > 1)
            groups--;

        return Math.Max(groups, 1);
    }

    private static bool IsVowel(char ch) => ch is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';

    // The non-empty, newline-delimited lines of plain text, counted as paragraphs (min 1 with content).
    private static int CountTextParagraphs(string text)
    {
        var count = 0;
        foreach (var line in text.Split('\n'))
        {
            if (line.Trim().Length > 0)
                count++;
        }
        return Math.Max(count, 1);
    }

    // Splits text into words: maximal runs of non-whitespace characters (matching WordCount.Words).
    private static IEnumerable<string> SplitWords(string text)
    {
        var start = -1;
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                if (start >= 0)
                {
                    yield return text[start..i];
                    start = -1;
                }
            }
            else if (start < 0)
            {
                start = i;
            }
        }
        if (start >= 0)
            yield return text[start..];
    }
}
