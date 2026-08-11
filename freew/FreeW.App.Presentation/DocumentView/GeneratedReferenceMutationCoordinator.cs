using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

public sealed record GeneratedReferenceMutationResult(int InsertIndex, int InsertedCount);

/// <summary>
/// Owns atomic insertion and refresh of generated back-matter regions such as an Index or Table of
/// Figures. Renderers provide model coordinates and builders; region placement, replacement, rollback,
/// and undo grouping remain toolkit-neutral.
/// </summary>
public static class GeneratedReferenceMutationCoordinator
{
    public static int NormalizeBackMatterInsertionIndex(TextDocument document, int requestedIndex)
    {
        ArgumentNullException.ThrowIfNull(document);
        return requestedIndex >= 0 && requestedIndex <= document.Blocks.Count
            ? requestedIndex
            : document.Blocks.Count;
    }

    public static GeneratedReferenceMutationResult Insert(
        TextDocument document,
        DocumentCommandBus commandBus,
        int requestedIndex,
        string label,
        IReadOnlyList<Paragraph> generated)
    {
        ArgumentNullException.ThrowIfNull(generated);
        var insertIndex = NormalizeBackMatterInsertionIndex(document, requestedIndex);
        return ApplyAtomic(
            document,
            commandBus,
            insertIndex,
            label,
            deleteIndicesDescending: [],
            () => generated);
    }

    public static GeneratedReferenceMutationResult Refresh(
        TextDocument document,
        DocumentCommandBus commandBus,
        Func<Block, bool> isGeneratedBlock,
        Func<IReadOnlyList<Paragraph>> buildGenerated,
        string label)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(isGeneratedBlock);
        ArgumentNullException.ThrowIfNull(buildGenerated);

        var indices = Enumerable.Range(0, document.Blocks.Count)
            .Where(index => isGeneratedBlock(document.Blocks[index]))
            .ToArray();
        var insertIndex = indices.Length > 0 ? indices[0] : document.Blocks.Count;
        Array.Reverse(indices);
        return ApplyAtomic(document, commandBus, insertIndex, label, indices, buildGenerated);
    }

    private static GeneratedReferenceMutationResult ApplyAtomic(
        TextDocument document,
        DocumentCommandBus commandBus,
        int insertIndex,
        string label,
        IReadOnlyList<int> deleteIndicesDescending,
        Func<IReadOnlyList<Paragraph>> buildGenerated)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(commandBus);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(deleteIndicesDescending);
        ArgumentNullException.ThrowIfNull(buildGenerated);

        commandBus.BeginUndoGroup();
        IReadOnlyList<Paragraph> generated;
        try
        {
            foreach (var deleteIndex in deleteIndicesDescending)
                commandBus.Execute(new DeleteParagraphCommand(deleteIndex));

            generated = buildGenerated();
            ArgumentNullException.ThrowIfNull(generated);
            if (generated.Count > 0)
            {
                commandBus.Execute(new ReplaceBlocksCommand(
                    Math.Clamp(insertIndex, 0, document.Blocks.Count),
                    0,
                    generated));
            }
        }
        catch
        {
            commandBus.RollbackUndoGroup();
            throw;
        }

        commandBus.CommitUndoGroup(label);
        return new GeneratedReferenceMutationResult(insertIndex, generated.Count);
    }
}
