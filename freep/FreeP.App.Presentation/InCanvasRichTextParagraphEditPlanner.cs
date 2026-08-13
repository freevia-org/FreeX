using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Maps edited paragraphs back to their source metadata lineage.
/// A WPF Enter operation can create more FlowDocument paragraphs than existed in
/// the source model; PowerPoint carries the split paragraph's list metadata forward.
/// A join keeps the leading paragraph's metadata.
/// An authored AutoNumStartAt is retained only on the first lineage paragraph;
/// split continuation paragraphs clear explicit restart intent through
/// <see cref="TextBodyModelCloner.CloneParagraphMetadata"/>.
/// </summary>
public static class InCanvasRichTextParagraphEditPlanner
{
    public static IReadOnlyList<int> ResolveSourceParagraphIndices(
        IReadOnlyList<Paragraph> sourceParagraphs,
        IReadOnlyList<string> editedParagraphTexts)
    {
        ArgumentNullException.ThrowIfNull(sourceParagraphs);
        ArgumentNullException.ThrowIfNull(editedParagraphTexts);

        if (sourceParagraphs.Count == 0)
            return Enumerable.Repeat(-1, editedParagraphTexts.Count).ToArray();

        var sourceTexts = sourceParagraphs.Select(ParagraphText).ToArray();
        var anchors = AlignExactParagraphTexts(sourceTexts, editedParagraphTexts);
        var result = Enumerable.Repeat(-1, editedParagraphTexts.Count).ToArray();

        foreach (var (sourceIndex, editedIndex) in anchors)
            result[editedIndex] = sourceIndex;

        int previousSource = -1;
        int previousEdited = -1;
        foreach (var (sourceIndex, editedIndex) in anchors.Append(
                     (sourceParagraphs.Count, editedParagraphTexts.Count)))
        {
            int sourceGapStart = previousSource + 1;
            int sourceGapEnd = sourceIndex - 1;
            int editedGapStart = previousEdited + 1;
            int editedGapEnd = editedIndex - 1;
            if (editedGapStart <= editedGapEnd)
            {
                int editedGapCount = editedGapEnd - editedGapStart + 1;
                int sourceGapCount = sourceGapStart <= sourceGapEnd
                    ? sourceGapEnd - sourceGapStart + 1
                    : 0;
                for (int gapOffset = 0; gapOffset < editedGapCount; gapOffset++)
                {
                    int sourceOffset = sourceGapCount > 0
                        ? ResolveGapSourceOffset(sourceGapCount, editedGapCount, gapOffset)
                        : 0;
                    result[editedGapStart + gapOffset] = sourceGapCount > 0
                        ? sourceGapStart + sourceOffset
                        : Math.Clamp(previousSource, 0, sourceParagraphs.Count - 1);
                }
            }

            previousSource = sourceIndex;
            previousEdited = editedIndex;
        }

        return result;
    }

    private static IReadOnlyList<(int SourceIndex, int EditedIndex)> AlignExactParagraphTexts(
        IReadOnlyList<string> sourceTexts,
        IReadOnlyList<string> editedTexts)
    {
        var lengths = new int[sourceTexts.Count + 1, editedTexts.Count + 1];
        for (int sourceIndex = sourceTexts.Count - 1; sourceIndex >= 0; sourceIndex--)
        {
            for (int editedIndex = editedTexts.Count - 1; editedIndex >= 0; editedIndex--)
            {
                lengths[sourceIndex, editedIndex] =
                    StringComparer.Ordinal.Equals(sourceTexts[sourceIndex], editedTexts[editedIndex])
                        ? lengths[sourceIndex + 1, editedIndex + 1] + 1
                        : Math.Max(
                            lengths[sourceIndex + 1, editedIndex],
                            lengths[sourceIndex, editedIndex + 1]);
            }
        }

        var anchors = new List<(int SourceIndex, int EditedIndex)>();
        int sourceCursor = 0;
        int editedCursor = 0;
        while (sourceCursor < sourceTexts.Count && editedCursor < editedTexts.Count)
        {
            if (StringComparer.Ordinal.Equals(sourceTexts[sourceCursor], editedTexts[editedCursor])
                && lengths[sourceCursor, editedCursor]
                    == lengths[sourceCursor + 1, editedCursor + 1] + 1)
            {
                anchors.Add((sourceCursor, editedCursor));
                sourceCursor++;
                editedCursor++;
                continue;
            }

            if (lengths[sourceCursor + 1, editedCursor] >= lengths[sourceCursor, editedCursor + 1])
                sourceCursor++;
            else
                editedCursor++;
        }

        return anchors;
    }

    private static int ResolveGapSourceOffset(
        int sourceGapCount,
        int editedGapCount,
        int editedGapOffset)
    {
        if (sourceGapCount <= 1)
            return 0;

        if (editedGapCount <= sourceGapCount)
            return Math.Min(editedGapOffset, sourceGapCount - 1);

        // Extra edited paragraphs are treated as a split of the leading source
        // paragraph; remaining source paragraphs retain their ordered lineage.
        int leadingSurplus = editedGapCount - sourceGapCount;
        return Math.Clamp(editedGapOffset - leadingSurplus, 0, sourceGapCount - 1);
    }

    private static string ParagraphText(Paragraph paragraph) =>
        string.Concat(paragraph.Runs.Select(run => run.Text ?? string.Empty));

}
