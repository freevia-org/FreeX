using System.Globalization;

namespace FreeW.Core.Model;

public enum ProofingDiagnosticKind
{
    Spelling,
    Grammar
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
    private readonly record struct ProofingToken(
        int Start,
        int Length,
        string Word,
        string Normalized,
        bool IsEmailOrUrlLike)
    {
        public int End => Start + Length;
    }

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
                document,
                customWords);
        }

        return diagnostics;
    }

    public static IReadOnlyList<ProofingDiagnostic> Build(
        TextDocument document,
        bool spellCheckEnabled,
        CustomDictionary customDictionary) =>
        Build(document, spellCheckEnabled, customDictionary.Words);

    /// <summary>
    /// Builds the diagnostics whose visual indicators are enabled by the document settings. The underlying
    /// diagnostics remain available through <see cref="Build(TextDocument, bool, IEnumerable{string}?)"/> so
    /// hiding squiggles does not discard proofing information or disable proofing commands.
    /// </summary>
    public static IReadOnlyList<ProofingDiagnostic> BuildVisibleIndicators(
        TextDocument document,
        bool spellCheckEnabled,
        IEnumerable<string>? customDictionaryWords = null) =>
        Build(document, spellCheckEnabled, customDictionaryWords)
            .Where(diagnostic => diagnostic.Kind switch
            {
                ProofingDiagnosticKind.Spelling => !document.HideSpellingErrors,
                ProofingDiagnosticKind.Grammar => !document.HideGrammaticalErrors,
                _ => true,
            })
            .ToArray();

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
        TextDocument document,
        HashSet<string> customWords)
    {
        var text = paragraph.PlainText;
        var noProofOffsets = BuildNoProofOffsets(document, paragraph, text.Length);
        var i = 0;
        ProofingToken? previousToken = null;
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

            var token = new ProofingToken(
                start,
                length,
                word,
                normalized,
                LooksLikeEmailOrUrlToken(text, start, length));

            if (TokenTouchesNoProof(noProofOffsets, token))
            {
                previousToken = null;
                continue;
            }

            if (!token.IsEmailOrUrlLike
                && !customWords.Contains(normalized)
                && ProofingCorrectionCatalog.Entries.Any(entry =>
                    string.Equals(entry.Misspelling, normalized, StringComparison.OrdinalIgnoreCase)))
            {
                AddDiagnostic(
                    diagnostics,
                    blockIndex,
                    paragraph,
                    document.DefaultRun.LanguageTag,
                    token,
                    ProofingDiagnosticKind.Spelling);
            }

            if (previousToken is { } previous
                && IsAdjacentRepeatedWord(previous, token, text))
            {
                AddDiagnostic(
                    diagnostics,
                    blockIndex,
                    paragraph,
                    document.DefaultRun.LanguageTag,
                    token,
                    ProofingDiagnosticKind.Grammar);
            }

            previousToken = token;
        }
    }

    private static bool[] BuildNoProofOffsets(TextDocument document, Paragraph paragraph, int textLength)
    {
        var offsets = new bool[textLength];
        var styleNoProof = ResolveStyleNoProof(document, paragraph.StyleId);
        var paragraphOffset = 0;
        foreach (var run in paragraph.Runs)
        {
            var noProof = run.Formatting.NoProof || styleNoProof || document.DefaultRun.NoProof;
            if (noProof)
            {
                var end = Math.Min(textLength, paragraphOffset + run.Text.Length);
                for (var offset = paragraphOffset; offset < end; offset++)
                    offsets[offset] = true;
            }

            paragraphOffset += run.Text.Length;
        }

        return offsets;
    }

    private static bool ResolveStyleNoProof(TextDocument document, string? styleId)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (!string.IsNullOrEmpty(styleId)
            && seen.Add(styleId)
            && document.Styles.TryGetValue(styleId, out var style))
        {
            if (style.Run.NoProof)
                return true;
            styleId = style.BasedOnStyleId;
        }

        return false;
    }

    private static bool TokenTouchesNoProof(bool[] noProofOffsets, ProofingToken token)
    {
        var end = Math.Min(noProofOffsets.Length, token.End);
        for (var offset = token.Start; offset < end; offset++)
        {
            if (noProofOffsets[offset])
                return true;
        }

        return false;
    }

    private static void AddDiagnostic(
        List<ProofingDiagnostic> diagnostics,
        int blockIndex,
        Paragraph paragraph,
        string? defaultLanguageTag,
        ProofingToken token,
        ProofingDiagnosticKind kind)
    {
        var (runIndex, runOffset, languageTag) = LocateRun(paragraph, token.Start, defaultLanguageTag);

        diagnostics.Add(new ProofingDiagnostic(
            blockIndex,
            runIndex,
            runOffset,
            token.Start,
            token.Length,
            token.Word,
            token.Normalized,
            languageTag,
            kind));
    }

    private static bool IsAdjacentRepeatedWord(ProofingToken previous, ProofingToken current, string text)
    {
        if (previous.IsEmailOrUrlLike || current.IsEmailOrUrlLike)
            return false;
        if (!string.Equals(previous.Normalized, current.Normalized, StringComparison.Ordinal))
            return false;
        if (previous.End >= current.Start)
            return false;

        for (var i = previous.End; i < current.Start; i++)
        {
            if (!char.IsWhiteSpace(text[i]))
                return false;
        }

        return true;
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
