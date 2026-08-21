using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Owns the model mutation and stabilization policy for generated tables of contents. Renderers provide
/// only a page-aware builder and, when necessary, a hook that refreshes native layout before measuring.
/// </summary>
public static class TableOfContentsMutationCoordinator
{
    public const int MaxStabilizationPasses = 8;

    public static int NormalizeInsertionIndex(TextDocument document, int requestedIndex)
    {
        ArgumentNullException.ThrowIfNull(document);
        return requestedIndex >= 0 && requestedIndex <= document.Blocks.Count ? requestedIndex : 0;
    }

    public static int FindRefreshInsertionIndex(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        for (var index = 0; index < document.Blocks.Count; index++)
        {
            if (TableOfContents.IsTocParagraph(document.Blocks[index]))
                return index;
        }

        return 0;
    }

    public static void Apply(
        TextDocument document,
        DocumentCommandBus commandBus,
        int insertionIndex,
        string label,
        bool replaceExisting,
        Func<IReadOnlyList<Paragraph>> buildGeneratedRegion,
        Action? prepareLayoutForMeasurement = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(commandBus);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(buildGeneratedRegion);

        TableOfContents.EnsureStyles(document);
        var at = NormalizeInsertionIndex(document, insertionIndex);
        var generated = buildGeneratedRegion();

        commandBus.BeginUndoGroup();
        try
        {
            if (replaceExisting)
                DeleteAllGeneratedRegions(document, commandBus);
            InsertRegion(document, commandBus, at, generated);

            var regionCount = generated.Count;
            var isStable = false;
            for (var pass = 0; pass < MaxStabilizationPasses; pass++)
            {
                prepareLayoutForMeasurement?.Invoke();
                var stabilized = buildGeneratedRegion();
                if (TableOfContents.MatchesGeneratedRegionAt(document, at, stabilized))
                {
                    isStable = true;
                    break;
                }

                ReplaceRegion(commandBus, at, regionCount, stabilized);
                regionCount = stabilized.Count;
            }

            if (!isStable)
            {
                prepareLayoutForMeasurement?.Invoke();
                var finalCheck = buildGeneratedRegion();
                if (!TableOfContents.MatchesGeneratedRegionAt(document, at, finalCheck))
                    throw new InvalidOperationException("Table of Contents pagination did not stabilize.");
            }
        }
        catch
        {
            commandBus.RollbackUndoGroup();
            throw;
        }

        commandBus.CommitUndoGroup(label);
    }

    private static void DeleteAllGeneratedRegions(TextDocument document, DocumentCommandBus commandBus)
    {
        var indices = FirstContiguousRun(Enumerable.Range(0, document.Blocks.Count)
            .Where(index => TableOfContents.IsTocParagraph(document.Blocks[index]))
            .ToArray());
        for (var index = indices.Length - 1; index >= 0; index--)
            commandBus.Execute(new DeleteParagraphCommand(indices[index]));
    }

    /// <summary>
    /// Narrows a sorted set of block indices down to only its first maximal run of consecutive
    /// indices. A document can legitimately hold more than one independent Table of Contents field
    /// (e.g. a main TOC plus a second TOC for an appendix); <see cref="TableOfContents.IsTocParagraph"/>
    /// matches every one of them indiscriminately, so without this narrowing a refresh would delete
    /// every TOC-marked paragraph in the document and reinsert only a single merged region. Scoping to
    /// the first contiguous run leaves any other, separately-located TOC region untouched. Mirrors
    /// DocumentReferenceEditingCoordinator.FirstContiguousRun, the fix applied to the shipping coordinator.
    /// </summary>
    private static int[] FirstContiguousRun(int[] sortedIndices)
    {
        if (sortedIndices.Length == 0)
            return sortedIndices;

        var end = 1;
        while (end < sortedIndices.Length && sortedIndices[end] == sortedIndices[end - 1] + 1)
            end++;
        return end == sortedIndices.Length ? sortedIndices : sortedIndices[..end];
    }

    private static void InsertRegion(
        TextDocument document,
        DocumentCommandBus commandBus,
        int insertionIndex,
        IReadOnlyList<Paragraph> generated) =>
        commandBus.Execute(new ReplaceBlocksCommand(
            Math.Clamp(insertionIndex, 0, document.Blocks.Count),
            0,
            generated));

    private static void ReplaceRegion(
        DocumentCommandBus commandBus,
        int insertionIndex,
        int currentCount,
        IReadOnlyList<Paragraph> generated) =>
        commandBus.Execute(new ReplaceBlocksCommand(insertionIndex, currentCount, generated));
}
