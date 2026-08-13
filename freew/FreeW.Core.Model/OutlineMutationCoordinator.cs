namespace FreeW.Core.Model;

/// <summary>
/// Shared command-bus entry point for outline mutations used by both document renderers.
/// The coordinator owns validation, style mapping, no-op suppression, subtree reordering,
/// and moved-index recovery so WPF and Avalonia cannot drift in their editing semantics.
/// </summary>
public static class OutlineMutationCoordinator
{
    public static bool Promote(
        DocumentCommandBus commandBus,
        TextDocument document,
        int blockIndex) =>
        SetStyle(commandBus, document, blockIndex, OutlineTools.Promote);

    public static bool Demote(
        DocumentCommandBus commandBus,
        TextDocument document,
        int blockIndex) =>
        SetStyle(commandBus, document, blockIndex, OutlineTools.Demote);

    public static bool PromoteToHeading1(
        DocumentCommandBus commandBus,
        TextDocument document,
        int blockIndex) =>
        SetStyle(commandBus, document, blockIndex, _ => "Heading1");

    public static bool SetHeadingLevel(
        DocumentCommandBus commandBus,
        TextDocument document,
        int blockIndex,
        int level)
    {
        var styleId = level < 0
            ? "Normal"
            : level == 0
                ? "Title"
                : $"Heading{Math.Min(level, OutlineTools.MaxHeadingLevel)}";
        return SetStyle(commandBus, document, blockIndex, _ => styleId);
    }

    public static OutlineMoveResult MoveHeading(
        DocumentCommandBus commandBus,
        TextDocument document,
        int blockIndex,
        bool moveUp,
        Action? beforeExecute = null)
    {
        ArgumentNullException.ThrowIfNull(commandBus);
        ArgumentNullException.ThrowIfNull(document);

        if (blockIndex < 0 || blockIndex >= document.Blocks.Count)
            return OutlineMoveResult.NoChange(blockIndex);

        var reordered = OutlineTools.MoveSubtree(document.Blocks, blockIndex, moveUp);
        if (ReferenceEquals(reordered, document.Blocks))
            return OutlineMoveResult.NoChange(blockIndex);

        var heading = document.Blocks[blockIndex];
        for (var currentIndex = 0; currentIndex < reordered.Count; currentIndex++)
        {
            if (ReferenceEquals(reordered[currentIndex], heading))
            {
                beforeExecute?.Invoke();
                commandBus.Execute(new ReorderBlocksCommand(reordered));
                return new OutlineMoveResult(blockIndex, currentIndex, WasMoved: true);
            }
        }

        // OutlineTools preserves block instances, so this is defensive only and must not create history.
        return OutlineMoveResult.NoChange(blockIndex);
    }

    private static bool SetStyle(
        DocumentCommandBus commandBus,
        TextDocument document,
        int blockIndex,
        Func<string?, string?> transform)
    {
        ArgumentNullException.ThrowIfNull(commandBus);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(transform);

        if (blockIndex < 0 || blockIndex >= document.Blocks.Count
            || document.Blocks[blockIndex] is not Paragraph paragraph)
        {
            return false;
        }

        var nextStyleId = transform(paragraph.StyleId);
        if (string.Equals(nextStyleId, paragraph.StyleId, StringComparison.Ordinal))
            return false;

        commandBus.Execute(new SetParagraphStyleCommand(blockIndex, nextStyleId));
        return true;
    }
}

public readonly record struct OutlineMoveResult(
    int OriginalBlockIndex,
    int CurrentBlockIndex,
    bool WasMoved)
{
    public static OutlineMoveResult NoChange(int blockIndex) =>
        new(blockIndex, blockIndex, WasMoved: false);
}
