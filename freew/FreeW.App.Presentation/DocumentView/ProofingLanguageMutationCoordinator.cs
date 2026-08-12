using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Applies a proofing-language plan to the document model. Range validation, exact run splitting,
/// metadata preservation, rollback, and undo grouping are shared by every renderer.
/// </summary>
public static class ProofingLanguageMutationCoordinator
{
    public const string UndoLabel = "Proofing Language";

    public static int Apply(
        TextDocument document,
        DocumentCommandBus commandBus,
        ProofingLanguageApplyPlan plan)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(commandBus);
        ArgumentNullException.ThrowIfNull(plan);

        var ranges = plan.Ranges
            .Where(range => range.BlockIndex >= 0
                && range.BlockIndex < document.Blocks.Count
                && document.Blocks[range.BlockIndex] is Paragraph paragraph
                && CoversText(paragraph, range))
            .ToArray();
        if (ranges.Length == 0)
            return 0;

        commandBus.BeginUndoGroup();
        try
        {
            foreach (var range in ranges)
            {
                commandBus.Execute(new ReplaceParagraphRunsCommand(range.BlockIndex, paragraph =>
                    RevisionEditPlanner.ApplyFormattingRange(
                        paragraph,
                        range.StartOffset,
                        range.EndOffset,
                        formatting => formatting with { LanguageTag = plan.LanguageTag })));
            }
        }
        catch
        {
            commandBus.RollbackUndoGroup();
            throw;
        }

        commandBus.CommitUndoGroup(UndoLabel);
        return ranges.Length;
    }

    private static bool CoversText(Paragraph paragraph, ProofingLanguageTextRange range)
    {
        var start = Math.Clamp(range.StartOffset, 0, paragraph.PlainText.Length);
        var end = Math.Clamp(range.EndOffset, 0, paragraph.PlainText.Length);
        return end > start;
    }
}
