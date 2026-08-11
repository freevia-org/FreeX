using FreeW.Core.Model;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.DocumentView;

public sealed record GeneratedReferenceMutationResult(int InsertIndex, int InsertedCount);

/// <summary>
/// Owns atomic insertion and refresh of generated back-matter regions such as an Index or Table of
/// Figures. Renderers provide model coordinates and builders; region placement, replacement, rollback,
/// and undo grouping remain toolkit-neutral.
/// </summary>
public static class GeneratedReferenceMutationCoordinator
{
    public const int MaxStabilizationPasses = 8;

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

    public static GeneratedReferenceMutationResult ApplyPlan(
        TextDocument document,
        DocumentCommandBus commandBus,
        IGeneratedReferenceRegionPlan plan,
        string label)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return ApplyAtomic(
            document,
            commandBus,
            plan.InsertIndex,
            label,
            plan.DeleteIndicesDescending,
            () => plan.Paragraphs);
    }

    public static GeneratedReferenceMutationResult ApplyStabilizingPlan(
        TextDocument document,
        DocumentCommandBus commandBus,
        IGeneratedReferenceRegionPlan initialPlan,
        string label,
        Func<IGeneratedReferenceRegionPlan> buildRefreshPlan,
        Func<IReadOnlyList<Paragraph>, bool> matchesGeneratedRegion,
        Action? prepareLayoutForMeasurement = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(commandBus);
        ArgumentNullException.ThrowIfNull(initialPlan);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(buildRefreshPlan);
        ArgumentNullException.ThrowIfNull(matchesGeneratedRegion);

        commandBus.BeginUndoGroup();
        var currentPlan = initialPlan;
        int appliedIndex;
        try
        {
            appliedIndex = ApplyPlanCommands(document, commandBus, currentPlan);
            var isStable = false;
            for (var pass = 0; pass < MaxStabilizationPasses; pass++)
            {
                prepareLayoutForMeasurement?.Invoke();
                var stabilized = buildRefreshPlan();
                if (matchesGeneratedRegion(stabilized.Paragraphs))
                {
                    isStable = true;
                    break;
                }

                currentPlan = stabilized;
                appliedIndex = ApplyPlanCommands(document, commandBus, currentPlan);
            }

            if (!isStable)
            {
                prepareLayoutForMeasurement?.Invoke();
                var finalCheck = buildRefreshPlan();
                if (!matchesGeneratedRegion(finalCheck.Paragraphs))
                    throw new InvalidOperationException("Generated reference pagination did not stabilize.");
            }
        }
        catch
        {
            commandBus.RollbackUndoGroup();
            throw;
        }

        commandBus.CommitUndoGroup(label);
        return new GeneratedReferenceMutationResult(appliedIndex, currentPlan.Paragraphs.Count);
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
        int appliedIndex;
        try
        {
            foreach (var deleteIndex in deleteIndicesDescending)
                commandBus.Execute(new DeleteParagraphCommand(deleteIndex));

            generated = buildGenerated();
            ArgumentNullException.ThrowIfNull(generated);
            appliedIndex = Math.Clamp(insertIndex, 0, document.Blocks.Count);
            if (generated.Count > 0)
                commandBus.Execute(new ReplaceBlocksCommand(appliedIndex, 0, generated));
        }
        catch
        {
            commandBus.RollbackUndoGroup();
            throw;
        }

        commandBus.CommitUndoGroup(label);
        return new GeneratedReferenceMutationResult(appliedIndex, generated.Count);
    }

    private static int ApplyPlanCommands(
        TextDocument document,
        DocumentCommandBus commandBus,
        IGeneratedReferenceRegionPlan plan)
    {
        foreach (var deleteIndex in plan.DeleteIndicesDescending)
            commandBus.Execute(new DeleteParagraphCommand(deleteIndex));

        var appliedIndex = Math.Clamp(plan.InsertIndex, 0, document.Blocks.Count);
        if (plan.Paragraphs.Count > 0)
            commandBus.Execute(new ReplaceBlocksCommand(appliedIndex, 0, plan.Paragraphs));
        return appliedIndex;
    }
}
