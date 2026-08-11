using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Owns style-catalog validation, mutation, and undo policy for every renderer.
/// Hosts remain responsible only for committing native editor state and enforcing edit locks.
/// </summary>
public static class StyleCatalogMutationCoordinator
{
    public const string CreateLabel = "New Style";
    public const string ModifyLabel = "Modify Style";
    public const string DeleteLabel = "Delete Style";

    public static DocumentStyle CreateAndApply(
        TextDocument document,
        DocumentCommandBus commandBus,
        IEnumerable<int> targetParagraphIndices,
        string name,
        string? basedOnId,
        RunFormatting run,
        ParagraphFormatting paragraph,
        string? nextStyleId = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(commandBus);
        ArgumentNullException.ThrowIfNull(targetParagraphIndices);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(paragraph);

        var targets = targetParagraphIndices.ToArray();
        DocumentStyle? created = null;
        commandBus.BeginUndoGroup();
        try
        {
            commandBus.Execute(new StyleCatalogCommand(CreateLabel, doc =>
                created = StyleManager.CreateStyle(doc, name, basedOnId, run, paragraph, nextStyleId)));

            foreach (var index in targets)
            {
                if (index >= 0 && index < document.Blocks.Count && document.Blocks[index] is Paragraph)
                    commandBus.Execute(new SetParagraphStyleCommand(index, created!.Id));
            }

            commandBus.CommitUndoGroup(CreateLabel);
            return created!;
        }
        catch
        {
            commandBus.RollbackUndoGroup();
            throw;
        }
    }

    public static DocumentStyle? Modify(
        TextDocument document,
        DocumentCommandBus commandBus,
        string styleId,
        RunFormatting run,
        ParagraphFormatting paragraph,
        string? basedOnId,
        string? nextStyleId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(commandBus);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(paragraph);
        if (string.IsNullOrWhiteSpace(styleId) || !document.Styles.ContainsKey(styleId))
            return null;

        DocumentStyle? updated = null;
        commandBus.Execute(new StyleCatalogCommand(ModifyLabel, doc =>
        {
            updated = StyleManager.ModifyStyle(
                doc,
                styleId,
                run: run,
                para: paragraph,
                basedOnId: basedOnId,
                clearBasedOn: basedOnId is null,
                nextStyleId: nextStyleId,
                clearNext: nextStyleId is null);
        }));
        return updated;
    }

    public static bool Delete(
        TextDocument document,
        DocumentCommandBus commandBus,
        string styleId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(commandBus);
        if (string.IsNullOrWhiteSpace(styleId)
            || StyleManager.IsBuiltIn(styleId)
            || !document.Styles.ContainsKey(styleId))
        {
            return false;
        }

        var deleted = false;
        commandBus.Execute(new StyleCatalogCommand(DeleteLabel, doc =>
            deleted = StyleManager.DeleteStyle(doc, styleId)));
        return deleted;
    }
}
