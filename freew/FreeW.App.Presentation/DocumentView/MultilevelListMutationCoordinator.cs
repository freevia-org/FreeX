using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Applies a complete multilevel-list definition to selected body paragraphs as one reversible edit.
/// Selection validation, level clamping, heading-style linking, number formats, and rollback are shared
/// so renderers only provide their selected model indices.
/// </summary>
public static class MultilevelListMutationCoordinator
{
    public const string UndoLabel = "Define Multilevel List";

    public static int ApplyDefinition(
        TextDocument document,
        DocumentCommandBus commandBus,
        IEnumerable<int> selectedBlockIndices,
        MultilevelListDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(commandBus);
        ArgumentNullException.ThrowIfNull(selectedBlockIndices);
        ArgumentNullException.ThrowIfNull(definition);

        var indices = selectedBlockIndices
            .Where(index => index >= 0
                && index < document.Blocks.Count
                && document.Blocks[index] is Paragraph)
            .Distinct()
            .ToArray();
        if (indices.Length == 0)
            return 0;

        commandBus.BeginUndoGroup();
        try
        {
            foreach (var index in indices)
            {
                var paragraph = (Paragraph)document.Blocks[index];
                var updated = MultilevelListDialogPlanner.ApplyDefinition(paragraph.Formatting, definition);
                commandBus.Execute(new SetParagraphFormattingCommand(index, updated));

                var linkedStyleId = MultilevelListDialogPlanner.ResolveLinkedHeadingStyleId(
                    updated.ListLevel,
                    definition);
                if (linkedStyleId is not null && document.Styles.ContainsKey(linkedStyleId))
                    commandBus.Execute(new SetParagraphStyleCommand(index, linkedStyleId));
            }

            commandBus.Execute(new SetMultiLevelNumberFormatsCommand(definition.NumberFormats));
            commandBus.CommitUndoGroup(UndoLabel);
        }
        catch
        {
            commandBus.RollbackUndoGroup();
            throw;
        }

        return indices.Length;
    }
}
