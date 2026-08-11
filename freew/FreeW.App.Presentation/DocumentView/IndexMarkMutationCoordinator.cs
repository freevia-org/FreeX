using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Owns index-mark normalization, duplicate detection, ruby-safe insertion, table addressing,
/// and undo policy. Renderers provide only the current body position and update native caret state.
/// </summary>
public static class IndexMarkMutationCoordinator
{
    public const string MarkAllLabel = "Mark All Index Entries";

    public static bool TryMark(
        TextDocument document,
        DocumentCommandBus commandBus,
        int blockIndex,
        int textOffset,
        IndexMark mark)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(commandBus);
        ArgumentNullException.ThrowIfNull(mark);

        var markRun = DocumentIndex.MarkRun(mark);
        if (DocumentIndex.MarkedEntry(markRun) is not { MainEntry.Length: > 0 } normalized
            || blockIndex < 0
            || blockIndex >= document.Blocks.Count
            || document.Blocks[blockIndex] is not Paragraph paragraph
            || paragraph.Runs.Any(run =>
                DocumentIndex.MarksEquivalent(DocumentIndex.MarkedEntry(run), normalized)))
        {
            return false;
        }

        commandBus.Execute(new ReplaceParagraphRunsCommand(blockIndex, target =>
            RevisionEditPlanner.InsertRunAtOffset(target, textOffset, markRun)));
        return true;
    }

    public static int MarkAll(
        TextDocument document,
        DocumentCommandBus commandBus,
        string sourceText,
        IndexMark mark)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(commandBus);
        ArgumentNullException.ThrowIfNull(mark);

        var markRun = DocumentIndex.MarkRun(mark);
        if (DocumentIndex.MarkedEntry(markRun) is not { MainEntry.Length: > 0 } normalized)
            return 0;

        var targets = DocumentIndex.MarkAllTargets(document, sourceText, normalized);
        if (targets.Count == 0)
            return 0;

        commandBus.BeginUndoGroup();
        try
        {
            foreach (var target in targets)
            {
                if (target.TableParagraph is { } tableParagraph)
                {
                    commandBus.Execute(new ReplaceTableCellParagraphRunsCommand(
                        target.BlockIndex,
                        tableParagraph,
                        paragraph => RevisionEditPlanner.InsertRunAtOffset(
                            paragraph,
                            target.TextOffset,
                            DocumentIndex.MarkRun(normalized))));
                }
                else
                {
                    commandBus.Execute(new ReplaceParagraphRunsCommand(target.BlockIndex, paragraph =>
                        RevisionEditPlanner.InsertRunAtOffset(
                            paragraph,
                            target.TextOffset,
                            DocumentIndex.MarkRun(normalized))));
                }
            }

            commandBus.CommitUndoGroup(MarkAllLabel);
        }
        catch
        {
            commandBus.RollbackUndoGroup();
            throw;
        }

        return targets.Count;
    }
}
