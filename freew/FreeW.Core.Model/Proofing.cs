using System.Globalization;

namespace FreeW.Core.Model;

public enum ProofingDiagnosticKind
{
    Spelling
}

public sealed record ProofingDiagnostic(
    int BlockIndex,
    int RunIndex,
    int RunOffset,
    int ParagraphOffset,
    int Length,
    string Word,
    string NormalizedWord,
    string? LanguageTag,
    ProofingDiagnosticKind Kind = ProofingDiagnosticKind.Spelling);

public static class ProofingDiagnosticPlanner
{
    private static readonly HashSet<string> KnownMisspellings = new(StringComparer.OrdinalIgnoreCase)
    {
        "acommodate",
        "adress",
        "arguement",
        "beleive",
        "definately",
        "enviroment",
        "occured",
        "recieve",
        "seperate",
        "teh",
        "wierd",
    };

    public static IReadOnlyList<ProofingDiagnostic> Build(
        TextDocument document,
        bool spellCheckEnabled,
        IEnumerable<string>? customDictionaryWords = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!spellCheckEnabled)
            return [];

        var customWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (customDictionaryWords is not null)
        {
            foreach (var word in customDictionaryWords)
                if (NormalizeWord(word) is { } normalized)
                    customWords.Add(normalized);
        }

        var diagnostics = new List<ProofingDiagnostic>();
        for (var blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            if (document.Blocks[blockIndex] is not Paragraph paragraph)
                continue;

            AddParagraphDiagnostics(
                diagnostics,
                blockIndex,
                paragraph,
                document.DefaultRun.LanguageTag,
                customWords);
        }

        return diagnostics;
    }

    public static IReadOnlyList<ProofingDiagnostic> Build(
        TextDocument document,
        bool spellCheckEnabled,
        CustomDictionary customDictionary) =>
        Build(document, spellCheckEnabled, customDictionary.Words);

    public static string? NormalizeWord(string? word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return null;

        var trimmed = word.Trim();
        var start = 0;
        while (start < trimmed.Length && !IsWordChar(trimmed[start]))
            start++;
        var end = trimmed.Length - 1;
        while (end >= start && !IsWordChar(trimmed[end]))
            end--;
        if (end < start)
            return null;

        var normalized = trimmed[start..(end + 1)];
        if (!normalized.Any(char.IsLetter))
            return null;
        if (normalized.Any(char.IsWhiteSpace))
            return null;

        return normalized.ToLower(CultureInfo.InvariantCulture);
    }

    private static void AddParagraphDiagnostics(
        List<ProofingDiagnostic> diagnostics,
        int blockIndex,
        Paragraph paragraph,
        string? defaultLanguageTag,
        HashSet<string> customWords)
    {
        var text = paragraph.PlainText;
        var i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && !IsWordChar(text[i]))
                i++;
            if (i >= text.Length)
                break;

            var start = i;
            while (i < text.Length && IsWordChar(text[i]))
                i++;
            var length = i - start;
            var word = text.Substring(start, length);

            if (NormalizeWord(word) is not { } normalized)
                continue;
            if (customWords.Contains(normalized))
                continue;
            if (!KnownMisspellings.Contains(normalized))
                continue;
            if (LooksLikeEmailOrUrlToken(text, start, length))
                continue;
            var (runIndex, runOffset, languageTag) = LocateRun(paragraph, start, defaultLanguageTag);

            diagnostics.Add(new ProofingDiagnostic(
                blockIndex,
                runIndex,
                runOffset,
                start,
                length,
                word,
                normalized,
                languageTag));
        }
    }

    private static (int RunIndex, int RunOffset, string? LanguageTag) LocateRun(
        Paragraph paragraph,
        int paragraphOffset,
        string? defaultLanguageTag)
    {
        var offset = 0;
        for (var i = 0; i < paragraph.Runs.Count; i++)
        {
            var run = paragraph.Runs[i];
            var next = offset + run.Text.Length;
            if (paragraphOffset < next || (paragraphOffset == next && run.Text.Length == 0))
            {
                return (
                    i,
                    Math.Clamp(paragraphOffset - offset, 0, run.Text.Length),
                    NormalizeLanguageTag(run.Formatting.LanguageTag) ?? NormalizeLanguageTag(defaultLanguageTag));
            }

            offset = next;
        }

        return (Math.Max(0, paragraph.Runs.Count - 1), 0, NormalizeLanguageTag(defaultLanguageTag));
    }

    private static string? NormalizeLanguageTag(string? tag) =>
        string.IsNullOrWhiteSpace(tag) ? null : tag.Trim();

    private static bool IsWordChar(char ch) =>
        char.IsLetter(ch) || ch is '\'' or '-' or '\u2019';

    private static bool LooksLikeEmailOrUrlToken(string text, int start, int length)
    {
        var end = start + length;
        if (start > 0 && text[start - 1] is '@' or '.' or '/')
            return true;
        if (end < text.Length && text[end] is '@' or '.')
            return true;

        var tokenStart = start;
        while (tokenStart > 0 && !char.IsWhiteSpace(text[tokenStart - 1]))
            tokenStart--;
        var tokenEnd = end;
        while (tokenEnd < text.Length && !char.IsWhiteSpace(text[tokenEnd]))
            tokenEnd++;

        var surroundingToken = text[tokenStart..tokenEnd];
        return surroundingToken.Contains('@', StringComparison.Ordinal)
            || surroundingToken.Contains("://", StringComparison.Ordinal)
            || surroundingToken.StartsWith("www.", StringComparison.OrdinalIgnoreCase);
    }
}
