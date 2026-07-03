using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public readonly record struct ProofingLanguageTextRange(int BlockIndex, int StartOffset, int EndOffset);

public sealed record ProofingLanguageCaretContext(int BlockIndex, int Offset, string ParagraphText);

public sealed record ProofingLanguageApplyPlan(string? LanguageTag, IReadOnlyList<ProofingLanguageTextRange> Ranges)
{
    public bool HasSelectedText => Ranges.Count > 0;
}

public static class ProofingLanguageApplyPlanner
{
    public static ProofingLanguageApplyPlan Build(
        string? languageTag,
        IReadOnlyList<int> selectedBlockIndices,
        int startOffset,
        int endOffset)
    {
        var ranges = new List<ProofingLanguageTextRange>();
        var normalizedTag = ProofingLanguageCatalog.NormalizeTag(languageTag);

        if (selectedBlockIndices.Count == 0)
            return new ProofingLanguageApplyPlan(normalizedTag, ranges);

        if (selectedBlockIndices.Count == 1)
        {
            AddRange(ranges, selectedBlockIndices[0], Math.Max(0, startOffset), Math.Max(0, endOffset));
            return new ProofingLanguageApplyPlan(normalizedTag, ranges);
        }

        for (var i = 0; i < selectedBlockIndices.Count; i++)
        {
            var blockIndex = selectedBlockIndices[i];
            var start = i == 0 ? Math.Max(0, startOffset) : 0;
            var end = i == selectedBlockIndices.Count - 1 ? Math.Max(0, endOffset) : int.MaxValue;
            AddRange(ranges, blockIndex, start, end);
        }

        return new ProofingLanguageApplyPlan(normalizedTag, ranges);
    }

    public static ProofingLanguageApplyPlan BuildForSelectionOrCaretWord(
        string? languageTag,
        IReadOnlyList<int> selectedBlockIndices,
        int startOffset,
        int endOffset,
        ProofingLanguageCaretContext? collapsedCaret)
    {
        var selectedPlan = Build(languageTag, selectedBlockIndices, startOffset, endOffset);
        if (selectedPlan.HasSelectedText
            || collapsedCaret is null
            || selectedBlockIndices.Count != 1
            || startOffset != endOffset)
        {
            return selectedPlan;
        }

        return BuildForCaretWord(
            languageTag,
            collapsedCaret.BlockIndex,
            collapsedCaret.Offset,
            collapsedCaret.ParagraphText);
    }

    public static ProofingLanguageApplyPlan BuildForCaretWord(
        string? languageTag,
        int blockIndex,
        int caretOffset,
        string? paragraphText)
    {
        var ranges = new List<ProofingLanguageTextRange>();
        var normalizedTag = ProofingLanguageCatalog.NormalizeTag(languageTag);

        if (blockIndex < 0 || string.IsNullOrEmpty(paragraphText))
            return new ProofingLanguageApplyPlan(normalizedTag, ranges);

        if (CurrentProofingWordRange(paragraphText, caretOffset) is { } wordRange)
            AddRange(ranges, blockIndex, wordRange.Start, wordRange.End);

        return new ProofingLanguageApplyPlan(normalizedTag, ranges);
    }

    private static void AddRange(List<ProofingLanguageTextRange> ranges, int blockIndex, int startOffset, int endOffset)
    {
        if (blockIndex < 0 || endOffset <= startOffset)
            return;

        ranges.Add(new ProofingLanguageTextRange(blockIndex, startOffset, endOffset));
    }

    private static (int Start, int End)? CurrentProofingWordRange(string text, int caretOffset)
    {
        if (text.Length == 0)
            return null;

        var index = Math.Clamp(caretOffset, 0, text.Length - 1);
        if (!IsProofingWordChar(text[index]) && index > 0 && IsProofingWordChar(text[index - 1]))
            index--;
        if (!IsProofingWordChar(text[index]))
            return null;

        var start = index;
        while (start > 0 && IsProofingWordChar(text[start - 1]))
            start--;

        var end = index + 1;
        while (end < text.Length && IsProofingWordChar(text[end]))
            end++;

        var word = text[start..end];
        return ProofingDiagnosticPlanner.NormalizeWord(word) is null ? null : (start, end);
    }

    private static bool IsProofingWordChar(char ch) =>
        char.IsLetter(ch) || ch is '\'' or '-' or '\u2019';
}
