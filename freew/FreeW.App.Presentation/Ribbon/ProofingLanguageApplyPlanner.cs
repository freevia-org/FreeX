namespace FreeW.App.Presentation.Ribbon;

public readonly record struct ProofingLanguageTextRange(int BlockIndex, int StartOffset, int EndOffset);

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

    private static void AddRange(List<ProofingLanguageTextRange> ranges, int blockIndex, int startOffset, int endOffset)
    {
        if (blockIndex < 0 || endOffset <= startOffset)
            return;

        ranges.Add(new ProofingLanguageTextRange(blockIndex, startOffset, endOffset));
    }
}
